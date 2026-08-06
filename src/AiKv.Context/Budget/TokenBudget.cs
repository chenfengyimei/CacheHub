namespace AiKv.Context.Budget;

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
    /// The effective available budget: min(target, hardLimit - safetyMargin).
    /// </summary>
    public int EffectiveBudget => Math.Min(ContextTarget, ContextHardLimit - SafetyMargin);

    /// <summary>
    /// Whether a given token count fits within the hard limit.
    /// </summary>
    public bool FitsHardLimit(int tokens) => tokens <= ContextHardLimit - SafetyMargin;

    /// <summary>
    /// Whether a given token count fits within the effective budget.
    /// </summary>
    public bool FitsEffective(int tokens) => tokens <= EffectiveBudget;
}

/// <summary>
/// Default token budget policy (version 1).
/// </summary>
public static class DefaultTokenBudgetPolicy
{
    public const string Version = "budget-v1";

    /// <summary>
    /// Creates a budget for a standard 128K context window model.
    /// </summary>
    public static TokenBudget Create(
        int modelContextWindow = 128_000,
        int agentReservedTokens = 18_000,
        int responseReservedTokens = 12_000,
        double targetRatio = 0.625, // 80K of 128K
        double hardLimitRatio = 0.703, // 90K
        int safetyMargin = 10_000)
    {
        var contextTarget = (int)(modelContextWindow * targetRatio);
        var contextHardLimit = (int)(modelContextWindow * hardLimitRatio);

        return new TokenBudget
        {
            ModelContextWindow = modelContextWindow,
            AgentReservedTokens = agentReservedTokens,
            ResponseReservedTokens = responseReservedTokens,
            ContextTarget = contextTarget,
            ContextHardLimit = contextHardLimit,
            SafetyMargin = safetyMargin,
            Tokenizer = "rough-estimate",
            TokenizerVersion = "v1",
        };
    }
}
