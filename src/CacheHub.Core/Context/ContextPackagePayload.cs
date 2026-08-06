using System.Text.Json.Serialization;

namespace CacheHub.Core.Context;

/// <summary>
/// Payload of a Context Package: actual code content, outlines, diffs, repo map.
/// Separated from Manifest to allow clients to inspect metadata without downloading content.
/// </summary>
public sealed record ContextPackagePayload
{
    [JsonPropertyName("contextPackageId")]
    public required string ContextPackageId { get; init; }

    [JsonPropertyName("format")]
    public required PayloadFormat Format { get; init; }

    [JsonPropertyName("items")]
    public required IReadOnlyList<PayloadItem> Items { get; init; }

    [JsonPropertyName("repoMap")]
    public string? RepoMap { get; init; }

    [JsonPropertyName("totalEstimatedTokens")]
    public required int TotalEstimatedTokens { get; init; }
}

/// <summary>
/// A single item in the payload with full provenance.
/// Immutable — produced by SelectionEngine and only materialized by PayloadGenerator.
/// </summary>
public sealed record PayloadItem
{
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("mode")]
    public required SelectionMode Mode { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }

    [JsonPropertyName("startLine")]
    public int? StartLine { get; init; }

    [JsonPropertyName("endLine")]
    public int? EndLine { get; init; }

    /// <summary>
    /// Content hash for this payload item (for verification and dedup).
    /// </summary>
    [JsonPropertyName("hash")]
    public string? Hash { get; init; }

    /// <summary>
    /// Estimated tokens for this specific item.
    /// </summary>
    [JsonPropertyName("tokens")]
    public int Tokens { get; init; }

    /// <summary>
    /// Selection reason for this item.
    /// </summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    /// <summary>
    /// Anchor source if this item was selected via anchor-based chunking.
    /// </summary>
    [JsonPropertyName("anchorSource")]
    public string? AnchorSource { get; init; }
}

/// <summary>
/// An immutable plan produced by SelectionEngine that defines exactly what
/// goes into the Payload. PayloadGenerator materializes this plan into actual content.
/// Manifest and Payload share the same plan, ensuring consistency.
/// </summary>
public sealed record PayloadPlan
{
    public required IReadOnlyList<PayloadPlanItem> Items { get; init; }
    public required int TotalEstimatedTokens { get; init; }
    public required int BudgetTarget { get; init; }
    public required int BudgetHardLimit { get; init; }
    public required bool BudgetExceeded { get; init; }
}

/// <summary>
/// A single item in the immutable payload plan (no content yet — just metadata).
/// </summary>
public sealed record PayloadPlanItem
{
    public required string Path { get; init; }
    public required SelectionMode Mode { get; init; }
    public required string ContentHash { get; init; }
    public required double Score { get; init; }
    public required int EstimatedTokens { get; init; }
    public required IReadOnlyList<string> Reasons { get; init; }
    public IReadOnlyList<LineRange>? Ranges { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PayloadFormat
{
    Markdown,
    Json,
    PlainText,
}
