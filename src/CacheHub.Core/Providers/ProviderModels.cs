using System.Text.Json.Serialization;

namespace CacheHub.Core.Providers;

/// <summary>
/// Provider contract: request transformation, streaming, errors, model list, usage.
/// </summary>
public interface IProvider
{
    string Id { get; }
    string Version { get; }
    string BaseUrl { get; }
    Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken ct = default);
    Task<ProviderResponse> SendAsync(ProviderRequest request, CancellationToken ct = default);
}

/// <summary>
/// Model metadata with capabilities and pricing.
/// </summary>
public sealed record ModelInfo
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required int ContextWindow { get; init; }
    public bool SupportsTools { get; init; }
    public bool SupportsStreaming { get; init; }
    public bool SupportsStructuredOutput { get; init; }
    public string? TokenizerId { get; init; }
    public PricingInfo? Pricing { get; init; }
}

/// <summary>
/// Pricing for a model, versioned with effective date.
/// </summary>
public sealed record PricingInfo
{
    public required string Version { get; init; }
    public required DateTimeOffset EffectiveDate { get; init; }
    public required decimal InputPricePer1M { get; init; }
    public required decimal OutputPricePer1M { get; init; }
    public decimal? CachedInputPricePer1M { get; init; }
    public string Currency { get; init; } = "USD";
}

/// <summary>
/// Request to a provider.
/// </summary>
public sealed record ProviderRequest
{
    public required string Model { get; init; }
    public required string Body { get; init; }
    public bool Stream { get; init; }
    public string? ApiKey { get; init; }
    public IReadOnlyDictionary<string, string>? ExtraHeaders { get; init; }
}

/// <summary>
/// Response from a provider.
/// </summary>
public sealed record ProviderResponse
{
    public required int StatusCode { get; init; }
    public required string Body { get; init; }
    public required bool Success { get; init; }
    public ModelUsageInfo? Usage { get; init; }
    public string? ErrorMessage { get; init; }
    public TimeSpan Latency { get; init; }
}

public sealed record ModelUsageInfo
{
    public required int PromptTokens { get; init; }
    public required int CompletionTokens { get; init; }
    public required int TotalTokens { get; init; }
}

/// <summary>
/// Credential reference: stores only a credential ID, never the secret.
/// </summary>
public sealed record CredentialRef
{
    public required string Id { get; init; }
    public required string ProviderId { get; init; }
    public string? KeyName { get; init; }
}

/// <summary>
/// Model alias mapping.
/// </summary>
public sealed record ModelAlias
{
    public required string Alias { get; init; }
    public required string ActualModelId { get; init; }
    public string? ProviderId { get; init; }
}

/// <summary>
/// Routing strategy for selecting a provider.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RoutingStrategy
{
    Explicit,
    RoundRobin,
    LeastLatency,
    Fallback,
}

/// <summary>
/// Provider routing configuration.
/// </summary>
public sealed record RoutingConfig
{
    public required RoutingStrategy Strategy { get; init; }
    public required IReadOnlyList<string> ProviderOrder { get; init; }
    public bool HealthCheckEnabled { get; init; } = true;
    public TimeSpan HealthCheckInterval { get; init; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Budget limits for a workspace or provider.
/// </summary>
public sealed record BudgetLimit
{
    public required string Scope { get; init; } // "workspace:<id>" or "provider:<id>"
    public decimal? DailyLimitUsd { get; init; }
    public decimal? MonthlyLimitUsd { get; init; }
    public int? MaxRequestsPerDay { get; init; }
}

/// <summary>
/// Usage record for budget tracking and audit.
/// </summary>
public sealed record UsageRecord
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string ProviderId { get; init; }
    public required string Model { get; init; }
    public required int PromptTokens { get; init; }
    public required int CompletionTokens { get; init; }
    public required decimal EstimatedCostUsd { get; init; }
    public bool Cached { get; init; }
    public string? WorkspaceId { get; init; }
}

/// <summary>
/// Cost calculator using versioned pricing tables.
/// </summary>
public static class CostCalculator
{
    public static decimal CalculateCost(ModelInfo model, int promptTokens, int completionTokens, bool cached = false)
    {
        if (model.Pricing is null) return 0m;

        var inputCost = (decimal)promptTokens / 1_000_000m * model.Pricing.InputPricePer1M;
        var outputCost = (decimal)completionTokens / 1_000_000m * model.Pricing.OutputPricePer1M;

        if (cached && model.Pricing.CachedInputPricePer1M is not null)
            inputCost = (decimal)promptTokens / 1_000_000m * model.Pricing.CachedInputPricePer1M.Value;

        return Math.Round(inputCost + outputCost, 6);
    }
}

/// <summary>
/// OpenAI-compatible provider (baseline, not hardcoded to any brand).
/// </summary>
public sealed class OpenAiCompatibleProvider : IProvider
{
    public string Id { get; }
    public string Version => "1.0";
    public string BaseUrl { get; }
    private readonly HttpClient _httpClient;

    public OpenAiCompatibleProvider(string id, string baseUrl, HttpClient? httpClient = null)
    {
        Id = id;
        BaseUrl = baseUrl;
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{BaseUrl}/v1/models", ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                return [];

            var models = new List<ModelInfo>();
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    var id = item.TryGetProperty("id", out var idProp) && idProp.ValueKind == System.Text.Json.JsonValueKind.String
                        ? idProp.GetString() ?? "unknown"
                        : "unknown";
                    models.Add(new ModelInfo
                    {
                        Id = id,
                        DisplayName = id,
                        ContextWindow = 128_000,
                        SupportsTools = true,
                        SupportsStreaming = true,
                    });
                }
            }

            return models;
        }
        catch
        {
            return [];
        }
    }

    public async Task<ProviderResponse> SendAsync(ProviderRequest request, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var msg = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/chat/completions");
            msg.Content = new StringContent(request.Body, System.Text.Encoding.UTF8, "application/json");
            if (request.ApiKey is not null)
                msg.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", request.ApiKey);
            if (request.ExtraHeaders is not null)
                foreach (var kv in request.ExtraHeaders)
                    msg.Headers.Add(kv.Key, kv.Value);

            var response = await _httpClient.SendAsync(msg, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            sw.Stop();

            return new ProviderResponse
            {
                StatusCode = (int)response.StatusCode,
                Body = responseBody,
                Success = response.IsSuccessStatusCode,
                Latency = sw.Elapsed,
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ProviderResponse
            {
                StatusCode = 0,
                Body = "",
                Success = false,
                ErrorMessage = ex.Message,
                Latency = sw.Elapsed,
            };
        }
    }
}
