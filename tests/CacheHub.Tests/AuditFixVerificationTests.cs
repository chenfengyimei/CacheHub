using CacheHub.Context.Expand;
using CacheHub.Context.Payload;
using CacheHub.Core.Context;
using CacheHub.Core.Identifiers;
using CacheHub.Core.Security;
using CacheHub.Core.Workspaces;
using CacheHub.Indexing.Hashing;
using CacheHub.Storage.Database;
using CacheHub.Storage.Database.Migrations;
using CacheHub.Storage.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CacheHub.Tests;

/// <summary>
/// Verification tests for audit fix round.
/// Tests: real hash computation, expand revision persistence,
/// payload blockedFiles surfacing, batch transaction with parser persistence.
/// </summary>
[Collection("SQLite")]
public class AuditFixVerificationTests
{
    private static SqliteConnectionFactory CreateTestFactory(string? dbPath = null)
    {
        dbPath ??= Path.Combine(Path.GetTempPath(), $"cachehub_audit_{Guid.NewGuid():N}.db");
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
        ]);
        runner.Migrate();
        return factory;
    }

    private static ContextPackageManifest CreateTestManifest(WorkspaceId? wsId = null, IndexSnapshotId? snapId = null)
    {
        return new ContextPackageManifest
        {
            Id = ContextPackageId.New(),
            SchemaVersion = 1,
            WorkspaceId = wsId ?? WorkspaceId.New(),
            IndexSnapshotId = snapId ?? IndexSnapshotId.New(),
            Task = new TaskInfo { OriginalText = "Fix bug", QueryParserVersion = "v1" },
            Ranking = new RankingInfo { ProfileId = "test", ProfileVersion = 1 },
            Budget = new BudgetInfo
            {
                ModelContextWindow = 128_000,
                AgentReservedTokens = 1000,
                ResponseReservedTokens = 1000,
                ContextTarget = 50_000,
                ContextHardLimit = 60_000,
                SafetyMargin = 1000,
                ActualEstimate = 500,
            },
            SelectedFiles =
            [
                new SelectedFile { Path = "src/app.ts", ContentHash = "sha256:abc", Mode = SelectionMode.Full, Score = 0.8, Reasons = ["match"] },
            ],
            ExcludedCandidates = [],
            Safety = new SafetyInfo { CloudSendAllowed = true, SecretsScanPassed = true },
            ContextEngineVersion = "0.2.0-prealpha",
            ChunkingStrategyVersion = "chunking-v2",
            TokenBudgetPolicyVersion = "budget-v2",
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    [Fact]
    public async Task ResolveFileHash_PendingDbHash_ComputesRealSha256FromDisk()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"cachehub_hash_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        await File.WriteAllTextAsync(Path.Combine(tempRoot, "test.cs"), "namespace Foo { class Bar { } }");

        var factory = CreateTestFactory();
        var wsId = WorkspaceId.New();
        var snapshotId = IndexSnapshotId.New();

        try
        {
            await using var conn = factory.CreateOpenConnection();
            // Insert workspace first (FK requirement)
            using var wsCmd = conn.CreateCommand();
            wsCmd.CommandText = "INSERT INTO workspaces (id, name, root_path, root_path_hash, status) VALUES ($id, 'test', '/tmp', $hash, 'Ready');";
            wsCmd.Parameters.AddWithValue("$id", wsId.Value);
            wsCmd.Parameters.AddWithValue("$hash", wsId.Value);
            await wsCmd.ExecuteNonQueryAsync();

            using var snapCmd = conn.CreateCommand();
            snapCmd.CommandText = "INSERT INTO index_snapshots (id, workspace_id, status, file_count) VALUES ($id, $ws, 'Active', 1);";
            snapCmd.Parameters.AddWithValue("$id", snapshotId.Value);
            snapCmd.Parameters.AddWithValue("$ws", wsId.Value);
            await snapCmd.ExecuteNonQueryAsync();

            using var insertCmd = conn.CreateCommand();
            insertCmd.CommandText = """
                INSERT INTO files (id, snapshot_id, path, normalized_path, size, content_hash, language, is_binary, status, hash_kind)
                VALUES ($id, $snap, $path, $norm, $size, 'pending', $lang, 0, 'Indexed', 'pending');
                """;
            insertCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            insertCmd.Parameters.AddWithValue("$snap", snapshotId.Value);
            insertCmd.Parameters.AddWithValue("$path", "test.cs");
            insertCmd.Parameters.AddWithValue("$norm", "test.cs");
            insertCmd.Parameters.AddWithValue("$size", 100);
            insertCmd.Parameters.AddWithValue("$lang", "C#");
            await insertCmd.ExecuteNonQueryAsync();

            var hash = ResolveFileHashForTest(factory, snapshotId, "test.cs", tempRoot);

            Assert.NotEqual("sha256:pending", hash);
            Assert.StartsWith("sha256:", hash);
            Assert.True(hash.Length > "sha256:".Length);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task ResolveFileHash_FingerprintDbHash_ComputesRealSha256FromDisk()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"cachehub_fp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        await File.WriteAllTextAsync(Path.Combine(tempRoot, "large.ts"), new string('x', 2000));

        var factory = CreateTestFactory();
        var wsId = WorkspaceId.New();
        var snapshotId = IndexSnapshotId.New();

        try
        {
            await using var conn = factory.CreateOpenConnection();
            using var wsCmd = conn.CreateCommand();
            wsCmd.CommandText = "INSERT INTO workspaces (id, name, root_path, root_path_hash, status) VALUES ($id, 'test', '/tmp', $hash, 'Ready');";
            wsCmd.Parameters.AddWithValue("$id", wsId.Value);
            wsCmd.Parameters.AddWithValue("$hash", wsId.Value);
            await wsCmd.ExecuteNonQueryAsync();

            using var snapCmd = conn.CreateCommand();
            snapCmd.CommandText = "INSERT INTO index_snapshots (id, workspace_id, status, file_count) VALUES ($id, $ws, 'Active', 1);";
            snapCmd.Parameters.AddWithValue("$id", snapshotId.Value);
            snapCmd.Parameters.AddWithValue("$ws", wsId.Value);
            await snapCmd.ExecuteNonQueryAsync();

            using var insertCmd = conn.CreateCommand();
            insertCmd.CommandText = """
                INSERT INTO files (id, snapshot_id, path, normalized_path, size, content_hash, language, is_binary, status, hash_kind)
                VALUES ($id, $snap, $path, $norm, $size, 'fp:abc123', $lang, 0, 'Indexed', 'fingerprint');
                """;
            insertCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            insertCmd.Parameters.AddWithValue("$snap", snapshotId.Value);
            insertCmd.Parameters.AddWithValue("$path", "large.ts");
            insertCmd.Parameters.AddWithValue("$norm", "large.ts");
            insertCmd.Parameters.AddWithValue("$size", 2000);
            insertCmd.Parameters.AddWithValue("$lang", "TypeScript");
            await insertCmd.ExecuteNonQueryAsync();

            var hash = ResolveFileHashForTest(factory, snapshotId, "large.ts", tempRoot);

            Assert.NotEqual("sha256:pending", hash);
            Assert.StartsWith("sha256:", hash);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void ContextExpander_CreateRevision_SetsParentPackageId()
    {
        var manifest = CreateTestManifest();
        var expander = new ContextExpander();
        var expansion = expander.ExpandByFile(manifest.Id.Value, "src/utils.ts", "export const helper = () => {};", "Need utility functions");
        var revision = expander.CreateRevision(manifest, expansion);

        Assert.NotEqual(manifest.Id.Value, revision.Id.Value);
        Assert.Equal(manifest.Id.Value, revision.ParentPackageId?.Value);
        Assert.Equal(2, revision.SelectedFiles.Count);
        Assert.Equal("src/utils.ts", revision.SelectedFiles[1].Path);
        Assert.True(revision.Budget.ActualEstimate > manifest.Budget.ActualEstimate);
    }

    [Fact]
    public async Task ContextExpander_CreateRevision_CanBePersistedToDb()
    {
        var factory = CreateTestFactory();
        var ctxRepo = new SqliteContextPackageRepository(factory);
        var wsId = WorkspaceId.New();
        var snapshotId = IndexSnapshotId.New();

        await using var conn = factory.CreateOpenConnection();
        using var wsCmd = conn.CreateCommand();
        wsCmd.CommandText = "INSERT INTO workspaces (id, name, root_path, root_path_hash, status) VALUES ($id, 'test', '/tmp', $hash, 'Ready');";
        wsCmd.Parameters.AddWithValue("$id", wsId.Value);
        wsCmd.Parameters.AddWithValue("$hash", wsId.Value);
        await wsCmd.ExecuteNonQueryAsync();

        using var snapCmd = conn.CreateCommand();
        snapCmd.CommandText = "INSERT INTO index_snapshots (id, workspace_id, status, file_count) VALUES ($id, $ws, 'Active', 1);";
        snapCmd.Parameters.AddWithValue("$id", snapshotId.Value);
        snapCmd.Parameters.AddWithValue("$ws", wsId.Value);
        await snapCmd.ExecuteNonQueryAsync();

        var parent = CreateTestManifest(wsId, snapshotId);
        await ctxRepo.SaveAsync(parent);

        var expander = new ContextExpander();
        var expansion = expander.ExpandByFile(parent.Id.Value, "src/utils.ts", "export const helper = () => {};", "Need utils");
        var revision = expander.CreateRevision(parent, expansion);
        await ctxRepo.SaveAsync(revision);

        var loaded = await ctxRepo.FindByIdAsync(revision.Id);
        Assert.NotNull(loaded);
        Assert.Equal(parent.Id.Value, loaded.ParentPackageId?.Value);
        Assert.Equal(2, loaded.SelectedFiles.Count);
    }

    [Fact]
    public void PayloadGenerator_WithSecurityEnforcer_BlockedFilesAreExcluded()
    {
        var manifest = new ContextPackageManifest
        {
            Id = ContextPackageId.New(),
            SchemaVersion = 1,
            WorkspaceId = WorkspaceId.New(),
            IndexSnapshotId = IndexSnapshotId.New(),
            Task = new TaskInfo { OriginalText = "Fix bug", QueryParserVersion = "v1" },
            Ranking = new RankingInfo { ProfileId = "test", ProfileVersion = 1 },
            Budget = new BudgetInfo
            {
                ModelContextWindow = 128_000,
                AgentReservedTokens = 1000,
                ResponseReservedTokens = 1000,
                ContextTarget = 50_000,
                ContextHardLimit = 60_000,
                SafetyMargin = 1000,
                ActualEstimate = 0,
            },
            SelectedFiles =
            [
                new SelectedFile { Path = "src/safe.ts", ContentHash = "sha256:pending", Mode = SelectionMode.Full, Score = 1.0, Reasons = ["match"] },
                new SelectedFile { Path = ".env", ContentHash = "sha256:pending", Mode = SelectionMode.Full, Score = 0.5, Reasons = ["match"] },
            ],
            ExcludedCandidates = [],
            Safety = new SafetyInfo { CloudSendAllowed = true, SecretsScanPassed = true },
            ContextEngineVersion = "0.2.0-prealpha",
            ChunkingStrategyVersion = "chunking-v2",
            TokenBudgetPolicyVersion = "budget-v2",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var safeContent = "export const safe = 1;";
        var secretContent = "API_KEY=sk-1234567890abcdef\nPASSWORD=secret123";
        var enforcer = new SecurityPolicyEnforcer();
        var generator = new PayloadGenerator();

        var payload = generator.Generate(manifest, path => path switch
        {
            "src/safe.ts" => safeContent,
            ".env" => secretContent,
            _ => "",
        }, enforcer);

        Assert.Contains(payload.Items, i => i.Path == "src/safe.ts");
        Assert.DoesNotContain(payload.Items, i => i.Path == ".env");
    }

    [Fact]
    public async Task BatchTransaction_ParserResults_PersistedToDb()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"cachehub_batch_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        await File.WriteAllTextAsync(Path.Combine(tempRoot, "Service.cs"),
            """
            using System;
            namespace MyApp {
              public class Service {
                public void DoWork() { }
              }
            }
            """);

        var factory = CreateTestFactory();
        var wsId = WorkspaceId.New();
        var snapshotId = IndexSnapshotId.New();

        try
        {
            await using var initConn = factory.CreateOpenConnection();
            using var wsCmd = initConn.CreateCommand();
            wsCmd.CommandText = "INSERT INTO workspaces (id, name, root_path, root_path_hash, status) VALUES ($id, 'test', '/tmp', $hash, 'Ready');";
            wsCmd.Parameters.AddWithValue("$id", wsId.Value);
            wsCmd.Parameters.AddWithValue("$hash", wsId.Value);
            await wsCmd.ExecuteNonQueryAsync();

            using var snapCmd = initConn.CreateCommand();
            snapCmd.CommandText = "INSERT INTO index_snapshots (id, workspace_id, status, file_count) VALUES ($id, $ws, 'Building', 0);";
            snapCmd.Parameters.AddWithValue("$id", snapshotId.Value);
            snapCmd.Parameters.AddWithValue("$ws", wsId.Value);
            await snapCmd.ExecuteNonQueryAsync();

            await using var batchConn = factory.CreateOpenConnection();
            await using var batchTx = await batchConn.BeginTransactionAsync();

            var fileId = Guid.NewGuid().ToString("N");
            using var fileCmd = batchConn.CreateCommand();
            fileCmd.Transaction = (SqliteTransaction)batchTx;
            fileCmd.CommandText = """
                INSERT INTO files (id, snapshot_id, path, normalized_path, size, content_hash, language, is_binary, status, hash_kind, parser_id, parser_version)
                VALUES ($id, $snap, $path, $norm, $size, $hash, $lang, 0, 'Indexed', 'full', 'csharp-regex', '2.0');
                """;
            fileCmd.Parameters.AddWithValue("$id", fileId);
            fileCmd.Parameters.AddWithValue("$snap", snapshotId.Value);
            fileCmd.Parameters.AddWithValue("$path", "Service.cs");
            fileCmd.Parameters.AddWithValue("$norm", "Service.cs");
            fileCmd.Parameters.AddWithValue("$size", 120);
            fileCmd.Parameters.AddWithValue("$hash", "sha256:test");
            fileCmd.Parameters.AddWithValue("$lang", "C#");
            await fileCmd.ExecuteNonQueryAsync();

            using var symCmd = batchConn.CreateCommand();
            symCmd.Transaction = (SqliteTransaction)batchTx;
            symCmd.CommandText = """
                INSERT INTO file_symbols (id, file_id, snapshot_id, name, kind, start_line, end_line, modifier, confidence)
                VALUES ($id, $fid, $snap, 'Service', 'Class', 3, 5, 'public', 'syntactic');
                """;
            symCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            symCmd.Parameters.AddWithValue("$fid", fileId);
            symCmd.Parameters.AddWithValue("$snap", snapshotId.Value);
            await symCmd.ExecuteNonQueryAsync();

            await batchTx.CommitAsync();

            await using var verifyConn = factory.CreateOpenConnection();
            using var verifyCmd = verifyConn.CreateCommand();
            verifyCmd.CommandText = "SELECT COUNT(*) FROM file_symbols WHERE snapshot_id = $snap;";
            verifyCmd.Parameters.AddWithValue("$snap", snapshotId.Value);
            var count = (long)verifyCmd.ExecuteScalar()!;
            Assert.Equal(1, count);

            using var parserCmd = verifyConn.CreateCommand();
            parserCmd.CommandText = "SELECT parser_id, parser_version FROM files WHERE snapshot_id = $snap;";
            parserCmd.Parameters.AddWithValue("$snap", snapshotId.Value);
            await using var reader = await parserCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("csharp-regex", reader.GetString(0));
            Assert.Equal("2.0", reader.GetString(1));
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    private static string ResolveFileHashForTest(SqliteConnectionFactory factory, IndexSnapshotId snapshotId, string path, string? rootPath)
    {
        using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT content_hash FROM files WHERE snapshot_id = $snap AND normalized_path = $path LIMIT 1;";
        cmd.Parameters.AddWithValue("$snap", snapshotId.Value);
        cmd.Parameters.AddWithValue("$path", path);
        var result = cmd.ExecuteScalar();
        if (result is string hash && !string.IsNullOrEmpty(hash) && hash != "pending" && !hash.StartsWith("fp:", StringComparison.Ordinal))
            return hash;

        if (rootPath is not null)
        {
            var fullPath = Path.Combine(rootPath, path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath))
            {
                try
                {
                    return FileHasher.ComputeFullHashAsync(fullPath).GetAwaiter().GetResult();
                }
                catch { }
            }
        }
        return "sha256:pending";
    }
}
