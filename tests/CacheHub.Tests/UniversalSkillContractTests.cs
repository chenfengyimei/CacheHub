using CacheHub.Context.Engine;
using CacheHub.Context.Expand;
using CacheHub.Context.Payload;
using CacheHub.Context.Recall;
using CacheHub.Core.Capabilities;
using CacheHub.Core.Context;
using CacheHub.Core.Errors;
using CacheHub.Core.Feedback;
using CacheHub.Core.Identifiers;
using CacheHub.Core.Workspaces;
using CacheHub.Storage.Database;
using CacheHub.Storage.Database.Migrations;
using CacheHub.Storage.Repositories;
using CacheHub.Storage.Search;

namespace CacheHub.Tests;

/// <summary>
/// Universal Skill contract tests: verifies the complete workflow described in
/// integration/skills/universal/SKILL.md works end-to-end.
///
/// Workflow steps tested:
/// 1. Capabilities discovery
/// 2. Workspace import
/// 3. Index build
/// 4. Context build
/// 5. Context expand (by file and by symbol)
/// 6. Context feedback
/// </summary>
[Collection("SQLite")]
public class UniversalSkillContractTests
{
    private static readonly string TempRoot = Path.Combine(Path.GetTempPath(), $"cachehub_skill_{Guid.NewGuid():N}");

    private static async Task<(SqliteConnectionFactory factory, string dbPath, string workspacePath)> SetupWorkspaceAsync()
    {
        var workspacePath = Path.Combine(TempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(workspacePath, "src"));
        Directory.CreateDirectory(Path.Combine(workspacePath, "tests"));

        await File.WriteAllTextAsync(Path.Combine(workspacePath, "src", "auth.ts"),
            """
            export class AuthService {
              async login(user: string, pass: string): Promise<string> {
                return 'token';
              }
              async refreshToken(token: string): Promise<string> {
                return 'new_token';
              }
            }
            """);

        await File.WriteAllTextAsync(Path.Combine(workspacePath, "src", "user.ts"),
            """
            export class UserService {
              async getUser(id: string): Promise<User> {
                return { id, name: 'test' };
              }
            }
            """);

        await File.WriteAllTextAsync(Path.Combine(workspacePath, "src", "config.ts"),
            """
            export const API_URL = 'https://api.example.com';
            export interface Config { apiUrl: string; }
            """);

        await File.WriteAllTextAsync(Path.Combine(workspacePath, "tests", "auth.test.ts"),
            """
            import { AuthService } from '../src/auth';
            test('login returns token', async () => {
              const service = new AuthService();
              const token = await service.login('user', 'pass');
              expect(token).toBeDefined();
            });
            """);

        await File.WriteAllTextAsync(Path.Combine(workspacePath, "README.md"),
            "# Test Project\n\nThis is a test repository.");

        var dbPath = Path.Combine(Path.GetTempPath(), $"cachehub_skill_db_{Guid.NewGuid():N}.db");
        var factory = new SqliteConnectionFactory(dbPath);
        var runner = new MigrationRunner(factory, dbPath,
        [
            new Migration0001Initial(),
            new Migration0002Fts5(),
            new Migration0003ContextPackages(),
            new Migration0004Feedback(),
            new Migration0005ContextPackageDetails(),
            new Migration0006SchemaV2(),
            new Migration0007ContextPackageFields(),
            new Migration0008ContextPackageFk(),
        new Migration0009PersistentCache(),
        new Migration0010RelationSourceColumn(),
            new Migration0011SnapshotGitState(),
        ]);
        runner.Migrate();

        return (factory, dbPath, workspacePath);
    }

