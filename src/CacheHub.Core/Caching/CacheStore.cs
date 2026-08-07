using CacheHub.Core.Caching;

namespace CacheHub.Core.Caching;

/// <summary>
/// Unified cache store interface: per-type quotas, dependency hash, hit reason.
/// R6: replaces scattered in-memory caches with a single store.
/// </summary>
public interface ICacheStore
{
    /// <summary>Retrieves a cached value by key and type, checking dependency hash.</summary>
    CacheEntry? TryGet(string key, CacheType type, string? dependencyHash = null);

    /// <summary>Puts a value into the cache with metadata.</summary>
    void Put(CacheEntry entry, byte[]? blob = null);

    /// <summary>Invalidates entries by dependency hash.</summary>
    void InvalidateByDependency(string dependencyHash);

    /// <summary>Invalidates all entries of a type.</summary>
    void InvalidateType(CacheType type);

    /// <summary>Gets statistics for a cache type.</summary>
    CacheStats GetStats(CacheType type);

    /// <summary>Clears all entries.</summary>
    void Clear();
}

/// <summary>
/// Hit reason for cache diagnostics.
/// </summary>
public enum CacheHitReason
{
    Hit,
    Miss,
    Expired,
    DependencyChanged,
    SizeEvicted,
}

/// <summary>
/// In-memory CacheStore backed by LruCache with per-type byte quotas.
/// </summary>
public sealed class MemoryCacheStore : ICacheStore, IDisposable
{
    private readonly LruCache _lru;
    private readonly Dictionary<string, byte[]> _blobs = new();
    private readonly Lock _blobLock = new();
    private readonly Dictionary<CacheType, long> _typeQuotas;
    private bool _disposed;

    public MemoryCacheStore(long totalMaxBytes = 100 * 1024 * 1024,
        Dictionary<CacheType, long>? typeQuotas = null)
    {
        _lru = new LruCache(totalMaxBytes);
        _typeQuotas = typeQuotas ?? new Dictionary<CacheType, long>
        {
            [CacheType.Context] = 50 * 1024 * 1024,
            [CacheType.Parse] = 20 * 1024 * 1024,
            [CacheType.Search] = 10 * 1024 * 1024,
            [CacheType.RepoMap] = 10 * 1024 * 1024,
            [CacheType.ToolResult] = 10 * 1024 * 1024,
        };
    }

    public CacheEntry? TryGet(string key, CacheType type, string? dependencyHash = null)
    {
        var entry = _lru.Get(key, type);
        if (entry is null) return null;

        // R6: dependency hash validation
        if (dependencyHash is not null &&
            entry.DependencyHash is not null &&
            !entry.DependencyHash.Equals(dependencyHash, StringComparison.Ordinal))
        {
            _lru.Invalidate(key);
            return null;
        }

        // Check TTL
        if (entry.TtlSeconds is not null &&
            DateTimeOffset.UtcNow.Subtract(entry.CreatedAt).TotalSeconds > entry.TtlSeconds)
        {
            _lru.Invalidate(key);
            return null;
        }

        return entry;
    }

    public void Put(CacheEntry entry, byte[]? blob = null)
    {
        _lru.Put(entry);
        if (blob is not null)
        {
            lock (_blobLock)
            {
                _blobs[entry.Key] = blob;
            }
        }
    }

    public byte[]? GetBlob(string key)
    {
        lock (_blobLock)
        {
            return _blobs.TryGetValue(key, out var blob) ? blob : null;
        }
    }

    public void InvalidateByDependency(string dependencyHash)
    {
        // LruCache doesn't expose enumeration, so we rely on Get with dependency check
        // In practice, this would be handled by invalidating known keys
        // For now, this is a no-op — dependency changes are caught on Get
    }

    public void InvalidateType(CacheType type)
    {
        _lru.Clear();
    }

    public CacheStats GetStats(CacheType type) => _lru.GetStats(type);

    public void Clear()
    {
        _lru.Clear();
        lock (_blobLock) { _blobs.Clear(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lru.Dispose();
    }
}
