using CacheHub.Storage.Database;
using CacheHub.Storage.Database.Migrations;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CacheHub.Tests;

/// <summary>
/// Tests for Migration0006SchemaV2: new columns and tables for parser results.
/// </summary>
[Collection("SQLite")]
public class Migration0006SchemaV2Tests
{
    [Fact]
    public void Migration0006_ShouldAddFileColumns()
    {
        var dbPath = GetTempDbPath();
        try
        {
            var factory = SetupFactory(dbPath);
            using var conn = factory.CreateOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA table_info(files);";
            var columns = new List<string>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                columns.Add(reader.GetString(1));

            Assert.Contains("mtime", columns);
            Assert.Contains("hash_kind", columns);
            Assert.Contains("parser_version", columns);
            Assert.Contains("parser_id", columns);
        }
        finally { SqliteConnection.ClearAllPools(); TryDelete(dbPath); }
    }

    [Fact]
    public void Migration0006_ShouldCreateSymbolsTable()
    {
        var dbPath = GetTempDbPath();
        try
        {
            var factory = SetupFactory(dbPath);
            using var conn = factory.CreateOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='file_symbols';";
            Assert.Equal(1L, cmd.ExecuteScalar());
        }
        finally { SqliteConnection.ClearAllPools(); TryDelete(dbPath); }
    }

    [Fact]
    public void Migration0006_ShouldCreateImportsTable()
    {
        var dbPath = GetTempDbPath();
        try
        {
            var factory = SetupFactory(dbPath);
            using var conn = factory.CreateOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='file_imports';";
            Assert.Equal(1L, cmd.ExecuteScalar());
        }
        finally { SqliteConnection.ClearAllPools(); TryDelete(dbPath); }
    }

    [Fact]
    public void Migration0006_ShouldCreateRelationsTable()
    {
        var dbPath = GetTempDbPath();
        try
        {
            var factory = SetupFactory(dbPath);
            using var conn = factory.CreateOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='file_relations';";
            Assert.Equal(1L, cmd.ExecuteScalar());
        }
        finally { SqliteConnection.ClearAllPools(); TryDelete(dbPath); }
    }

    [Fact]
    public void Migration0006_ShouldCreateIndexes()
    {
        var dbPath = GetTempDbPath();
        try
        {
            var factory = SetupFactory(dbPath);
            using var conn = factory.CreateOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='index' AND name LIKE 'idx_symbols%';";
            Assert.True((long)cmd.ExecuteScalar()! >= 3);

            cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='index' AND name LIKE 'idx_imports%';";
            Assert.True((long)cmd.ExecuteScalar()! >= 3);

            cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='index' AND name LIKE 'idx_relations%';";
            Assert.True((long)cmd.ExecuteScalar()! >= 3);
        }
        finally { SqliteConnection.ClearAllPools(); TryDelete(dbPath); }
    }

    [Fact]
    public void Migration0006_ShouldBeIdempotent()
    {
        var dbPath = GetTempDbPath();
        try
        {
            var factory = SetupFactory(dbPath);
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
            var secondRun = runner.Migrate();
            Assert.Equal(0, secondRun);
        }
        finally { SqliteConnection.ClearAllPools(); TryDelete(dbPath); }
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
        Path.Combine(Path.GetTempPath(), $"cachehub_m6_{Guid.NewGuid():N}.db");

    private static void TryDelete(string dbPath)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { if (File.Exists(dbPath + suffix)) File.Delete(dbPath + suffix); }
            catch { }
        }
    }
}