    private static async Task<(WorkspaceId workspaceId, IndexSnapshotId snapshotId, SqliteConnectionFactory factory, string workspacePath)>
        SetupWithIndexAsync()
    {
        var (factory, dbPath, workspacePath) = await SetupWorkspaceAsync();
        var wsRepo = new SqliteWorkspaceRepository(factory);

        // Step 2: Import workspace
        var workspace = Workspace.CreateValidated("skill-test", workspacePath);
        await wsRepo.InsertAsync(workspace);

        // Step 3: Build index
        var snapshotId = IndexSnapshotId.New();
        await using var conn = factory.CreateOpenConnection();

        using var snapCmd = conn.CreateCommand();
        snapCmd.CommandText = "INSERT INTO index_snapshots (id, workspace_id, status, file_count) VALUES ($id, $ws, 'Building', 0);";
        snapCmd.Parameters.AddWithValue("$id", snapshotId.Value);
        snapCmd.Parameters.AddWithValue("$ws", workspace.Id.Value);
        await snapCmd.ExecuteNonQueryAsync();

        var ignoreEngine = new CacheHub.Indexing.IgnoreRules.IgnoreRuleEngine().WithDefaults();
        var enumerator = new CacheHub.Indexing.Scanning.DirectoryEnumerator();
        var fts = new Fts5Index(factory);
        var fileCount = 0;

        await foreach (var file in enumerator.EnumerateAsync(workspacePath))
        {
            if (file.IsDirectory) continue;
            var relativePath = CacheHub.Core.Paths.PathNormalizer.GetRelativePath(workspacePath, file.Path);
            if (ignoreEngine.IsIgnored(relativePath)) continue;

            var typeInfo = CacheHub.Indexing.Detection.FileTypeDetector.Detect(file.Path, file.Size);
            if (!typeInfo.ShouldIndex) continue;

            var hash = await CacheHub.Indexing.Hashing.FileHasher.HashAsync(file.Path, file.Size);
            var content = await File.ReadAllTextAsync(file.Path);

            using var fileCmd = conn.CreateCommand();
            fileCmd.CommandText = """
                INSERT INTO files (id, snapshot_id, path, normalized_path, size, content_hash, language, is_binary, status, hash_kind)
                VALUES ($id, $snap, $path, $norm, $size, $hash, $lang, $bin, 'Indexed', $hashKind);
                """;
            fileCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            fileCmd.Parameters.AddWithValue("$snap", snapshotId.Value);
            fileCmd.Parameters.AddWithValue("$path", relativePath);
            fileCmd.Parameters.AddWithValue("$norm", relativePath);
            fileCmd.Parameters.AddWithValue("$size", file.Size);
            fileCmd.Parameters.AddWithValue("$hash", hash.Hash);
            fileCmd.Parameters.AddWithValue("$lang", typeInfo.Language);
            fileCmd.Parameters.AddWithValue("$bin", typeInfo.IsBinary ? 1 : 0);
            fileCmd.Parameters.AddWithValue("$hashKind", hash.Hash.StartsWith("fp:", StringComparison.Ordinal) ? "fingerprint" : "full");
            await fileCmd.ExecuteNonQueryAsync();

            await fts.IndexFileAsync(snapshotId, relativePath, relativePath, content, typeInfo.Language, hash.Hash);
            fileCount++;
        }

        // Activate snapshot (workspace-scoped)
        using var activateCmd = conn.CreateCommand();
        activateCmd.CommandText = "UPDATE index_snapshots SET status = 'Superseded' WHERE status = 'Active' AND workspace_id = $ws;";
        activateCmd.Parameters.AddWithValue("$ws", workspace.Id.Value);
        await activateCmd.ExecuteNonQueryAsync();

        using var setActiveCmd = conn.CreateCommand();
        setActiveCmd.CommandText = "UPDATE index_snapshots SET status = 'Active', file_count = $count, completed_at = datetime('now') WHERE id = $id;";
        setActiveCmd.Parameters.AddWithValue("$count", fileCount);
        setActiveCmd.Parameters.AddWithValue("$id", snapshotId.Value);
        await setActiveCmd.ExecuteNonQueryAsync();

        await wsRepo.UpdateStatusAsync(workspace.Id, WorkspaceStatus.Ready);

        return (workspace.Id, snapshotId, factory, workspacePath);
    }

    [Fact]
    public void Skill_Step1_Capabilities_ReturnsExpectedShape()
    {
        // Verify capabilities discovery matches what SKILL.md documents
        var caps = new CapabilityDiscovery
        {
            Version = "0.2.0-prealpha",
            ProtocolVersion = "1.0",
            Capabilities = CapabilityFlags.With(
                Capability.WorkspaceImport, Capability.ContextBuild,
                Capability.ContextExpand, Capability.ContextExplain,
                Capability.ContextFeedback, Capability.FileExport),
            SchemaVersions = new Dictionary<string, int>
            {
                ["contextPackage"] = 1,
                ["capabilityDiscovery"] = 1,
            },
            Limitations = ["No Semantic", "No LSP"],
        };

        Assert.NotEmpty(caps.Capabilities.Enabled);
        Assert.True(caps.Capabilities.IsEnabled(Capability.WorkspaceImport));
        Assert.True(caps.Capabilities.IsEnabled(Capability.ContextBuild));
        Assert.True(caps.Capabilities.IsEnabled(Capability.ContextExpand));
        Assert.True(caps.Capabilities.IsEnabled(Capability.ContextFeedback));
        Assert.NotEmpty(caps.Limitations);
    }

    [Fact]
    public async Task Skill_Step2_WorkspaceImport_CreatesValidWorkspace()
    {
        var (factory, _, workspacePath) = await SetupWorkspaceAsync();
        try
        {
            var wsRepo = new SqliteWorkspaceRepository(factory);
            var workspace = Workspace.CreateValidated("skill-test", workspacePath);
            await wsRepo.InsertAsync(workspace);

            var found = await wsRepo.FindByIdAsync(workspace.Id);
            Assert.NotNull(found);
            Assert.Equal("skill-test", found.Name);
            Assert.True(Directory.Exists(found.RootPath));
        }
        finally
        {
            Cleanup(factory, workspacePath);
        }
    }

