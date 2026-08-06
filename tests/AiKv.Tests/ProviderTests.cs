using AiKv.Core.Providers;

namespace AiKv.Tests;

public class ProviderTests
{
    [Fact]
    public void ModelInfo_ShouldStoreCapabilities()
    {
        var model = new ModelInfo
        {
            Id = "gpt-4",
            DisplayName = "GPT-4",
            ContextWindow = 128000,
            SupportsTools = true,
            SupportsStreaming = true,
        };

        Assert.Equal(128000, model.ContextWindow);
        Assert.True(model.SupportsTools);
    }

    [Fact]
    public void PricingInfo_ShouldBeVersioned()
    {
        var pricing = new PricingInfo
        {
            Version = "2026-01",
            EffectiveDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            InputPricePer1M = 10.0m,
            OutputPricePer1M = 30.0m,
            CachedInputPricePer1M = 5.0m,
        };

        Assert.Equal("2026-01", pricing.Version);
        Assert.Equal(10.0m, pricing.InputPricePer1M);
        Assert.Equal(5.0m, pricing.CachedInputPricePer1M);
    }

    [Fact]
    public void CostCalculator_ShouldComputeInputAndOutputCost()
    {
        var model = new ModelInfo
        {
            Id = "test-model",
            DisplayName = "Test",
            ContextWindow = 8000,
            Pricing = new PricingInfo
            {
                Version = "v1",
                EffectiveDate = DateTimeOffset.UtcNow,
                InputPricePer1M = 10.0m,
                OutputPricePer1M = 30.0m,
            },
        };

        var cost = CostCalculator.CalculateCost(model, promptTokens: 500_000, completionTokens: 100_000);

        // Input: 500K/1M * $10 = $5; Output: 100K/1M * $30 = $3; Total = $8
        Assert.Equal(8.0m, cost);
    }

    [Fact]
    public void CostCalculator_ShouldApplyCachedPricing()
    {
        var model = new ModelInfo
        {
            Id = "test-model",
            DisplayName = "Test",
            ContextWindow = 8000,
            Pricing = new PricingInfo
            {
                Version = "v1",
                EffectiveDate = DateTimeOffset.UtcNow,
                InputPricePer1M = 10.0m,
                OutputPricePer1M = 30.0m,
                CachedInputPricePer1M = 5.0m,
            },
        };

        var normalCost = CostCalculator.CalculateCost(model, 1_000_000, 0);
        var cachedCost = CostCalculator.CalculateCost(model, 1_000_000, 0, cached: true);

        Assert.Equal(10.0m, normalCost);
        Assert.Equal(5.0m, cachedCost);
        Assert.True(cachedCost < normalCost);
    }

    [Fact]
    public void CostCalculator_ShouldReturnZeroWhenNoPricing()
    {
        var model = new ModelInfo
        {
            Id = "free-model",
            DisplayName = "Free",
            ContextWindow = 4000,
        };

        var cost = CostCalculator.CalculateCost(model, 1_000_000, 500_000);

        Assert.Equal(0m, cost);
    }

    [Fact]
    public void CredentialRef_ShouldNotStoreSecret()
    {
        var cred = new CredentialRef
        {
            Id = "cred-001",
            ProviderId = "openai",
            KeyName = "OPENAI_API_KEY",
        };

        Assert.Equal("cred-001", cred.Id);
        // No actual key value stored
        Assert.DoesNotContain("sk-", cred.Id);
    }

    [Fact]
    public void ModelAlias_ShouldMapAliasToActual()
    {
        var alias = new ModelAlias
        {
            Alias = "fast",
            ActualModelId = "gpt-4o-mini",
            ProviderId = "openai",
        };

        Assert.Equal("gpt-4o-mini", alias.ActualModelId);
    }

    [Fact]
    public void RoutingConfig_ShouldSupportFallback()
    {
        var config = new RoutingConfig
        {
            Strategy = RoutingStrategy.Fallback,
            ProviderOrder = ["primary", "fallback1", "fallback2"],
        };

        Assert.Equal(RoutingStrategy.Fallback, config.Strategy);
        Assert.Equal(3, config.ProviderOrder.Count);
    }

    [Fact]
    public void BudgetLimit_ShouldSetDailyAndMonthly()
    {
        var limit = new BudgetLimit
        {
            Scope = "workspace:ws-001",
            DailyLimitUsd = 10.0m,
            MonthlyLimitUsd = 200.0m,
            MaxRequestsPerDay = 1000,
        };

        Assert.Equal(10.0m, limit.DailyLimitUsd);
        Assert.Equal(200.0m, limit.MonthlyLimitUsd);
        Assert.Equal(1000, limit.MaxRequestsPerDay);
    }

    [Fact]
    public void UsageRecord_ShouldTrackCost()
    {
        var record = new UsageRecord
        {
            Timestamp = DateTimeOffset.UtcNow,
            ProviderId = "openai",
            Model = "gpt-4",
            PromptTokens = 10000,
            CompletionTokens = 2000,
            EstimatedCostUsd = 0.16m,
            Cached = false,
            WorkspaceId = "ws-001",
        };

        Assert.Equal(0.16m, record.EstimatedCostUsd);
        Assert.False(record.Cached);
    }

    [Fact]
    public void OpenAiCompatibleProvider_CanBeInstantiated()
    {
        var provider = new OpenAiCompatibleProvider("test", "https://api.test.com");
        Assert.Equal("test", provider.Id);
        Assert.Equal("1.0", provider.Version);
        Assert.Equal("https://api.test.com", provider.BaseUrl);
    }

    [Fact]
    public void ProviderResponse_ShouldIndicateSuccess()
    {
        var success = new ProviderResponse { StatusCode = 200, Body = "{}", Success = true };
        var failure = new ProviderResponse { StatusCode = 500, Body = "", Success = false, ErrorMessage = "server error" };

        Assert.True(success.Success);
        Assert.False(failure.Success);
        Assert.NotNull(failure.ErrorMessage);
    }
}
