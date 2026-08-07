using CacheHub.Core.Identifiers;
using CacheHub.Core.Workspaces;
using CacheHub.Storage.Database;
using CacheHub.Storage.Search;

namespace CacheHub.Storage.Query;

/// <summary>
/// Index query service: centralizes all read queries against the index database.
/// CLI, Desktop, and Context Engine use this instead of writing SQL directly.
/// R4-W002: eliminates duplicated SQL across ContextCommands, IndexCommands, SearchCommands, and Desktop Program.cs.
/// </summary>
public interface IIndexQueryService
{
    /// <summary>Gets the active snapshot ID for a workspace, or null if none.</summary>
    Task<IndexSnapshotId?> GetActiveSnapshotIdAsync(string workspaceId, CancellationToken ct = default);

    /// <summary>Gets all indexed files for the active snapshot of a workspace.</summary>
    Task<IReadOnlyList<IndexedFileRecord>> GetIndexedFilesAsync(string workspaceId, CancellationToken ct = default);

    /// <summary>Gets all indexed files for a specific snapshot.</summary>
    Task<IReadOnlyList<IndexedFileRecord>> GetIndexedFilesBySnapshotAsync(IndexSnapshotId snapshotId, CancellationToken ct = default);

    /// <summary>Gets the content hash for a specific file in a snapshot.</summary>
    Task<string?> GetFileHashAsync(IndexSnapshotId snapshotId, string normalizedPath, CancellationToken ct = default);

    /// <summary>Searches FTS5 for a query within a snapshot.</summary>
    Task<IReadOnlyList<FtsSearchResult>> SearchFtsAsync(IndexSnapshotId snapshotId, string query, int limit = 50, CancellationToken ct = default);

    /// <summary>Searches file_symbols for a symbol name (exact match first, then LIKE).</summary>
    Task<IReadOnlyList<SymbolSearchResult>> SearchSymbolsAsync(IndexSnapshotId snapshotId, string symbolName, CancellationToken ct = default);

    /// <summary>Gets symbols for a specific file in a snapshot.</summary>
    Task<IReadOnlyList<SymbolRecord>> GetFileSymbolsAsync(IndexSnapshotId snapshotId, string normalizedPath, CancellationToken ct = default);

    /// <summary>Gets imports for a specific file in a snapshot.</summary>
    Task<IReadOnlyList<ImportRecord>> GetFileImportsAsync(IndexSnapshotId snapshotId, string normalizedPath, CancellationToken ct = default);

    /// <summary>Gets relations for a specific file in a snapshot.</summary>
    Task<IReadOnlyList<RelationRecord>> GetFileRelationsAsync(IndexSnapshotId snapshotId, string normalizedPath, CancellationToken ct = default);

    /// <summary>Gets files that import a specific symbol (for import relation recall).</summary>
    Task<IReadOnlyList<string>> GetFilesByImportedSymbolAsync(IndexSnapshotId snapshotId, string symbolName, CancellationToken ct = default);

    /// <summary>Gets the snapshot status and file count.</summary>
    Task<SnapshotStatusRecord?> GetSnapshotStatusAsync(IndexSnapshotId snapshotId, CancellationToken ct = default);
}

/// <summary>
/// A file record from the index.
/// </summary>
public sealed record IndexedFileRecord
{
    public required string NormalizedPath { get; init; }
    public required long Size { get; init; }
    public required string Language { get; init; }
    public string? ContentHash { get; init; }
    public string? Mtime { get; init; }
    public string? HashKind { get; init; }
    public string? ParserId { get; init; }
    public string? ParserVersion { get; init; }
}

/// <summary>
/// A symbol search result.
/// </summary>
public sealed record SymbolSearchResult
{
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required string NormalizedPath { get; init; }
    public required bool ExactMatch { get; init; }
}

/// <summary>
/// A symbol record for a file.
/// </summary>
public sealed record SymbolRecord
{
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public string? Modifier { get; init; }
}

/// <summary>
/// An import record for a file.
/// </summary>
public sealed record ImportRecord
{
    public required string Module { get; init; }
    public string? ImportedName { get; init; }
    public required int Line { get; init; }
}

/// <summary>
/// A relation record for a file.
/// </summary>
public sealed record RelationRecord
{
    public required string RelationType { get; init; }
    public required string Relation { get; init; }
    public required string TargetName { get; init; }
    public required double Confidence { get; init; }
    public required string Source { get; init; }
}

/// <summary>
/// FTS search result.
/// </summary>
public sealed record FtsSearchResult
{
    public required string Path { get; init; }
    public required string Language { get; init; }
    public required string Snippet { get; init; }
}