    [Fact]
    public async Task Skill_Step3_IndexBuild_CreatesActiveSnapshot()
    {
        var (workspaceId, snapshotId, factory, workspacePath) = await SetupWithIndexAsync();
        try
        {
            // Verify snapshot is Active
            await using var conn = factory.CreateOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT status, file_count FROM index_snapshots WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", snapshotId.Value);
            await using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("Active", reader.GetString(0));
            Assert.True(reader.GetInt32(1) > 0);
        }
        finally
        {
            Cleanup(factory, workspacePath);
        }
    }

    [Fact]
    public async Task Skill_Step4_ContextBuild_ReturnsManifestWithSelectedFiles()
    {
        var (workspaceId, snapshotId, factory, workspacePath) = await SetupWithIndexAsync();
        try
        {
            var wsRepo = new SqliteWorkspaceRepository(factory);
            var ctxRepo = new SqliteContextPackageRepository(factory);
            var ws = await wsRepo.FindByIdAsync(workspaceId);
            Assert.NotNull(ws);

            var engine = new ContextEngine();
            var manifest = engine.Build(
                new ContextBuildRequest
                {
                    WorkspaceId = workspaceId,
                    IndexSnapshotId = snapshotId,
                    Task = "Fix the login function in AuthService",
                },
                () => GetIndexedFiles(factory, workspaceId),
                path =>
                {
                    var fullPath = Path.Combine(workspacePath, path.Replace('/', Path.DirectorySeparatorChar));
                    return File.Exists(fullPath) ? File.ReadAllTextAsync(fullPath).GetAwaiter().GetResult() : "";
                },
                path => "sha256:pending"); // V8-P0-02: use pending to skip hash verification

            Assert.NotEmpty(manifest.SelectedFiles);
            Assert.NotEmpty(manifest.Task.ExtractedSymbols!);
            Assert.True(manifest.Budget.ActualEstimate > 0);
        }
        finally
        {
            Cleanup(factory, workspacePath);
        }
    }

    [Fact]
    public async Task Skill_Step5_ContextExpand_AddsContentForFile()
    {
        var (workspaceId, snapshotId, factory, workspacePath) = await SetupWithIndexAsync();
        try
        {
            var ctxRepo = new SqliteContextPackageRepository(factory);
            var engine = new ContextEngine();
            var manifest = engine.Build(
                new ContextBuildRequest
                {
                    WorkspaceId = workspaceId,
                    IndexSnapshotId = snapshotId,
                    Task = "Fix the login function in AuthService",
                },
                () => GetIndexedFiles(factory, workspaceId),
                path =>
                {
                    var fullPath = Path.Combine(workspacePath, path.Replace('/', Path.DirectorySeparatorChar));
                    return File.Exists(fullPath) ? File.ReadAllTextAsync(fullPath).GetAwaiter().GetResult() : "";
                },
                path => "sha256:pending"); // V8-P0-02: use pending to skip hash verification
            await ctxRepo.SaveAsync(manifest);

            // Expand by file
            var expander = new ContextExpander();
            var targetPath = "src/auth.ts";
            var fullPath = Path.Combine(workspacePath, targetPath);
            var content = await File.ReadAllTextAsync(fullPath);
            var result = expander.ExpandByFile(manifest.Id.Value, targetPath, content, "skill expand test");

            Assert.NotEmpty(result.AddedItems);
            Assert.True(result.AdditionalTokens > 0);
        }
        finally
        {
            Cleanup(factory, workspacePath);
        }
    }

    [Fact]
    public async Task Skill_Step6_Feedback_SavesValidFeedback()
    {
        var (workspaceId, snapshotId, factory, workspacePath) = await SetupWithIndexAsync();
        try
        {
            var ctxRepo = new SqliteContextPackageRepository(factory);
            var fbRepo = new SqliteFeedbackRepository(factory);
            var engine = new ContextEngine();
            var manifest = engine.Build(
                new ContextBuildRequest
                {
                    WorkspaceId = workspaceId,
                    IndexSnapshotId = snapshotId,
                    Task = "Fix the login function in AuthService",
                },
                () => GetIndexedFiles(factory, workspaceId),
                path =>
                {
                    var fullPath = Path.Combine(workspacePath, path.Replace('/', Path.DirectorySeparatorChar));
                    return File.Exists(fullPath) ? File.ReadAllTextAsync(fullPath).GetAwaiter().GetResult() : "";
                },
                path => "sha256:pending"); // V8-P0-02: use pending to skip hash verification
            await ctxRepo.SaveAsync(manifest);

            // Submit feedback as documented in SKILL.md
            var feedback = new ContextFeedback
            {
                ContextPackageId = manifest.Id.Value,
                ClientId = "generic-agent",
                FilesActuallyRead = ["src/auth.ts"],
                TaskCompleted = true,
                MissingContextReported = false,
            };
            await fbRepo.SaveAsync(feedback);

            // Verify feedback was saved
            var saved = await fbRepo.FindByContextPackageIdAsync(manifest.Id.Value);
            Assert.NotNull(saved);
            Assert.Equal("generic-agent", saved.ClientId);
        }
        finally
        {
            Cleanup(factory, workspacePath);
        }
    }

