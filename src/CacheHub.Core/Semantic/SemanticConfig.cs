using System.Text.Json.Serialization;

namespace CacheHub.Core.Semantic;

/// <summary>
/// R10-W001: Semantic mode controls how semantic references are used.
/// Off = disabled, Reference = adds candidates only (default), StrictExperimental = may influence ranking more.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SemanticMode
{
    /// <summary>Disabled. No semantic recall at all.</summary>
    Off,

    /// <summary>Default. Semantic hits only add candidates or hints, never replace deterministic results.</summary>
    Reference,

    /// <summary>Experimental. Semantic signals may have higher weight in ranking. Only after proven gain.</summary>
    StrictExperimental,
}

/// <summary>
/// R10-W001: Semantic configuration with mode and safety boundaries.
/// </summary>
public sealed record SemanticConfig
{
    /// <summary>The semantic mode. Default: Reference.</summary>
    public SemanticMode Mode { get; init; } = SemanticMode.Reference;

    /// <summary>Maximum semantic candidates to inject per query.</summary>
    public int MaxCandidates { get; init; } = 5;

    /// <summary>Minimum similarity threshold 0..1.</summary>
    public double MinSimilarity { get; init; } = 0.3;

    /// <summary>Whether cross-workspace semantic sharing is allowed. Default: false.</summary>
    public bool AllowCrossWorkspace { get; init; }

    /// <summary>Whether to directly return historical model answers. Default: false (reference only).</summary>
    public bool ReturnHistoricalAnswers { get; init; }

    /// <summary>The embedding provider ID to use.</summary>
    public string? EmbeddingProviderId { get; init; }

    /// <summary>Whether semantic is effectively enabled.</summary>
    public bool IsEnabled => Mode != SemanticMode.Off;
}
