using CacheHub.Core.Identifiers;
using Microsoft.Data.Sqlite;

namespace CacheHub.Storage.Database;

/// <summary>
/// V5: Extracted snapshot clone logic into a testable service.
/// Previously this was a private method in IndexCommands, and tests had to
/// replicate the logic (which could drift from production).
/// Now tests call this service directly.
/// </summary>
public static class SnapshotCloneService
{
    /// <summary>
    /// Clones all data (files, symbols, imports, relations, FTS) from one snapshot to another.
    /// Generates new primary keys for all rows and builds an old_file_id→new_file_id mapping
    /// so child tables reference the correct new parent. Does NOT copy PKs directly.
    /// </summary>
    public static async Task CloneSnapshotDataAsync(SqliteConnectionFactory factory, IndexSnapshotId fromSnapshot, IndexSnapshotId toSnapshot)
    {
        await using var conn = factory.CreateOpenConnection();
        await using var tx = await conn.BeginTransactionAsync();

        var fileIdMap = new Dictionary<string, string>(StringComparer.Ordinal);

        // Phase 1: Clone files with new IDs
        using (var readCmd = conn.CreateCommand())
        {
            readCmd.Transaction = (SqliteTransaction)tx;
            readCmd.CommandText = """
                SELECT id, path, normalized_path, size, content_hash, language, is_binary, status, hash_kind, mtime, parser_id, parser_version
                FROM files WHERE snapshot_id = $from;
                """;
            readCmd.Parameters.AddWithValue("$from", fromSnapshot.Value);
            await using var reader = await readCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var oldFileId = reader.GetString(0);
                var newFileId = Guid.NewGuid().ToString("N");
                fileIdMap[oldFileId] = newFileId;

                using var insCmd = conn.CreateCommand();
                insCmd.Transaction = (SqliteTransaction)tx;
                insCmd.CommandText = """
                    INSERT INTO files (id, snapshot_id, path, normalized_path, size, content_hash, language, is_binary, status, hash_kind, mtime, parser_id, parser_version)
                    VALUES ($id, $snap, $path, $norm, $size, $hash, $lang, $bin, $status, $hashKind, $mtime, $parserId, $parserVer);
                    """;
                insCmd.Parameters.AddWithValue("$id", newFileId);
                insCmd.Parameters.AddWithValue("$snap", toSnapshot.Value);
                insCmd.Parameters.AddWithValue("$path", reader.GetString(1));
                insCmd.Parameters.AddWithValue("$norm", reader.GetString(2));
                insCmd.Parameters.AddWithValue("$size", reader.GetInt64(3));
                insCmd.Parameters.AddWithValue("$hash", reader.GetString(4));
                insCmd.Parameters.AddWithValue("$lang", reader.GetString(5));
                insCmd.Parameters.AddWithValue("$bin", reader.GetInt32(6));
                insCmd.Parameters.AddWithValue("$status", reader.GetString(7));
                insCmd.Parameters.AddWithValue("$hashKind", reader.GetString(8));
                insCmd.Parameters.AddWithValue("$mtime", reader.IsDBNull(9) ? DBNull.Value : reader.GetString(9));
                insCmd.Parameters.AddWithValue("$parserId", reader.IsDBNull(10) ? DBNull.Value : reader.GetString(10));
                insCmd.Parameters.AddWithValue("$parserVer", reader.IsDBNull(11) ? DBNull.Value : reader.GetString(11));
                await insCmd.ExecuteNonQueryAsync();
            }
        }

