using CacheHub.Core.Identifiers;
using CacheHub.Core.Parsing;
using CacheHub.Storage.Database;
using CacheHub.Storage.Database.Migrations;
using CacheHub.Storage.Indexing;
using Microsoft.Data.Sqlite;

namespace CacheHub.Tests;

[Collection("SQLite")]
public sealed class IndexSnapshotDocumentWriterTests
{
    [Fact]
    public async Task PersistAsync_WritesFileAndAllParserDerivedRowsInOneSnapshot()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cachehub_writer_{Guid.NewGuid():N}.db");
        var factory = new SqliteConnectionFactory(dbPath);
        new MigrationRunner(factory, dbPath,
        [
            new Migration0001Initial(), new Migration0002Fts5(), new Migration0003ContextPackages(),
            new Migration0004Feedback(), new Migration0005ContextPackageDetails(), new Migration0006SchemaV2(),
            new Migration0007ContextPackageFields(), new Migration0008ContextPackageFk(),
            new Migration0009PersistentCache(), new Migration0010RelationSourceColumn(), new Migration0011SnapshotGitState(),
        ]).Migrate();

        try
        {
            var workspaceId = WorkspaceId.New();
            var snapshotId = IndexSnapshotId.New();
            await using (var connection = factory.CreateOpenConnection())
            {
                using var workspace = connection.CreateCommand();
                workspace.CommandText = "INSERT INTO workspaces (id, name, root_path, root_path_hash, status) VALUES ($id, 'writer', '/tmp/writer', 'hash', 'Ready');";
                workspace.Parameters.AddWithValue("$id", workspaceId.Value);
                await workspace.ExecuteNonQueryAsync();

                using var snapshot = connection.CreateCommand();
                snapshot.CommandText = "INSERT INTO index_snapshots (id, workspace_id, status, file_count) VALUES ($id, $workspace, 'Building', 0);";
                snapshot.Parameters.AddWithValue("$id", snapshotId.Value);
                snapshot.Parameters.AddWithValue("$workspace", workspaceId.Value);
                await snapshot.ExecuteNonQueryAsync();
            }

            var result = new ParseResult
            {
                ParserId = "test-parser", ParserVersion = "1", Language = "C#",
                Symbols = [new CodeSymbol { Name = "Runner", Kind = SymbolKind.Class, StartLine = 1, EndLine = 5 }],
                Imports = [new ImportDeclaration { Module = "System", Line = 1 }],
                Relations = [new CodeRelation { RelationType = RelationType.Syntactic, Relation = "uses", TargetName = "System", Confidence = 1, Source = "test", Line = 1 }],
            };
            var document = new IndexSnapshotDocument("src/Runner.cs", 42, "sha256:test", "C#", false, "test-parser", "1", result);

            await new IndexSnapshotDocumentWriter(factory).PersistAsync(snapshotId, [document]);

            await using var verify = factory.CreateOpenConnection();
            Assert.Equal(1L, await CountAsync(verify, "files", snapshotId));
            Assert.Equal(1L, await CountAsync(verify, "file_symbols", snapshotId));
            Assert.Equal(1L, await CountAsync(verify, "file_imports", snapshotId));
            Assert.Equal(1L, await CountAsync(verify, "file_relations", snapshotId));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try { File.Delete(dbPath + suffix); } catch { }
            }
        }
    }

    private static async Task<long> CountAsync(SqliteConnection connection, string table, IndexSnapshotId snapshotId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE snapshot_id = $snapshotId;";
        command.Parameters.AddWithValue("$snapshotId", snapshotId.Value);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
