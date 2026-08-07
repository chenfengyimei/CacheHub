using System.Text.Json.Serialization;
using CacheHub.Core.Identifiers;

namespace CacheHub.Core.Context;

/// <summary>
/// Manifest of a Context Package: metadata, selected files, reasons, budget, and security info.
/// Separated from payload to allow clients to inspect without downloading content.
/// </summary>
public sealed record ContextPackageManifest
{
    [JsonPropertyName("id")]
    public required ContextPackageId Id { get; init; }

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("workspaceId")]
    public required WorkspaceId WorkspaceId { get; init; }

    [JsonPropertyName("indexSnapshotId")]
    public required IndexSnapshotId IndexSnapshotId { get; init; }

    [JsonPropertyName("repositoryCommit")]
    public string? RepositoryCommit { get; init; }

    [JsonPropertyName("branch")]
    public string? Branch { get; init; }

    [JsonPropertyName("dirtyStateHash")]
    public string? DirtyStateHash { get; init; }

    [JsonPropertyName("task")]
    public required TaskInfo Task { get; init; }

    [JsonPropertyName("ranking")]
    public required RankingInfo Ranking { get; init; }

    [JsonPropertyName("budget")]
    public required BudgetInfo Budget { get; init; }

    [JsonPropertyName("selectedFiles")]
    public required IReadOnlyList<SelectedFile> SelectedFiles { get; init; }

    [JsonPropertyName("excludedCandidates")]
    public required IReadOnlyList<ExcludedCandidate> ExcludedCandidates { get; init; }

    [JsonPropertyName("safety")]
    public required SafetyInfo Safety { get; init; }

    [JsonPropertyName("parserVersions")]
    public IReadOnlyDictionary<string, string>? ParserVersions { get; init; }

    [JsonPropertyName("repoMapVersion")]
    public string? RepoMapVersion { get; init; }

    [JsonPropertyName("contextEngineVersion")]
    public required string ContextEngineVersion { get; init; }

    [JsonPropertyName("chunkingStrategyVersion")]
    public required string ChunkingStrategyVersion { get; init; }

    [JsonPropertyName("tokenBudgetPolicyVersion")]
    public required string TokenBudgetPolicyVersion { get; init; }

    [JsonPropertyName("createdAt")]
    public required DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("parentPackageId")]
    public ContextPackageId? ParentPackageId { get; init; }
}

public sealed record TaskInfo
{
    [JsonPropertyName("originalText")]
    public required string OriginalText { get; init; }

    [JsonPropertyName("queryParserVersion")]
    public required string QueryParserVersion { get; init; }

    [JsonPropertyName("extractedSymbols")]
    public IReadOnlyList<string>? ExtractedSymbols { get; init; }

    [JsonPropertyName("extractedPaths")]
    public IReadOnlyList<string>? ExtractedPaths { get; init; }
}

public sealed record RankingInfo
{
    [JsonPropertyName("profileId")]
    public required string ProfileId { get; init; }

    [JsonPropertyName("profileVersion")]
    public required int ProfileVersion { get; init; }
}

public sealed record BudgetInfo
{
    [JsonPropertyName("modelContextWindow")]
    public required int ModelContextWindow { get; init; }

    [JsonPropertyName("agentReservedTokens")]
    public required int AgentReservedTokens { get; init; }

    [JsonPropertyName("responseReservedTokens")]
    public required int ResponseReservedTokens { get; init; }

    [JsonPropertyName("contextTarget")]
    public required int ContextTarget { get; init; }

    [JsonPropertyName("contextHardLimit")]
    public required int ContextHardLimit { get; init; }

    [JsonPropertyName("safetyMargin")]
    public required int SafetyMargin { get; init; }

    [JsonPropertyName("actualEstimate")]
    public required int ActualEstimate { get; init; }

    [JsonPropertyName("tokenizer")]
    public string? Tokenizer { get; init; }

    [JsonPropertyName("tokenizerVersion")]
    public string? TokenizerVersion { get; init; }

    /// <summary>
    /// True if the token estimate used a rough fallback (chars/4) rather than a model-specific tokenizer.
    /// </summary>
    [JsonPropertyName("isEstimated")]
    public bool IsEstimated { get; init; }
}

public sealed record SelectedFile
{
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("contentHash")]
    public required string ContentHash { get; init; }

    [JsonPropertyName("mode")]
    public required SelectionMode Mode { get; init; }

    [JsonPropertyName("score")]
    public required double Score { get; init; }

    [JsonPropertyName("reasons")]
    public required IReadOnlyList<string> Reasons { get; init; }

    [JsonPropertyName("ranges")]
    public IReadOnlyList<LineRange>? Ranges { get; init; }
}

public sealed record ExcludedCandidate
{
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("score")]
    public required double Score { get; init; }

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }
}

public sealed record SafetyInfo
{
    [JsonPropertyName("cloudSendAllowed")]
    public required bool CloudSendAllowed { get; init; }

    [JsonPropertyName("secretsScanPassed")]
    public required bool SecretsScanPassed { get; init; }

    [JsonPropertyName("ignoreRulesHash")]
    public string? IgnoreRulesHash { get; init; }

    [JsonPropertyName("securityPolicyVersion")]
    public string? SecurityPolicyVersion { get; init; }

    [JsonPropertyName("secretScannerVersion")]
    public string? SecretScannerVersion { get; init; }

    [JsonPropertyName("approvalId")]
    public string? ApprovalId { get; init; }

    [JsonPropertyName("sensitiveExclusions")]
    public IReadOnlyList<string>? SensitiveExclusions { get; init; }
}

public sealed record LineRange
{
    [JsonPropertyName("startLine")]
    public required int StartLine { get; init; }

    [JsonPropertyName("endLine")]
    public required int EndLine { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SelectionMode
{
    Full,
    Chunks,
    Outline,
    DeterministicSummary,
    Metadata,
}
