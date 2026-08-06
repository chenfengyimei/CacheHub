using CacheHub.Context.Ranking;
using CacheHub.Core.Context;
using CacheHub.Core.Identifiers;

namespace CacheHub.Context.Explain;

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
    /// <summary>
    /// Estimated tokens for this file (if selected).
    /// </summary>
    public int EstimatedTokens { get; init; }
    /// <summary>
    /// Selection mode (if selected).
    /// </summary>
    public string? Mode { get; init; }
}

/// <summary>
/// Complete breakdown of all feature scores for a file.
/// Includes weights and weighted contributions.
/// </summary>
public sealed record FeatureBreakdown
{
    public double SymbolMatch { get; init; }
    public double TextMatch { get; init; }
    public double PathMatch { get; init; }
    public double GitDiff { get; init; }
    public double CurrentFileRelation { get; init; }
    public double RecentChange { get; init; }
    public double SizeEfficiency { get; init; }

    /// <summary>
    /// Weighted contributions (score × weight) for transparency.
    /// </summary>
    public double SymbolMatchContribution { get; init; }
    public double TextMatchContribution { get; init; }
    public double PathMatchContribution { get; init; }
    public double GitDiffContribution { get; init; }
    public double CurrentFileRelationContribution { get; init; }
    public double RecentChangeContribution { get; init; }
    public double SizeEfficiencyContribution { get; init; }
}

/// <summary>
/// Complete explanation result with budget, misses, and per-file breakdowns.
/// </summary>
public sealed record ExplainResult
{
    public required IReadOnlyList<FileExplanation> Explanations { get; init; }
    public required IReadOnlyList<string> PotentialMisses { get; init; }
    public required string BudgetSummary { get; init; }
    public required int TotalSelected { get; init; }
    public required int TotalExcluded { get; init; }
    public required int TotalEstimatedTokens { get; init; }
}

/// <summary>
/// Context explain: explains selection, score composition, budget eviction, potential misses.
/// Version 2: includes complete FeatureBreakdown with weights and contributions.
/// </summary>
public static class ContextExplainer
{
    /// <summary>
    /// Produces a complete explanation for selected and excluded files.
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
                Features = ExtractFeaturesFromReasons(file.Reasons),
                ExclusionReason = null,
                EstimatedTokens = 0,
                Mode = file.Mode.ToString(),
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
    /// Produces a complete ExplainResult with budget, misses, and explanations.
    /// </summary>
    public static ExplainResult ExplainFull(ContextPackageManifest manifest)
    {
        var explanations = Explain(manifest);
        var misses = DetectPotentialMisses(manifest);
        var budget = BudgetSummary(manifest);
        var totalTokens = manifest.SelectedFiles.Count > 0
            ? manifest.Budget.ActualEstimate
            : 0;

        return new ExplainResult
        {
            Explanations = explanations,
            PotentialMisses = misses,
            BudgetSummary = budget,
            TotalSelected = manifest.SelectedFiles.Count,
            TotalExcluded = manifest.ExcludedCandidates.Count,
            TotalEstimatedTokens = totalTokens,
        };
    }

    /// <summary>
    /// Detects potential missing context: high-score excluded files.
    /// </summary>
    public static IReadOnlyList<string> DetectPotentialMisses(ContextPackageManifest manifest)
    {
        return manifest.ExcludedCandidates
            .Where(e => e.Score > 0.3)
            .OrderByDescending(e => e.Score)
            .Select(e => $"{e.Path} (score: {e.Score:F2}, reason: {e.Reason})")
            .ToList();
    }

    /// <summary>
    /// Budget summary for human-readable explanation.
    /// </summary>
    public static string BudgetSummary(ContextPackageManifest manifest)
    {
        var b = manifest.Budget;
        return $"已用 {b.ActualEstimate} / 目标 {b.ContextTarget} (硬限制 {b.ContextHardLimit}), 安全余量 {b.SafetyMargin}, Agent预留 {b.AgentReservedTokens}, 响应预留 {b.ResponseReservedTokens}";
    }

    /// <summary>
    /// Extracts feature scores from reason strings (best-effort when Features aren't stored in manifest).
    /// </summary>
    private static FeatureBreakdown? ExtractFeaturesFromReasons(IReadOnlyList<string> reasons)
    {
        if (reasons.Count == 0) return null;

        double symbolMatch = 0, textMatch = 0, pathMatch = 0, gitDiff = 0;
        double currentFile = 0, recentChange = 0, sizeEfficiency = 0;

        foreach (var reason in reasons)
        {
            if (reason.Contains("符号匹配", StringComparison.Ordinal)) symbolMatch = 1.0;
            if (reason.Contains("全文匹配", StringComparison.Ordinal)) textMatch = 1.0;
            if (reason.Contains("路径匹配", StringComparison.Ordinal)) pathMatch = 1.0;
            if (reason.Contains("Git Diff", StringComparison.Ordinal)) gitDiff = 1.0;
            if (reason.Contains("当前文件", StringComparison.Ordinal)) currentFile = 1.0;
            if (reason.Contains("最近修改", StringComparison.Ordinal)) recentChange = 1.0;
            if (reason.Contains("体积小", StringComparison.Ordinal)) sizeEfficiency = 1.0;
        }

        return new FeatureBreakdown
        {
            SymbolMatch = symbolMatch,
            TextMatch = textMatch,
            PathMatch = pathMatch,
            GitDiff = gitDiff,
            CurrentFileRelation = currentFile,
            RecentChange = recentChange,
            SizeEfficiency = sizeEfficiency,
            SymbolMatchContribution = symbolMatch * 0.30,
            TextMatchContribution = textMatch * 0.22,
            PathMatchContribution = pathMatch * 0.15,
            GitDiffContribution = gitDiff * 0.13,
            CurrentFileRelationContribution = currentFile * 0.08,
            RecentChangeContribution = recentChange * 0.07,
            SizeEfficiencyContribution = sizeEfficiency * 0.05,
        };
    }
}
