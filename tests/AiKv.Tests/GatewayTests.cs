using System.Text.Json;
using AiKv.Core.Caching;
using AiKv.Core.Gateway;

namespace AiKv.Tests;

public class GatewayTests
{
    [Fact]
    public void CacheSafetyChecker_IsCacheable_ShouldAcceptSimpleRequest()
    {
        var body = """{"model":"gpt-4","messages":[{"role":"user","content":"hello"}],"temperature":0.3}""";

        Assert.True(CacheSafetyChecker.IsCacheable(body, "gpt-4"));
    }

    [Fact]
    public void CacheSafetyChecker_IsCacheable_ShouldRejectTools()
    {
        var body = """{"model":"gpt-4","messages":[],"tools":[{"type":"function","function":{"name":"test"}}]}""";

        Assert.False(CacheSafetyChecker.IsCacheable(body, "gpt-4"));
    }

    [Fact]
    public void CacheSafetyChecker_IsCacheable_ShouldRejectHighTemperature()
    {
        var body = """{"model":"gpt-4","messages":[],"temperature":1.0}""";

        Assert.False(CacheSafetyChecker.IsCacheable(body, "gpt-4"));
    }

    [Fact]
    public void CacheSafetyChecker_IsCacheable_ShouldRejectStreaming()
    {
        var body = """{"model":"gpt-4","messages":[],"stream":true}""";

        Assert.False(CacheSafetyChecker.IsCacheable(body, "gpt-4"));
    }

    [Fact]
    public void CacheSafetyChecker_IsCacheable_ShouldRespectNoCacheFlag()
    {
        var body = """{"model":"gpt-4","messages":[],"metadata":{"no_cache":true}}""";

        Assert.False(CacheSafetyChecker.IsCacheable(body, "gpt-4"));
    }

    [Fact]
    public void CacheSafetyChecker_HasToolCalls_ShouldDetectToolCalls()
    {
        var body = """{"choices":[{"message":{"tool_calls":[{"id":"1","type":"function"}]}}]}""";

        Assert.True(CacheSafetyChecker.HasToolCalls(body));
    }

    [Fact]
    public void CacheSafetyChecker_HasToolCalls_ShouldReturnFalseForNormalResponse()
    {
        var body = """{"choices":[{"message":{"content":"hello"}}]}""";

        Assert.False(CacheSafetyChecker.HasToolCalls(body));
    }

    [Fact]
    public async Task SingleFlight_ShouldDeduplicateConcurrentRequests()
    {
        var sf = new SingleFlight();
        var callCount = 0;

        var task1 = sf.ExecuteAsync("key1", async () =>
        {
            await Task.Delay(50);
            Interlocked.Increment(ref callCount);
            return "result";
        });

        var task2 = sf.ExecuteAsync("key1", async () =>
        {
            Interlocked.Increment(ref callCount);
            return "should-not-run";
        });

        var results = await Task.WhenAll(task1, task2);

        Assert.Equal("result", results[0]);
        Assert.Equal("result", results[1]);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task SingleFlight_ShouldAllowDifferentKeys()
    {
        var sf = new SingleFlight();

        var r1 = await sf.ExecuteAsync("key1", async () => await Task.FromResult("result1"));
        var r2 = await sf.ExecuteAsync("key2", async () => await Task.FromResult("result2"));

        Assert.Equal("result1", r1);
        Assert.Equal("result2", r2);
    }

    [Fact]
    public Task SingleFlight_ShouldAllowDifferentKeys_Sync()
    {
        var sf = new SingleFlight();

        var r1 = sf.ExecuteAsync("key1", async () => await Task.FromResult("result1"));
        var r2 = sf.ExecuteAsync("key2", async () => await Task.FromResult("result2"));

        return Task.CompletedTask;
    }

    [Fact]
    public void GatewayConfig_Default_ShouldBindLoopback()
    {
        var config = new GatewayConfig
        {
            ProviderBaseUrl = "https://api.openai.com",
            ProviderApiKey = "sk-test",
        };

        Assert.Equal("127.0.0.1", config.Host);
        Assert.Equal(5218, config.Port);
        Assert.True(config.EnableCache);
    }

    [Fact]
    public void ModelUsage_ShouldTrackTokens()
    {
        var usage = new ModelUsage
        {
            PromptTokens = 1000,
            CompletionTokens = 500,
            TotalTokens = 1500,
            IsEstimated = false,
        };

        Assert.Equal(1500, usage.TotalTokens);
        Assert.False(usage.IsEstimated);
    }

    [Fact]
    public void GatewayStats_ShouldCalculateHitRate()
    {
        var stats = new GatewayStats
        {
            TotalRequests = 100,
            CacheHits = 30,
            CacheMisses = 70,
            CacheHitRate = 0.3,
            TotalPromptTokens = 500000,
            TotalCompletionTokens = 100000,
            CachedTokensSaved = 150000,
            AvgLatencyMs = 850.5,
        };

        Assert.Equal(0.3, stats.CacheHitRate);
        Assert.Equal(150000, stats.CachedTokensSaved);
    }

    [Fact]
    public void RawCacheEntry_ShouldStoreResponseMetadata()
    {
        var entry = new RawCacheEntry
        {
            RequestHash = "sha256:abc",
            ResponseBody = """{"choices":[]}""",
            CreatedAt = DateTimeOffset.UtcNow,
            Model = "gpt-4",
            HasToolCalls = false,
            HasFunctionCalls = false,
        };

        Assert.False(entry.HasToolCalls);
        Assert.False(entry.HasFunctionCalls);
    }
}
