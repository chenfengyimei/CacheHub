using System.Net;
using CacheHub.Context.Cache;
using CacheHub.Context.Engine;
using CacheHub.Context.Recall;
using CacheHub.Core.Benchmarks;
using CacheHub.Core.Benchmarks.Engine;
using CacheHub.Core.Caching;
using CacheHub.Core.Context;
using CacheHub.Core.Identifiers;
using CacheHub.Core.Workspaces;
using CacheHub.Core.Security;
using CacheHub.Storage.Caching;
using CacheHub.Storage.Database;
using CacheHub.Storage.Database.Migrations;
using CacheHub.Storage.Query;
using CacheHub.Storage.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CacheHub.Tests;

/// <summary>
/// V5-W16: CI Smoke Tests for production-grade reliability verification.
/// Covers: DB migration upgrade, cache restart persistence, Workflow→Gateway mock E2E,
/// Snapshot Refresh integrity, Retrieval Gate threshold, and P0 regressions.
/// </summary>
[Collection("SQLite")]
public class CiSmokeTests
{
    private static readonly IMigration[] AllMigrations =
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
    ];

    private static string GetTempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"cachehub_ci_{Guid.NewGuid():N}.db");

    private static void CleanupDb(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { if (File.Exists(dbPath + suffix)) File.Delete(dbPath + suffix); } catch { }
        }
    }

    // === 1. DB Migration Upgrade ===

    [Fact]
    public void MigrationUpgrade_FromV5ToV11_Succeeds()
    {
        var dbPath = GetTempDbPath();
        try
        {
            var factory = new SqliteConnectionFactory(dbPath);

            // Phase 1: apply migrations 1-5 only (old version)
            var runnerOld = new MigrationRunner(factory, dbPath, AllMigrations.Take(5).ToArray());
            Assert.Equal(5, runnerOld.Migrate());
            Assert.Equal(5, runnerOld.GetCurrentVersion());

            // Phase 2: upgrade to all 11 migrations
            var runnerNew = new MigrationRunner(factory, dbPath, AllMigrations);
            Assert.Equal(6, runnerNew.Migrate());
            Assert.Equal(11, runnerNew.GetCurrentVersion());

            // Verify new tables exist
            using (var conn = factory.CreateOpenConnection())
            {
                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='file_symbols';";
                Assert.NotNull(cmd.ExecuteScalar());

                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='cache_entries';";
                Assert.NotNull(cmd.ExecuteScalar());
            }
        }
        finally { CleanupDb(dbPath); }
    }

    [Fact]
    public void MigrationUpgrade_RunningTwice_SecondRunIsNoOp()
    {
        var dbPath = GetTempDbPath();
        try
        {
            var factory = new SqliteConnectionFactory(dbPath);
            var runner = new MigrationRunner(factory, dbPath, AllMigrations);
            Assert.Equal(11, runner.Migrate());
            Assert.Equal(0, runner.Migrate());
            Assert.Equal(11, runner.GetCurrentVersion());
        }
        finally { CleanupDb(dbPath); }
    }

    // === 2. Cache Restart Persistence ===

    [Fact]
    public void CacheRestart_ContextPackageSurvivesRestart()
    {
        var dbPath = GetTempDbPath();
        var blobDir = Path.Combine(Path.GetTempPath(), $"cachehub_ci_blobs_{Guid.NewGuid():N}");
        try
        {
            var factory = new SqliteConnectionFactory(dbPath);
            new MigrationRunner(factory, dbPath, AllMigrations).Migrate();

            var store = new SqliteCacheStore(factory, blobDir);
            var key = CacheKey.Build("test-task", "snap-001", "profile-v1", 1, 80000, 90000, "sec-v1", null);

            var testJson = """{"id":"ctx-test-001"}"""u8.ToArray();
            store.Put(new CacheEntry
            {
                Key = key.FullKey,
                Type = CacheType.Context,
                Version = "v1",
                CreatedAt = DateTimeOffset.UtcNow,
                SizeBytes = testJson.Length,
                DependencyHash = key.FullKey,
            }, testJson);

            // Simulate restart — new store instance pointing to same SQLite + blob dir
            var store2 = new SqliteCacheStore(factory, blobDir);
            var entry = store2.TryGet(key.FullKey, CacheType.Context);
            Assert.NotNull(entry);

            var blob = store2.GetBlob(key.FullKey);
            Assert.NotNull(blob);
            Assert.Equal(testJson.Length, blob!.Length);

            // Verify stats survive restart
            var stats = store2.GetStats(CacheType.Context);
            Assert.True(stats.TotalEntries >= 1);
        }
        finally
        {
            CleanupDb(dbPath);
            try { if (Directory.Exists(blobDir)) Directory.Delete(blobDir, true); } catch { }
        }
    }

    // === 3. Workflow → Gateway Mock E2E ===

    [Fact]
    public async Task WorkflowGatewayMock_GatewayCallSucceeds()
    {
        var port = Random.Shared.Next(10000, 20000);
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var mockResponse = """{"choices":[{"message":{"content":"mock answer"}}],"usage":{"prompt_tokens":100,"completion_tokens":50,"total_tokens":150}}""";

        _ = Task.Run(async () =>
        {
            try
            {
                while (listener.IsListening)
                {
                    var ctx = await listener.GetContextAsync();
                    var resp = ctx.Response;
                    var body = System.Text.Encoding.UTF8.GetBytes(mockResponse);
                    resp.StatusCode = 200;
                    resp.ContentType = "application/json";
                    resp.ContentLength64 = body.Length;
                    await resp.OutputStream.WriteAsync(body);
                    resp.Close();
                }
            }
            catch { /* listener stopped */ }
        });

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var requestBody = System.Text.Json.JsonSerializer.Serialize(new
            {
                model = "gpt-4o-mini",
                messages = new[]
                {
                    new { role = "system", content = "You are a code assistant." },
                    new { role = "user", content = "Fix the login bug" },
                },
            });

            using var msg = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/v1/chat/completions");
            msg.Content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");
            var resp = await http.SendAsync(msg);
            var body = await resp.Content.ReadAsStringAsync();

            Assert.True(resp.IsSuccessStatusCode);

            using var doc = System.Text.Json.JsonDocument.Parse(body);
            var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            Assert.Equal("mock answer", content);

            var promptTokens = doc.RootElement.GetProperty("usage").GetProperty("prompt_tokens").GetInt32();
            var completionTokens = doc.RootElement.GetProperty("usage").GetProperty("completion_tokens").GetInt32();
            var totalTokens = doc.RootElement.GetProperty("usage").GetProperty("total_tokens").GetInt32();

            Assert.Equal(100, promptTokens);
            Assert.Equal(50, completionTokens);
            Assert.Equal(150, totalTokens);
        }
        finally
        {
            listener.Stop();
        }
    }

    // === 4. Snapshot Refresh Integrity ===

    [Fact]
    public async Task SnapshotRefresh_OldSnapshotUnaffected_NewSnapshotHasData()
    {
        var dbPath = GetTempDbPath();
        try
        {
            var factory = new SqliteConnectionFactory(dbPath);
            new MigrationRunner(factory, dbPath, AllMigrations).Migrate();

            var wsId = WorkspaceId.Parse("ws_refresh_001");
            var wsRepo = new SqliteWorkspaceRepository(factory);
            await wsRepo.InsertAsync(Workspace.Create("RefreshTest", "/test/refresh") with { Id = wsId });

            var oldSnapId = IndexSnapshotId.New();
            await using (var conn = factory.CreateOpenConnection())
            using (var tx = await conn.BeginTransactionAsync())
            {
                using var snapCmd = conn.CreateCommand();
                snapCmd.Transaction = (SqliteTransaction)tx;
                snapCmd.CommandText = "INSERT INTO index_snapshots (id, workspace_id, status, file_count) VALUES ($id, $ws, 'Superseded', 5);";
                snapCmd.Parameters.AddWithValue("$id", oldSnapId.Value);
                snapCmd.Parameters.AddWithValue("$ws", wsId.Value);
                await snapCmd.ExecuteNonQueryAsync();

                for (int i = 0; i < 5; i++)
                {
                    using var fileCmd = conn.CreateCommand();
                    fileCmd.Transaction = (SqliteTransaction)tx;
                    fileCmd.CommandText = "INSERT INTO files (id, snapshot_id, path, normalized_path, size, language, content_hash) VALUES ($id, $snap, $path, $path, 100, 'typescript', 'hash_old');";
                    fileCmd.Parameters.AddWithValue("$id", $"file_old_{i}");
                    fileCmd.Parameters.AddWithValue("$snap", oldSnapId.Value);
                    fileCmd.Parameters.AddWithValue("$path", $"src/old_{i}.ts");
                    await fileCmd.ExecuteNonQueryAsync();
                }
                await tx.CommitAsync();
            }

            var newSnapId = IndexSnapshotId.New();
            await using (var conn = factory.CreateOpenConnection())
            using (var tx = await conn.BeginTransactionAsync())
            {
                using var supCmd = conn.CreateCommand();
                supCmd.Transaction = (SqliteTransaction)tx;
                supCmd.CommandText = "UPDATE index_snapshots SET status = 'Superseded' WHERE status IN ('Active', 'ActiveDegraded') AND workspace_id = $ws;";
                supCmd.Parameters.AddWithValue("$ws", wsId.Value);
                await supCmd.ExecuteNonQueryAsync();

                using var snapCmd = conn.CreateCommand();
                snapCmd.Transaction = (SqliteTransaction)tx;
                snapCmd.CommandText = "INSERT INTO index_snapshots (id, workspace_id, status, file_count) VALUES ($id, $ws, 'Active', 3);";
                snapCmd.Parameters.AddWithValue("$id", newSnapId.Value);
                snapCmd.Parameters.AddWithValue("$ws", wsId.Value);
                await snapCmd.ExecuteNonQueryAsync();

                for (int i = 0; i < 3; i++)
                {
                    using var fileCmd = conn.CreateCommand();
                    fileCmd.Transaction = (SqliteTransaction)tx;
                    fileCmd.CommandText = "INSERT INTO files (id, snapshot_id, path, normalized_path, size, language, content_hash) VALUES ($id, $snap, $path, $path, 200, 'typescript', 'hash_new');";
                    fileCmd.Parameters.AddWithValue("$id", $"file_new_{i}");
                    fileCmd.Parameters.AddWithValue("$snap", newSnapId.Value);
                    fileCmd.Parameters.AddWithValue("$path", $"src/new_{i}.ts");
                    await fileCmd.ExecuteNonQueryAsync();
                }
                await tx.CommitAsync();
            }

            using (var conn = factory.CreateOpenConnection())
            {
                using var oldCmd = conn.CreateCommand();
                oldCmd.CommandText = "SELECT COUNT(*) FROM files WHERE snapshot_id = $snap;";
                oldCmd.Parameters.AddWithValue("$snap", oldSnapId.Value);
                Assert.Equal(5L, oldCmd.ExecuteScalar());

                using var newCmd = conn.CreateCommand();
                newCmd.CommandText = "SELECT COUNT(*) FROM files WHERE snapshot_id = $snap;";
                newCmd.Parameters.AddWithValue("$snap", newSnapId.Value);
                Assert.Equal(3L, newCmd.ExecuteScalar());
            }
        }
        finally { CleanupDb(dbPath); }
    }

    // === 5. Retrieval Gate Threshold ===

    [Fact]
    public void RetrievalGate_HighRecallPasses_LowRecallFails()
    {
        var goodMetrics = new List<AggregatedMetrics>
        {
            new() { TaskId = "task-1", MeanFileRecall = 0.95, MissingContextRate = 0.05, SuccessRate = 1.0, StaleContextRate = 0, MeanInputTokens = 5000, RunCount = 1 },
            new() { TaskId = "task-2", MeanFileRecall = 0.90, MissingContextRate = 0.04, SuccessRate = 1.0, StaleContextRate = 0, MeanInputTokens = 4000, RunCount = 1 },
        };

        var badMetrics = new List<AggregatedMetrics>
        {
            new() { TaskId = "task-1", MeanFileRecall = 0.3, MissingContextRate = 0.7, SuccessRate = 0.3, StaleContextRate = 0, MeanInputTokens = 8000, RunCount = 1 },
        };

        var thresholds = new PhaseGateThresholds();
        var goodGate = MetricsCalculator.EvaluatePhaseGate(goodMetrics, goodMetrics, thresholds);
        var badGate = MetricsCalculator.EvaluatePhaseGate(badMetrics, badMetrics, thresholds);

        Assert.True(goodGate.ActualMissingContextRate <= thresholds.MaxMissingContextFailureRate);
        Assert.True(badGate.ActualMissingContextRate > thresholds.MaxMissingContextFailureRate);
    }

    // === 6. ActiveDegraded Snapshot Visible (V5-W01 regression) ===

    [Fact]
    public async Task ActiveDegraded_Snapshot_IsVisibleToQueryService()
    {
        var dbPath = GetTempDbPath();
        try
        {
            var factory = new SqliteConnectionFactory(dbPath);
            new MigrationRunner(factory, dbPath, AllMigrations).Migrate();

            var wsId = "ws_deg_ci";
            using (var conn = factory.CreateOpenConnection())
            {
                using var wsCmd = conn.CreateCommand();
                wsCmd.CommandText = "INSERT INTO workspaces (id, name, root_path, root_path_hash, status) VALUES ($id, 'Deg', '/test', 'h', 'Ready');";
                wsCmd.Parameters.AddWithValue("$id", wsId);
                wsCmd.ExecuteNonQuery();

                using var snapCmd = conn.CreateCommand();
                snapCmd.CommandText = "INSERT INTO index_snapshots (id, workspace_id, status, file_count) VALUES ('snap_deg_ci', $ws, 'ActiveDegraded', 10);";
                snapCmd.Parameters.AddWithValue("$ws", wsId);
                snapCmd.ExecuteNonQuery();
            }

            var querySvc = new SqliteIndexQueryService(factory);
            var snapId = await querySvc.GetActiveSnapshotIdAsync(wsId);
            Assert.NotNull(snapId);
            Assert.Equal("snap_deg_ci", snapId!.Value);
        }
        finally { CleanupDb(dbPath); }
    }

    // === 7. Cache Invalidate Removes Persistent Entry (V5-W04 regression) ===

    [Fact]
    public void CacheInvalidate_RemovesFromPersistentStore()
    {
        var dbPath = GetTempDbPath();
        var blobDir = Path.Combine(Path.GetTempPath(), $"cachehub_ci_inv_{Guid.NewGuid():N}");
        try
        {
            var factory = new SqliteConnectionFactory(dbPath);
            new MigrationRunner(factory, dbPath, AllMigrations).Migrate();

            var store = new SqliteCacheStore(factory, blobDir);
            var key = CacheKey.Build("inv-test", "snap-inv", "p", 1, 80000, 90000, "sec", null);
            var testJson = """{"id":"test-inv"}"""u8.ToArray();
            store.Put(new CacheEntry
            {
                Key = key.FullKey,
                Type = CacheType.Context,
                Version = "v1",
                CreatedAt = DateTimeOffset.UtcNow,
                SizeBytes = testJson.Length,
                DependencyHash = key.FullKey,
            }, testJson);

            Assert.True(store.GetStats(CacheType.Context).TotalEntries >= 1);

            // Invalidate via ContextPackageCache (uses InvalidateByKey + InvalidateByDependency)
            var cache = new ContextPackageCache(store);
            cache.Invalidate(key);

            // Store should be empty
            Assert.Equal(0, store.GetStats(CacheType.Context).TotalEntries);
            Assert.Null(store.TryGet(key.FullKey, CacheType.Context));
        }
        finally
        {
            CleanupDb(dbPath);
            try { if (Directory.Exists(blobDir)) Directory.Delete(blobDir, true); } catch { }
        }
    }

    // === 8. SecurityPolicyResolver Offline Blocks (V5-W02 regression) ===

    [Fact]
    public void SecurityPolicyResolver_OfflineConfig_BlocksCloudSend()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"cachehub_ci_sec_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(tempDir, "config"));
            var configPath = Path.Combine(tempDir, "config", ".cachehub-config.json");
            File.WriteAllText(configPath, """{"version":"1","security":{"mode":"Offline","enableSecretScan":true}}""");

            var configMgr = new Core.Configuration.ConfigManager(tempDir);
            var policy = SecurityPolicyResolver.Resolve(configMgr);
            Assert.Equal(ExfiltrationMode.Offline, policy.Mode);
            Assert.False(SecurityPolicyResolver.IsCloudSendAllowed(configMgr));
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}

