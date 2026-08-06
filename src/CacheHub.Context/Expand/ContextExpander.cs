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
            Reason = $"Expanded: {reason}",
        };
    }
}
