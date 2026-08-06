using AiKv.Core.Caching;
using AiKv.Indexing.Parsing.Cache;
using AiKv.Core.Parsing;

namespace AiKv.Tests;

public class CacheTests
{
    [Fact]
    public void LruCache_Get_ShouldReturnEntry_WhenExists()
    {
        var cache = new LruCache();
        var entry = CreateEntry("key1", CacheType.Parse, 100);
        cache.Put(entry);

        var result = cache.Get("key1");

        Assert.NotNull(result);
        Assert.True(result!.IsHit);
    }

    [Fact]
    public void LruCache_Get_ShouldReturnNull_WhenNotExists()
    {
        var cache = new LruCache();

        var result = cache.Get("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public void LruCache_Put_ShouldOverwriteExisting()
    {
        var cache = new LruCache();
        cache.Put(CreateEntry("key1", CacheType.Parse, 100, "v1"));
        cache.Put(CreateEntry("key1", CacheType.Parse, 200, "v2"));

        var result = cache.Get("key1");

        Assert.NotNull(result);
        Assert.Equal("v2", result!.Version);
        Assert.Equal(200, result.SizeBytes);
    }

    [Fact]
    public void LruCache_ShouldEvictLru_WhenSizeExceeded()
    {
        var cache = new LruCache(maxSizeBytes: 300);
        cache.Put(CreateEntry("k1", CacheType.Parse, 100));
        cache.Put(CreateEntry("k2", CacheType.Parse, 100));
        cache.Put(CreateEntry("k3", CacheType.Parse, 100));

        // Access k1 to make it more recently used than k2
        cache.Get("k1");

        // Add k4 — should evict k2 (least recently used)
        cache.Put(CreateEntry("k4", CacheType.Parse, 100));

        Assert.Null(cache.Get("k2"));
        Assert.NotNull(cache.Get("k1"));
        Assert.NotNull(cache.Get("k3"));
        Assert.NotNull(cache.Get("k4"));
    }

    [Fact]
    public void LruCache_Invalidate_ShouldRemoveEntry()
    {
        var cache = new LruCache();
        cache.Put(CreateEntry("key1", CacheType.Parse, 100));
        cache.Invalidate("key1");

        Assert.Null(cache.Get("key1"));
    }

    [Fact]
    public void LruCache_Clear_ShouldRemoveAll()
    {
        var cache = new LruCache();
        cache.Put(CreateEntry("k1", CacheType.Parse, 100));
        cache.Put(CreateEntry("k2", CacheType.Search, 100));
        cache.Clear();

        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void LruCache_Get_ShouldRespectTtl()
    {
        var cache = new LruCache();
        cache.Put(CreateEntry("key1", CacheType.Parse, 100, ttlSeconds: 0));

        // TTL of 0 means it expires immediately
        var result = cache.Get("key1");

        Assert.Null(result);
    }

    [Fact]
    public void LruCache_GetStats_ShouldReturnCorrectStats()
    {
        var cache = new LruCache();
        cache.Put(CreateEntry("k1", CacheType.Parse, 100));
        cache.Put(CreateEntry("k2", CacheType.Parse, 200));
        cache.Get("k1"); // hit

        var stats = cache.GetStats(CacheType.Parse);

        Assert.Equal(2, stats.TotalEntries);
        Assert.Equal(300, stats.TotalSizeBytes);
    }

    [Fact]
    public void ParserCache_GetOrParse_ShouldCacheResults()
    {
        var cache = new ParserCache();
        var parser = new AiKv.Indexing.Parsing.CSharpRegexParser();

        var result1 = cache.GetOrParse("public class A {}", "a.cs", "hash_a", parser);
        var result2 = cache.GetOrParse("public class A {}", "a.cs", "hash_a", parser);

        Assert.Same(result1, result2);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void ParserCache_Invalidate_ShouldRemoveEntries()
    {
        var cache = new ParserCache();
        var parser = new AiKv.Indexing.Parsing.CSharpRegexParser();

        cache.GetOrParse("public class A {}", "a.cs", "hash_a", parser);
        cache.Invalidate("hash_a");

        Assert.Equal(0, cache.Count);
        Assert.Null(cache.TryGet("hash_a", parser.Id, parser.Version));
    }

    private static CacheEntry CreateEntry(
        string key, CacheType type, long size, string? version = null, int? ttlSeconds = null) => new()
        {
            Key = key,
            Type = type,
            Version = version ?? "1.0",
            CreatedAt = DateTimeOffset.UtcNow,
            SizeBytes = size,
            TtlSeconds = ttlSeconds,
        };
}
