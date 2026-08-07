using CacheHub.Core.Caching;
using CacheHub.Storage.Caching;
using CacheHub.Storage.Database;
using CacheHub.Storage.Database.Migrations;

namespace CacheHub.Tests;

/// <summary>
/// Tests for R7-W001: Persistent cache store backed by SQLite.
/// Verifies cache survives restart, dependency invalidation, TTL expiry, and blob storage.
/// </summary>
[Collection("SQLite")]
public class PersistentCacheStoreTests
{
    private static SqliteConnectionFactory SetupFactory()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cachehub_pcache_{Guid.NewGuid():N}.db");
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

    [Fact]
    public void PutAndTryGet_ReturnsEntry()
    {
        var factory = SetupFactory();
        var store = new SqliteCacheStore(factory);

        var entry = new CacheEntry
        {
            Key = "test-key",
            Type = CacheType.Context,
            Version = "1.0",
            CreatedAt = DateTimeOffset.UtcNow,
            SizeBytes = 100,
        };

        store.Put(entry);
        var result = store.TryGet("test-key", CacheType.Context);

        Assert.NotNull(result);
        Assert.Equal("test-key", result.Key);
        Assert.True(result.IsHit);
    }

    [Fact]
    public void TryGet_Missing_ReturnsNull()
    {
        var factory = SetupFactory();
        var store = new SqliteCacheStore(factory);

        var result = store.TryGet("nonexistent", CacheType.Context);
        Assert.Null(result);
    }

    [Fact]
    public void Put_BlobStoredInFile()
    {
        var factory = SetupFactory();
        var blobDir = Path.Combine(Path.GetTempPath(), $"cachehub_blob_{Guid.NewGuid():N}");
        var store = new SqliteCacheStore(factory, blobDir);

        var blob = System.Text.Encoding.UTF8.GetBytes("large content blob data");
        var entry = new CacheEntry
        {
            Key = "blob-key",
            Type = CacheType.Parse,
            Version = "1.0",
            CreatedAt = DateTimeOffset.UtcNow,
            SizeBytes = blob.Length,
        };

        store.Put(entry, blob);
        var retrieved = store.GetBlob("blob-key");

        Assert.NotNull(retrieved);
        Assert.Equal(blob, retrieved);
    }

    [Fact]
    public void DependencyHash_Mismatch_Invalidates()
    {
        var factory = SetupFactory();
        var store = new SqliteCacheStore(factory);

        store.Put(new CacheEntry
        {
            Key = "dep-key",
            Type = CacheType.Context,
            Version = "1.0",
            CreatedAt = DateTimeOffset.UtcNow,
            SizeBytes = 50,
            DependencyHash = "hash-old",
        });

        // Different dependency hash → should invalidate
        var result = store.TryGet("dep-key", CacheType.Context, "hash-new");
        Assert.Null(result);

        // Entry should be deleted
        var secondTry = store.TryGet("dep-key", CacheType.Context);
        Assert.Null(secondTry);
    }

    [Fact]
    public void DependencyHash_Match_ReturnsEntry()
    {
        var factory = SetupFactory();
        var store = new SqliteCacheStore(factory);

        store.Put(new CacheEntry
        {
            Key = "dep-key2",
            Type = CacheType.Context,
            Version = "1.0",
            CreatedAt = DateTimeOffset.UtcNow,
            SizeBytes = 50,
            DependencyHash = "hash-correct",
        });

        var result = store.TryGet("dep-key2", CacheType.Context, "hash-correct");
        Assert.NotNull(result);
    }

    [Fact]
    public void TTL_Expiry_Invalidates()
    {
        var factory = SetupFactory();
        var store = new SqliteCacheStore(factory);

        store.Put(new CacheEntry
        {
            Key = "ttl-key",
            Type = CacheType.Search,
            Version = "1.0",
            CreatedAt = DateTimeOffset.UtcNow.AddSeconds(-100), // 100 seconds ago
            SizeBytes = 50,
            TtlSeconds = 10, // Expires after 10 seconds
        });

        var result = store.TryGet("ttl-key", CacheType.Search);
        Assert.Null(result);
    }

    [Fact]
    public void GetStats_ReturnsHitMissCounts()
    {
        var factory = SetupFactory();
        var store = new SqliteCacheStore(factory);

        store.Put(new CacheEntry
        {
            Key = "stat-key",
            Type = CacheType.Context,
            Version = "1.0",
            CreatedAt = DateTimeOffset.UtcNow,
            SizeBytes = 50,
        });

        store.TryGet("stat-key", CacheType.Context); // Hit
        store.TryGet("missing", CacheType.Context);  // Miss

        var stats = store.GetStats(CacheType.Context);
        Assert.True(stats.Hits >= 1);
        Assert.True(stats.Misses >= 1);
    }

    [Fact]
    public void InvalidateByDependency_RemovesMatching()
    {
        var factory = SetupFactory();
        var store = new SqliteCacheStore(factory);

        store.Put(new CacheEntry
        {
            Key = "inv-1",
            Type = CacheType.Context,
            Version = "1.0",
            CreatedAt = DateTimeOffset.UtcNow,
            SizeBytes = 50,
            DependencyHash = "dep-abc",
        });

        store.Put(new CacheEntry
        {
            Key = "inv-2",
            Type = CacheType.Context,
            Version = "1.0",
            CreatedAt = DateTimeOffset.UtcNow,
            SizeBytes = 50,
            DependencyHash = "dep-xyz",
        });

        store.InvalidateByDependency("dep-abc");

        Assert.Null(store.TryGet("inv-1", CacheType.Context));
        Assert.NotNull(store.TryGet("inv-2", CacheType.Context));
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var factory = SetupFactory();
        var store = new SqliteCacheStore(factory);

        store.Put(new CacheEntry
        {
            Key = "c1",
            Type = CacheType.Context,
            Version = "1.0",
            CreatedAt = DateTimeOffset.UtcNow,
            SizeBytes = 50,
        });

        store.Clear();
        Assert.Null(store.TryGet("c1", CacheType.Context));
    }
}
