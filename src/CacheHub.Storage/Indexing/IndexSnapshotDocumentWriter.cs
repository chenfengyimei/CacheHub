using CacheHub.Core.Identifiers;
using CacheHub.Core.Parsing;
using CacheHub.Storage.Database;
using Microsoft.Data.Sqlite;

namespace CacheHub.Storage.Indexing;

/// <summary>
/// Parsed source document ready for atomic persistence in one index snapshot.
/// </summary>
public sealed record IndexSnapshotDocument(
    string Path,
    long Size,
    string ContentHash,
    string Language,
    bool IsBinary,
    string ParserId,
    string ParserVersion,
    ParseResult ParseResult);

/// <summary>
/// Shared persistence stage for CLI and Desktop index builds. Files and all
/// parser-derived rows are committed together, so an incomplete parse cannot
/// become visible within an otherwise active snapshot.
/// </summary>
public sealed class IndexSnapshotDocumentWriter(SqliteConnectionFactory factory)
{
    public async Task PersistAsync(IndexSnapshotId snapshotId, IReadOnlyList<IndexSnapshotDocument> documents, CancellationToken ct = default)
    {
        await using var connection = factory.CreateOpenConnection();
        await using var transaction = await connection.BeginTransactionAsync(ct);
        try
        {
            foreach (var document in documents)
                await PersistDocumentAsync(connection, (SqliteTransaction)transaction, snapshotId, document, ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private static async Task PersistDocumentAsync(SqliteConnection connection, SqliteTransaction transaction,
        IndexSnapshotId snapshotId, IndexSnapshotDocument document, CancellationToken ct)
    {
        var fileId = Guid.NewGuid().ToString("N");
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO files (id, snapshot_id, path, normalized_path, size, content_hash, language, is_binary, status, hash_kind, parser_id, parser_version)
                VALUES ($id, $snap, $path, $norm, $size, $hash, $lang, $bin, 'Indexed', $hashKind, $parserId, $parserVer);
                """;
            command.Parameters.AddWithValue("$id", fileId);
            command.Parameters.AddWithValue("$snap", snapshotId.Value);
            command.Parameters.AddWithValue("$path", document.Path);
            command.Parameters.AddWithValue("$norm", document.Path);
            command.Parameters.AddWithValue("$size", document.Size);
            command.Parameters.AddWithValue("$hash", document.ContentHash);
            command.Parameters.AddWithValue("$lang", document.Language);
            command.Parameters.AddWithValue("$bin", document.IsBinary ? 1 : 0);
            command.Parameters.AddWithValue("$hashKind", document.ContentHash.StartsWith("fp:", StringComparison.Ordinal) ? "fingerprint" : "full");
            command.Parameters.AddWithValue("$parserId", document.ParserId);
            command.Parameters.AddWithValue("$parserVer", document.ParserVersion);
            await command.ExecuteNonQueryAsync(ct);
        }

        foreach (var symbol in document.ParseResult.Symbols)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO file_symbols (id, file_id, snapshot_id, name, kind, start_line, end_line, modifier, confidence) VALUES ($id, $fid, $snap, $name, $kind, $sl, $el, $mod, $conf);";
            command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue("$fid", fileId);
            command.Parameters.AddWithValue("$snap", snapshotId.Value);
            command.Parameters.AddWithValue("$name", symbol.Name);
            command.Parameters.AddWithValue("$kind", symbol.Kind.ToString());
            command.Parameters.AddWithValue("$sl", symbol.StartLine);
            command.Parameters.AddWithValue("$el", symbol.EndLine);
            command.Parameters.AddWithValue("$mod", (object?)symbol.Modifier ?? DBNull.Value);
            command.Parameters.AddWithValue("$conf", "syntactic");
            await command.ExecuteNonQueryAsync(ct);
        }

        foreach (var import in document.ParseResult.Imports)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO file_imports (id, file_id, snapshot_id, module, imported_name, line) VALUES ($id, $fid, $snap, $mod, $name, $line);";
            command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue("$fid", fileId);
            command.Parameters.AddWithValue("$snap", snapshotId.Value);
            command.Parameters.AddWithValue("$mod", import.Module);
            command.Parameters.AddWithValue("$name", (object?)import.ImportedName ?? DBNull.Value);
            command.Parameters.AddWithValue("$line", import.Line);
            await command.ExecuteNonQueryAsync(ct);
        }

        foreach (var relation in document.ParseResult.Relations)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO file_relations (id, file_id, snapshot_id, source_symbol, target_symbol, relation_type, confidence, line, source) VALUES ($id, $fid, $snap, $src, $tgt, $rt, $conf, $line, $source);";
            command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue("$fid", fileId);
            command.Parameters.AddWithValue("$snap", snapshotId.Value);
            command.Parameters.AddWithValue("$src", string.IsNullOrEmpty(relation.SourceSymbol) ? relation.Relation : relation.SourceSymbol);
            command.Parameters.AddWithValue("$tgt", relation.TargetName);
            command.Parameters.AddWithValue("$rt", relation.RelationType.ToString());
            command.Parameters.AddWithValue("$conf", relation.Confidence.ToString(System.Globalization.CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$line", relation.Line > 0 ? relation.Line : DBNull.Value);
            command.Parameters.AddWithValue("$source", relation.Source);
            await command.ExecuteNonQueryAsync(ct);
        }
    }
}