/// <summary>
/// Snapshot status record.
/// </summary>
public sealed record SnapshotStatusRecord
{
    public required string Id { get; init; }
    public required string Status { get; init; }
    public required int FileCount { get; init; }
}

/// <summary>
/// SQLite-backed implementation of IIndexQueryService.
/// </summary>
public sealed class SqliteIndexQueryService : IIndexQueryService
{
    private readonly SqliteConnectionFactory _factory;

    public SqliteIndexQueryService(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<IndexSnapshotId?> GetActiveSnapshotIdAsync(string workspaceId, CancellationToken ct = default)
    {
        await using var conn = _factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM index_snapshots WHERE workspace_id = $ws AND status = 'Active' LIMIT 1;";
        cmd.Parameters.AddWithValue("$ws", workspaceId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is string id ? IndexSnapshotId.Parse(id) : null;
    }

    public async Task<IReadOnlyList<IndexedFileRecord>> GetIndexedFilesAsync(string workspaceId, CancellationToken ct = default)
    {
        var snapshotId = await GetActiveSnapshotIdAsync(workspaceId, ct);
        if (snapshotId is null) return [];
        return await GetIndexedFilesBySnapshotAsync(snapshotId, ct);
    }

    public async Task<IReadOnlyList<IndexedFileRecord>> GetIndexedFilesBySnapshotAsync(IndexSnapshotId snapshotId, CancellationToken ct = default)
    {
        await using var conn = _factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT normalized_path, size, language, content_hash, mtime, hash_kind, parser_id, parser_version
            FROM files
            WHERE snapshot_id = $snap;
            """;
        cmd.Parameters.AddWithValue("$snap", snapshotId.Value);

        var results = new List<IndexedFileRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new IndexedFileRecord
            {
                NormalizedPath = reader.GetString(0),
                Size = reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
                Language = reader.IsDBNull(2) ? "unknown" : reader.GetString(2),
                ContentHash = reader.IsDBNull(3) ? null : reader.GetString(3),
                Mtime = reader.IsDBNull(4) ? null : reader.GetString(4),
                HashKind = reader.IsDBNull(5) ? null : reader.GetString(5),
                ParserId = reader.IsDBNull(6) ? null : reader.GetString(6),
                ParserVersion = reader.IsDBNull(7) ? null : reader.GetString(7),
            });
        }
        return results;
    }

    public async Task<string?> GetFileHashAsync(IndexSnapshotId snapshotId, string normalizedPath, CancellationToken ct = default)
    {
        await using var conn = _factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT content_hash FROM files WHERE snapshot_id = $snap AND normalized_path = $path LIMIT 1;";
        cmd.Parameters.AddWithValue("$snap", snapshotId.Value);
        cmd.Parameters.AddWithValue("$path", normalizedPath);
        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is string hash && !string.IsNullOrEmpty(hash) && hash != "pending")
            return hash;
        return null;
    }

    public async Task<IReadOnlyList<FtsSearchResult>> SearchFtsAsync(IndexSnapshotId snapshotId, string query, int limit = 50, CancellationToken ct = default)
    {
        var fts = new Fts5Index(_factory);
        var results = await fts.SearchAsync(snapshotId, query, limit, ct);
        return results.Select(r => new FtsSearchResult
        {
            Path = r.Path,
            Language = r.Language,
            Snippet = r.Snippet,
        }).ToList();
    }

    public async Task<IReadOnlyList<SymbolSearchResult>> SearchSymbolsAsync(IndexSnapshotId snapshotId, string symbolName, CancellationToken ct = default)
    {
        await using var conn = _factory.CreateOpenConnection();

        // First try exact match
        var exactResults = await QuerySymbols(conn, snapshotId, symbolName, true, ct);
        if (exactResults.Count > 0) return exactResults;

        // Fall back to LIKE
        return await QuerySymbols(conn, snapshotId, symbolName, false, ct);
    }

    private static async Task<List<SymbolSearchResult>> QuerySymbols(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        IndexSnapshotId snapshotId,
        string symbolName,
        bool exact,
        CancellationToken ct)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = exact
            ? """
                SELECT s.name, s.kind, s.start_line, s.end_line, f.normalized_path
                FROM file_symbols s
                INNER JOIN files f ON s.file_id = f.id
                WHERE s.snapshot_id = $snap AND s.name = $name
                ORDER BY s.start_line;
                """
            : """
                SELECT s.name, s.kind, s.start_line, s.end_line, f.normalized_path
                FROM file_symbols s
                INNER JOIN files f ON s.file_id = f.id
                WHERE s.snapshot_id = $snap AND s.name LIKE $name
                ORDER BY s.start_line;
                """;
        cmd.Parameters.AddWithValue("$snap", snapshotId.Value);
        cmd.Parameters.AddWithValue("$name", exact ? symbolName : $"%{symbolName}%");

        var results = new List<SymbolSearchResult>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new SymbolSearchResult
            {
                Name = reader.GetString(0),
                Kind = reader.GetString(1),
                StartLine = reader.GetInt32(2),
                EndLine = reader.GetInt32(3),
                NormalizedPath = reader.GetString(4),
                ExactMatch = exact,
            });
        }
        return results;
    }

    public async Task<IReadOnlyList<SymbolRecord>> GetFileSymbolsAsync(IndexSnapshotId snapshotId, string normalizedPath, CancellationToken ct = default)
    {
        await using var conn = _factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT s.name, s.kind, s.start_line, s.end_line, s.modifier
            FROM file_symbols s
            INNER JOIN files f ON s.file_id = f.id
            WHERE s.snapshot_id = $snap AND f.normalized_path = $path
            ORDER BY s.start_line;
            """;
        cmd.Parameters.AddWithValue("$snap", snapshotId.Value);
        cmd.Parameters.AddWithValue("$path", normalizedPath);

        var results = new List<SymbolRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new SymbolRecord
            {
                Name = reader.GetString(0),
                Kind = reader.GetString(1),
                StartLine = reader.GetInt32(2),
                EndLine = reader.GetInt32(3),
                Modifier = reader.IsDBNull(4) ? null : reader.GetString(4),
            });
        }
        return results;
    }

