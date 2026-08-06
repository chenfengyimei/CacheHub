using CacheHub.Core.Identifiers;
using CacheHub.Storage.Database;
using Microsoft.Data.Sqlite;

namespace CacheHub.Storage.Search;

/// <summary>
/// Manages FTS5 full-text search index for file contents.
/// Binds all entries to an IndexSnapshotId for version-aware retrieval.
/// </summary>
public sealed class Fts5Index(SqliteConnectionFactory factory)
{
    /// <summary>
    /// Indexes a file's content into FTS5.
    /// </summary>
    public async Task IndexFileAsync(
        IndexSnapshotId snapshotId,
        string path,
        string normalizedPath,
        string content,
        string language,
        string contentHash,
        CancellationToken ct = default)
    {
        await using var connection = factory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO file_contents_fts (path, normalized_path, content, language, content_hash, snapshot_id)
            VALUES ($path, $normPath, $content, $lang, $hash, $snapshot);
            """;
        cmd.Parameters.AddWithValue("$path", path);
        cmd.Parameters.AddWithValue("$normPath", normalizedPath);
        cmd.Parameters.AddWithValue("$content", content);
        cmd.Parameters.AddWithValue("$lang", language);
        cmd.Parameters.AddWithValue("$hash", contentHash);
        cmd.Parameters.AddWithValue("$snapshot", snapshotId.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Indexes a file chunk into FTS5 and file_chunks table.
    /// </summary>
    public async Task IndexChunkAsync(
        IndexSnapshotId snapshotId,
        string filePath,
        int startLine,
        int endLine,
        string content,
        CancellationToken ct = default)
    {
        await using var connection = factory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO file_chunks (id, snapshot_id, file_path, start_line, end_line, content)
            VALUES ($id, $snapshot, $path, $start, $end, $content);
            """;
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("$snapshot", snapshotId.Value);
        cmd.Parameters.AddWithValue("$path", filePath);
        cmd.Parameters.AddWithValue("$start", startLine);
        cmd.Parameters.AddWithValue("$end", endLine);
        cmd.Parameters.AddWithValue("$content", content);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Searches file contents using FTS5 full-text search.
    /// </summary>
    public async Task<IReadOnlyList<FtsSearchResult>> SearchAsync(
        IndexSnapshotId snapshotId,
        string query,
        int limit = 50,
        CancellationToken ct = default)
    {
        await using var connection = factory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT path, language, snippet(file_contents_fts, 2, '<mark>', '</mark>', '...', 10) as snippet
            FROM file_contents_fts
            WHERE file_contents_fts MATCH $query AND snapshot_id = $snapshot
            ORDER BY rank
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$query", query);
        cmd.Parameters.AddWithValue("$snapshot", snapshotId.Value);
        cmd.Parameters.AddWithValue("$limit", limit);

        var results = new List<FtsSearchResult>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new FtsSearchResult(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2)));
        }
        return results;
    }

    /// <summary>
    /// Removes all FTS5 entries for a specific snapshot.
    /// </summary>
    public async Task ClearSnapshotAsync(IndexSnapshotId snapshotId, CancellationToken ct = default)
    {
        await using var connection = factory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM file_contents_fts WHERE snapshot_id = $snapshot;";
        cmd.Parameters.AddWithValue("$snapshot", snapshotId.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

/// <summary>
/// A single FTS5 search result.
/// </summary>
public sealed record FtsSearchResult(string Path, string Language, string Snippet);
