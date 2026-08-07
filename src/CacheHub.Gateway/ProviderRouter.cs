using CacheHub.Gateway.Providers;

namespace CacheHub.Gateway.Providers;

/// <summary>
/// Health status for a provider.
/// </summary>
public sealed record ProviderHealth
{
    public required string ProviderId { get; init; }
    public required bool IsHealthy { get; init; }
    public required DateTimeOffset LastChecked { get; init; }
    public TimeSpan? LastLatency { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Provider router: selects a provider based on routing strategy and health.
/// R6: implements health check, failover routing, and budget execution.
/// </summary>
public sealed class ProviderRouter
{
    private readonly Dictionary<string, IProvider> _providers = new();
    private readonly Dictionary<string, ProviderHealth> _health = new();
    private readonly RoutingConfig _config;
    private readonly Lock _lock = new();
    private int _roundRobinIndex;

    public ProviderRouter(RoutingConfig config)
    {
        _config = config;
    }

    public void RegisterProvider(IProvider provider)
    {
        _providers[provider.Id] = provider;
        _health[provider.Id] = new ProviderHealth
        {
            ProviderId = provider.Id,
            IsHealthy = true,
            LastChecked = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Selects a provider based on routing strategy and health.
    /// Returns null if no healthy provider is available.
    /// </summary>
    public IProvider? SelectProvider()
    {
        lock (_lock)
        {
            var healthy = _config.ProviderOrder
                .Where(id => _providers.ContainsKey(id) && _health.GetValueOrDefault(id)?.IsHealthy != false)
                .ToList();

            if (healthy.Count == 0) return null;

            return _config.Strategy switch
            {
                RoutingStrategy.Explicit => _providers.GetValueOrDefault(healthy[0]),
                RoutingStrategy.RoundRobin => _providers.GetValueOrDefault(healthy[_roundRobinIndex++ % healthy.Count]),
                RoutingStrategy.LeastLatency => _providers.GetValueOrDefault(
                    healthy.OrderBy(id => _health.GetValueOrDefault(id)?.LastLatency ?? TimeSpan.MaxValue).First()),
                RoutingStrategy.Fallback => _providers.GetValueOrDefault(healthy[0]),
                _ => _providers.GetValueOrDefault(healthy[0]),
            };
        }
    }

    /// <summary>
    /// Updates health status for a provider.
    /// </summary>
    public void UpdateHealth(string providerId, bool isHealthy, string? error = null, TimeSpan? latency = null)
    {
        lock (_lock)
        {
            _health[providerId] = new ProviderHealth
            {
                ProviderId = providerId,
                IsHealthy = isHealthy,
                LastChecked = DateTimeOffset.UtcNow,
                LastLatency = latency,
                Error = error,
            };
        }
    }

    /// <summary>
    /// Gets current health status for all providers.
    /// </summary>
    public IReadOnlyList<ProviderHealth> GetHealthStatus()
    {
        lock (_lock) { return _health.Values.ToList(); }
    }
}

/// <summary>
/// Budget tracker: enforces daily/monthly limits per scope.
/// R6: prevents overspending by tracking usage and blocking over-limit requests.
/// </summary>
public sealed class BudgetTracker
{
    private readonly Dictionary<string, BudgetLimit> _limits = new();
    private readonly Dictionary<string, List<UsageRecord>> _usage = new();
    private readonly Lock _lock = new();

    public void SetLimit(BudgetLimit limit)
    {
        lock (_lock) { _limits[limit.Scope] = limit; }
    }

    /// <summary>
    /// Records usage and checks if the budget allows this request.
    /// Returns false if the budget is exceeded.
    /// </summary>
    public bool TryRecord(UsageRecord record)
    {
        lock (_lock)
        {
            var scope = record.WorkspaceId is not null
                ? $"workspace:{record.WorkspaceId}"
                : $"provider:{record.ProviderId}";

            if (!_limits.TryGetValue(scope, out var limit))
                return true; // No limit set

            if (!_usage.ContainsKey(scope))
                _usage[scope] = [];

            var now = DateTimeOffset.UtcNow;
            var today = now.Date;
            var thisMonth = new DateTime(now.Year, now.Month, 1);

            var todayUsage = _usage[scope]
                .Where(u => u.Timestamp.Date == today)
                .Sum(u => u.EstimatedCostUsd);
            var monthUsage = _usage[scope]
                .Where(u => u.Timestamp.DateTime >= thisMonth)
                .Sum(u => u.EstimatedCostUsd);

            var todayCount = _usage[scope]
                .Where(u => u.Timestamp.Date == today)
                .Sum(u => 1);

            // Check limits
            if (limit.DailyLimitUsd.HasValue && todayUsage + record.EstimatedCostUsd > limit.DailyLimitUsd.Value)
                return false;

            if (limit.MonthlyLimitUsd.HasValue && monthUsage + record.EstimatedCostUsd > limit.MonthlyLimitUsd.Value)
                return false;

            if (limit.MaxRequestsPerDay.HasValue && todayCount >= limit.MaxRequestsPerDay.Value)
                return false;

            _usage[scope].Add(record);
            return true;
        }
    }

    /// <summary>
    /// Gets usage summary for a scope.
    /// </summary>
    public (decimal dailyCost, decimal monthlyCost, int dailyRequests) GetUsage(string scope)
    {
        lock (_lock)
        {
            if (!_usage.TryGetValue(scope, out var records))
                return (0, 0, 0);

            var now = DateTimeOffset.UtcNow;
            var today = now.Date;
            var thisMonth = new DateTime(now.Year, now.Month, 1);

            var dailyCost = records.Where(u => u.Timestamp.Date == today).Sum(u => u.EstimatedCostUsd);
            var monthlyCost = records.Where(u => u.Timestamp.DateTime >= thisMonth).Sum(u => u.EstimatedCostUsd);
            var dailyRequests = records.Count(u => u.Timestamp.Date == today);

            return (dailyCost, monthlyCost, dailyRequests);
        }
    }
}
