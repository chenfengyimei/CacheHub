using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using CacheHub.Core.Caching;
using CacheHub.Core.Errors;
using CacheHub.Gateway;
using CacheHub.Gateway.Stats;

namespace CacheHub.Gateway.Server;

/// <summary>
/// Gateway HTTP server using HttpListener.
/// Listens on loopback only. Forwards OpenAI-compatible requests to a provider.
/// Supports SSE streaming, auth, concurrency limits, safe caching, and headers.
/// </summary>
public sealed class GatewayServer : IDisposable
{
    private readonly GatewayConfig _config;
    private readonly HttpListener _listener;
    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, RawCacheEntry> _cache = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<ProviderResponse>>> _inFlight = new();
    private readonly List<ModelRequestLog> _logs = [];
    private readonly Lock _lock = new();
    private readonly SemaphoreSlim? _concurrencyGate;
    private readonly string _accessToken;
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
    private readonly GatewayStatsStore? _statsStore;  // V7-W14: persistent stats

    public GatewayServer(GatewayConfig config)
    {
        _config = config;
        _concurrencyGate = config.MaxConcurrentRequests > 0
            ? new SemaphoreSlim(config.MaxConcurrentRequests, config.MaxConcurrentRequests)
            : null;

        // Security: force loopback — ignore config.Host if it's not a loopback address
        var host = config.Host == "127.0.0.1" || config.Host == "localhost"
            ? "127.0.0.1"
            : "127.0.0.1"; // Always force loopback

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://{host}:{config.Port}/");
        _httpClient = new HttpClient();

        // Security: generate a random access token
        _accessToken = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        // V7-W14: Initialize persistent stats store so Desktop Dashboard can read Gateway stats
        var statsDbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CacheHub", "gateway", "stats.db");
        try
        {
            _statsStore = new GatewayStatsStore(statsDbPath);
        }
        catch
        {
            _statsStore = null; // non-fatal: Gateway works without persistent stats
        }
    }

