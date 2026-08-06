using System.Net;
using System.Text.Json;
using CacheHub.Core.Gateway;

namespace CacheHub.Core.Gateway.Server;

/// <summary>
/// Minimal Gateway HTTP server using HttpListener.
/// Listens on loopback only. Forwards OpenAI-compatible requests to a provider.
/// </summary>
public sealed class GatewayServer : IDisposable
{
    private readonly GatewayConfig _config;
    private readonly HttpListener _listener;
    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, RawCacheEntry> _cache = new();
    private readonly SingleFlight _singleFlight = new();
    private readonly List<ModelRequestLog> _logs = [];
    private readonly Lock _lock = new();
    private const int MaxCacheEntries = 10_000;
    private const int MaxLogEntries = 1_000;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);
    private long _totalRequests;
    private long _cacheHits;
    private long _cacheMisses;
    private long _totalPromptTokens;
    private long _totalCompletionTokens;
    private long _cachedTokensSaved;
    private double _totalLatencyMs;
    private bool _disposed;

    public GatewayServer(GatewayConfig config)
    {
        _config = config;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://{config.Host}:{config.Port}/");
        _httpClient = new HttpClient();
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        _listener.Start();

        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync();
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (ct.IsCancellationRequested)
            {
                break;
            }
            _ = HandleRequestAsync(ctx, ct);
        }
    }

    public void Stop() => _listener.Stop();

    private async Task HandleRequestAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var req = ctx.Request;
        var resp = ctx.Response;

        try
        {
            var path = req.Url?.AbsolutePath ?? "/";

            if (path == "/v1/models" && req.HttpMethod == "GET")
            {
                await HandleModelsAsync(resp);
                return;
            }

            if (path == "/v1/chat/completions" && req.HttpMethod == "POST")
            {
                await HandleChatCompletionsAsync(req, resp, ct);
                return;
            }

            if (path == "/v1/responses" && req.HttpMethod == "POST")
            {
                await HandleResponsesAsync(req, resp, ct);
                return;
            }

            resp.StatusCode = 404;
            var emptyBytes = System.Text.Encoding.UTF8.GetBytes("{}");
            resp.ContentLength64 = emptyBytes.Length;
            await resp.OutputStream.WriteAsync(emptyBytes, ct);
        }
        catch (Exception ex)
        {
            resp.StatusCode = 500;
            var errorJson = JsonSerializer.Serialize(new { error = new { message = ex.Message } });
            var bytes = System.Text.Encoding.UTF8.GetBytes(errorJson);
            resp.ContentLength64 = bytes.Length;
            await resp.OutputStream.WriteAsync(bytes, ct);
        }
        finally
        {
            resp.Close();
        }
    }

    private async Task HandleModelsAsync(HttpListenerResponse resp)
    {
        // Forward to provider
        var providerResp = await _httpClient.GetAsync($"{_config.ProviderBaseUrl}/v1/models");
        var body = await providerResp.Content.ReadAsStringAsync();
        resp.StatusCode = (int)providerResp.StatusCode;
        resp.ContentType = "application/json";
        var bytes = System.Text.Encoding.UTF8.GetBytes(body);
        resp.ContentLength64 = bytes.Length;
        await resp.OutputStream.WriteAsync(bytes);
    }

    private async Task HandleChatCompletionsAsync(HttpListenerRequest req, HttpListenerResponse resp, CancellationToken ct)
    {
        using var reader = new StreamReader(req.InputStream);
        var requestBody = await reader.ReadToEndAsync(ct);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Interlocked.Increment(ref _totalRequests);

        // Check cache
        var requestHash = ComputeHash(requestBody);
        if (_config.EnableCache && CacheSafetyChecker.IsCacheable(requestBody, "model"))
        {
            RawCacheEntry? cachedEntry;
            lock (_lock)
            {
                _cache.TryGetValue(requestHash, out cachedEntry);
                // TTL check on read
                if (cachedEntry is not null &&
                    DateTimeOffset.UtcNow.Subtract(cachedEntry.CreatedAt) > CacheTtl)
                {
                    _cache.Remove(requestHash);
                    cachedEntry = null;
                }
            }

            if (cachedEntry is not null)
            {
                Interlocked.Increment(ref _cacheHits);
                sw.Stop();
                LogRequest("cached", "model", sw.Elapsed, 200, true, 0, 0);
                var cachedBytes = System.Text.Encoding.UTF8.GetBytes(cachedEntry.ResponseBody);
                resp.StatusCode = 200;
                resp.ContentType = "application/json";
                resp.ContentLength64 = cachedBytes.Length;
                await resp.OutputStream.WriteAsync(cachedBytes, ct);
                return;
            }
        }

        Interlocked.Increment(ref _cacheMisses);

        // Forward to provider (with SingleFlight for safe requests)
        string responseBody;
        int statusCode;

        if (_config.EnableSingleFlight && CacheSafetyChecker.IsCacheable(requestBody, "model"))
        {
            responseBody = await _singleFlight.ExecuteAsync(requestHash, async () =>
            {
                return await ForwardToProviderAsync(requestBody, ct);
            });
            statusCode = 200;
        }
        else
        {
            responseBody = await ForwardToProviderAsync(requestBody, ct);
            statusCode = 200;
        }

        sw.Stop();

        // Cache response if safe
        if (_config.EnableCache && CacheSafetyChecker.IsCacheable(requestBody, "model") &&
            !CacheSafetyChecker.HasToolCalls(responseBody))
        {
            lock (_lock)
            {
                _cache[requestHash] = new RawCacheEntry
                {
                    RequestHash = requestHash,
                    ResponseBody = responseBody,
                    CreatedAt = DateTimeOffset.UtcNow,
                    Model = "model",
                    HasToolCalls = false,
                };
                EvictStaleCacheLocked();
            }
        }

        LogRequest("direct", "model", sw.Elapsed, statusCode, false, 0, 0);

        resp.StatusCode = statusCode;
        resp.ContentType = "application/json";
        var respBytes = System.Text.Encoding.UTF8.GetBytes(responseBody);
        resp.ContentLength64 = respBytes.Length;
        await resp.OutputStream.WriteAsync(respBytes, ct);
    }

    private async Task<string> ForwardToProviderAsync(string requestBody, CancellationToken ct)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Post, $"{_config.ProviderBaseUrl}/v1/chat/completions");
        msg.Content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");
        msg.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.ProviderApiKey);

        var response = await _httpClient.SendAsync(msg, ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>
    /// Handles POST /v1/responses — forwards to provider's responses endpoint.
    /// Similar to chat/completions but uses the responses API path.
    /// </summary>
    private async Task HandleResponsesAsync(HttpListenerRequest req, HttpListenerResponse resp, CancellationToken ct)
    {
        using var reader = new StreamReader(req.InputStream);
        var requestBody = await reader.ReadToEndAsync(ct);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Interlocked.Increment(ref _totalRequests);

        // Forward to provider's /v1/responses endpoint
        using var msg = new HttpRequestMessage(HttpMethod.Post, $"{_config.ProviderBaseUrl}/v1/responses");
        msg.Content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");
        msg.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.ProviderApiKey);

        var response = await _httpClient.SendAsync(msg, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        sw.Stop();
        LogRequest("responses", "model", sw.Elapsed, (int)response.StatusCode, false, 0, 0);

        resp.StatusCode = (int)response.StatusCode;
        resp.ContentType = "application/json";
        var respBytes = System.Text.Encoding.UTF8.GetBytes(responseBody);
        resp.ContentLength64 = respBytes.Length;
        await resp.OutputStream.WriteAsync(respBytes, ct);
    }

    private void LogRequest(string endpoint, string model, TimeSpan latency, int statusCode, bool cached, int promptTokens, int completionTokens)
    {
        lock (_lock)
        {
            _logs.Add(new ModelRequestLog
            {
                Timestamp = DateTimeOffset.UtcNow,
                Model = model,
                Endpoint = endpoint,
                Usage = new ModelUsage { PromptTokens = promptTokens, CompletionTokens = completionTokens, TotalTokens = promptTokens + completionTokens },
                Cached = cached,
                Streaming = false,
                StatusCode = statusCode,
                LatencyMs = (long)latency.TotalMilliseconds,
            });

            // Bound the in-memory log to prevent unbounded growth.
            if (_logs.Count > MaxLogEntries)
                _logs.RemoveRange(0, _logs.Count - MaxLogEntries);

            _totalPromptTokens += promptTokens;
            _totalCompletionTokens += completionTokens;
            _totalLatencyMs += latency.TotalMilliseconds;

            if (cached)
                _cachedTokensSaved += promptTokens;
        }
    }

    private void EvictStaleCacheLocked()
    {
        var now = DateTimeOffset.UtcNow;
        // TTL eviction
        if (_cache.Count > MaxCacheEntries)
        {
            foreach (var key in _cache.Keys.ToList())
            {
                var age = now.Subtract(_cache[key].CreatedAt);
                if (age > CacheTtl)
                    _cache.Remove(key);
            }
        }

        // Count cap: if still overflowing, remove oldest entries.
        while (_cache.Count > MaxCacheEntries && _cache.Count > 0)
        {
            var oldestKey = _cache.MinBy(kvp => kvp.Value.CreatedAt).Key;
            _cache.Remove(oldestKey);
        }
    }

    public GatewayStats GetStats()
    {
        lock (_lock)
        {
            var total = _totalRequests;
            return new GatewayStats
            {
                TotalRequests = total,
                CacheHits = _cacheHits,
                CacheMisses = _cacheMisses,
                CacheHitRate = total > 0 ? (double)_cacheHits / total : 0,
                TotalPromptTokens = _totalPromptTokens,
                TotalCompletionTokens = _totalCompletionTokens,
                CachedTokensSaved = _cachedTokensSaved,
                AvgLatencyMs = total > 0 ? _totalLatencyMs / total : 0,
            };
        }
    }

    private static string ComputeHash(string input)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _listener.Close();
        _httpClient.Dispose();
    }
}