    public async Task<IReadOnlyList<ImportRecord>> GetFileImportsAsync(IndexSnapshotId snapshotId, string normalizedPath, CancellationToken ct = default)
    {
        await using var conn = _factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT i.module, i.imported_name, i.line
            FROM file_imports i
            INNER JOIN files f ON i.file_id = f.id
            WHERE i.snapshot_id = $snap AND f.normalized_path = $path
            ORDER BY i.line;
            """;
        cmd.Parameters.AddWithValue("$snap", snapshotId.Value);
        cmd.Parameters.AddWithValue("$path", normalizedPath);

        var results = new List<ImportRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new ImportRecord
            {
                Module = reader.GetString(0),
                ImportedName = reader.IsDBNull(1) ? null : reader.GetString(1),
                Line = reader.GetInt32(2),
            });
        }
        return results;
    }

    public async Task<IReadOnlyList<RelationRecord>> GetFileRelationsAsync(IndexSnapshotId snapshotId, string normalizedPath, CancellationToken ct = default)
    {
        await using var conn = _factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT r.relation_type, r.source_symbol, r.target_symbol, r.confidence, r.line
            FROM file_relations r
            INNER JOIN files f ON r.file_id = f.id
            WHERE r.snapshot_id = $snap AND f.normalized_path = $path
            ORDER BY r.line;
            """;
        cmd.Parameters.AddWithValue("$snap", snapshotId.Value);
        cmd.Parameters.AddWithValue("$path", normalizedPath);

        var results = new List<RelationRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new RelationRecord
            {
                RelationType = reader.GetString(0),
                Relation = reader.GetString(1),
                TargetName = reader.GetString(2),
                Confidence = reader.IsDBNull(3) ? 0 : double.TryParse(reader.GetString(3), out var c) ? c : 0,
                Source = reader.IsDBNull(4) ? "" : reader.GetInt32(4).ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
        }
        return results;
    }

    public async Task<IReadOnlyList<string>> GetFilesByImportedSymbolAsync(IndexSnapshotId snapshotId, string symbolName, CancellationToken ct = default)
    {
        await using var conn = _factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT f.normalized_path
            FROM file_imports i
            INNER JOIN files f ON i.file_id = f.id
            WHERE i.snapshot_id = $snap AND (i.imported_name = $name OR i.module LIKE '%' || $name || '%')
            ORDER BY f.normalized_path;
            """;
        cmd.Parameters.AddWithValue("$snap", snapshotId.Value);
        cmd.Parameters.AddWithValue("$name", symbolName);

        var results = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(reader.GetString(0));
        }
        return results;
    }

    public async Task<SnapshotStatusRecord?> GetSnapshotStatusAsync(IndexSnapshotId snapshotId, CancellationToken ct = default)
    {
        await using var conn = _factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, status, file_count FROM index_snapshots WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", snapshotId.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        return new SnapshotStatusRecord
        {
            Id = reader.GetString(0),
            Status = reader.GetString(1),
            FileCount = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
        };
    }
}
