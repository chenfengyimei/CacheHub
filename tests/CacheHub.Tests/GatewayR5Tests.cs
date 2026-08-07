using CacheHub.Core.Gateway;
using CacheHub.Core.Gateway.Server;

namespace CacheHub.Tests;

/// <summary>
/// Tests for R5 Gateway enhancements: streaming, concurrency, headers, cache safety.
/// </summary>
public class GatewayR5Tests
{
    [Fact]
    public void GatewayConfig_DefaultConcurrency_ShouldBe32()
    {
        var config = new GatewayConfig
        {
            ProviderBaseUrl = "https://api.example.com",
            ProviderApiKey = "test-key",
        };

        Assert.Equal(32, config.MaxConcurrentRequests);
        Assert.Equal(64 * 1024 * 1024, config.MaxCacheBytes);
    }

    [Fact]
    public void GatewayConfig_AllowedResponseHeaders_IncludesRetryAfter()
    {
        var config = new GatewayConfig
        {
            ProviderBaseUrl = "https://api.example.com",
            ProviderApiKey = "test-key",
        };

        Assert.Contains("Retry-After", config.AllowedResponseHeaders);
        Assert.Contains("X-Request-ID", config.AllowedResponseHeaders);
        Assert.Contains("X-RateLimit-Limit", config.AllowedResponseHeaders);
    }

    [Fact]
    public void CacheSafetyChecker_RejectsStreamingRequests()
    {
        var requestBody = """{"model":"gpt-4","stream":true,"messages":[]}""";
        Assert.False(CacheSafetyChecker.IsCacheable(requestBody, "gpt-4"));
    }

    [Fact]
    public void CacheSafetyChecker_RejectsToolCalls()
    {
        var requestBody = """{"model":"gpt-4","tools":[{"type":"function"}],"messages":[]}""";
        Assert.False(CacheSafetyChecker.IsCacheable(requestBody, "gpt-4"));
    }

    [Fact]
    public void CacheSafetyChecker_RejectsHighTemperature()
    {
        var requestBody = """{"model":"gpt-4","temperature":1.0,"messages":[]}""";
        Assert.False(CacheSafetyChecker.IsCacheable(requestBody, "gpt-4"));
    }

    [Fact]
    public void CacheSafetyChecker_AcceptsSafeRequest()
    {
        var requestBody = """{"model":"gpt-4","temperature":0,"messages":[{"role":"user","content":"Hello"}]}""";
        Assert.True(CacheSafetyChecker.IsCacheable(requestBody, "gpt-4"));
    }

    [Fact]
    public void CacheSafetyChecker_DetectsToolCallsInResponse()
    {
        var responseBody = """{"choices":[{"message":{"tool_calls":[{"id":"call_1","type":"function","function":{"name":"test"}}]}}]}""";
        Assert.True(CacheSafetyChecker.HasToolCalls(responseBody));
    }

    [Fact]
    public void CacheSafetyChecker_NoToolCallsInResponse()
    {
        var responseBody = """{"choices":[{"message":{"content":"Hello"}}]}""";
        Assert.False(CacheSafetyChecker.HasToolCalls(responseBody));
    }

    [Fact]
    public async Task SingleFlight_ExecuteAsync_ShouldDeduplicate()
    {
        var sf = new SingleFlight();
        var callCount = 0;

        // Simulate two concurrent calls with the same key
        var task1 = sf.ExecuteAsync("key1", async () =>
        {
            await Task.Delay(50);
            Interlocked.Increment(ref callCount);
            return "result1";
        });

        var task2 = sf.ExecuteAsync("key1", async () =>
        {
            await Task.Delay(50);
            Interlocked.Increment(ref callCount);
            return "result2";
        });

        await Task.WhenAll(task1, task2);

        // SingleFlight should deduplicate results
        var r1 = await task1;
        var r2 = await task2;
        Assert.NotEmpty(r1);
        Assert.NotEmpty(r2);
    }

    [Fact]
    public void GatewayServer_AccessToken_IsGenerated()
    {
        using var server = new GatewayServer(new GatewayConfig
        {
            ProviderBaseUrl = "https://api.example.com",
            ProviderApiKey = "test-key",
            Port = 15218,
        });

        Assert.NotEmpty(server.AccessToken);
        Assert.True(server.AccessToken.Length >= 32);
    }

    [Fact]
    public void GatewayStats_Default_ShouldBeZero()
    {
        using var server = new GatewayServer(new GatewayConfig
        {
            ProviderBaseUrl = "https://api.example.com",
            ProviderApiKey = "test-key",
            Port = 15219,
        });

        var stats = server.GetStats();
        Assert.Equal(0, stats.TotalRequests);
        Assert.Equal(0, stats.CacheHits);
    }
}
