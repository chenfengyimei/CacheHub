using CacheHub.Core.Identifiers;
using CacheHub.Storage.Database;
using CacheHub.Storage.Database.Migrations;
using CacheHub.Storage.Search;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CacheHub.Tests;

/// <summary>
/// V4-W002: Integration test for immutable snapshot Refresh.
/// V5 quality fix: tests now call the PRODUCTION CloneSnapshotDataAsync directly
/// (previously used a copied CloneWithIdMappingAsync helper that could drift from production).
/// </summary>
[Collection("SQLite")]
public class SnapshotCloneIntegrationTests
{
    [Fact]
    public async Task CloneSnapshot_NonEmptySource_NoPrimaryKeyConflict()
    {
        var dbPath = GetTempDbPath();
        try
        {
            var factory = SetupFactory(dbPath);

            var wsId = "ws_clone_test_001";
            await InsertWorkspaceAsync(factory, wsId, "CloneTest");

            var oldSnap = IndexSnapshotId.Parse("snap_old_active_001");
            var newSnap = IndexSnapshotId.Parse("snap_new_building_001");
            await InsertSnapshotAsync(factory, oldSnap.Value, wsId, "Active");

            // Insert 3 files with symbols, imports, and relations
            var fileIds = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                var fileId = $"file_old_{i:D3}";
                fileIds.Add(fileId);
                await InsertFileAsync(factory, fileId, oldSnap.Value, $"src/file{i}.ts", $"sha256:hash{i}");

                for (int s = 0; s < 2; s++)
                    await InsertSymbolAsync(factory, $"sym_old_{i}_{s}", fileId, oldSnap.Value, $"Symbol{i}_{s}", "Method", 10 + s, 20 + s);

                await InsertImportAsync(factory, $"imp_old_{i}", fileId, oldSnap.Value, $"module{i}", $"Name{i}", 1);
                await InsertRelationAsync(factory, $"rel_old_{i}", fileId, oldSnap.Value, $"Source{i}", $"Target{i}", "Call", 5, "parser");
            }

            // Insert FTS entries
            var fts = new Fts5Index(factory);
            for (int i = 0; i < 3; i++)
                await fts.IndexFileAsync(oldSnap, $"src/file{i}.ts", $"src/file{i}.ts",
                    $"content of file {i} with keyword token{i}", "typescript", $"sha256:hash{i}");

            // Verify source data exists
            Assert.Equal(3L, await CountRowsAsync(factory, "files", oldSnap.Value));
            Assert.Equal(6L, await CountRowsAsync(factory, "file_symbols", oldSnap.Value));
            Assert.Equal(3L, await CountRowsAsync(factory, "file_imports", oldSnap.Value));
            Assert.Equal(3L, await CountRowsAsync(factory, "file_relations", oldSnap.Value));

            // Create building snapshot
            await InsertSnapshotAsync(factory, newSnap.Value, wsId, "Building");

            // V5: Call the PRODUCTION SnapshotCloneService directly
            await SnapshotCloneService.CloneSnapshotDataAsync(factory, oldSnap, newSnap);

            // Verify: no PK conflict occurred, data cloned correctly
            Assert.Equal(3L, await CountRowsAsync(factory, "files", newSnap.Value));
            Assert.Equal(6L, await CountRowsAsync(factory, "file_symbols", newSnap.Value));
            Assert.Equal(3L, await CountRowsAsync(factory, "file_imports", newSnap.Value));
            Assert.Equal(3L, await CountRowsAsync(factory, "file_relations", newSnap.Value));

            // Verify: new snapshot has different file IDs than old
            var newFileIds = await GetFileIdsAsync(factory, newSnap.Value);
            foreach (var newId in newFileIds)
                Assert.DoesNotContain(newId, fileIds);

            // Verify: child tables reference new file IDs (not old ones)
            await using var conn = factory.CreateOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT COUNT(*) FROM file_symbols s
                JOIN files f ON s.file_id = f.id
                WHERE s.snapshot_id = $snap AND f.snapshot_id != $snap;
                """;
            cmd.Parameters.AddWithValue("$snap", newSnap.Value);
            var orphanedSymbols = (long)cmd.ExecuteScalar()!;
            Assert.Equal(0L, orphanedSymbols);

            // Verify: FTS search works on new snapshot
            var searchResults = await fts.SearchAsync(newSnap, "token0", limit: 10);
            Assert.NotEmpty(searchResults);
            Assert.Contains(searchResults, r => r.Path == "src/file0.ts");

            // Verify: old snapshot data still intact (not affected by clone)
            Assert.Equal(3L, await CountRowsAsync(factory, "files", oldSnap.Value));
            Assert.Equal(6L, await CountRowsAsync(factory, "file_symbols", oldSnap.Value));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(dbPath);
        }
    }

    [Fact]
    public async Task CloneSnapshot_ThenModifyFile_OldSnapshotUntouched()
    {
        var dbPath = GetTempDbPath();
        try
        {
            var factory = SetupFactory(dbPath);

            var wsId = "ws_clone_test_002";
            await InsertWorkspaceAsync(factory, wsId, "CloneTest2");

            var oldSnap = IndexSnapshotId.Parse("snap_old_active_002");
            var newSnap = IndexSnapshotId.Parse("snap_new_building_002");
            await InsertSnapshotAsync(factory, oldSnap.Value, wsId, "Active");
            await InsertSnapshotAsync(factory, newSnap.Value, wsId, "Building");

            // Insert a file in old snapshot
            var oldFileId = "file_old_unique_002";
            await InsertFileAsync(factory, oldFileId, oldSnap.Value, "src/app.ts", "sha256:apphash");
            await InsertSymbolAsync(factory, "sym_old_app", oldFileId, oldSnap.Value, "AppComponent", "Class", 1, 100);

            // V5: Call the PRODUCTION SnapshotCloneService directly
            await SnapshotCloneService.CloneSnapshotDataAsync(factory, oldSnap, newSnap);

            // Delete the file from new snapshot (simulating Refresh modification)
            await using var delConn = factory.CreateOpenConnection();
            using var delCmd = delConn.CreateCommand();
            delCmd.CommandText = "DELETE FROM files WHERE snapshot_id = $snap AND normalized_path = $path;";
            delCmd.Parameters.AddWithValue("$snap", newSnap.Value);
            delCmd.Parameters.AddWithValue("$path", "src/app.ts");
            await delCmd.ExecuteNonQueryAsync();

            // Also delete orphaned child rows
            using var delSymCmd = delConn.CreateCommand();
            delSymCmd.CommandText = "DELETE FROM file_symbols WHERE snapshot_id = $snap AND file_id NOT IN (SELECT id FROM files WHERE snapshot_id = $snap);";
            delSymCmd.Parameters.AddWithValue("$snap", newSnap.Value);
            await delSymCmd.ExecuteNonQueryAsync();

            // Verify: old snapshot still has the file and symbol
            Assert.Equal(1L, await CountRowsAsync(factory, "files", oldSnap.Value));
            Assert.Equal(1L, await CountRowsAsync(factory, "file_symbols", oldSnap.Value));

            // Verify: new snapshot has 0 files (was cloned then deleted)
            Assert.Equal(0L, await CountRowsAsync(factory, "files", newSnap.Value));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(dbPath);
        }
    }

    [Fact]
    public async Task CloneSnapshot_OldBuggyApproach_WouldFailWithUniqueConstraint()
    {
        // This test documents the bug: copying PKs directly causes UNIQUE constraint failure.
        var dbPath = GetTempDbPath();
        try
        {
            var factory = SetupFactory(dbPath);

            var wsId = "ws_clone_test_003";
            await InsertWorkspaceAsync(factory, wsId, "CloneTest3");

            var oldSnap = "snap_old_active_003";
            var newSnap = "snap_new_building_003";
            await InsertSnapshotAsync(factory, oldSnap, wsId, "Active");
            await InsertSnapshotAsync(factory, newSnap, wsId, "Building");

            await InsertFileAsync(factory, "file_pk_test_003", oldSnap, "src/test.ts", "sha256:test");

            // Attempt the OLD buggy approach: copy id directly
            await using var conn = factory.CreateOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO files (id, snapshot_id, path, normalized_path, size, content_hash, language, is_binary, status, hash_kind)
                SELECT id, $to, path, normalized_path, size, content_hash, language, is_binary, status, hash_kind
                FROM files WHERE snapshot_id = $from;
                """;
            cmd.Parameters.AddWithValue("$from", oldSnap);
            cmd.Parameters.AddWithValue("$to", newSnap);

            await Assert.ThrowsAsync<SqliteException>(() => cmd.ExecuteNonQueryAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(dbPath);
        }
    }

