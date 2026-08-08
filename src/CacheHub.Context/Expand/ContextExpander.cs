using CacheHub.Context.Chunking;
using CacheHub.Core.Context;

namespace CacheHub.Context.Expand;

/// <summary>
/// Request to expand a Context Package with additional content.
/// </summary>
public sealed record ContextExpansionRequest
{
    public required string ContextPackageId { get; init; }
    public string? FileVirtualPath { get; init; }
    public string? SymbolName { get; init; }
    public string? Reason { get; init; }
    public int MaxAdditionalTokens { get; init; } = 4000;
}

/// <summary>
/// Result of a context expansion: new addition and token delta.
/// </summary>
public sealed record ExpansionResult
{
    public required string ContextPackageId { get; init; }
    public required IReadOnlyList<PayloadItem> AddedItems { get; init; }
    public required int AdditionalTokens { get; init; }
    public required string Reason { get; init; }
    /// <summary>
    /// The source type of the expansion: "file" or "symbol".
    /// </summary>
    public required string ExpansionType { get; init; } = "file";
}

/// <summary>
/// A symbol search result for expansion.
/// </summary>
public sealed record SymbolMatch
{
    public required string FilePath { get; init; }
    public required string SymbolName { get; init; }
    public required string SymbolKind { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
}

/// <summary>
/// Expands a Context Package by file or symbol, tracking incremental tokens and reason.
/// </summary>
public sealed class ContextExpander
{
    private const int DefaultMaxAdditionalTokens = 4000;

    /// <summary>
    /// Expands by file path.
    /// </summary>
    public ExpansionResult ExpandByFile(
        string contextPackageId,
        string filePath,
        string content,
        string reason,
        int maxAdditionalTokens = DefaultMaxAdditionalTokens)
    {
        var tokens = ChunkingStrategy.EstimateTokens(content);

        // If content exceeds budget, chunk it
        IReadOnlyList<PayloadItem> items;
        if (tokens <= maxAdditionalTokens)
        {
            items = [new PayloadItem
            {
                Path = filePath,
                Mode = SelectionMode.Chunks,
                Content = content,
                StartLine = 1,
            }];
        }
        else
        {
            // Truncate to fit budget — include only the portion that fits
            var estimatedChars = maxAdditionalTokens * 4;
            var truncatedContent = content.Length > estimatedChars
                ? content[..estimatedChars] + "\n... (truncated to fit token budget)"
                : content;
            tokens = ChunkingStrategy.EstimateTokens(truncatedContent);

            items = [new PayloadItem
            {
                Path = filePath,
                Mode = SelectionMode.Chunks,
                Content = truncatedContent,
                StartLine = 1,
            }];
        }

        return new ExpansionResult
        {
            ContextPackageId = contextPackageId,
            AddedItems = items,
            AdditionalTokens = tokens,
            Reason = $"Expanded by file: {reason}",
            ExpansionType = "file",
        };
    }

    /// <summary>
    /// Expands by symbol name. Returns the symbol's definition and surrounding context.
    /// </summary>
    /// <param name="contextPackageId">The parent context package ID.</param>
    /// <param name="symbolMatches">Symbol matches found by the caller (from file_symbols table).</param>
    /// <param name="contentProvider">Function to get file content by path.</param>
    /// <param name="reason">Reason for expansion.</param>
    /// <param name="maxAdditionalTokens">Max tokens to add.</param>
    public ExpansionResult ExpandBySymbol(
        string contextPackageId,
        IReadOnlyList<SymbolMatch> symbolMatches,
        Func<string, string> contentProvider,
        string reason,
        int maxAdditionalTokens = DefaultMaxAdditionalTokens)
    {
        var items = new List<PayloadItem>();
        var totalTokens = 0;

        foreach (var match in symbolMatches)
        {
            if (totalTokens >= maxAdditionalTokens)
                break;

            var content = contentProvider(match.FilePath);
            var lines = content.Split('\n');

            // Extract the symbol's line range + context (±10 lines)
            var startLine = Math.Max(0, match.StartLine - 11);
            var endLine = Math.Min(lines.Length - 1, match.EndLine + 10);
            var symbolContent = string.Join('\n', lines.Skip(startLine).Take(endLine - startLine + 1));
            var tokens = ChunkingStrategy.EstimateTokens(symbolContent);

            if (totalTokens + tokens > maxAdditionalTokens)
            {
                var remaining = maxAdditionalTokens - totalTokens;
                if (remaining < 50) break;
                var charsToFit = remaining * 4;
                if (symbolContent.Length > charsToFit)
                    symbolContent = symbolContent[..charsToFit] + "\n... (truncated)";
                tokens = ChunkingStrategy.EstimateTokens(symbolContent);
            }

            items.Add(new PayloadItem
            {
                Path = match.FilePath,
                Mode = SelectionMode.Chunks,
                Content = symbolContent,
                StartLine = startLine + 1,
                EndLine = endLine + 1,
            });
            totalTokens += tokens;
        }

        return new ExpansionResult
        {
            ContextPackageId = contextPackageId,
            AddedItems = items,
            AdditionalTokens = totalTokens,
            Reason = $"Expanded by symbol: {reason}",
            ExpansionType = "symbol",
        };
    }

