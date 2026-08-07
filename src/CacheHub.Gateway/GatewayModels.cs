using System.Net;
using System.Text.Json;

namespace CacheHub.Gateway;

/// <summary>
/// Configuration for the Gateway process.
/// </summary>
public sealed record GatewayConfig
{
    public required string ProviderBaseUrl { get; init; }
    public required string ProviderApiKey { get; init; }
    public int Port { get; init; } = 5218;
    public string Host { get; init; } = "127.0.0.1";
    public bool EnableCache { get; init; } = true;
    public bool EnableSingleFlight { get; init; } = true;
    public int MaxRequestSizeBytes { get; init; } = 10 * 1024 * 1024;

    /// <summary>Maximum concurrent in-flight provider requests (0 = unlimited).</summary>
    public int MaxConcurrentRequests { get; init; } = 32;

    /// <summary>Maximum total cache size in bytes (0 = unlimited).</summary>
    public long MaxCacheBytes { get; init; } = 64 * 1024 * 1024;

    /// <summary>Additional fallback providers. Primary provider is always first.</summary>
    public IReadOnlyList<FallbackProvider> FallbackProviders { get; init; } = [];

    /// <summary>Allowed response headers to forward to clients. Empty = forward all safe headers.</summary>
    public IReadOnlyList<string> AllowedResponseHeaders { get; init; } =
    [
        "Content-Type", "Retry-After", "X-Request-ID", "X-RateLimit-Limit",
        "X-RateLimit-Remaining", "X-RateLimit-Reset",
    ];

    /// <summary>All providers in order: primary + fallbacks.</summary>
    public IReadOnlyList<(string BaseUrl, string ApiKey)> GetAllProviders()
    {
        var list = new List<(string, string)> { (ProviderBaseUrl, ProviderApiKey) };
        foreach (var fp in FallbackProviders)
            list.Add((fp.BaseUrl, fp.ApiKey));
        return list;
    }
}

/// <summary>
/// A fallback provider for multi-provider routing.
/// </summary>
public sealed record FallbackProvider
{
    public required string BaseUrl { get; init; }
    public required string ApiKey { get; init; }
}

/// <summary>
/// Model usage statistics for a request.
/// </summary>
public sealed record ModelUsage
{
    public required int PromptTokens { get; init; }
    public required int CompletionTokens { get; init; }
    public required int TotalTokens { get; init; }
    public bool IsEstimated { get; init; }
}

/// <summary>
/// A logged model request with metadata.
/// </summary>
public sealed record ModelRequestLog
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string Model { get; init; }
    public required string Endpoint { get; init; }
    public required ModelUsage Usage { get; init; }
    public bool Cached { get; init; }
    public bool Streaming { get; init; }
    public string? WorkspaceId { get; init; }
    public string? ContextPackageId { get; init; }
    public int StatusCode { get; init; }
    public long LatencyMs { get; init; }
}

/// <summary>
/// Raw exact cache entry: stores the exact request hash and response.
/// Only for safe, no-tool-call, deterministic requests.
/// </summary>
public sealed record RawCacheEntry
{
    public required string RequestHash { get; init; }
    public required string ResponseBody { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required string Model { get; init; }
    public bool HasToolCalls { get; init; }
    public bool HasFunctionCalls { get; init; }
}

/// <summary>
/// Statistics for Gateway operations.
/// </summary>
public sealed record GatewayStats
{
    public required long TotalRequests { get; init; }
    public required long CacheHits { get; init; }
    public required long CacheMisses { get; init; }
    public required double CacheHitRate { get; init; }
    public required long TotalPromptTokens { get; init; }
    public required long TotalCompletionTokens { get; init; }
    public required long CachedTokensSaved { get; init; }
    public required double AvgLatencyMs { get; init; }
}

/// <summary>
/// Checks if a request is safe to cache (no tools, no functions, no high temperature).
/// </summary>
public static class CacheSafetyChecker
{
    public static bool IsCacheable(string requestBody, string model)
    {
        try
        {
            using var doc = JsonDocument.Parse(requestBody);
            var root = doc.RootElement;

            // Reject if tools or functions are present
            if (root.TryGetProperty("tools", out _) || root.TryGetProperty("functions", out _))
                return false;

            // Reject if temperature is too high
            if (root.TryGetProperty("temperature", out var temp) && temp.ValueKind == JsonValueKind.Number && temp.GetDouble() > 0.7)
                return false;

            // Reject if stream is true (streaming responses not cached in v1)
            if (root.TryGetProperty("stream", out var stream) && stream.ValueKind == JsonValueKind.True)
                return false;

            // Reject if no-cache header is present
            if (root.TryGetProperty("metadata", out var meta) &&
                meta.TryGetProperty("no_cache", out var nc) && nc.ValueKind == JsonValueKind.True)
                return false;

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static bool HasToolCalls(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("choices", out var choices))
            {
                foreach (var choice in choices.EnumerateArray())
                {
                    if (choice.TryGetProperty("message", out var msg) &&
                        msg.TryGetProperty("tool_calls", out _))
                        return true;
                }
            }
            return false;
        }
        catch { return true; } // Treat unparseable as unsafe
    }
}

/// <summary>
/// SingleFlight: deduplicates concurrent identical requests.
/// Only for safe (no-tool-call) requests.
/// </summary>
public sealed class SingleFlight
{
    private readonly Dictionary<string, Task<string>> _inFlight = new();
    private readonly Lock _lock = new();

    public async Task<string> ExecuteAsync(string key, Func<Task<string>> factory)
    {
        Task<string>? existing;
        lock (_lock)
        {
            _inFlight.TryGetValue(key, out existing);
        }

        if (existing is not null)
            return await existing;

        var task = factory();
        lock (_lock) { _inFlight[key] = task; }

        try
        {
            return await task;
        }
        finally
        {
            lock (_lock) { _inFlight.Remove(key); }
        }
    }

    public int InFlightCount
    {
        get { lock (_lock) return _inFlight.Count; }
    }
}
