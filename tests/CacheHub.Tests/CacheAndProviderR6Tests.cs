using CacheHub.Core.Caching;
using CacheHub.Core.Providers;

namespace CacheHub.Tests;

/// <summary>
/// Tests for R6: unified CacheStore, ProviderRouter, BudgetTracker, and model listing.
/// </summary>
public class CacheAndProviderR6Tests
{
    // === CacheStore Tests ===

    [Fact]
    public void CacheStore_PutAndGet_ReturnsEntry()
    {
        using var store = new MemoryCacheStore(1024 * 1024);
        var entry = new CacheEntry
        {
            Key = "k1",
            Type = CacheType.Parse,
            Version = "1.0",
            CreatedAt = DateTimeOffset.UtcNow,
            SizeBytes = 100,
        };

        store.Put(entry);
        var result = store.TryGet("k1", CacheType.Parse);

        Assert.NotNull(result);
        Assert.Equal("k1", result.Key);
    }

    [Fact]
    public void CacheStore_Miss_ReturnsNull()
    {
        using var store = new MemoryCacheStore(1024 * 1024);
        var result = store.TryGet("missing", CacheType.Parse);
        Assert.Null(result);
    }

    [Fact]
    public void CacheStore_DependencyHashMismatch_ReturnsNull()
    {
        using var store = new MemoryCacheStore(1024 * 1024);
        store.Put(new CacheEntry
        {
            Key = "k",
            Type = CacheType.Parse,
            Version = "1.0",
            CreatedAt = DateTimeOffset.UtcNow,
            SizeBytes = 100,
            DependencyHash = "hash-old",
        });

        // Requesting with a different dependency hash should invalidate
        var result = store.TryGet("k", CacheType.Parse, "hash-new");
        Assert.Null(result);
    }

    [Fact]
    public void CacheStore_DependencyHashMatch_ReturnsEntry()
    {
        using var store = new MemoryCacheStore(1024 * 1024);
        store.Put(new CacheEntry
        {
            Key = "k",
            Type = CacheType.Parse,
            Version = "1.0",
            CreatedAt = DateTimeOffset.UtcNow,
            SizeBytes = 100,
            DependencyHash = "hash-1",
        });

        var result = store.TryGet("k", CacheType.Parse, "hash-1");
        Assert.NotNull(result);
    }

    [Fact]
    public void CacheStore_GetStats_ReturnsHitMissCounts()
    {
        using var store = new MemoryCacheStore(1024 * 1024);
        store.Put(new CacheEntry
        {
            Key = "k",
            Type = CacheType.Context,
            Version = "1.0",
            CreatedAt = DateTimeOffset.UtcNow,
            SizeBytes = 100,
        });

        store.TryGet("k", CacheType.Context);
        store.TryGet("missing", CacheType.Context);

        var stats = store.GetStats(CacheType.Context);
        Assert.True(stats.Hits >= 1);
        Assert.True(stats.Misses >= 1);
    }

    // === ProviderRouter Tests ===

    [Fact]
    public void Router_ExplicitStrategy_SelectsFirstHealthyProvider()
    {
        var router = new ProviderRouter(new RoutingConfig
        {
            Strategy = RoutingStrategy.Explicit,
            ProviderOrder = ["p1", "p2"],
        });
        router.RegisterProvider(new TestProvider("p1"));
        router.RegisterProvider(new TestProvider("p2"));

        var selected = router.SelectProvider();
        Assert.Equal("p1", selected?.Id);
    }

    [Fact]
    public void Router_HealthFailure_SkipsUnhealthyProvider()
    {
        var router = new ProviderRouter(new RoutingConfig
        {
            Strategy = RoutingStrategy.Explicit,
            ProviderOrder = ["p1", "p2"],
        });
        router.RegisterProvider(new TestProvider("p1"));
        router.RegisterProvider(new TestProvider("p2"));
        router.UpdateHealth("p1", false, "down");

        var selected = router.SelectProvider();
        Assert.Equal("p2", selected?.Id);
    }

    [Fact]
    public void Router_AllUnhealthy_ReturnsNull()
    {
        var router = new ProviderRouter(new RoutingConfig
        {
            Strategy = RoutingStrategy.Fallback,
            ProviderOrder = ["p1"],
        });
        router.RegisterProvider(new TestProvider("p1"));
        router.UpdateHealth("p1", false, "down");

        var selected = router.SelectProvider();
        Assert.Null(selected);
    }