        // Phase 2: Clone symbols with new IDs and mapped file_id
        using (var readCmd = conn.CreateCommand())
        {
            readCmd.Transaction = (SqliteTransaction)tx;
            readCmd.CommandText = """
                SELECT id, file_id, name, kind, start_line, end_line, modifier, confidence
                FROM file_symbols WHERE snapshot_id = $from;
                """;
            readCmd.Parameters.AddWithValue("$from", fromSnapshot.Value);
            await using var reader = await readCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var oldFileId = reader.GetString(1);
                if (!fileIdMap.TryGetValue(oldFileId, out var newFileId))
                    continue;

                using var insCmd = conn.CreateCommand();
                insCmd.Transaction = (SqliteTransaction)tx;
                insCmd.CommandText = """
                    INSERT INTO file_symbols (id, file_id, snapshot_id, name, kind, start_line, end_line, modifier, confidence)
                    VALUES ($id, $fid, $snap, $name, $kind, $sl, $el, $mod, $conf);
                    """;
                insCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
                insCmd.Parameters.AddWithValue("$fid", newFileId);
                insCmd.Parameters.AddWithValue("$snap", toSnapshot.Value);
                insCmd.Parameters.AddWithValue("$name", reader.GetString(2));
                insCmd.Parameters.AddWithValue("$kind", reader.GetString(3));
                insCmd.Parameters.AddWithValue("$sl", reader.GetInt32(4));
                insCmd.Parameters.AddWithValue("$el", reader.GetInt32(5));
                insCmd.Parameters.AddWithValue("$mod", reader.IsDBNull(6) ? DBNull.Value : reader.GetString(6));
                insCmd.Parameters.AddWithValue("$conf", reader.GetString(7));
                await insCmd.ExecuteNonQueryAsync();
            }
        }

        // Phase 3: Clone imports with new IDs and mapped file_id
        using (var readCmd = conn.CreateCommand())
        {
            readCmd.Transaction = (SqliteTransaction)tx;
            readCmd.CommandText = """
                SELECT id, file_id, module, imported_name, line
                FROM file_imports WHERE snapshot_id = $from;
                """;
            readCmd.Parameters.AddWithValue("$from", fromSnapshot.Value);
            await using var reader = await readCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var oldFileId = reader.GetString(1);
                if (!fileIdMap.TryGetValue(oldFileId, out var newFileId))
                    continue;

                using var insCmd = conn.CreateCommand();
                insCmd.Transaction = (SqliteTransaction)tx;
                insCmd.CommandText = """
                    INSERT INTO file_imports (id, file_id, snapshot_id, module, imported_name, line)
                    VALUES ($id, $fid, $snap, $mod, $name, $line);
                    """;
                insCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
                insCmd.Parameters.AddWithValue("$fid", newFileId);
                insCmd.Parameters.AddWithValue("$snap", toSnapshot.Value);
                insCmd.Parameters.AddWithValue("$mod", reader.GetString(2));
                insCmd.Parameters.AddWithValue("$name", reader.IsDBNull(3) ? DBNull.Value : reader.GetString(3));
                insCmd.Parameters.AddWithValue("$line", reader.GetInt32(4));
                await insCmd.ExecuteNonQueryAsync();
            }
        }

        // Phase 4: Clone relations with new IDs and mapped file_id
        using (var readCmd = conn.CreateCommand())
        {
            readCmd.Transaction = (SqliteTransaction)tx;
            readCmd.CommandText = """
                SELECT id, file_id, source_symbol, target_symbol, relation_type, confidence, line, source
                FROM file_relations WHERE snapshot_id = $from;
                """;
            readCmd.Parameters.AddWithValue("$from", fromSnapshot.Value);
            await using var reader = await readCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var oldFileId = reader.GetString(1);
                if (!fileIdMap.TryGetValue(oldFileId, out var newFileId))
                    continue;

                using var insCmd = conn.CreateCommand();
                insCmd.Transaction = (SqliteTransaction)tx;
                insCmd.CommandText = """
                    INSERT INTO file_relations (id, file_id, snapshot_id, source_symbol, target_symbol, relation_type, confidence, line, source)
                    VALUES ($id, $fid, $snap, $src, $tgt, $rt, $conf, $line, $source);
                    """;
                insCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
                insCmd.Parameters.AddWithValue("$fid", newFileId);
                insCmd.Parameters.AddWithValue("$snap", toSnapshot.Value);
                insCmd.Parameters.AddWithValue("$src", reader.GetString(2));
                insCmd.Parameters.AddWithValue("$tgt", reader.GetString(3));
                insCmd.Parameters.AddWithValue("$rt", reader.GetString(4));
                insCmd.Parameters.AddWithValue("$conf", reader.GetString(5));
                insCmd.Parameters.AddWithValue("$line", reader.IsDBNull(6) ? DBNull.Value : reader.GetInt32(6));
                insCmd.Parameters.AddWithValue("$source", reader.IsDBNull(7) ? DBNull.Value : reader.GetString(7));
                await insCmd.ExecuteNonQueryAsync();
            }
        }

        await tx.CommitAsync();

        // Clone FTS entries (separate — FTS5 virtual table, no transaction)
        using var ftsConn = factory.CreateOpenConnection();
        using var ftsCmd = ftsConn.CreateCommand();
        ftsCmd.CommandText = """
            INSERT INTO file_contents_fts (path, normalized_path, content, language, content_hash, snapshot_id)
            SELECT path, normalized_path, content, language, content_hash, $to
            FROM file_contents_fts WHERE snapshot_id = $from;
            """;
        ftsCmd.Parameters.AddWithValue("$from", fromSnapshot.Value);
        ftsCmd.Parameters.AddWithValue("$to", toSnapshot.Value);
        await ftsCmd.ExecuteNonQueryAsync();
    }
}
