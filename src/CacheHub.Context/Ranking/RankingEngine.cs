namespace CacheHub.Context.Ranking;

/// <summary>
/// Feature scores for a candidate file, each normalized to 0..1.
/// Only includes features that are actually implemented and populated.
/// </summary>
public sealed record FeatureScores
{
    public double SymbolMatch { get; init; }
    public double TextMatch { get; init; }
    public double PathMatch { get; init; }
    public double GitDiff { get; init; }
    public double CurrentFileRelation { get; init; }
    public double RecentChange { get; init; }

    /// <summary>
    /// Import relation signal: files that import matched symbols.
    /// </summary>
    public double ImportRelation { get; init; }

    /// <summary>
    /// Test relation signal: test files related to matched source files.
    /// </summary>
    public double TestRelation { get; init; }

    /// <summary>
    /// Config relation signal: config files in directories of matched files.
    /// </summary>
    public double ConfigRelation { get; init; }

    /// <summary>
    /// Directory fallback signal: file was included as a safe fallback.
    /// </summary>
    public double DirectoryFallback { get; init; }

    /// <summary>
    /// Size efficiency signal: 0..1. Larger files score lower (prefer compact files).
    /// Computed from candidate size relative to the largest candidate.
    /// </summary>
    public double SizeEfficiency { get; init; }
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
/// Weights sum to 1.0 and only include implemented features.
/// </summary>
public sealed record FeatureWeights
{
    public double SymbolMatch { get; init; } = 0.24;
    public double TextMatch { get; init; } = 0.18;
    public double PathMatch { get; init; } = 0.12;
    public double GitDiff { get; init; } = 0.10;
    public double CurrentFileRelation { get; init; } = 0.06;
    public double RecentChange { get; init; } = 0.06;
    public double ImportRelation { get; init; } = 0.08;
    public double TestRelation { get; init; } = 0.06;
    public double ConfigRelation { get; init; } = 0.04;
    public double SizeEfficiency { get; init; } = 0.06;

