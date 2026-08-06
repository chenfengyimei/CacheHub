using CacheHub.Context.Budget;
using CacheHub.Context.Chunking;
using CacheHub.Context.Ranking;
using CacheHub.Core.Context;

namespace CacheHub.Context.Selection;

/// <summary>
/// Result of the selection process: selected files with mode and token estimates.
/// Includes an immutable PayloadPlan that PayloadGenerator uses to produce the final payload.
/// </summary>
public sealed record SelectionResult
{
    public required IReadOnlyList<SelectedFileItem> SelectedFiles { get; init; }
    public required IReadOnlyList<ExcludedFileItem> ExcludedCandidates { get; init; }
    public required int TotalEstimatedTokens { get; init; }
    public required int BudgetTarget { get; init; }
    public required int BudgetHardLimit { get; init; }
    public required bool BudgetExceeded { get; init; }

    /// <summary>
    /// The immutable payload plan. Manifest and Payload share this plan.
    /// </summary>
    public PayloadPlan? Plan { get; init; }
}

public sealed record SelectedFileItem
{
    public required string Path { get; init; }
    public required string ContentHash { get; init; }
    public required SelectionMode Mode { get; init; }
    public required double Score { get; init; }
    public required IReadOnlyList<string> Reasons { get; init; }
    public required int EstimatedTokens { get; init; }
    public IReadOnlyList<LineRange>? Ranges { get; init; }
}

public sealed record ExcludedFileItem
{
    public required string Path { get; init; }
    public required double Score { get; init; }
    public required string Reason { get; init; }
}

/// <summary>
/// Selects files and chunks within the token budget.
/// Determines selection mode per file based on score and size.
/// </summary>
public sealed class SelectionEngine
{
    private readonly ChunkingStrategy _chunker = new();

    /// <summary>
    /// Selects files from ranked candidates within the token budget.
    /// </summary>
    public SelectionResult Select(
        IReadOnlyList<RankedCandidate> rankedCandidates,
        TokenBudget budget,
        Func<string, string> contentProvider,
        Func<string, string> hashProvider)
    {
        var selected = new List<SelectedFileItem>();
        var excluded = new List<ExcludedFileItem>();
        var totalTokens = 0;
        var effectiveBudget = budget.EffectiveBudget;

        foreach (var candidate in rankedCandidates)
        {
            var content = contentProvider(candidate.Path);
            var tokens = ChunkingStrategy.EstimateTokens(content);
            var mode = DetermineMode(candidate, tokens, budget);

            var (actualTokens, ranges) = ApplyMode(candidate.Path, content, mode, effectiveBudget - totalTokens);

            if (totalTokens + actualTokens > effectiveBudget)
            {
                // Try to fit as chunks
                if (mode != SelectionMode.Metadata)
                {
                    mode = SelectionMode.Chunks;
                    (actualTokens, ranges) = ApplyMode(candidate.Path, content, mode, effectiveBudget - totalTokens);

                    if (actualTokens == 0 || totalTokens + actualTokens > effectiveBudget)
                    {
                        mode = SelectionMode.Outline;
                        (actualTokens, ranges) = ApplyMode(candidate.Path, content, mode, effectiveBudget - totalTokens);
                    }
                }

                if (totalTokens + actualTokens > effectiveBudget)
                {
                    excluded.Add(new ExcludedFileItem
                    {
                        Path = candidate.Path,
                        Score = candidate.Score,
                        Reason = "Token 预算不足",
                    });
                    continue;
                }
            }

            selected.Add(new SelectedFileItem
            {
                Path = candidate.Path,
                ContentHash = hashProvider(candidate.Path),
                Mode = mode,
                Score = candidate.Score,
                Reasons = candidate.Reasons,
                EstimatedTokens = actualTokens,
                Ranges = ranges,
            });
            totalTokens += actualTokens;
        }

        return new SelectionResult
        {
            SelectedFiles = selected,
            ExcludedCandidates = excluded,
            TotalEstimatedTokens = totalTokens,
            BudgetTarget = budget.ContextTarget,
            BudgetHardLimit = budget.ContextHardLimit,
            BudgetExceeded = totalTokens > budget.ContextHardLimit,
            Plan = new PayloadPlan
            {
                Items = selected.Select(s => new PayloadPlanItem
                {
                    Path = s.Path,
                    Mode = s.Mode,
                    ContentHash = s.ContentHash,
                    Score = s.Score,
                    EstimatedTokens = s.EstimatedTokens,
                    Reasons = s.Reasons,
                    Ranges = s.Ranges,
                }).ToList(),
                TotalEstimatedTokens = totalTokens,
                BudgetTarget = budget.ContextTarget,
                BudgetHardLimit = budget.ContextHardLimit,
                BudgetExceeded = totalTokens > budget.ContextHardLimit,
            },
        };
    }

    private static SelectionMode DetermineMode(RankedCandidate candidate, int tokens, TokenBudget budget)
    {
        // Small files: full
        if (tokens < 500) return SelectionMode.Full;

        // High score + medium: chunks
        if (candidate.Score > 0.5 && tokens < 5000) return SelectionMode.Chunks;

        // Medium score: outline
        if (candidate.Score > 0.3) return SelectionMode.Outline;

        // Low score: metadata
        return SelectionMode.Metadata;
    }

    private (int tokens, IReadOnlyList<LineRange>? ranges) ApplyMode(
        string path, string content, SelectionMode mode, int remainingBudget)
    {
        var chunks = _chunker.Chunk(path, content, mode, remainingBudget);

        if (chunks.Count == 0) return (0, null);

        var totalTokens = chunks.Sum(c => c.EstimatedTokens);
        var ranges = chunks
            .Where(c => c.StartLine > 0)
            .Select(c => new Core.Context.LineRange { StartLine = c.StartLine, EndLine = c.EndLine })
            .ToList();

        return (totalTokens, ranges.Count > 0 ? ranges : null);
    }
}
