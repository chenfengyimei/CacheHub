using System.Text.Json;

namespace CacheHub.Cli.Commands;

/// <summary>
/// IAgentModelExecutor backed by the local / remote Gateway.
/// Calls POST {gatewayUrl}/v1/chat/completions with the assembled prompt,
/// parses the model response + usage tokens.
/// Enables real Agent Benchmark runs against actual models.
/// </summary>
public sealed class GatewayAgentModelExecutor : Core.Benchmarks.Agent.IAgentModelExecutor, IDisposable
{
    private readonly string _gatewayUrl;
    private readonly string _gatewayToken;
    private readonly string _model;
    private readonly HttpClient _http;
    private readonly double? _overrideInputPer1M;
    private readonly double? _overrideOutputPer1M;
    private bool _disposed;

    public GatewayAgentModelExecutor(
        string gatewayUrl,
        string gatewayToken,
        string model,
        double? overrideInputPer1M = null,
        double? overrideOutputPer1M = null)
    {
        _gatewayUrl = gatewayUrl.TrimEnd('/');
        _gatewayToken = gatewayToken;
        _model = model;
        _overrideInputPer1M = overrideInputPer1M;
        _overrideOutputPer1M = overrideOutputPer1M;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    public string ModelId => _model;

    public async Task<Core.Benchmarks.Agent.AgentModelResponse> GenerateAsync(
        string systemPrompt, string userContent, CancellationToken ct = default)
    {
        var requestBody = JsonSerializer.Serialize(new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userContent },
            },
        });

        using var msg = new HttpRequestMessage(HttpMethod.Post, $"{_gatewayUrl}/v1/chat/completions");
        msg.Content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");
        if (!string.IsNullOrEmpty(_gatewayToken))
            msg.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _gatewayToken);

        var resp = await _http.SendAsync(msg, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Gateway returned {resp.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";

        var promptTokens = 0;
        var completionTokens = 0;
        if (doc.RootElement.TryGetProperty("usage", out var usage))
        {
            promptTokens = usage.TryGetProperty("prompt_tokens", out var p) ? p.GetInt32() : 0;
            completionTokens = usage.TryGetProperty("completion_tokens", out var c) ? c.GetInt32() : 0;
        }

        return new Core.Benchmarks.Agent.AgentModelResponse
        {
            Content = content,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            Cost = EstimateCost(promptTokens, completionTokens, _model),
        };
    }

    /// <summary>
    /// V6: Per-model cost estimation (USD per 1M tokens).
    /// Priority: constructor override → env vars (CACHEHUB_MODEL_INPUT_PRICE/OUTPUT_PRICE)
    /// → built-in pricing table → GPT-4o-class fallback.
    /// This lets users plug in their actual negotiated price (review #17).
    /// </summary>
    private double? EstimateCost(int promptTokens, int completionTokens, string model)
    {
        var inputPer1M = _overrideInputPer1M;
        var outputPer1M = _overrideOutputPer1M;

        if (inputPer1M is null || outputPer1M is null)
        {
            var (envIn, envOut) = ReadPricingFromEnv();
            inputPer1M ??= envIn;
            outputPer1M ??= envOut;
        }

        if (inputPer1M is null || outputPer1M is null)
        {
            (inputPer1M, outputPer1M) = GetModelPricing(model);
        }

        return (promptTokens / 1_000_000.0) * inputPer1M.Value
             + (completionTokens / 1_000_000.0) * outputPer1M.Value;
    }

    /// <summary>
    /// Optional per-run pricing override via environment variables, so the same
    /// binary can be benchmarked against the user's actual API billing rate.
    /// Format: comma-separated "inputPer1M,outputPer1M" (e.g. "3.0,15.0").
    /// </summary>
    private static (double?, double?) ReadPricingFromEnv()
    {
        var raw = Environment.GetEnvironmentVariable("CACHEHUB_MODEL_PRICE");
        if (string.IsNullOrWhiteSpace(raw)) return (null, null);

        var parts = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return (null, null);
        if (!double.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out var inP)) return (null, null);
        if (!double.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out var outP)) return (null, null);
        return (inP, outP);
    }

    private static (double inputPer1M, double outputPer1M) GetModelPricing(string model)
    {
        // Pricing as of 2026-08 (approximate, per 1M tokens)
        var m = model.ToLowerInvariant();
        return m switch
        {
            "gpt-4o" => (2.50, 10.00),
            "gpt-4o-mini" => (0.15, 0.60),
            "gpt-4-turbo" => (10.00, 30.00),
            "gpt-3.5-turbo" => (0.50, 1.50),
            "claude-3-5-sonnet" or "claude-3-5-sonnet-20241022" => (3.00, 15.00),
            "claude-3-5-haiku" or "claude-3-5-haiku-20241022" => (0.80, 4.00),
            "claude-3-opus" => (15.00, 75.00),
            "gemini-1.5-pro" => (1.25, 5.00),
            "gemini-1.5-flash" => (0.075, 0.30),
            "deepseek-chat" or "deepseek-coder" => (0.14, 0.28),
            "qwen-plus" => (0.40, 1.20),
            "qwen-turbo" => (0.05, 0.20),
            _ => (2.50, 10.00), // default: GPT-4o-class
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }
}
