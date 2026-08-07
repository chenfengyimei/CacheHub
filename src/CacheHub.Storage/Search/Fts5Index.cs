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
    /// Query is compiled via FtsQueryCompiler for safe escaping and prefix matching.
    /// Returns BM25 rank score and best hit line number for each result.
    /// </summary>
    public async Task<IReadOnlyList<FtsSearchResult>> SearchAsync(
        IndexSnapshotId snapshotId,
        string query,
        int limit = 50,
        CancellationToken ct = default)
    {
        var compiledQuery = FtsQueryCompiler.Compile(query);
        await using var connection = factory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT path, language,
                   snippet(file_contents_fts, 2, '<mark>', '</mark>', '...', 10) as snippet,
                   rank, content
            FROM file_contents_fts
            WHERE file_contents_fts MATCH $query AND snapshot_id = $snapshot
            ORDER BY rank
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$query", compiledQuery);
        cmd.Parameters.AddWithValue("$snapshot", snapshotId.Value);
        cmd.Parameters.AddWithValue("$limit", limit);

        var results = new List<FtsSearchResult>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var path = reader.GetString(0);
            var language = reader.GetString(1);
            var snippet = reader.GetString(2);
            var rankScore = reader.IsDBNull(3) ? 0 : reader.GetDouble(3);
            var content = reader.IsDBNull(4) ? "" : reader.GetString(4);

            // Prefer the actual FTS5 snippet-highlighted match position over the first keyword occurrence.
            var hitLine = FindHitLine(content, snippet, query);

            results.Add(new FtsSearchResult(path, language, snippet, rankScore, hitLine));
        }
        return results;
    }

    /// <summary>
    /// Finds the 1-based line number that best matches the FTS5 snippet's highlighted region.
    /// Strategy:
    ///   1. If the snippet contains highlighted terms, locate the line holding the longest highlighted
    ///      phrase first — this is closer to the actual BM25-chosen context.
    ///   2. Fallback to the first occurrence of any query keyword (original behavior).
    /// </summary>
    private static int? FindHitLine(string content, string snippet, string query)
    {
        if (string.IsNullOrEmpty(content)) return null;

        var lines = content.Split('\n');
        if (lines.Length == 0) return null;

        // 1. Try to locate the snippet's highlighted region in the content.
        var highlighted = ExtractHighlightedTerms(snippet);
        if (highlighted.Count > 0)
        {
            // Strategy A: If the snippet has surrounding context, try to find the full
            // snippet fragment (with <mark> tags stripped) in the content.
            var snippetText = StripMarkTags(snippet).Trim();
            if (snippetText.Length > 10)
            {
                var idx = content.IndexOf(snippetText, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                    return LineFromOffset(content, idx);
            }

            // Strategy B: Find the line containing the most highlighted terms.
            // Prefer non-comment lines (lines not starting with // or #) when there's a tie.
            var best = FindLineWithLongest(lines, highlighted);
            if (best is not null) return best.Value + 1;
        }

        // 2. Fallback: first occurrence of any query keyword.
        if (string.IsNullOrEmpty(query)) return null;
        var keywords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var keyword in keywords)
        {
            var cleanKeyword = keyword.Trim('"', '*', '(', ')');
            if (string.IsNullOrEmpty(cleanKeyword)) continue;
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(cleanKeyword, StringComparison.OrdinalIgnoreCase))
                    return i + 1;
            }
        }

        return null;
    }

    /// <summary>
    /// Strips &lt;mark&gt; and &lt;/mark&gt; tags from a string.
    /// </summary>
    private static string StripMarkTags(string snippet)
    {
        if (string.IsNullOrEmpty(snippet)) return snippet;
        return snippet
            .Replace("<mark>", "", StringComparison.Ordinal)
            .Replace("</mark>", "", StringComparison.Ordinal);
    }

    /// <summary>
    /// Returns the 1-based line number for a character offset in the content.
    /// </summary>
    private static int LineFromOffset(string content, int offset)
    {
        var line = 1;
        for (var i = 0; i < offset && i < content.Length; i++)
        {
            if (content[i] == '\n') line++;
        }
        return line;
    }

    /// <summary>
    /// Extracts highlighted terms (inside &lt;mark&gt; tags) from the FTS5 snippet.
    /// </summary>
    private static List<string> ExtractHighlightedTerms(string snippet)
    {
        var terms = new List<string>();
        if (string.IsNullOrEmpty(snippet)) return terms;

        const string startTag = "<mark>";
        const string endTag = "</mark>";
        var index = 0;
        while (index < snippet.Length)
        {
            var start = snippet.IndexOf(startTag, index, StringComparison.Ordinal);
            if (start < 0) break;
            start += startTag.Length;
            var end = snippet.IndexOf(endTag, start, StringComparison.Ordinal);
            if (end < 0) break;
            var term = snippet[start..end].Trim();
            if (term.Length > 0) terms.Add(term);
            index = end + endTag.Length;
        }
        return terms;
    }

    /// <summary>
    /// Returns the index (0-based) of the line containing the most highlighted terms.
    /// Prefers non-comment lines when counts tie (comments are less likely to be the
    /// BM25-chosen context), then breaks ties by total matched term length.
    /// </summary>
    private static int? FindLineWithLongest(string[] lines, IReadOnlyList<string> highlighted)
    {
        var bestIndex = -1;
        var bestMatches = -1;
        var bestLength = -1;
        var bestIsComment = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var lineText = lines[i];
            var matchCount = 0;
            var matchLength = 0;
            foreach (var term in highlighted)
            {
                if (lineText.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    matchCount++;
                    matchLength += term.Length;
                }
            }

            if (matchCount == 0) continue;

            var isComment = IsCommentLine(lineText);
            var better = false;
            if (matchCount > bestMatches)
                better = true;
            else if (matchCount == bestMatches)
            {
                // Prefer non-comment lines over comment lines.
                if (bestIsComment && !isComment)
                    better = true;
                else if (bestIsComment == isComment && matchLength > bestLength)
                    better = true;
            }

            if (better)
            {
                bestMatches = matchCount;
                bestLength = matchLength;
                bestIndex = i;
                bestIsComment = isComment;
            }
        }

        return bestIndex >= 0 ? bestIndex : null;
    }

    private static bool IsCommentLine(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
            trimmed.StartsWith('#') ||
            trimmed.StartsWith("/*", StringComparison.Ordinal))
            return true;
        return false;
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

    /// <summary>
    /// R6-W001: Deletes a single file's FTS5 entry within a snapshot.
    /// Allows incremental updates without clearing the entire snapshot.
    /// </summary>
    public async Task DeleteFileAsync(
        IndexSnapshotId snapshotId,
        string normalizedPath,
        CancellationToken ct = default)
    {
        await using var connection = factory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            DELETE FROM file_contents_fts
            WHERE snapshot_id = $snapshot AND normalized_path = $normPath;
            """;
        cmd.Parameters.AddWithValue("$snapshot", snapshotId.Value);
        cmd.Parameters.AddWithValue("$normPath", normalizedPath);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// R6-W001: Upserts (delete + insert) a single file's FTS5 entry within a snapshot.
    /// If an entry for the same snapshot+path exists, it is replaced.
    /// </summary>
    public async Task UpsertFileAsync(
        IndexSnapshotId snapshotId,
        string path,
        string normalizedPath,
        string content,
        string language,
        string contentHash,
        CancellationToken ct = default)
    {
        await using var connection = factory.CreateOpenConnection();
        await using var transaction = await connection.BeginTransactionAsync(ct);

        // Delete existing entry for this snapshot+path
        using (var delCmd = connection.CreateCommand())
        {
            delCmd.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
            delCmd.CommandText = """
                DELETE FROM file_contents_fts
                WHERE snapshot_id = $snapshot AND normalized_path = $normPath;
                """;
            delCmd.Parameters.AddWithValue("$snapshot", snapshotId.Value);
            delCmd.Parameters.AddWithValue("$normPath", normalizedPath);
            await delCmd.ExecuteNonQueryAsync(ct);
        }

        // Insert new entry
        using (var insCmd = connection.CreateCommand())
        {
            insCmd.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
            insCmd.CommandText = """
                INSERT INTO file_contents_fts (path, normalized_path, content, language, content_hash, snapshot_id)
                VALUES ($path, $normPath, $content, $lang, $hash, $snapshot);
                """;
            insCmd.Parameters.AddWithValue("$path", path);
            insCmd.Parameters.AddWithValue("$normPath", normalizedPath);
            insCmd.Parameters.AddWithValue("$content", content);
            insCmd.Parameters.AddWithValue("$lang", language);
            insCmd.Parameters.AddWithValue("$hash", contentHash);
            insCmd.Parameters.AddWithValue("$snapshot", snapshotId.Value);
            await insCmd.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }

    /// <summary>
    /// R6-W001: Checks whether a file entry exists in FTS5 for a given snapshot+path.
    /// </summary>
    public async Task<bool> FileExistsAsync(
        IndexSnapshotId snapshotId,
        string normalizedPath,
        CancellationToken ct = default)
    {
        await using var connection = factory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM file_contents_fts
            WHERE snapshot_id = $snapshot AND normalized_path = $normPath;
            """;
        cmd.Parameters.AddWithValue("$snapshot", snapshotId.Value);
        cmd.Parameters.AddWithValue("$normPath", normalizedPath);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is long count && count > 0;
    }
}

/// <summary>
/// A single FTS5 search result.
/// </summary>
public sealed record FtsSearchResult(string Path, string Language, string Snippet, double RankScore = 0, int? HitLine = null);
