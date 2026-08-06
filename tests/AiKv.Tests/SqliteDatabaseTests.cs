using AiKv.Storage;
using AiKv.Storage.Database;
using AiKv.Storage.Database.Migrations;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AiKv.Tests;

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
        Path.Combine(Path.GetTempPath(), $"aikv_test_{Guid.NewGuid():N}.db");

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
}

[CollectionDefinition("SQLite")]
public class SqliteCollection;