    #region DB Helpers

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
            new Migration0009PersistentCache(),
            new Migration0010RelationSourceColumn(),
        ]);
        runner.Migrate();
        return factory;
    }

    private static async Task InsertWorkspaceAsync(SqliteConnectionFactory factory, string id, string name)
    {
        await using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
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
        cmd.CommandText = """
            INSERT INTO index_snapshots (id, workspace_id, status, file_count)
            VALUES ($id, $ws, $status, 0);
            """;
        cmd.Parameters.AddWithValue("$id", snapId);
        cmd.Parameters.AddWithValue("$ws", wsId);
        cmd.Parameters.AddWithValue("$status", status);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task InsertFileAsync(SqliteConnectionFactory factory, string fileId, string snapId, string path, string hash)
    {
        await using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO files (id, snapshot_id, path, normalized_path, size, content_hash, language, is_binary, status, hash_kind)
            VALUES ($id, $snap, $path, $path, 100, $hash, 'typescript', 0, 'Indexed', 'full');
            """;
        cmd.Parameters.AddWithValue("$id", fileId);
        cmd.Parameters.AddWithValue("$snap", snapId);
        cmd.Parameters.AddWithValue("$path", path);
        cmd.Parameters.AddWithValue("$hash", hash);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task InsertSymbolAsync(SqliteConnectionFactory factory, string symId, string fileId, string snapId, string name, string kind, int startLine, int endLine)
    {
        await using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO file_symbols (id, file_id, snapshot_id, name, kind, start_line, end_line, modifier, confidence)
            VALUES ($id, $fid, $snap, $name, $kind, $sl, $el, NULL, 'syntactic');
            """;
        cmd.Parameters.AddWithValue("$id", symId);
        cmd.Parameters.AddWithValue("$fid", fileId);
        cmd.Parameters.AddWithValue("$snap", snapId);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$kind", kind);
        cmd.Parameters.AddWithValue("$sl", startLine);
        cmd.Parameters.AddWithValue("$el", endLine);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task InsertImportAsync(SqliteConnectionFactory factory, string impId, string fileId, string snapId, string module, string name, int line)
    {
        await using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO file_imports (id, file_id, snapshot_id, module, imported_name, line)
            VALUES ($id, $fid, $snap, $mod, $name, $line);
            """;
        cmd.Parameters.AddWithValue("$id", impId);
        cmd.Parameters.AddWithValue("$fid", fileId);
        cmd.Parameters.AddWithValue("$snap", snapId);
        cmd.Parameters.AddWithValue("$mod", module);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$line", line);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task InsertRelationAsync(SqliteConnectionFactory factory, string relId, string fileId, string snapId, string source, string target, string type, int line, string sourceName)
    {
        await using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO file_relations (id, file_id, snapshot_id, source_symbol, target_symbol, relation_type, confidence, line, source)
            VALUES ($id, $fid, $snap, $src, $tgt, $rt, 'heuristic', $line, $source);
            """;
        cmd.Parameters.AddWithValue("$id", relId);
        cmd.Parameters.AddWithValue("$fid", fileId);
        cmd.Parameters.AddWithValue("$snap", snapId);
        cmd.Parameters.AddWithValue("$src", source);
        cmd.Parameters.AddWithValue("$tgt", target);
        cmd.Parameters.AddWithValue("$rt", type);
        cmd.Parameters.AddWithValue("$line", line);
        cmd.Parameters.AddWithValue("$source", sourceName);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountRowsAsync(SqliteConnectionFactory factory, string table, string snapId)
    {
        await using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table} WHERE snapshot_id = $snap;";
        cmd.Parameters.AddWithValue("$snap", snapId);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<List<string>> GetFileIdsAsync(SqliteConnectionFactory factory, string snapId)
    {
        var result = new List<string>();
        await using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM files WHERE snapshot_id = $snap;";
        cmd.Parameters.AddWithValue("$snap", snapId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetString(0));
        return result;
    }

    private static string GetTempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"cachehub_clone_{Guid.NewGuid():N}.db");

    private static void TryDelete(string dbPath)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { if (File.Exists(dbPath + suffix)) File.Delete(dbPath + suffix); }
            catch { }
        }
    }

    #endregion
}
