using AiKv.Context.Ranking;
using AiKv.Core.Context;
using AiKv.Core.Identifiers;
using System.Text.Json;

namespace AiKv.Context.Explain;

/// <summary>
/// Explanation of why a file was selected or excluded.
/// </summary>
public sealed record FileExplanation
{
    public required string Path { get; init; }
    public required bool Selected { get; init; }
    public required double Score { get; init; }
    public required IReadOnlyList<string> Reasons { get; init; }
    public required FeatureBreakdown? Features { get; init; }
    public required string? ExclusionReason { get; init; }
}

/// <summary>
/// Breakdown of feature scores.
/// </summary>
public sealed record FeatureBreakdown
{
    public double SymbolMatch { get; init; }
    public double TextMatch { get; init; }
    public double PathMatch { get; init; }
    public double GitDiff { get; init; }
    public double CurrentFileRelation { get; init; }
}

/// <summary>
/// Context explain: explains selection, score composition, budget eviction, potential misses.
/// </summary>
public static class ContextExplainer
{
    /// <summary>
    /// Produces explanations for selected and excluded files.
    /// </summary>
    public static IReadOnlyList<FileExplanation> Explain(ContextPackageManifest manifest)
    {
        var explanations = new List<FileExplanation>();

        foreach (var file in manifest.SelectedFiles)
        {
            explanations.Add(new FileExplanation
            {
                Path = file.Path,
                Selected = true,
                Score = file.Score,
                Reasons = file.Reasons,
                Features = null, // Features not stored in manifest
                ExclusionReason = null,
            });
        }

        foreach (var excluded in manifest.ExcludedCandidates)
        {
            explanations.Add(new FileExplanation
            {
                Path = excluded.Path,
                Selected = false,
                Score = excluded.Score,
                Reasons = [],
                Features = null,
                ExclusionReason = excluded.Reason,
            });
        }

        return explanations;
    }

    /// <summary>
    /// Detects potential missing context: high-score excluded files.
    /// </summary>
    public static IReadOnlyList<string> DetectPotentialMisses(ContextPackageManifest manifest)
    {
        return manifest.ExcludedCandidates
            .Where(e => e.Score > 0.3)
            .Select(e => e.Path)
            .ToList();
    }

    /// <summary>
    /// Budget summary for human-readable explanation.
    /// </summary>
    public static string BudgetSummary(ContextPackageManifest manifest)
    {
        var b = manifest.Budget;
        return $"已用 {b.ActualEstimate} / {b.ContextTarget} (硬限制 {b.ContextHardLimit}), 安全余量 {b.SafetyMargin}";
    }
}
