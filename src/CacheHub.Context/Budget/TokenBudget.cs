namespace CacheHub.Context.Budget;

/// <summary>
/// Token budget configuration for a context build request.
/// Budget must distinguish model window, agent reserved, response reserved, target, hard limit, and safety margin.
/// </summary>
public sealed record TokenBudget
{
    public required int ModelContextWindow { get; init; }
    public required int AgentReservedTokens { get; init; }
    public required int ResponseReservedTokens { get; init; }
    public required int ContextTarget { get; init; }
    public required int ContextHardLimit { get; init; }
    public required int SafetyMargin { get; init; }
    public string? Tokenizer { get; init; }
    public string? TokenizerVersion { get; init; }

    /// <summary>
    /// The total reserved tokens (agent + response + safety margin).
    /// </summary>
    public int TotalReserved => AgentReservedTokens + ResponseReservedTokens + SafetyMargin;

    /// <summary>
    /// The maximum context tokens available: modelWindow - agentReserved - responseReserved - safetyMargin.
    /// This is the absolute ceiling for context content.
    /// </summary>
    public int MaxAvailable => Math.Max(0, ModelContextWindow - TotalReserved);

    /// <summary>
    /// The effective available budget: min(target, MaxAvailable).
    /// Accounts for all reserved tokens, not just safety margin.
    /// </summary>
    public int EffectiveBudget => Math.Min(ContextTarget, MaxAvailable);

    /// <summary>
    /// Whether a given token count fits within the hard limit (including reserved tokens).
    /// </summary>
    public bool FitsHardLimit(int tokens) => tokens <= MaxAvailable;

    /// <summary>
    /// Whether a given token count fits within the effective budget.
    /// </summary>
    public bool FitsEffective(int tokens) => tokens <= EffectiveBudget;

    /// <summary>
    /// Validates that the budget configuration is internally consistent.
    /// Returns false if the target exceeds the available window.
    /// </summary>
    public bool IsValid => ContextTarget <= MaxAvailable
        && ContextHardLimit <= ModelContextWindow
        && ContextHardLimit >= ContextTarget;
}

/// <summary>
/// Default token budget policy (version 2: accounts for reserved tokens).
/// </summary>
public static class DefaultTokenBudgetPolicy
{
    public const string Version = "budget-v2";

    /// <summary>
    /// Creates a budget for a standard 128K context window model.
    /// Target and hard limit are now constrained by modelWindow - reserved - safetyMargin.
    /// </summary>
    public static TokenBudget Create(
        int modelContextWindow = 128_000,
        int agentReservedTokens = 18_000,
        int responseReservedTokens = 12_000,
        double targetRatio = 0.50, // ~64K, well within available window
        int safetyMargin = 10_000)
    {
        var maxAvailable = modelContextWindow - agentReservedTokens - responseReservedTokens - safetyMargin;
        var contextTarget = Math.Min((int)(modelContextWindow * targetRatio), maxAvailable);
        var contextHardLimit = (int)(maxAvailable * 0.85); // 85% of available as hard limit

        return new TokenBudget
        {
            ModelContextWindow = modelContextWindow,
            AgentReservedTokens = agentReservedTokens,
            ResponseReservedTokens = responseReservedTokens,
            ContextTarget = contextTarget,
            ContextHardLimit = contextHardLimit,
            SafetyMargin = safetyMargin,
            Tokenizer = "rough-estimate",
            TokenizerVersion = "v2",
        };
    }
}
