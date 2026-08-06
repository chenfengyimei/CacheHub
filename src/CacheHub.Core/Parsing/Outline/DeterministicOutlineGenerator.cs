using CacheHub.Core.Parsing;

namespace CacheHub.Core.Parsing.Outline;

/// <summary>
/// Generates a deterministic, stable outline from a ParseResult.
/// Sorts symbols by line number, includes imports and key symbols.
/// </summary>
public sealed record OutlineEntry
{
    public required string Name { get; init; }
    public required SymbolKind Kind { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public string? Modifier { get; init; }
}

/// <summary>
/// Deterministic outline of a file.
/// </summary>
public sealed record FileOutline
{
    public required string FilePath { get; init; }
    public required string Language { get; init; }
    public required string ParserId { get; init; }
    public required string ParserVersion { get; init; }
    public required IReadOnlyList<OutlineEntry> Symbols { get; init; }
    public required IReadOnlyList<ImportDeclaration> Imports { get; init; }
}

/// <summary>
/// Generates stable outlines from parse results.
/// </summary>
public static class DeterministicOutlineGenerator
{
    /// <summary>
    /// Creates a deterministic outline from a parse result.
    /// Symbols are sorted by start line, then by name for stability.
    /// </summary>
    public static FileOutline Generate(ParseResult result, string filePath)
    {
        var sortedSymbols = result.Symbols
            .OrderBy(s => s.StartLine)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .Select(s => new OutlineEntry
            {
                Name = s.Name,
                Kind = s.Kind,
                StartLine = s.StartLine,
                EndLine = s.EndLine,
                Modifier = s.Modifier,
            })
            .ToList();

        return new FileOutline
        {
            FilePath = filePath,
            Language = result.Language,
            ParserId = result.ParserId,
            ParserVersion = result.ParserVersion,
            Symbols = sortedSymbols,
            Imports = result.Imports,
        };
    }
}
