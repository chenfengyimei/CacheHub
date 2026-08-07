using CacheHub.Core.Caching;
using CacheHub.Gateway;
using CacheHub.Storage.Caching;
using CacheHub.Storage.Database;
using CacheHub.Storage.Database.Migrations;

namespace CacheHub.Tests;

/// <summary>
/// R7 Gate regression tests: persistent cache, dependency invalidation, safety, corruption isolation.
/// </summary>
[Collection("SQLite")]
public class R7GateRegressionTests
{
    private static SqliteConnectionFactory SetupFactory()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cachehub_r7gate_{Guid.NewGuid():N}.db");
        var factory = new SqliteConnectionFactory(dbPath);
        var runner = new MigrationRunner(factory, dbPath,
        [
            new Migration0001Initial(), new Migration0002Fts5(),
            new Migration0003ContextPackages(), new Migration0004Feedback(),
            new Migration0005ContextPackageDetails(),
            new Migration0006SchemaV2(), new Migration0007ContextPackageFields(),
            new Migration0008ContextPackageFk(),
            new Migration0009PersistentCache(),
        ]);
        runner.Migrate();
        return factory;
    }

    // R7 Gate: Process restart 鈫?safe cache still hits
    [Fact]
    public void Gate_RestartSafe_CacheStillHits()
    {
        var factory = SetupFactory();

        // Simulate "first process" 鈥?put into cache
        var store1 = new SqliteCacheStore(factory);
        store1.Put(new CacheEntry
        {
            Key = "restart-test",
            Type = CacheType.Context,
            Version = "1.0",
            CreatedAt = DateTimeOffset.UtcNow,
            SizeBytes = 100,
        });

        // Simulate "second process" 鈥?new store instance, same DB
        var store2 = new SqliteCacheStore(factory);
        var result = store2.TryGet("restart-test", CacheType.Context);

        Assert.NotNull(result);
        Assert.Equal("restart-test", result.Key);
    }

    // R7 Gate: File/policy change 鈫?dependency cache correctly invalidates
    [Fact]
    public void Gate_DependencyChange_InvalidatesCache()
    {
        var factory = SetupFactory();
        var store = new SqliteCacheStore(factory);

        store.Put(new CacheEntry
        {
            Key = "dep-test",
            Type = CacheType.Context,
            Version = "1.0",
            CreatedAt = DateTimeOffset.UtcNow,
            SizeBytes = 50,
            DependencyHash = "file-hash-v1",
        });

        // Same dependency hash 鈫?hit
        Assert.NotNull(store.TryGet("dep-test", CacheType.Context, "file-hash-v1"));

        // Different dependency hash (file changed) 鈫?miss + invalidated
        Assert.Null(store.TryGet("dep-test", CacheType.Context, "file-hash-v2"));

        // Should now be deleted
        Assert.Null(store.TryGet("dep-test", CacheType.Context, "file-hash-v1"));
    }

    // R7 Gate: Failed/partial streaming/tool calls 鈫?cannot enter cache
    [Fact]
    public void Gate_ToolCallRequests_NotCached()
    {
        var requestWithTools = """{"model":"gpt-4","tools":[{"type":"function"}],"messages":[]}""";
        Assert.False(CacheSafetyChecker.IsCacheable(requestWithTools, "gpt-4"));
    }

    [Fact]
    public void Gate_StreamingRequests_NotCached()
    {
        var streamingRequest = """{"model":"gpt-4","stream":true,"messages":[]}""";
        Assert.False(CacheSafetyChecker.IsCacheable(streamingRequest, "gpt-4"));
    }

    [Fact]
    public void Gate_HighTemperature_NotCached()
    {
        var highTempRequest = """{"model":"gpt-4","temperature":1.5,"messages":[]}""";
        Assert.False(CacheSafetyChecker.IsCacheable(highTempRequest, "gpt-4"));
    }

    [Fact]
    public void Gate_SafeRequest_CanBeCached()
    {
        var safeRequest = """{"model":"gpt-4","temperature":0,"messages":[{"role":"user","content":"hello"}]}""";
        Assert.True(CacheSafetyChecker.IsCacheable(safeRequest, "gpt-4"));
    }

    [Fact]
    public void Gate_ResponseWithToolCalls_NotCached()
    {
        var responseWithTools = """{"choices":[{"message":{"tool_calls":[{"id":"call_1","type":"function"}]}}]}""";
        Assert.True(CacheSafetyChecker.HasToolCalls(responseWithTools));
    }

    [Fact]
    public void Gate_ResponseWithoutToolCalls_CanBeCached()
    {
        var safeResponse = """{"choices":[{"message":{"content":"hello"}}]}""";
        Assert.False(CacheSafetyChecker.HasToolCalls(safeResponse));
    }

    // R7 Gate: Cache corruption 鈫?auto-isolate and fall back
    [Fact]
    public void Gate_CorruptedBlob_FallsBackGracefully()
    {
        var factory = SetupFactory();
        var blobDir = Path.Combine(Path.GetTempPath(), $"cachehub_corrupt_{Guid.NewGuid():N}");
        var store = new SqliteCacheStore(factory, blobDir);

        store.Put(new CacheEntry
        {
            Key = "corrupt-test",
            Type = CacheType.Context,
            Version = "1.0",
            CreatedAt = DateTimeOffset.UtcNow,
            SizeBytes = 50,
        }, System.Text.Encoding.UTF8.GetBytes("valid blob"));

        // Corrupt the blob file
        var blobFiles = Directory.GetFiles(blobDir);
        if (blobFiles.Length > 0)
            File.Delete(blobFiles[0]); // Simulate corruption by deleting

        // TryGet should still return the metadata (blob is separate)
        var result = store.TryGet("corrupt-test", CacheType.Context);
        Assert.NotNull(result); // Metadata still valid

        // GetBlob should return null gracefully (not throw)
        var blob = store.GetBlob("corrupt-test");
        Assert.Null(blob);
    }

    // R7 Gate: TTL expiry 鈫?automatic invalidation
    [Fact]
    public void Gate_TTLExpiry_AutoInvalidates()
    {
        var factory = SetupFactory();
        var store = new SqliteCacheStore(factory);

        store.Put(new CacheEntry
        {
            Key = "ttl-gate",
            Type = CacheType.Parse,
            Version = "1.0",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            SizeBytes = 50,
            TtlSeconds = 60, // Expires after 1 minute
        });

        Assert.Null(store.TryGet("ttl-gate", CacheType.Parse));
    }

    // R7 Gate: Statistics tracking
    [Fact]
    public void Gate_Statistics_TrackHitsAndMisses()
    {
        var factory = SetupFactory();
        var store = new SqliteCacheStore(factory);

        store.Put(new CacheEntry
        {
            Key = "stat-1",
            Type = CacheType.Context,
            Version = "1.0",
            CreatedAt = DateTimeOffset.UtcNow,
            SizeBytes = 50,
        });

        store.TryGet("stat-1", CacheType.Context);  // Hit
        store.TryGet("stat-1", CacheType.Context);  // Hit
        store.TryGet("missing", CacheType.Context);  // Miss

        var stats = store.GetStats(CacheType.Context);
        Assert.True(stats.Hits >= 2);
        Assert.True(stats.Misses >= 1);
        Assert.True(stats.HitRate > 0);
    }
}
