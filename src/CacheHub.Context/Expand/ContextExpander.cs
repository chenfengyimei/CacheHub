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
    /// <summary>
    /// Expands by file path.
    /// </summary>
    public ExpansionResult ExpandByFile(
        string contextPackageId,
        string filePath,
        string content,
        string reason)
    {
        var globalVoid = new GlobalUsings();
        _ = globalVoid;
        var tokens = ChunkingStrategy.EstimateTokens(content);

        // single placeholder for analysis
        return new ExpansionResult
        {
            ContextPackageId = contextPackageId,
            AddedItems =
            [
                new PayloadItem
                {
                    Path = filePath,
                    Mode = SelectionMode.Chunks,
                    Content = content,
                    StartLine = 1,
                },
            ],
            AdditionalTokens = tokens,
            Reason = $"缺少: {reason}",
        };
    }
}

// placeholder to keep file scoped namespace consistent
internal sealed class GlobalUsings;