    public double Sum => SymbolMatch + TextMatch + PathMatch + GitDiff +
        CurrentFileRelation + RecentChange + ImportRelation +
        TestRelation + ConfigRelation + SizeEfficiency;
}

/// <summary>
/// Default ranking profile (deterministic-v2, version 5).
/// Includes relation sources (import/test/config) with appropriate weights.
/// </summary>
public static class DefaultRankingProfile
{
    public static RankingProfile Create() => new()
    {
        Id = "deterministic-v2",
        Version = 5,
        Weights = new FeatureWeights(),
    };
}

/// <summary>
/// Computes feature scores and final ranking score for candidates.
/// </summary>
public sealed class RankingEngine
{
    /// <summary>
    /// Normalizes feature scores to 0..1 using true min-max scaling.
    /// </summary>
    public List<RankedCandidate> Rank(
        IReadOnlyList<Recall.CandidateFile> candidates,
        RankingProfile profile,
        Parsing.ParsedTask task,
        string? currentFile = null)
    {
        if (candidates.Count == 0) return [];

        // Compute raw features (including size efficiency which uses min-max)
        var rawFeatures = candidates.Select(c => ComputeFeatures(c, task, currentFile)).ToList();

        // True min-max normalization per feature
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

    /// <summary>
    /// Computes raw feature scores for a candidate.
    /// </summary>
    private static FeatureScores ComputeFeatures(
        Recall.CandidateFile candidate,
        Parsing.ParsedTask task,
        string? currentFile)
    {
        var symbolMatch = 0.0;
        if (task.ExtractedSymbols.Count > 0)
        {
            foreach (var sym in task.ExtractedSymbols)
            {
                if (candidate.MatchedSymbols.Any(m => m.Contains(sym, StringComparison.OrdinalIgnoreCase)))
                    symbolMatch += 1.0 / task.ExtractedSymbols.Count;
            }
        }

        var textMatch = 0.0;
        if (task.ExtractedKeywords.Count > 0)
        {
            foreach (var kw in task.ExtractedKeywords)
            {
                if (candidate.NormalizedPath.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    textMatch += 1.0 / task.ExtractedKeywords.Count;
            }
        }

        var pathMatch = 0.0;
        foreach (var path in task.ExtractedPaths)
        {
            if (candidate.NormalizedPath.Contains(path, StringComparison.OrdinalIgnoreCase))
            {
                pathMatch = 1.0;
                break;
            }
        }

        var gitDiff = candidate.Sources.Contains(Recall.RecallSource.GitDiff) ? 1.0 : 0.0;
        var recentChange = candidate.Sources.Contains(Recall.RecallSource.RecentChange) ? 1.0 : 0.0;
        var currentFileRelation = currentFile is not null &&
            candidate.NormalizedPath.EndsWith(currentFile, StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.0;

        var importRelation = candidate.Sources.Contains(Recall.RecallSource.ImportRelation) ? 1.0 : 0.0;
        var testRelation = candidate.Sources.Contains(Recall.RecallSource.TestRelation) ? 1.0 : 0.0;
        var configRelation = candidate.Sources.Contains(Recall.RecallSource.ConfigRelation) ? 1.0 : 0.0;
        var directoryFallback = candidate.Sources.Contains(Recall.RecallSource.DirectoryFallback) ? 1.0 : 0.0;

        return new FeatureScores
        {
            SymbolMatch = Math.Min(symbolMatch, 1.0),
            TextMatch = Math.Min(textMatch, 1.0),
            PathMatch = pathMatch,
            GitDiff = gitDiff,
            CurrentFileRelation = currentFileRelation,
            RecentChange = recentChange,
            ImportRelation = importRelation,
            TestRelation = testRelation,
            ConfigRelation = configRelation,
            DirectoryFallback = directoryFallback,
            // Absolute size in bytes; normalized later to 0..1 (larger = less efficient)
            SizeEfficiency = candidate.Size,
        };
    }

    /// <summary>
    /// True min-max normalization per feature. SizeEfficiency is inverted:
    /// the largest file gets 0, smallest gets 1.
    /// </summary>
    private static List<FeatureScores> NormalizeFeatures(List<FeatureScores> raw)
    {
        if (raw.Count == 0) return [];

        var maxSymbol = raw.Max(f => f.SymbolMatch);
        var maxText = raw.Max(f => f.TextMatch);
        var maxSize = raw.Max(f => f.SizeEfficiency);
        var minSize = raw.Min(f => f.SizeEfficiency);

        double MinMax(double value, double max) => max > 0 ? value / max : 0;

        return raw.Select(f =>
        {
            var sizeEfficiency = maxSize > minSize ? 1.0 - ((f.SizeEfficiency - minSize) / (maxSize - minSize)) : 1.0;

            return new FeatureScores
            {
                SymbolMatch = MinMax(f.SymbolMatch, maxSymbol),
                TextMatch = MinMax(f.TextMatch, maxText),
                PathMatch = f.PathMatch,
                GitDiff = f.GitDiff,
                CurrentFileRelation = f.CurrentFileRelation,
                RecentChange = f.RecentChange,
                ImportRelation = f.ImportRelation,
                TestRelation = f.TestRelation,
                ConfigRelation = f.ConfigRelation,
                DirectoryFallback = f.DirectoryFallback,
                SizeEfficiency = sizeEfficiency,
            };
        }).ToList();
    }

    private static double ComputeScore(FeatureScores features, RankingProfile profile)
    {
        var w = profile.Weights;
        var score = features.SymbolMatch * w.SymbolMatch
            + features.TextMatch * w.TextMatch
            + features.PathMatch * w.PathMatch
            + Math.Min(features.GitDiff * w.GitDiff, profile.GitDiffMaxBonus)
            + features.CurrentFileRelation * w.CurrentFileRelation
            + features.RecentChange * w.RecentChange
            + features.ImportRelation * w.ImportRelation
            + features.TestRelation * w.TestRelation
            + features.ConfigRelation * w.ConfigRelation
            + features.SizeEfficiency * w.SizeEfficiency;

        // Directory fallback candidates get a small penalty
        if (features.DirectoryFallback > 0)
            score *= 0.1;

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
        if (features.ImportRelation > 0) reasons.Add($"导入关系 ({features.ImportRelation:F2}×{w.ImportRelation})");
        if (features.TestRelation > 0) reasons.Add($"测试关联 ({features.TestRelation:F2}×{w.TestRelation})");
        if (features.ConfigRelation > 0) reasons.Add($"配置关联 ({features.ConfigRelation:F2}×{w.ConfigRelation})");
        if (features.DirectoryFallback > 0) reasons.Add("目录降级");
        if (features.SizeEfficiency > 0.5) reasons.Add($"体积小 ({candidate.Size} bytes)");

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
}\
\
