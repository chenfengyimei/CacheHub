using CacheHub.Core.Caching;
using CacheHub.Storage.Database;

namespace CacheHub.Storage.Caching;

/// <summary>
/// SQLite-backed persistent cache store.
/// R7-W001: stores cache entries in SQLite, large blobs in file store.
/// Survives process restart. Supports dependency-based invalidation and TTL.
/// </summary>
public sealed class SqliteCacheStore : ICacheStore
{
    private readonly SqliteConnectionFactory _factory;
    private readonly string? _blobDirectory;

    public SqliteCacheStore(SqliteConnectionFactory factory, string? blobDirectory = null)
    {
        _factory = factory;
        _blobDirectory = blobDirectory;
        if (_blobDirectory is not null)
            Directory.CreateDirectory(_blobDirectory);
    }

    public CacheEntry? TryGet(string key, CacheType type, string? dependencyHash = null)
    {
        CacheEntry? result = null;
        bool shouldInvalidate = false;

        using (var conn = _factory.CreateOpenConnection())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT key, cache_type, version, created_at, size_bytes,
                       dependency_hash, ttl_seconds, producer_version
                FROM cache_entries
                WHERE key = $key AND cache_type = $type;
                """;
            cmd.Parameters.AddWithValue("$key", key);
            cmd.Parameters.AddWithValue("$type", type.ToString());

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                RecordMiss(type);
                return null;
            }

            var createdAt = DateTimeOffset.Parse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind);
            var depHash = reader.IsDBNull(5) ? null : reader.GetString(5);
            var ttlSeconds = reader.IsDBNull(6) ? null : (int?)reader.GetInt32(6);

            // Check dependency hash
            if (dependencyHash is not null && depHash is not null &&
                !depHash.Equals(dependencyHash, StringComparison.Ordinal))
            {
                shouldInvalidate = true;
            }
            // Check TTL
            else if (ttlSeconds is not null &&
                DateTimeOffset.UtcNow.Subtract(createdAt).TotalSeconds > ttlSeconds.Value)
            {
                shouldInvalidate = true;
            }

            if (!shouldInvalidate)
            {
                result = new CacheEntry
                {
                    Key = reader.GetString(0),
                    Type = type,
                    Version = reader.GetString(2),
                    CreatedAt = createdAt,
                    SizeBytes = reader.GetInt64(4),
                    DependencyHash = depHash,
                    TtlSeconds = ttlSeconds,
                    ProducerVersion = reader.IsDBNull(7) ? null : reader.GetString(7),
                    IsHit = true,
                };
            }
        }

        if (shouldInvalidate)
        {
            Invalidate(key, type);
            return null;
        }

        if (result is not null)
            IncrementStat(type, "hits");
        return result;
    }

    public void Put(CacheEntry entry, byte[]? blob = null)
    {
        string? blobPath = null;
        string? contentHash = null;

        if (blob is not null && _blobDirectory is not null && blob.Length > 0)
        {
            contentHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(blob)).ToLowerInvariant();
            blobPath = Path.Combine(_blobDirectory, $"{contentHash}.blob");
            if (!File.Exists(blobPath))
                File.WriteAllBytes(blobPath, blob);
        }

        using var conn = _factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO cache_entries
                (key, cache_type, version, created_at, size_bytes,
                 dependency_hash, ttl_seconds, producer_version, blob_path, content_hash)
            VALUES ($key, $type, $ver, $created, $size, $dep, $ttl, $prod, $blob, $hash);
            """;
        cmd.Parameters.AddWithValue("$key", entry.Key);
        cmd.Parameters.AddWithValue("$type", entry.Type.ToString());
        cmd.Parameters.AddWithValue("$ver", entry.Version);
        cmd.Parameters.AddWithValue("$created", entry.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$size", entry.SizeBytes);
        cmd.Parameters.AddWithValue("$dep", (object?)entry.DependencyHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ttl", (object?)entry.TtlSeconds ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$prod", (object?)entry.ProducerVersion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$blob", (object?)blobPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$hash", (object?)contentHash ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public byte[]? GetBlob(string key)
    {
        using var conn = _factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT blob_path FROM cache_entries WHERE key = $key LIMIT 1;";
        cmd.Parameters.AddWithValue("$key", key);

        var result = cmd.ExecuteScalar();
        if (result is string path && !string.IsNullOrEmpty(path) && File.Exists(path))
            return File.ReadAllBytes(path);
        return null;
    }

    public void InvalidateByDependency(string dependencyHash)
    {
        using var conn = _factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM cache_entries WHERE dependency_hash = $dep;";
        cmd.Parameters.AddWithValue("$dep", dependencyHash);
        cmd.ExecuteNonQuery();
    }

    public void InvalidateType(CacheType type)
    {
        using var conn = _factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM cache_entries WHERE cache_type = $type;";
        cmd.Parameters.AddWithValue("$type", type.ToString());
        cmd.ExecuteNonQuery();
    }

    public void Invalidate(string key, CacheType type)
    {
        using var conn = _factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM cache_entries WHERE key = $key AND cache_type = $type;";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$type", type.ToString());
        cmd.ExecuteNonQuery();
    }

    public CacheStats GetStats(CacheType type)
    {
        using var conn = _factory.CreateOpenConnection();

        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*), COALESCE(SUM(size_bytes), 0) FROM cache_entries WHERE cache_type = $type;";
        countCmd.Parameters.AddWithValue("$type", type.ToString());
        using var reader = countCmd.ExecuteReader();
        int totalEntries = 0;
        long totalBytes = 0;
        if (reader.Read())
        {
            totalEntries = reader.GetInt32(0);
            totalBytes = reader.GetInt64(1);
        }

        using var statCmd = conn.CreateCommand();
        statCmd.CommandText = "SELECT hits, misses FROM cache_stats WHERE cache_type = $type;";
        statCmd.Parameters.AddWithValue("$type", type.ToString());
        using var statReader = statCmd.ExecuteReader();
        int hits = 0, misses = 0;
        if (statReader.Read())
        {
            hits = statReader.GetInt32(0);
            misses = statReader.GetInt32(1);
        }

        var total = hits + misses;
        return new CacheStats
        {
            Type = type,
            TotalEntries = totalEntries,
            TotalSizeBytes = totalBytes,
            Hits = hits,
            Misses = misses,
            HitRate = total > 0 ? (double)hits / total : 0,
        };
    }

    public void Clear()
    {
        using var conn = _factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM cache_entries; DELETE FROM cache_stats;";
        cmd.ExecuteNonQuery();
    }

    private void IncrementStat(CacheType type, string column)
    {
        using var conn = _factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO cache_stats (cache_type, {column})
            VALUES ($type, 1)
            ON CONFLICT(cache_type) DO UPDATE SET {column} = {column} + 1;
            """;
        cmd.Parameters.AddWithValue("$type", type.ToString());
        cmd.ExecuteNonQuery();
    }

    private void RecordMiss(CacheType type)
    {
        IncrementStat(type, "misses");
    }
}