    /// <summary>
    /// The access token clients must use to authenticate.
    /// </summary>
    public string AccessToken => _accessToken;

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
            // Security: verify authentication
            var authHeader = req.Headers["Authorization"];
            if (string.IsNullOrEmpty(authHeader) || !authHeader.Equals($"Bearer {_accessToken}", StringComparison.OrdinalIgnoreCase))
            {
                resp.StatusCode = 401;
                resp.ContentType = "application/json";
                var errBytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ErrorEnvelope.From(ErrorCode.AuthRequired, "Unauthorized")));
                resp.ContentLength64 = errBytes.Length;
                await resp.OutputStream.WriteAsync(errBytes, ct);
                return;
            }

            var path = req.Url?.AbsolutePath ?? "/";

            if (path == "/v1/models" && req.HttpMethod == "GET")
            {
                await HandleModelsAsync(resp, ct);
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
            var errorJson = JsonSerializer.Serialize(ErrorEnvelope.From(ErrorCode.ProviderError, ex.Message));
            var bytes = System.Text.Encoding.UTF8.GetBytes(errorJson);
            resp.ContentLength64 = bytes.Length;
            await resp.OutputStream.WriteAsync(bytes, ct);
        }
        finally
        {
            resp.Close();
        }
    }

    private async Task HandleModelsAsync(HttpListenerResponse resp, CancellationToken ct)
    {
        var providers = _config.GetAllProviders();
        foreach (var (baseUrl, apiKey) in providers)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/v1/models");
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

                var providerResp = await _httpClient.SendAsync(req, ct);
                var statusCode = (int)providerResp.StatusCode;
                var body = await providerResp.Content.ReadAsStringAsync(ct);

                // On 429/5xx, try next provider instead of returning the error
                if (statusCode == 429 || statusCode >= 500)
                    continue;

                resp.StatusCode = statusCode;
                resp.ContentType = "application/json";
                var bytes = System.Text.Encoding.UTF8.GetBytes(body);
                resp.ContentLength64 = bytes.Length;
                await resp.OutputStream.WriteAsync(bytes, ct);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                continue;
            }
        }

        resp.StatusCode = 502;
        resp.ContentType = "application/json";
        var errBytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            ErrorEnvelope.From(ErrorCode.ProviderUnavailable, "All providers failed for /v1/models")));
        resp.ContentLength64 = errBytes.Length;
        await resp.OutputStream.WriteAsync(errBytes, ct);
    }

    private async Task HandleChatCompletionsAsync(HttpListenerRequest req, HttpListenerResponse resp, CancellationToken ct)
    {
        // V6: Final Offline defense — block provider forwarding at the Gateway level
        if (_config.IsOfflineMode)
        {
            resp.StatusCode = 403;
            resp.ContentType = "application/json";
            var offlineBytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                ErrorEnvelope.From(ErrorCode.SecurityPolicyViolation, "Gateway blocked: security.mode = Offline. No cloud requests allowed.")));
            resp.ContentLength64 = offlineBytes.Length;
            await resp.OutputStream.WriteAsync(offlineBytes, ct);
            return;
        }

        using var reader = new StreamReader(req.InputStream);
        var requestBody = await reader.ReadToEndAsync(ct);

        // Security: enforce max request size
        if (requestBody.Length > _config.MaxRequestSizeBytes)
        {
            resp.StatusCode = 413;
            resp.ContentType = "application/json";
            var errBytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ErrorEnvelope.From(ErrorCode.RequestTooLarge, "Request too large")));
            resp.ContentLength64 = errBytes.Length;
            await resp.OutputStream.WriteAsync(errBytes, ct);
            return;
        }

        // Parse model and stream flag from request
        string model = "unknown";
        bool isStream = false;
        try
        {
            using var doc = JsonDocument.Parse(requestBody);
            if (doc.RootElement.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String)
                model = m.GetString() ?? "unknown";
            if (doc.RootElement.TryGetProperty("stream", out var s) && s.ValueKind == JsonValueKind.True)
                isStream = true;
        }
        catch { }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Interlocked.Increment(ref _totalRequests);

        // V5-W06: Cache key includes endpoint + model + provider identity + request body
        var providerFingerprint = string.Join("|", _config.GetAllProviders().Select(p => p.BaseUrl));
        var requestHash = ComputeHash("/v1/chat/completions" + "|" + model + "|" + providerFingerprint + "|" + requestBody);

        // Check cache (only for non-streaming, cacheable requests)
        if (!isStream && _config.EnableCache && CacheSafetyChecker.IsCacheable(requestBody, model))
        {
            // Use persistent ICacheStore if available, otherwise fall back to in-memory
            if (_config.CacheStore is not null)
            {
                var cached = _config.CacheStore.TryGet(requestHash, CacheType.GatewayResponse);
                if (cached is not null)
                {
                    var bodyBytes = _config.CacheStore.GetBlob(requestHash);
                    if (bodyBytes is not null && bodyBytes.Length > 0)
                    {
                        // V5-W07: Parse the cached envelope to extract body + usage
                        var cachedEnvelope = TryParseCachedGatewayResponse(bodyBytes);
                        if (cachedEnvelope is not null)
                        {
                            Interlocked.Increment(ref _cacheHits);
                            sw.Stop();
                            LogRequest("cached", model, sw.Elapsed, 200, true, cachedEnvelope.PromptTokens, cachedEnvelope.CompletionTokens);
                            var cachedBytes = System.Text.Encoding.UTF8.GetBytes(cachedEnvelope.ResponseBody);
                            resp.StatusCode = 200;
                            resp.ContentType = "application/json";
                            resp.ContentLength64 = cachedBytes.Length;
                            await resp.OutputStream.WriteAsync(cachedBytes, ct);
                            return;
                        }
                    }
                }
            }
            else
            {
                RawCacheEntry? cachedEntry;
                lock (_lock)
                {
                    _cache.TryGetValue(requestHash, out cachedEntry);
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
                    // Track saved tokens from the original response
                    LogRequest("cached", model, sw.Elapsed, 200, true, cachedEntry.PromptTokens, cachedEntry.CompletionTokens);
                    var cachedBytes = System.Text.Encoding.UTF8.GetBytes(cachedEntry.ResponseBody);
                    resp.StatusCode = 200;
                    resp.ContentType = "application/json";
                    resp.ContentLength64 = cachedBytes.Length;
                    await resp.OutputStream.WriteAsync(cachedBytes, ct);
                    return;
                }
            }
        }

        Interlocked.Increment(ref _cacheMisses);

        // R5-W003: Concurrency limiting
        if (_concurrencyGate is not null)
            await _concurrencyGate.WaitAsync(ct);

        try
        {
            // R5-W002: SSE streaming passthrough
            if (isStream)
            {
                await HandleStreamingAsync(requestBody, model, resp, ct);
                return;
            }

            // Non-streaming: forward to provider (with atomic SingleFlight)
            ProviderResponse providerResponse;

            if (_config.EnableSingleFlight && CacheSafetyChecker.IsCacheable(requestBody, model))
            {
                var lazy = _inFlight.GetOrAdd(requestHash,
                    _ => new Lazy<Task<ProviderResponse>>(() => ForwardToProviderWithStatusAsync(requestBody, model, ct)));
                providerResponse = await lazy.Value;
                _inFlight.TryRemove(requestHash, out _);
            }
            else
            {
                providerResponse = await ForwardToProviderWithStatusAsync(requestBody, model, ct);
            }

            sw.Stop();

            // Extract usage from response (R5-W004)
            var (promptTokens, completionTokens) = ExtractUsage(providerResponse.Body);

            // Cache response ONLY if 2xx, cacheable, and no tool calls (R5-W005)
            if (_config.EnableCache &&
                providerResponse.StatusCode >= 200 && providerResponse.StatusCode < 300 &&
                CacheSafetyChecker.IsCacheable(requestBody, model) &&
                !CacheSafetyChecker.HasToolCalls(providerResponse.Body))
            {
                if (_config.CacheStore is not null)
                {
                    // V5-W07: Store response + usage metadata as a JSON envelope
                    var envelope = JsonSerializer.Serialize(new CachedGatewayResponse
                    {
                        ResponseBody = providerResponse.Body,
                        PromptTokens = promptTokens,
                        CompletionTokens = completionTokens,
                    });
                    var bodyBytes = System.Text.Encoding.UTF8.GetBytes(envelope);
                    _config.CacheStore.Put(new CacheEntry
                    {
                        Key = requestHash,
                        Type = CacheType.GatewayResponse,
                        Version = "gateway-v2",
                        CreatedAt = DateTimeOffset.UtcNow,
                        SizeBytes = bodyBytes.Length,
                        TtlSeconds = (int)CacheTtl.TotalSeconds,
                    }, bodyBytes);
                }
                else
                {
                    // Fall back to in-memory cache
                    lock (_lock)
                    {
                        _cache[requestHash] = new RawCacheEntry
                        {
                            RequestHash = requestHash,
                            ResponseBody = providerResponse.Body,
                            CreatedAt = DateTimeOffset.UtcNow,
                            Model = model,
                            HasToolCalls = false,
                            PromptTokens = promptTokens,
                            CompletionTokens = completionTokens,
                        };
                        EvictStaleCacheLocked();
                    }
                }
            }

            LogRequest("chat/completions", model, sw.Elapsed, providerResponse.StatusCode, false, promptTokens, completionTokens);

            // R5-W004: Forward provider status code and select headers
            resp.StatusCode = providerResponse.StatusCode;
            resp.ContentType = "application/json";
            ForwardAllowedHeaders(providerResponse.Headers, resp);
            var respBytes = System.Text.Encoding.UTF8.GetBytes(providerResponse.Body);
            resp.ContentLength64 = respBytes.Length;
            await resp.OutputStream.WriteAsync(respBytes, ct);
        }
        finally
        {
            _concurrencyGate?.Release();
        }
    }

    /// <summary>
    /// R5-W002: SSE streaming passthrough — streams provider response to client in real-time.
    /// </summary>
    private async Task HandleStreamingAsync(string requestBody, string model, HttpListenerResponse resp, CancellationToken ct)
    {
        var providers = _config.GetAllProviders();
        Exception? lastError = null;

        foreach (var (baseUrl, apiKey) in providers)
        {
            try
            {
                using var msg = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/chat/completions");
                msg.Content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");
                msg.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

                var response = await _httpClient.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct);
                var statusCode = (int)response.StatusCode;

                // On 429 or 5xx, try next provider (but only before streaming starts)
                if (statusCode == 429 || statusCode >= 500)
                {
                    lastError = new HttpRequestException($"Provider {baseUrl} returned {statusCode}");
                    response.Dispose();
                    continue;
                }

                resp.StatusCode = statusCode;
                resp.ContentType = response.Content.Headers.ContentType?.ToString() ?? "text/event-stream";
                ForwardAllowedHeaders(response.Headers, resp);

                // Stream the response body to client while parsing SSE events for Usage
                var streamSw = System.Diagnostics.Stopwatch.StartNew();
                using var stream = await response.Content.ReadAsStreamAsync(ct);
                var (promptTokens, completionTokens) = await StreamAndParseUsageAsync(stream, resp.OutputStream, ct);
                resp.OutputStream.Flush();
                streamSw.Stop();

                // Log streaming request with parsed usage and true latency
                LogRequest("chat/completions", model, streamSw.Elapsed, statusCode, false, promptTokens, completionTokens, streaming: true);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
                continue;
            }
        }

        // All providers failed for streaming
        resp.StatusCode = 502;
        resp.ContentType = "application/json";
        var errBytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            ErrorEnvelope.From(ErrorCode.ProviderUnavailable, $"All providers failed. Last error: {lastError?.Message ?? "unknown"}")));
        resp.ContentLength64 = errBytes.Length;
        await resp.OutputStream.WriteAsync(errBytes, ct);
    }

    /// <summary>
    /// R5-W004: Forward allowed response headers from provider to client.
    /// </summary>
    private void ForwardAllowedHeaders(System.Net.Http.Headers.HttpResponseHeaders? providerHeaders, HttpListenerResponse resp)
    {
        if (providerHeaders is null) return;
        foreach (var allowed in _config.AllowedResponseHeaders)
        {
            if (providerHeaders.TryGetValues(allowed, out var values))
            {
                resp.Headers[allowed] = string.Join(", ", values);
            }
        }
    }

    /// <summary>
    /// Streams SSE data from provider to client while parsing usage from final chunk.
    /// SSE format: lines starting with "data: " containing JSON.
    /// The final chunk often includes a "usage" object with prompt/completion tokens.
    /// </summary>
    private static async Task<(int promptTokens, int completionTokens)> StreamAndParseUsageAsync(
        Stream inputStream, Stream outputStream, CancellationToken ct)
    {
        var promptTokens = 0;
        var completionTokens = 0;
        var buffer = new byte[8192];
        var lineBuffer = new System.Text.StringBuilder();

        int bytesRead;
        while ((bytesRead = await inputStream.ReadAsync(buffer, ct)) > 0)
        {
            // Forward to client immediately
            await outputStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);

            // Parse for usage: accumulate text and check for data lines
            var text = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
            lineBuffer.Append(text);

            // Process complete lines
            while (true)
            {
                var newlineIdx = lineBuffer.ToString().IndexOf('\n');
                if (newlineIdx < 0) break;

                var line = lineBuffer.ToString(0, newlineIdx).TrimEnd('\r');
                lineBuffer.Remove(0, newlineIdx + 1);

                // Check for data lines containing usage
                if (line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    var data = line["data: ".Length..];
                    if (data != "[DONE]")
                    {
                        TryParseUsageFromSseChunk(data, ref promptTokens, ref completionTokens);
                    }
                }
            }
        }

        return (promptTokens, completionTokens);
    }

    /// <summary>
    /// Attempts to extract usage tokens from an SSE data chunk.
    /// </summary>
    private static void TryParseUsageFromSseChunk(string data, ref int promptTokens, ref int completionTokens)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            if (doc.RootElement.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("prompt_tokens", out var p) && p.ValueKind == JsonValueKind.Number)
                    promptTokens = p.GetInt32();
                if (usage.TryGetProperty("completion_tokens", out var c) && c.ValueKind == JsonValueKind.Number)
                    completionTokens = c.GetInt32();
            }
        }
        catch { }
    }

    /// <summary>
    /// R5-W004: Extract usage tokens from provider response body.
    /// </summary>
    private static (int prompt, int completion) ExtractUsage(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("usage", out var usage))
            {
                var prompt = usage.TryGetProperty("prompt_tokens", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : 0;
                var completion = usage.TryGetProperty("completion_tokens", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0;
                return (prompt, completion);
            }
        }
        catch { }
        return (0, 0);
    }

    private async Task<ProviderResponse> ForwardToProviderWithStatusAsync(string requestBody, string model, CancellationToken ct)
    {
        var providers = _config.GetAllProviders();
        Exception? lastError = null;

        foreach (var (baseUrl, apiKey) in providers)
        {
            try
            {
                using var msg = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/chat/completions");
                msg.Content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");
                msg.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

                var response = await _httpClient.SendAsync(msg, ct);
                var body = await response.Content.ReadAsStringAsync(ct);
                var statusCode = (int)response.StatusCode;

                // On 429 (rate limit) or 5xx (server error), try next provider
                if (statusCode == 429 || statusCode >= 500)
                {
                    lastError = new HttpRequestException($"Provider {baseUrl} returned {statusCode}");
                    continue;
                }

                return new ProviderResponse(statusCode, body, response.Headers);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
                continue;
            }
        }

        // All providers failed — return last error as 502
        var errorBody = JsonSerializer.Serialize(ErrorEnvelope.From(
            ErrorCode.ProviderUnavailable,
            $"All providers failed. Last error: {lastError?.Message ?? "unknown"}"));
        return new ProviderResponse(502, errorBody, null);
    }

    /// <summary>
    /// Handles POST /v1/responses — forwards to provider's responses endpoint.
    /// </summary>
    private async Task HandleResponsesAsync(HttpListenerRequest req, HttpListenerResponse resp, CancellationToken ct)
    {
        // V6: Final Offline defense — block provider forwarding at the Gateway level
        if (_config.IsOfflineMode)
        {
            resp.StatusCode = 403;
            resp.ContentType = "application/json";
            var offlineBytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                ErrorEnvelope.From(ErrorCode.SecurityPolicyViolation, "Gateway blocked: security.mode = Offline. No cloud requests allowed.")));
            resp.ContentLength64 = offlineBytes.Length;
            await resp.OutputStream.WriteAsync(offlineBytes, ct);
            return;
        }

        using var reader = new StreamReader(req.InputStream);
        var requestBody = await reader.ReadToEndAsync(ct);

        // Security: enforce max request size
        if (requestBody.Length > _config.MaxRequestSizeBytes)
        {
            resp.StatusCode = 413;
            resp.ContentType = "application/json";
            var errBytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ErrorEnvelope.From(ErrorCode.RequestTooLarge, "Request too large")));
            resp.ContentLength64 = errBytes.Length;
            await resp.OutputStream.WriteAsync(errBytes, ct);
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Interlocked.Increment(ref _totalRequests);

        string model = "unknown";
        var isStream = false;
        try
        {
            using var doc = JsonDocument.Parse(requestBody);
            if (doc.RootElement.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String)
                model = m.GetString() ?? "unknown";
            if (doc.RootElement.TryGetProperty("stream", out var s) && s.ValueKind == JsonValueKind.True)
                isStream = true;
        }
        catch { }

        // R5-W003: Concurrency limiting
        if (_concurrencyGate is not null)
            await _concurrencyGate.WaitAsync(ct);

        try
        {
            // Forward to provider's /v1/responses endpoint with fallback
            var providers = _config.GetAllProviders();
            Exception? lastError = null;

            foreach (var (baseUrl, apiKey) in providers)
            {
                try
                {
                    // Use streaming mode if requested
                    var completionOption = isStream ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead;
                    using var msg = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/responses");
                    msg.Content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");
                    msg.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

                    var response = await _httpClient.SendAsync(msg, completionOption, ct);
                    var statusCode = (int)response.StatusCode;

                    // On 429 or 5xx, try next provider
                    if (statusCode == 429 || statusCode >= 500)
                    {
                        lastError = new HttpRequestException($"Provider {baseUrl} returned {statusCode}");
                        response.Dispose();
                        continue;
                    }

                    if (isStream)
                    {
                        // Stream SSE response to client with Usage parsing
                        resp.StatusCode = statusCode;
                        resp.ContentType = response.Content.Headers.ContentType?.ToString() ?? "text/event-stream";
                        ForwardAllowedHeaders(response.Headers, resp);

                        using var stream = await response.Content.ReadAsStreamAsync(ct);
                        var (promptTokens, completionTokens) = await StreamAndParseUsageAsync(stream, resp.OutputStream, ct);
                        resp.OutputStream.Flush();

                        sw.Stop();
                        LogRequest("responses", model, sw.Elapsed, statusCode, false, promptTokens, completionTokens);
                        return;
                    }
                    else
                    {
                        var responseBody = await response.Content.ReadAsStringAsync(ct);

                        sw.Stop();
                        LogRequest("responses", model, sw.Elapsed, statusCode, false, 0, 0);

                        resp.StatusCode = statusCode;
                        resp.ContentType = "application/json";
                        ForwardAllowedHeaders(response.Headers, resp);
                        var respBytes = System.Text.Encoding.UTF8.GetBytes(responseBody);
                        resp.ContentLength64 = respBytes.Length;
                        await resp.OutputStream.WriteAsync(respBytes, ct);
                        return;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lastError = ex;
                    continue;
                }
            }

            // All providers failed
            sw.Stop();
            resp.StatusCode = 502;
            resp.ContentType = "application/json";
            var errBytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                ErrorEnvelope.From(ErrorCode.ProviderUnavailable, $"All providers failed. Last error: {lastError?.Message ?? "unknown"}")));
            resp.ContentLength64 = errBytes.Length;
            await resp.OutputStream.WriteAsync(errBytes, ct);
        }
        finally
        {
            _concurrencyGate?.Release();
        }
    }

    private void LogRequest(string endpoint, string model, TimeSpan latency, int statusCode, bool cached, int promptTokens, int completionTokens, bool streaming = false)
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
                Streaming = streaming,
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

        // V7-W14: Persist to SQLite so Desktop Dashboard can read cross-process Gateway stats
        _statsStore?.RecordRequest(endpoint, model, statusCode, cached, streaming,
            promptTokens, completionTokens, (long)latency.TotalMilliseconds);
    }

    private void EvictStaleCacheLocked()
    {
        var now = DateTimeOffset.UtcNow;

        // TTL eviction: always check for expired entries (not just when over cap)
        var expiredKeys = _cache
            .Where(kvp => now.Subtract(kvp.Value.CreatedAt) > CacheTtl)
            .Select(kvp => kvp.Key)
            .ToList();
        foreach (var key in expiredKeys)
            _cache.Remove(key);

        // Count cap + byte cap eviction (R5-W006: byte LRU)
        if (_config.MaxCacheBytes > 0)
        {
            var totalBytes = _cache.Values.Sum(e => System.Text.Encoding.UTF8.GetByteCount(e.ResponseBody));
            if (totalBytes > _config.MaxCacheBytes)
            {
                // Evict oldest entries until under byte cap
                foreach (var kvp in _cache.OrderBy(k => k.Value.CreatedAt).ToList())
                {
                    _cache.Remove(kvp.Key);
                    totalBytes -= System.Text.Encoding.UTF8.GetByteCount(kvp.Value.ResponseBody);
                    if (totalBytes <= _config.MaxCacheBytes) break;
                }
            }
        }

        if (_cache.Count > MaxCacheEntries)
        {
            var toRemove = _cache
                .OrderBy(kvp => kvp.Value.CreatedAt)
                .Take(_cache.Count - MaxCacheEntries)
                .Select(kvp => kvp.Key)
                .ToList();
            foreach (var key in toRemove)
                _cache.Remove(key);
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

    public sealed record ProviderResponse(
        int StatusCode,
        string Body,
        System.Net.Http.Headers.HttpResponseHeaders? Headers = null);

    /// <summary>
    /// V5-W07: Envelope for persistent gateway cache — stores response body + usage metadata.
    /// </summary>
    public sealed record CachedGatewayResponse
    {
        public required string ResponseBody { get; init; }
        public required int PromptTokens { get; init; }
        public required int CompletionTokens { get; init; }
    }

    /// <summary>
    /// V5-W07: Parse a cached gateway response envelope from blob bytes.
    /// </summary>
    private static CachedGatewayResponse? TryParseCachedGatewayResponse(byte[] blob)
    {
        try
        {
            var json = System.Text.Encoding.UTF8.GetString(blob);
            return JsonSerializer.Deserialize<CachedGatewayResponse>(json);
        }
        catch { return null; }
    }
}
