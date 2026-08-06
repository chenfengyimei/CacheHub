namespace CacheHub.Context.Ranking;

/// <summary>
/// Feature scores for a candidate file, each normalized to 0..1.
/// </summary>
public sealed record FeatureScores
{
    public double SymbolMatch { get; init; }
    public double TextMatch { get; init; }
    public double PathMatch { get; init; }
    public double GitDiff { get; init; }
    public double DependencyRelation { get; init; }
    public double CurrentFileRelation { get; init; }
    public double RecentChange { get; init; }
    public double TestRelation { get; init; }
    public double ConfigRelation { get; init; }
}

/// <summary>
/// Versioned ranking profile with configurable feature weights.
/// Weights must not be hardcoded in business logic.
/// </summary>
public sealed record RankingProfile
{
    public required string Id { get; init; }
    public required int Version { get; init; }
    public required FeatureWeights Weights { get; init; }
    public double GitDiffMaxBonus { get; init; } = 0.15;
    public string Normalization { get; init; } = "minmax-per-query";
}

/// <summary>
/// Feature weights for the ranking profile.
/// </summary>
public sealed record FeatureWeights
{
    public double SymbolMatch { get; init; } = 0.22;
    public double TextMatch { get; init; } = 0.18;
    public double PathMatch { get; init; } = 0.12;
    public double GitDiff { get; init; } = 0.12;
    public double DependencyRelation { get; init; } = 0.10;
    public double CurrentFileRelation { get; init; } = 0.08;
    public double RecentChange { get; init; } = 0.07;
    public double TestRelation { get; init; } = 0.06;
    public double ConfigRelation { get; init; } = 0.05;

    public double Sum => SymbolMatch + TextMatch + PathMatch + GitDiff +
        DependencyRelation + CurrentFileRelation + RecentChange +
        TestRelation + ConfigRelation;
}

/// <summary>
/// Default ranking profile (deterministic-v1, version 3).
/// </summary>
public static class DefaultRankingProfile
{
    public static RankingProfile Create() => new()
    {
        Id = "deterministic-v1",
        Version = 3,
        Weights = new FeatureWeights(),
    };
}

