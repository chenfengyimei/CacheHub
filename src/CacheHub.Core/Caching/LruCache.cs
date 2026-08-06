using System.Text.Json.Serialization;

namespace CacheHub.Core.Caching;

/// <summary>
/// Type of cache entry.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CacheType
{
    Parse,
    Search,
    RepoMap,
    Context,
    ToolResult,
}

/// <summary>
/// Unified cache metadata for all cache types.
/// </summary>
public sealed record CacheEntry
{
    public required string Key { get; init; }
    public required CacheType Type { get; init; }
    public required string Version { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required long SizeBytes { get; init; }
    public string? DependencyHash { get; init; }
    public int? TtlSeconds { get; init; }
    public string? ProducerVersion { get; init; }
    public bool IsHit { get; init; }
}

/// <summary>
/// Statistics for a cache type.
/// </summary>
public sealed record CacheStats
{
    public required CacheType Type { get; init; }
    public required int TotalEntries { get; init; }
    public required long TotalSizeBytes { get; init; }
    public required int Hits { get; init; }
    public required int Misses { get; init; }
    public required double HitRate { get; init; }
}

/// <summary>
/// LRU cache with size limit and TTL support.
/// Thread-safe.
/// </summary>
public sealed class LruCache : IDisposable
{
    private readonly Lock _lock = new();
    private readonly LinkedList<CacheEntry> _lruList = [];
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _map = new();
    private readonly long _maxSizeBytes;
    private readonly Dictionary<CacheType, long> _hitCounts = new();
    private readonly Dictionary<CacheType, long> _missCounts = new();
    private long _currentSizeBytes;
    private bool _disposed;

    public LruCache(long maxSizeBytes = 100 * 1024 * 1024)
    {
        _maxSizeBytes = maxSizeBytes;
    }

    public int Count
    {
        get { lock (_lock) return _map.Count; }
    }

    public long SizeBytes
    {
        get { lock (_lock) return _currentSizeBytes; }
    }

    public CacheEntry? Get(string key)
    {
        lock (_lock)
        {
            if (!_map.TryGetValue(key, out var node))
            {
                RecordMiss(node?.Value?.Type);
                return null;
            }

            // Check TTL
            if (node.Value.TtlSeconds is not null &&
                DateTimeOffset.UtcNow.Subtract(node.Value.CreatedAt).TotalSeconds > node.Value.TtlSeconds)
            {
                var ttlType = node.Value.Type;
                RemoveNode(node);
                RecordMiss(ttlType);
                return null;
            }

            // Move to front (most recently used)
            _lruList.Remove(node);
            _lruList.AddFirst(node);

            RecordHit(node.Value.Type);
            return node.Value with { IsHit = true };
        }
    }

    public void Put(CacheEntry entry)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(entry.Key, out var existing))
                RemoveNode(existing);

            var node = new LinkedListNode<CacheEntry>(entry);
            _lruList.AddFirst(node);
            _map[entry.Key] = node;
            _currentSizeBytes += entry.SizeBytes;

            EvictIfNeeded();
        }
    }

    public void Invalidate(string key)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
                RemoveNode(node);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _lruList.Clear();
            _map.Clear();
            _currentSizeBytes = 0;
            _hitCounts.Clear();
            _missCounts.Clear();
        }
    }

    public CacheStats GetStats(CacheType type)
    {
        lock (_lock)
        {
            var entries = _map.Values.Where(n => n.Value.Type == type).Select(n => n.Value).ToList();
            var hits = _hitCounts.GetValueOrDefault(type);
            var misses = _missCounts.GetValueOrDefault(type);
            var total = hits + misses;
            return new CacheStats
            {
                Type = type,
                TotalEntries = entries.Count,
                TotalSizeBytes = entries.Sum(e => e.SizeBytes),
                Hits = (int)hits,
                Misses = (int)misses,
                HitRate = total > 0 ? (double)hits / total : 0,
            };
        }
    }

    private void RecordHit(CacheType type) =>
        _hitCounts[type] = _hitCounts.GetValueOrDefault(type) + 1;

    private void RecordMiss(CacheType? type)
    {
        if (type is null) return;
        _missCounts[type.Value] = _missCounts.GetValueOrDefault(type.Value) + 1;
    }

    private void EvictIfNeeded()
    {
        while (_currentSizeBytes > _maxSizeBytes && _lruList.Last is not null)
        {
            var last = _lruList.Last;
            RemoveNode(last);
        }
    }

    private void RemoveNode(LinkedListNode<CacheEntry> node)
    {
        _lruList.Remove(node);
        _map.Remove(node.Value.Key);
        _currentSizeBytes -= node.Value.SizeBytes;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Clear();
    }
}
