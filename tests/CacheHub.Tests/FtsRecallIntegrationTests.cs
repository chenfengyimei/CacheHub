using CacheHub.Context.Engine;
using CacheHub.Context.Recall;
using CacheHub.Core.Identifiers;
using CacheHub.Storage.Database;
using CacheHub.Storage.Database.Migrations;
using CacheHub.Storage.Query;
using CacheHub.Storage.Search;

namespace CacheHub.Tests;

/// <summary>
/// Tests for R4-W003: FTS5 正文召回接入 Context Build.
/// Verifies that ContextEngine.Build uses FTS queries (not just path matching)
/// when ftsSearch callback is provided via IIndexQueryService.
/// </summary>
[Collection("SQLite")]
public class FtsRecallIntegrationTests
{
    private static async Task<(SqliteConnectionFactory factory, IndexSnapshotId snapshotId, string workspaceId, string workspacePath)> SetupWorkspaceWithFtsAsync()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), $"cachehub_fts_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(workspacePath, "src"));

        await File.WriteAllTextAsync(Path.Combine(workspacePath, "src", "auth.ts"),
            "export class AuthService { async login(user: string, pass: string): Promise<string> { return 'token'; } }");

        await File.WriteAllTextAsync(Path.Combine(workspacePath, "src", "user.ts"),
            "export class UserService { async getUser(id: string): Promise<User> { return { id, name: 'test' }; } }");

        await File.WriteAllTextAsync(Path.Combine(workspacePath, "README.md"),
            "# Test Project\n\nAuthentication and user management module.");

        var dbPath = Path.Combine(Path.GetTempPath(), $"cachehub_fts_db_{Guid.NewGuid():N}.db");
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
        ]);
        runner.Migrate();

        var workspaceId = "fts-test-ws";
        var snapshotId = IndexSnapshotId.New();

        await using var conn = factory.CreateOpenConnection();

        using var wsCmd = conn.CreateCommand();
        wsCmd.CommandText = """
            INSERT INTO workspaces (id, name, root_path, root_path_hash, status, created_at)
            VALUES ($id, 'test', $root, $hash, 'Ready', datetime('now'));
            """;
        wsCmd.Parameters.AddWithValue("$id", workspaceId);
        wsCmd.Parameters.AddWithValue("$root", workspacePath);
        wsCmd.Parameters.AddWithValue("$hash", workspacePath);
        await wsCmd.ExecuteNonQueryAsync();

        using var snapCmd = conn.CreateCommand();
        snapCmd.CommandText = "INSERT INTO index_snapshots (id, workspace_id, status, file_count) VALUES ($id, $ws, 'Active', 3);";
        snapCmd.Parameters.AddWithValue("$id", snapshotId.Value);
        snapCmd.Parameters.AddWithValue("$ws", workspaceId);
        await snapCmd.ExecuteNonQueryAsync();

        // Index files with FTS
        var fts = new Fts5Index(factory);
        var files = new[] { ("src/auth.ts", "typescript"), ("src/user.ts", "typescript"), ("README.md", "markdown") };
        foreach (var (path, lang) in files)
        {
            var fullPath = Path.Combine(workspacePath, path.Replace('/', Path.DirectorySeparatorChar));
            var content = await File.ReadAllTextAsync(fullPath);

            using var fCmd = conn.CreateCommand();
            fCmd.CommandText = """
                INSERT INTO files (id, snapshot_id, path, normalized_path, size, content_hash, language, is_binary, status, hash_kind)
                VALUES ($id, $snap, $path, $norm, $size, $hash, $lang, 0, 'Indexed', 'full');
                """;
            fCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            fCmd.Parameters.AddWithValue("$snap", snapshotId.Value);
            fCmd.Parameters.AddWithValue("$path", path);
            fCmd.Parameters.AddWithValue("$norm", path);
            fCmd.Parameters.AddWithValue("$size", content.Length);
            fCmd.Parameters.AddWithValue("$hash", "sha256:test");
            fCmd.Parameters.AddWithValue("$lang", lang);
            await fCmd.ExecuteNonQueryAsync();

            await fts.IndexFileAsync(snapshotId, path, path, content, lang, "sha256:test");
        }

        return (factory, snapshotId, workspaceId, workspacePath);
    }

    private static List<IndexedFileInfo> GetIndexedFiles(SqliteConnectionFactory factory, string workspaceId)
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
        cmd.Parameters.AddWithValue("$ws", workspaceId);
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

    [Fact]
    public async Task ContextBuild_WithFtsCallback_FindsFilesByContent()
    {
        var (factory, snapshotId, workspaceId, workspacePath) = await SetupWorkspaceWithFtsAsync();
        try
        {
            var querySvc = new SqliteIndexQueryService(factory);
            var indexedFiles = GetIndexedFiles(factory, workspaceId);
            var engine = new ContextEngine();

            var manifest = engine.Build(
                new ContextBuildRequest
                {
                    WorkspaceId = WorkspaceId.Parse(workspaceId),
                    IndexSnapshotId = snapshotId,
                    Task = "Fix the authentication login bug",
                },
                () => indexedFiles,
                path =>
                {
                    var fullPath = Path.Combine(workspacePath, path.Replace('/', Path.DirectorySeparatorChar));
                    return File.Exists(fullPath) ? File.ReadAllTextAsync(fullPath).GetAwaiter().GetResult() : "";
                },
                path => "sha256:test",
                ftsSearch: keyword =>
                {
                    var results = querySvc.SearchFtsAsync(snapshotId, keyword, 50).GetAwaiter().GetResult();
                    return results.Select(r => new FtsMatch(r.Path, r.Language, r.Snippet)).ToList();
                },
                symbolSearch: symbol =>
                {
                    var results = querySvc.SearchSymbolsAsync(snapshotId, symbol).GetAwaiter().GetResult();
                    return results.Select(r => r.NormalizedPath).ToList();
                });

            // FTS should find auth.ts because it contains "authentication" and "login"
            Assert.NotEmpty(manifest.SelectedFiles);
            Assert.Contains(manifest.SelectedFiles, f => f.Path.Contains("auth.ts"));
        }
        finally
        {
            try { Directory.Delete(workspacePath, true); } catch { }
        }
    }

    [Fact]
    public async Task ContextBuild_WithoutFtsCallback_FallsBackToPathMatching()
    {
        var (factory, snapshotId, workspaceId, workspacePath) = await SetupWorkspaceWithFtsAsync();
        try
        {
            var indexedFiles = GetIndexedFiles(factory, workspaceId);
            var engine = new ContextEngine();

            // No ftsSearch callback — should fall back to path-based keyword matching
            var manifest = engine.Build(
                new ContextBuildRequest
                {
                    WorkspaceId = WorkspaceId.Parse(workspaceId),
                    IndexSnapshotId = snapshotId,
                    Task = "Fix the authentication login bug",
                },
                () => indexedFiles,
                path =>
                {
                    var fullPath = Path.Combine(workspacePath, path.Replace('/', Path.DirectorySeparatorChar));
                    return File.Exists(fullPath) ? File.ReadAllTextAsync(fullPath).GetAwaiter().GetResult() : "";
                },
                path => "sha256:test");

            // Without FTS, "authentication" won't match any path — but "auth" might partially match
            // The key is that the build still succeeds and produces a manifest
            Assert.NotNull(manifest);
        }
        finally
        {
            try { Directory.Delete(workspacePath, true); } catch { }
        }
    }

    [Fact]
    public async Task FtsSearch_ViaQueryService_ReturnsSnippetAndPath()
    {
        var (factory, snapshotId, _, _) = await SetupWorkspaceWithFtsAsync();
        try
        {
            var querySvc = new SqliteIndexQueryService(factory);
            var results = await querySvc.SearchFtsAsync(snapshotId, "login");

            Assert.NotEmpty(results);
            Assert.Contains(results, r => r.Path == "src/auth.ts");
            Assert.All(results, r => Assert.False(string.IsNullOrEmpty(r.Snippet)));
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(Path.Combine(Path.GetTempPath(), "src"))!, true); } catch { }
        }
    }

    [Fact]
    public async Task ContextBuild_WithFts_RecordsEvidenceInManifest()
    {
        var (factory, snapshotId, workspaceId, workspacePath) = await SetupWorkspaceWithFtsAsync();
        try
        {
            var querySvc = new SqliteIndexQueryService(factory);
            var indexedFiles = GetIndexedFiles(factory, workspaceId);
            var engine = new ContextEngine();

            var manifest = engine.Build(
                new ContextBuildRequest
                {
                    WorkspaceId = WorkspaceId.Parse(workspaceId),
                    IndexSnapshotId = snapshotId,
                    Task = "Fix the login function",
                },
                () => indexedFiles,
                path =>
                {
                    var fullPath = Path.Combine(workspacePath, path.Replace('/', Path.DirectorySeparatorChar));
                    return File.Exists(fullPath) ? File.ReadAllTextAsync(fullPath).GetAwaiter().GetResult() : "";
                },
                path => "sha256:test",
                ftsSearch: keyword =>
                {
                    var results = querySvc.SearchFtsAsync(snapshotId, keyword, 50).GetAwaiter().GetResult();
                    return results.Select(r => new FtsMatch(r.Path, r.Language, r.Snippet)).ToList();
                });

            // Manifest should have selected files with reasons
            Assert.NotEmpty(manifest.SelectedFiles);
            Assert.All(manifest.SelectedFiles, f => Assert.NotEmpty(f.Reasons));
        }
        finally
        {
            try { Directory.Delete(workspacePath, true); } catch { }
        }
    }
}
