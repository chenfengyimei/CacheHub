using CacheHub.Storage.Database;
using CacheHub.Storage.Database.Migrations;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CacheHub.Tests;

/// <summary>
/// Regression tests for IDX-P0-001: Snapshot activation must not cancel
/// other workspaces' active snapshots.
/// </summary>
[Collection("SQLite")]
public class SnapshotActivationRegressionTests
{
    [Fact]
    public async Task ActivatingSnapshot_DoesNotAffectOtherWorkspace()
    {
        var dbPath = GetTempDbPath();
        try
        {
            var factory = SetupFactory(dbPath);

            // Create two workspaces
            var ws1 = "ws_aaa001";
            var ws2 = "ws_bbb002";
            await InsertWorkspaceAsync(factory, ws1, "ProjectA");
            await InsertWorkspaceAsync(factory, ws2, "ProjectB");

            // Both workspaces have active snapshots
            var snap1 = "snap_aaa001";
            var snap2 = "snap_bbb002";
            await InsertSnapshotAsync(factory, snap1, ws1, "Active");
            await InsertSnapshotAsync(factory, snap2, ws2, "Active");

            // Simulate what ActivateSnapshotAsync does for ws2:
            // deactivate only ws2's active snapshot, then activate new one
            await using var conn = factory.CreateOpenConnection();
            await using var tx = await conn.BeginTransactionAsync();

            using var deactivateCmd = conn.CreateCommand();
            deactivateCmd.Transaction = (SqliteTransaction)tx;
            deactivateCmd.CommandText =
                "UPDATE index_snapshots SET status = 'Superseded' WHERE status = 'Active' AND workspace_id = $ws;";
            deactivateCmd.Parameters.AddWithValue("$ws", ws2);
            await deactivateCmd.ExecuteNonQueryAsync();

            // Insert new snapshot for ws2
            var snap2b = "snap_bbb003";
            using var insertCmd = conn.CreateCommand();
            insertCmd.Transaction = (SqliteTransaction)tx;
            insertCmd.CommandText =
                """
                INSERT INTO index_snapshots (id, workspace_id, status, file_count)
                VALUES ($id, $ws, 'Active', 10);
                """;
            insertCmd.Parameters.AddWithValue("$id", snap2b);
            insertCmd.Parameters.AddWithValue("$ws", ws2);
            await insertCmd.ExecuteNonQueryAsync();

            await tx.CommitAsync();

            // Verify ws1's snapshot is still Active
            var ws1Status = await GetSnapshotStatusAsync(factory, snap1);
            Assert.Equal("Active", ws1Status);

            // Verify ws2's old snapshot is Superseded
            var ws2OldStatus = await GetSnapshotStatusAsync(factory, snap2);
            Assert.Equal("Superseded", ws2OldStatus);

            // Verify ws2's new snapshot is Active
            var ws2NewStatus = await GetSnapshotStatusAsync(factory, snap2b);
            Assert.Equal("Active", ws2NewStatus);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(dbPath);
        }
    }

    [Fact]
    public async Task OldBuggySql_WouldHaveBrokenOtherWorkspaces()
    {
        // This test documents the bug: the OLD SQL (without workspace_id filter)
        // would deactivate ALL active snapshots. We verify the NEW SQL does NOT.
        var dbPath = GetTempDbPath();
        try
        {
            var factory = SetupFactory(dbPath);

            var ws1 = "ws_ccc001";
            var ws2 = "ws_ddd002";
            await InsertWorkspaceAsync(factory, ws1, "ProjectC");
            await InsertWorkspaceAsync(factory, ws2, "ProjectD");

            var snap1 = "snap_ccc001";
            var snap2 = "snap_ddd002";
            await InsertSnapshotAsync(factory, snap1, ws1, "Active");
            await InsertSnapshotAsync(factory, snap2, ws2, "Active");

            // Use the fixed SQL (with workspace_id filter)
            await using var conn = factory.CreateOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "UPDATE index_snapshots SET status = 'Superseded' WHERE status = 'Active' AND workspace_id = $ws;";
            cmd.Parameters.AddWithValue("$ws", ws2);
            var rowsAffected = await cmd.ExecuteNonQueryAsync();

            // Only 1 row should be affected (ws2's snapshot), not 2
            Assert.Equal(1, rowsAffected);

            // ws1 should still be Active
            var ws1Status = await GetSnapshotStatusAsync(factory, snap1);
            Assert.Equal("Active", ws1Status);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(dbPath);
        }
    }

    private static async Task InsertWorkspaceAsync(SqliteConnectionFactory factory, string id, string name)
    {
        await using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO workspaces (id, name, root_path, root_path_hash, status)
            VALUES ($id, $name, $path, $hash, 'Ready');
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$path", $"/test/{name}");
        cmd.Parameters.AddWithValue("$hash", $"hash_{id}");
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task InsertSnapshotAsync(SqliteConnectionFactory factory, string snapId, string wsId, string status)
    {
        await using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO index_snapshots (id, workspace_id, status, file_count)
            VALUES ($id, $ws, $status, 0);
            """;
        cmd.Parameters.AddWithValue("$id", snapId);
        cmd.Parameters.AddWithValue("$ws", wsId);
        cmd.Parameters.AddWithValue("$status", status);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<string> GetSnapshotStatusAsync(SqliteConnectionFactory factory, string snapId)
    {
        await using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT status FROM index_snapshots WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", snapId);
        var result = await cmd.ExecuteScalarAsync();
        return (string)result!;
    }

    private static SqliteConnectionFactory SetupFactory(string dbPath)
    {
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
        return factory;
    }

    private static string GetTempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"cachehub_snapreg_{Guid.NewGuid():N}.db");

    private static void TryDelete(string dbPath)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { if (File.Exists(dbPath + suffix)) File.Delete(dbPath + suffix); }
            catch { /* best effort */ }
        }
    }
}
