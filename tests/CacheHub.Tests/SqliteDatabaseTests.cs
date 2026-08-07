using CacheHub.Storage;
using CacheHub.Storage.Database;
using CacheHub.Storage.Database.Migrations;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CacheHub.Tests;

[Collection("SQLite")]
public class SqliteDatabaseTests
{
    [Fact]
    public void ConnectionFactory_ShouldCreateConnectionWithWal()
    {
        var dbPath = GetTempDbPath();
        try
        {
            var factory = new SqliteConnectionFactory(dbPath);
            using (var connection = factory.CreateOpenConnection())
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "PRAGMA journal_mode;";
                var mode = (string)cmd.ExecuteScalar()!;
                Assert.Equal("wal", mode.ToLowerInvariant());
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(dbPath);
        }
    }

    [Fact]
    public void MigrationRunner_ShouldCreateBaseTables()
    {
        var dbPath = GetTempDbPath();
        try
        {
            var factory = new SqliteConnectionFactory(dbPath);
            var runner = new MigrationRunner(factory, dbPath, [new Migration0001Initial()]);

            var applied = runner.Migrate();

            Assert.Equal(1, applied);
            Assert.Equal(1, runner.GetCurrentVersion());

            using (var connection = factory.CreateOpenConnection())
            {
                foreach (var table in new[] { "workspaces", "repositories", "components", "index_snapshots", "files", "background_jobs" })
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = $"SELECT count(*) FROM sqlite_master WHERE type='table' AND name='{table}';";
                    Assert.Equal(1L, cmd.ExecuteScalar());
                }
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(dbPath);
        }
    }

    [Fact]
    public void MigrationRunner_ShouldBeIdempotent()
    {
        var dbPath = GetTempDbPath();
        try
        {
            var factory = new SqliteConnectionFactory(dbPath);
            var runner = new MigrationRunner(factory, dbPath, [new Migration0001Initial()]);

            runner.Migrate();
            var secondRun = runner.Migrate();

            Assert.Equal(0, secondRun);
            Assert.Equal(1, runner.GetCurrentVersion());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(dbPath);
        }
    }

    [Fact]
    public void MigrationRunner_ShouldRollbackOnFailure()
    {
        var dbPath = GetTempDbPath();
        try
        {
            var factory = new SqliteConnectionFactory(dbPath);
            var runner = new MigrationRunner(factory, dbPath,
            [
                new Migration0001Initial(),
                new FailingMigration(),
            ]);

            // First Migrate applies migration 1 then tries migration 2 which fails.
            // Transaction is rolled back, so version remains 0.
            Assert.Throws<InvalidOperationException>(() => runner.Migrate());
            Assert.Equal(0, runner.GetCurrentVersion());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(dbPath);
        }
    }

    private static string GetTempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"cachehub_test_{Guid.NewGuid():N}.db");

    private static void TryDelete(string dbPath)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { if (File.Exists(dbPath + suffix)) File.Delete(dbPath + suffix); }
            catch { /* best effort */ }
        }
    }

    private sealed class FailingMigration : MigrationBase
    {
        public FailingMigration() : base(2, "Fails on purpose") { }

        public override void Up(SqliteConnection connection, SqliteTransaction transaction)
        {
            throw new InvalidOperationException("Intentional failure");
        }
    }

    [Fact]
    public async Task Migration_UpgradeFromV5ToV9_SucceedsIncrementally()
    {
        // Test incremental upgrade: run migrations 1-5, verify, then run 6-9, verify
        var dbPath = GetTempDbPath();
        try
        {
            // Phase 1: Run only migrations 1-5 (older schema)
            var factory1 = new SqliteConnectionFactory(dbPath);
            var runner1 = new MigrationRunner(factory1, dbPath,
            [
                new Migration0001Initial(),
                new Migration0002Fts5(),
                new Migration0003ContextPackages(),
                new Migration0004Feedback(),
                new Migration0005ContextPackageDetails(),
            ]);
            runner1.Migrate();

            // Verify schema v1: basic tables exist
            using (var conn = factory1.CreateOpenConnection())
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type IN ('table','view') ORDER BY name;";
                var tables = new List<string>();
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    tables.Add(reader.GetString(0));

                Assert.Contains("workspaces", tables);
                Assert.Contains("index_snapshots", tables);
                Assert.Contains("files", tables);
                Assert.Contains("context_packages", tables);
                Assert.Contains("context_feedback", tables);

                // V2 tables should NOT exist yet
                Assert.DoesNotContain("file_symbols", tables);
                Assert.DoesNotContain("file_imports", tables);
                Assert.DoesNotContain("file_relations", tables);
            }

            // Verify migration version is 5
            using (var conn2 = factory1.CreateOpenConnection())
            {
                using var cmd = conn2.CreateCommand();
                cmd.CommandText = "SELECT MAX(version) FROM __schema_version WHERE applied = 1;";
                var version = cmd.ExecuteScalar();
                Assert.Equal(5L, Convert.ToInt64(version));
            }

            // Phase 2: Upgrade to v9 (run remaining migrations 6-9)
            var factory2 = new SqliteConnectionFactory(dbPath);
            var runner2 = new MigrationRunner(factory2, dbPath,
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
            runner2.Migrate();

            // Verify upgrade: new tables now exist
            using (var conn3 = factory2.CreateOpenConnection())
            {
                using var cmd = conn3.CreateCommand();
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type IN ('table','view') ORDER BY name;";
                var tables = new List<string>();
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    tables.Add(reader.GetString(0));

                // All original tables still exist
                Assert.Contains("workspaces", tables);
                Assert.Contains("files", tables);

                // V2 tables now exist
                Assert.Contains("file_symbols", tables);
                Assert.Contains("file_imports", tables);
                Assert.Contains("file_relations", tables);

                // Cache tables from v9
                Assert.Contains("cache_entries", tables);
                Assert.Contains("cache_stats", tables);
            }

            // Verify final migration version is 9
            using (var conn4 = factory2.CreateOpenConnection())
            {
                using var cmd = conn4.CreateCommand();
                cmd.CommandText = "SELECT MAX(version) FROM __schema_version WHERE applied = 1;";
                var version = cmd.ExecuteScalar();
                Assert.Equal(9L, Convert.ToInt64(version));
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(dbPath);
        }
    }
}

[CollectionDefinition("SQLite")]
public class SqliteCollection;
