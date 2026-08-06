using System.Text.Json.Serialization;
using CacheHub.Core.Parsing;
using CacheHub.Core.Parsing.Outline;

namespace CacheHub.Core.Parsing.RepoMap;

/// <summary>
/// A node in the repository map tree.
/// </summary>
public sealed record RepoMapNode
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required RepoMapNodeType Type { get; init; }
    public required int SymbolCount { get; init; }
    public IReadOnlyList<RepoMapNode> Children { get; init; } = [];
    public IReadOnlyList<OutlineEntry> KeySymbols { get; init; } = [];
}

/// <summary>
/// Type of node in the repository map.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RepoMapNodeType
{
    Directory,
    File,
    Symbol,
}

/// <summary>
/// A repository map: compressed tree of directories, files, and key symbols.
/// Strictly budget-limited for token efficiency.
/// </summary>
public sealed record RepoMap
{
    public required string RootPath { get; init; }
    public required RepoMapNode Root { get; init; }
    public required int TotalFiles { get; init; }
    public required int TotalSymbols { get; init; }
    public required int EstimatedTokens { get; init; }
}

/// <summary>
/// Generates a budget-limited repository map from file outlines.
/// Prioritizes by symbol count and directory depth.
/// </summary>
public static class RepoMapGenerator
{
    /// <summary>
    /// Generates a repo map from a collection of file outlines.
    /// </summary>
    public static RepoMap Generate(
        string rootPath,
        IReadOnlyList<(string relativePath, FileOutline outline)> files,
        int maxEstimatedTokens = 4000)
    {
        var root = BuildTree(rootPath, files, maxEstimatedTokens);
        var totalFiles = files.Count;
        var totalSymbols = files.Sum(f => f.outline.Symbols.Count);
        var estimatedTokens = EstimateTokens(root);

        return new RepoMap
        {
            RootPath = rootPath,
            Root = root,
            TotalFiles = totalFiles,
            TotalSymbols = totalSymbols,
            EstimatedTokens = estimatedTokens,
        };
    }

    private static RepoMapNode BuildTree(
        string rootPath,
        IReadOnlyList<(string relativePath, FileOutline outline)> files,
        int maxTokens)
    {
        var root = new RepoMapNode
        {
            Name = Path.GetFileName(rootPath),
            Path = "",
            Type = RepoMapNodeType.Directory,
            SymbolCount = files.Sum(f => f.outline.Symbols.Count),
        };

        var dirChildren = new Dictionary<string, List<(string relativePath, FileOutline outline)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var dir = Path.GetDirectoryName(file.relativePath)?.Replace('\\', '/') ?? "";
            if (!dirChildren.TryGetValue(dir, out var list))
            {
                list = [];
                dirChildren[dir] = list;
            }
            list.Add(file);
        }

        var children = new List<RepoMapNode>();
        foreach (var (dir, dirFiles) in dirChildren.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            foreach (var file in dirFiles)
            {
                var keySymbols = file.outline.Symbols
                    .Where(s => s.Kind is SymbolKind.Class or SymbolKind.Interface or SymbolKind.Function or SymbolKind.Method)
                    .Take(5)
                    .ToList();

                children.Add(new RepoMapNode
                {
                    Name = Path.GetFileName(file.relativePath),
                    Path = file.relativePath,
                    Type = RepoMapNodeType.File,
                    SymbolCount = file.outline.Symbols.Count,
                    KeySymbols = keySymbols,
                });
            }
        }

        return root with { Children = children };
    }

    private static int EstimateTokens(RepoMapNode node)
    {
        // Rough estimate: ~4 tokens per symbol name + ~2 tokens per file path.
        var tokens = node.Name.Length / 4 + 2;
        foreach (var child in node.Children)
        {
            tokens += EstimateTokens(child);
        }
        foreach (var sym in node.KeySymbols)
        {
            tokens += sym.Name.Length / 4;
        }
        return tokens;
    }
}