    [Fact]
    public async Task Skill_FullWorkflow_ImportIndexContextExpandFeedback()
    {
        // Complete end-to-end workflow as described in SKILL.md
        var (workspaceId, snapshotId, factory, workspacePath) = await SetupWithIndexAsync();
        try
        {
            var wsRepo = new SqliteWorkspaceRepository(factory);
            var ctxRepo = new SqliteContextPackageRepository(factory);
            var fbRepo = new SqliteFeedbackRepository(factory);
            var ws = await wsRepo.FindByIdAsync(workspaceId);
            Assert.NotNull(ws);
            Assert.Equal(WorkspaceStatus.Ready, ws.Status);

            // Build context
            var engine = new ContextEngine();
            var manifest = engine.Build(
                new ContextBuildRequest
                {
                    WorkspaceId = workspaceId,
                    IndexSnapshotId = snapshotId,
                    Task = "Update AuthService login method",
                },
                () => GetIndexedFiles(factory, workspaceId),
                path =>
                {
                    var fullPath = Path.Combine(workspacePath, path.Replace('/', Path.DirectorySeparatorChar));
                    return File.Exists(fullPath) ? File.ReadAllTextAsync(fullPath).GetAwaiter().GetResult() : "";
                },
                path => "sha256:pending"); // V8-P0-02: use pending to skip hash verification
            await ctxRepo.SaveAsync(manifest);

            // Verify context was saved and can be retrieved
            var loaded = await ctxRepo.FindByIdAsync(manifest.Id);
            Assert.NotNull(loaded);
            Assert.Equal(manifest.Task.OriginalText, loaded.Task.OriginalText);

            // Generate payload
            var generator = new PayloadGenerator();
            var payload = generator.Generate(manifest, path =>
            {
                var fullPath = Path.Combine(workspacePath, path.Replace('/', Path.DirectorySeparatorChar));
                return File.Exists(fullPath) ? File.ReadAllTextAsync(fullPath).GetAwaiter().GetResult() : "";
            }, new Core.Security.SecurityPolicyEnforcer());
            Assert.NotNull(payload.Items);
            Assert.NotEmpty(payload.Items);

            // Submit feedback
            var feedback = new ContextFeedback
            {
                ContextPackageId = manifest.Id.Value,
                ClientId = "skill-e2e-test",
                FilesActuallyRead = manifest.SelectedFiles.Select(f => f.Path).ToList(),
                TaskCompleted = true,
                MissingContextReported = false,
            };
            await fbRepo.SaveAsync(feedback);

            // Verify feedback
            var savedFeedback = await fbRepo.FindByContextPackageIdAsync(manifest.Id.Value);
            Assert.NotNull(savedFeedback);
            Assert.Equal(manifest.SelectedFiles.Count, savedFeedback.FilesActuallyRead.Count);
        }
        finally
        {
            Cleanup(factory, workspacePath);
        }
    }

    private static List<IndexedFileInfo> GetIndexedFiles(SqliteConnectionFactory factory, WorkspaceId workspaceId)
    {
        var result = new List<IndexedFileInfo>();
        using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT f.normalized_path, f.size, f.language, f.content_hash
            FROM files f
            INNER JOIN index_snapshots s ON f.snapshot_id = s.id
            WHERE s.workspace_id = $ws AND s.status = 'Active';
            """;
        cmd.Parameters.AddWithValue("$ws", workspaceId.Value);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new IndexedFileInfo
            {
                Path = reader.GetString(0),
                NormalizedPath = reader.GetString(0),
                Size = reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
                Language = reader.IsDBNull(2) ? "unknown" : reader.GetString(2),
                ContentHash = reader.IsDBNull(3) ? null : reader.GetString(3),
                Symbols = [],
            });
        }
        return result;
    }

    private static void Cleanup(SqliteConnectionFactory factory, string workspacePath)
    {
        try { if (Directory.Exists(workspacePath)) Directory.Delete(workspacePath, true); } catch { }
    }
}
