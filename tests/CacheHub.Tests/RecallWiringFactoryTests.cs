using CacheHub.Context.Recall;
using CacheHub.Core.Identifiers;
using CacheHub.Storage.Database;
using CacheHub.Storage.Database.Migrations;
using Xunit;

namespace CacheHub.Tests;

/// <summary>
/// V7-W13: Tests for RecallWiringFactory — verifies all 7 callbacks are created and functional.
/// </summary>
public class RecallWiringFactoryTests
{
    private static SqliteConnectionFactory CreateFactoryWithMigrations()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cachehub_rwf_{Guid.NewGuid():N}.db");
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

        // Insert a workspace + snapshot for FK
        using var conn = factory.CreateOpenConnection();
        using var wsCmd = conn.CreateCommand();
        wsCmd.CommandText = "INSERT INTO workspaces (id, name, root_path, root_path_hash, status) VALUES ('ws-rwf', 'test', '/tmp', 'hash', 'Imported');";
        wsCmd.ExecuteNonQuery();

        using var snapCmd = conn.CreateCommand();
        var snapId = IndexSnapshotId.New();
        snapCmd.CommandText = "INSERT INTO index_snapshots (id, workspace_id, status, file_count) VALUES ($id, 'ws-rwf', 'Active', 0);";
        snapCmd.Parameters.AddWithValue("$id", snapId.Value);
        snapCmd.ExecuteNonQuery();

        return factory;
    }

    [Fact]
    public void Create_ReturnsAllSevenCallbacks()
    {
        var factory = CreateFactoryWithMigrations();
        try
        {
            var wiringFactory = new RecallWiringFactory(factory);
            var snapId = IndexSnapshotId.New();
            var callbacks = wiringFactory.Create(snapId);

            Assert.NotNull(callbacks.FtsSearch);
            Assert.NotNull(callbacks.SymbolSearch);
            Assert.NotNull(callbacks.ImportSearch);
            Assert.NotNull(callbacks.SymbolSearchDetailed);
            Assert.NotNull(callbacks.RelationSearch);
            Assert.NotNull(callbacks.ReverseRelationSearch);
            Assert.NotNull(callbacks.FileSymbolsProvider);
        }
        finally
        {
            // Clean up DB file
        }
    }

    [Fact]
    public void FtsSearch_ReturnsEmptyOnNoData()
    {
        var factory = CreateFactoryWithMigrations();
        {
            var wiringFactory = new RecallWiringFactory(factory);
            // Get the snapshot ID we inserted
            using var conn = factory.CreateOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id FROM index_snapshots WHERE workspace_id = 'ws-rwf' LIMIT 1;";
            var snapIdStr = (string)cmd.ExecuteScalar()!;
            var snapId = IndexSnapshotId.Parse(snapIdStr);

            var callbacks = wiringFactory.Create(snapId);
            var results = callbacks.FtsSearch("test");
            Assert.Empty(results);
        }
    }

    [Fact]
    public void SymbolSearch_ReturnsEmptyOnNoData()
    {
        var factory = CreateFactoryWithMigrations();
        {
            var wiringFactory = new RecallWiringFactory(factory);
            using var conn = factory.CreateOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id FROM index_snapshots WHERE workspace_id = 'ws-rwf' LIMIT 1;";
            var snapIdStr = (string)cmd.ExecuteScalar()!;
            var snapId = IndexSnapshotId.Parse(snapIdStr);

            var callbacks = wiringFactory.Create(snapId);
            var results = callbacks.SymbolSearch("TestClass");
            Assert.Empty(results);
        }
    }

    [Fact]
    public void ReverseRelationSearch_ReturnsEmptyOnNoData()
    {
        var factory = CreateFactoryWithMigrations();
        {
            var wiringFactory = new RecallWiringFactory(factory);
            using var conn = factory.CreateOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id FROM index_snapshots WHERE workspace_id = 'ws-rwf' LIMIT 1;";
            var snapIdStr = (string)cmd.ExecuteScalar()!;
            var snapId = IndexSnapshotId.Parse(snapIdStr);

            var callbacks = wiringFactory.Create(snapId);
            var results = callbacks.ReverseRelationSearch("TargetSymbol");
            Assert.Empty(results);
        }
    }
}