    /// <summary>
    /// R5-W007: Creates a revision ContextPackageManifest from an expansion result.
    /// The new manifest has ParentPackageId set to the original, includes the expanded files,
    /// and records the incremental token count.
    /// V8-P0-02: Propagates DirtyStateHash from parent to ensure version consistency.
    /// </summary>
    public ContextPackageManifest CreateRevision(
        ContextPackageManifest parentManifest,
        ExpansionResult expansion)
    {
        var newSelected = parentManifest.SelectedFiles.ToList();
        foreach (var item in expansion.AddedItems)
        {
            newSelected.Add(new SelectedFile
            {
                Path = item.Path,
                ContentHash = parentManifest.SelectedFiles
                    .FirstOrDefault(f => f.Path == item.Path)?.ContentHash ?? "sha256:expanded",
                Mode = item.Mode,
                Score = 0.0, // Expanded files have no ranking score
                Reasons = [$"扩展: {expansion.Reason}"],
                Ranges = item.StartLine.HasValue && item.EndLine.HasValue
                    ? [new LineRange { StartLine = item.StartLine.Value, EndLine = item.EndLine.Value }]
                    : null,
            });
        }

        return new ContextPackageManifest
        {
            Id = Core.Identifiers.ContextPackageId.New(),
            SchemaVersion = parentManifest.SchemaVersion,
            WorkspaceId = parentManifest.WorkspaceId,
            IndexSnapshotId = parentManifest.IndexSnapshotId,
            RepositoryCommit = parentManifest.RepositoryCommit,
            Branch = parentManifest.Branch,
            DirtyStateHash = parentManifest.DirtyStateHash, // V8-P0-02: propagate version fingerprint
            Task = parentManifest.Task,
            Ranking = parentManifest.Ranking,
            Budget = new BudgetInfo
            {
                ModelContextWindow = parentManifest.Budget.ModelContextWindow,
                AgentReservedTokens = parentManifest.Budget.AgentReservedTokens,
                ResponseReservedTokens = parentManifest.Budget.ResponseReservedTokens,
                ContextTarget = parentManifest.Budget.ContextTarget,
                ContextHardLimit = parentManifest.Budget.ContextHardLimit,
                SafetyMargin = parentManifest.Budget.SafetyMargin,
                ActualEstimate = parentManifest.Budget.ActualEstimate + expansion.AdditionalTokens,
                Tokenizer = parentManifest.Budget.Tokenizer,
                TokenizerVersion = parentManifest.Budget.TokenizerVersion,
                IsEstimated = parentManifest.Budget.IsEstimated,
            },
            SelectedFiles = newSelected,
            ExcludedCandidates = parentManifest.ExcludedCandidates,
            Safety = parentManifest.Safety,
            ContextEngineVersion = parentManifest.ContextEngineVersion,
            ChunkingStrategyVersion = parentManifest.ChunkingStrategyVersion,
            TokenBudgetPolicyVersion = parentManifest.TokenBudgetPolicyVersion,
            RepoMapVersion = parentManifest.RepoMapVersion,
            CreatedAt = DateTimeOffset.UtcNow,
            ParentPackageId = parentManifest.Id,
        };
    }
}