/// <summary>
/// Computes feature scores and final ranking score for candidates.
/// </summary>
public sealed class RankingEngine
{
    /// <summary>
    /// Normalizes feature scores to 0..1 using min-max per query.
    /// </summary>
    public List<RankedCandidate> Rank(
        IReadOnlyList<Recall.CandidateFile> candidates,
        RankingProfile profile,
        Parsing.ParsedTask task,
        string? currentFile = null)
    {
        if (candidates.Count == 0) return [];

        // Compute raw features
        var rawFeatures = candidates.Select(c => ComputeFeatures(c, task, currentFile)).ToList();

        // Normalize
        var normalized = NormalizeFeatures(rawFeatures);

        // Compute final score
        var ranked = new List<RankedCandidate>();
        for (var i = 0; i < candidates.Count; i++)
        {
            var score = ComputeScore(normalized[i], profile);
            var reasons = ComputeReasons(candidates[i], normalized[i], profile);

            ranked.Add(new RankedCandidate
            {
                Path = candidates[i].Path,
                NormalizedPath = candidates[i].NormalizedPath,
                Language = candidates[i].Language,
                Size = candidates[i].Size,
                Score = score,
                Reasons = reasons,
                Features = normalized[i],
            });
        }

        // Stable sort: score desc, then path asc
        return ranked
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.NormalizedPath, StringComparer.Ordinal)
            .ToList();
    }

    private static FeatureScores ComputeFeatures(
        Recall.CandidateFile candidate,
        Parsing.ParsedTask task,
        string? currentFile)
    {
        var symbolMatch = 0.0;
        foreach (var sym in task.ExtractedSymbols)
        {
            if (candidate.MatchedSymbols.Any(m => m.Contains(sym, StringComparison.OrdinalIgnoreCase)))
                symbolMatch += 1.0 / task.ExtractedSymbols.Count;
        }

        var textMatch = 0.0;
        foreach (var kw in task.ExtractedKeywords)
        {
            if (candidate.NormalizedPath.Contains(kw, StringComparison.OrdinalIgnoreCase))
                textMatch += 1.0 / Math.Max(task.ExtractedKeywords.Count, 1);
        }

        var pathMatch = 0.0;
        foreach (var path in task.ExtractedPaths)
        {
            if (candidate.NormalizedPath.Contains(path, StringComparison.OrdinalIgnoreCase))
                pathMatch = 1.0;
        }

        var gitDiff = candidate.Sources.Contains(Recall.RecallSource.GitDiff) ? 1.0 : 0.0;
        var recentChange = candidate.Sources.Contains(Recall.RecallSource.RecentChange) ? 1.0 : 0.0;
        var currentFileRelation = currentFile is not null &&
            candidate.NormalizedPath.EndsWith(currentFile, StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.0;

        return new FeatureScores
        {
            SymbolMatch = Math.Min(symbolMatch, 1.0),
            TextMatch = Math.Min(textMatch, 1.0),
            PathMatch = pathMatch,
            GitDiff = gitDiff,
            DependencyRelation = 0.0, // Requires import graph (later phase)
            CurrentFileRelation = currentFileRelation,
            RecentChange = recentChange,
            TestRelation = 0.0,
            ConfigRelation = 0.0,
        };
    }

    private static List<FeatureScores> NormalizeFeatures(List<FeatureScores> raw)
    {
        if (raw.Count == 0) return [];

        var maxSymbol = raw.Max(f => f.SymbolMatch);
        if (maxSymbol == 0) maxSymbol = 1;
        var maxText = raw.Max(f => f.TextMatch);
        if (maxText == 0) maxText = 1;

        return raw.Select(f => new FeatureScores
        {
            SymbolMatch = maxSymbol > 0 ? f.SymbolMatch / maxSymbol : 0,
            TextMatch = maxText > 0 ? f.TextMatch / maxText : 0,
            PathMatch = f.PathMatch,
            GitDiff = f.GitDiff,
            DependencyRelation = f.DependencyRelation,
            CurrentFileRelation = f.CurrentFileRelation,
            RecentChange = f.RecentChange,
            TestRelation = f.TestRelation,
            ConfigRelation = f.ConfigRelation,
        }).ToList();
    }

    private static double ComputeScore(FeatureScores features, RankingProfile profile)
    {
        var w = profile.Weights;
        var score = features.SymbolMatch * w.SymbolMatch
            + features.TextMatch * w.TextMatch
            + features.PathMatch * w.PathMatch
            + Math.Min(features.GitDiff * w.GitDiff, profile.GitDiffMaxBonus)
            + features.DependencyRelation * w.DependencyRelation
            + features.CurrentFileRelation * w.CurrentFileRelation
            + features.RecentChange * w.RecentChange
            + features.TestRelation * w.TestRelation
            + features.ConfigRelation * w.ConfigRelation;

        return Math.Round(Math.Min(score, 1.0), 4);
    }

    private static List<string> ComputeReasons(
        Recall.CandidateFile candidate,
        FeatureScores features,
        RankingProfile profile)
    {
        var reasons = new List<string>();
        var w = profile.Weights;

        if (features.SymbolMatch > 0) reasons.Add($"符号匹配 ({features.SymbolMatch:F2}×{w.SymbolMatch})");
        if (features.PathMatch > 0) reasons.Add($"路径匹配 ({features.PathMatch:F2}×{w.PathMatch})");
        if (features.TextMatch > 0) reasons.Add($"全文匹配 ({features.TextMatch:F2}×{w.TextMatch})");
        if (features.GitDiff > 0) reasons.Add($"Git Diff ({features.GitDiff:F2}×{w.GitDiff})");
        if (features.CurrentFileRelation > 0) reasons.Add("当前文件");
        if (features.RecentChange > 0) reasons.Add("最近修改");

        return reasons.Count > 0 ? reasons : ["低相关"];
    }
}

/// <summary>
/// A ranked candidate with score, features, and reasons.
/// </summary>
public sealed record RankedCandidate
{
    public required string Path { get; init; }
    public required string NormalizedPath { get; init; }
    public required string Language { get; init; }
    public required long Size { get; init; }
    public required double Score { get; init; }
    public required IReadOnlyList<string> Reasons { get; init; }
    public required FeatureScores Features { get; init; }
}

/// <summary>
/// Aggregation: deduplicates and merges reasons from multiple sources.
/// </summary>
public static class CandidateAggregator
{
    /// <summary>
    /// Deduplicates candidates by path, merging sources and reasons.
    /// </summary>
    public static List<Recall.CandidateFile> Deduplicate(IReadOnlyList<Recall.CandidateFile> candidates)
    {
        return candidates
            .GroupBy(c => c.NormalizedPath, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                return first with
                {
                    MatchedSymbols = g.SelectMany(c => c.MatchedSymbols).Distinct().ToList(),
                    Sources = g.SelectMany(c => c.Sources).Distinct().ToList(),
                };
            })
            .ToList();
    }
}