    [Fact]
    public void Router_RoundRobin_AlternatesProviders()
    {
        var router = new ProviderRouter(new RoutingConfig
        {
            Strategy = RoutingStrategy.RoundRobin,
            ProviderOrder = ["p1", "p2"],
        });
        router.RegisterProvider(new TestProvider("p1"));
        router.RegisterProvider(new TestProvider("p2"));

        var first = router.SelectProvider();
        var second = router.SelectProvider();

        Assert.NotEqual(first?.Id, second?.Id);
    }

    // === BudgetTracker Tests ===

    [Fact]
    public void BudgetTracker_NoLimit_MaxAllowAll()
    {
        var tracker = new BudgetTracker();
        var allowed = tracker.TryRecord(new UsageRecord
        {
            Timestamp = DateTimeOffset.UtcNow,
            ProviderId = "p1",
            Model = "gpt-4",
            PromptTokens = 100,
            CompletionTokens = 50,
            EstimatedCostUsd = 0.01m,
        });

        Assert.True(allowed);
    }

    [Fact]
    public void BudgetTracker_DailyLimit_BlocksWhenExceeded()
    {
        var tracker = new BudgetTracker();
        tracker.SetLimit(new BudgetLimit
        {
            Scope = "provider:p1",
            DailyLimitUsd = 0.02m,
        });

        // Record 0.015, within limit
        Assert.True(tracker.TryRecord(new UsageRecord
        {
            Timestamp = DateTimeOffset.UtcNow,
            ProviderId = "p1",
            Model = "gpt-4",
            PromptTokens = 100,
            CompletionTokens = 50,
            EstimatedCostUsd = 0.015m,
        }));

        // Record 0.01 more — would exceed 0.02 daily limit
        Assert.False(tracker.TryRecord(new UsageRecord
        {
            Timestamp = DateTimeOffset.UtcNow,
            ProviderId = "p1",
            Model = "gpt-4",
            PromptTokens = 100,
            CompletionTokens = 50,
            EstimatedCostUsd = 0.01m,
        }));
    }

    [Fact]
    public void BudgetTracker_MonthlyLimit_BlocksWhenExceeded()
    {
        var tracker = new BudgetTracker();
        tracker.SetLimit(new BudgetLimit
        {
            Scope = "provider:p2",
            MonthlyLimitUsd = 0.05m,
        });

        Assert.True(tracker.TryRecord(new UsageRecord
        {
            Timestamp = DateTimeOffset.UtcNow,
            ProviderId = "p2",
            Model = "test",
            PromptTokens = 100,
            CompletionTokens = 50,
            EstimatedCostUsd = 0.04m,
        }));

        Assert.False(tracker.TryRecord(new UsageRecord
        {
            Timestamp = DateTimeOffset.UtcNow,
            ProviderId = "p2",
            Model = "test",
            PromptTokens = 100,
            CompletionTokens = 50,
            EstimatedCostUsd = 0.02m,
        }));
    }

    [Fact]
    public void BudgetTracker_GetUsage_ReturnsAggregates()
    {
        var tracker = new BudgetTracker();
        // Set a high limit so records are stored
        tracker.SetLimit(new BudgetLimit
        {
            Scope = "provider:p1",
            DailyLimitUsd = 100m,
        });

        tracker.TryRecord(new UsageRecord
        {
            Timestamp = DateTimeOffset.UtcNow,
            ProviderId = "p1",
            Model = "test",
            PromptTokens = 100,
            CompletionTokens = 50,
            EstimatedCostUsd = 0.01m,
        });

        var (dailyCost, monthlyCost, dailyRequests) = tracker.GetUsage("provider:p1");
        Assert.Equal(0.01m, dailyCost);
        Assert.Equal(0.01m, monthlyCost);
        Assert.Equal(1, dailyRequests);
    }

    // === Provider ListModels ===

    [Fact]
    public async Task Provider_ListModels_ReturnsEmptyOnError()
    {
        var provider = new OpenAiCompatibleProvider("test", "http://127.0.0.1:1"); // invalid endpoint
        var models = await provider.ListModelsAsync();
        Assert.Empty(models);
    }

    private sealed class TestProvider : IProvider
    {
        public TestProvider(string id)
        {
            Id = id;
            BaseUrl = $"http://{id}.example.com";
        }

        public string Id { get; }
        public string Version => "1.0";
        public string BaseUrl { get; }

        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ModelInfo>>([]);

        public Task<ProviderResponse> SendAsync(ProviderRequest request, CancellationToken ct = default)
            => Task.FromResult(new ProviderResponse
            {
                StatusCode = 200,
                Body = "{}",
                Success = true,
            });
    }
}