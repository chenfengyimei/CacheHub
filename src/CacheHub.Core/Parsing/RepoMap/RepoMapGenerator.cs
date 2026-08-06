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
    /// <summary>
    /// Importance score 0..1 based on symbol count, type, and depth.
    /// </summary>
    public double Importance { get; set; }
    public List<RepoMapNode> Children { get; init; } = [];
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
    public required int MaxTokens { get; init; }
    public required bool BudgetExceeded { get; init; }
    public const string Version = "repomap-v2";
}

/// <summary>
/// Generates a budget-limited repository map from file outlines.
/// Version 2: real directory tree hierarchy, importance scoring, strict budget enforcement.
/// </summary>
public static class RepoMapGenerator
{
    private const int MaxKeySymbolsPerFile = 3;
    private const int MaxDepth = 10;

    /// <summary>
    /// Generates a repo map from a collection of file outlines.
    /// </summary>
    public static RepoMap Generate(
        string rootPath,
        IReadOnlyList<(string relativePath, FileOutline outline)> files,
        int maxEstimatedTokens = 4000)
    {
        var root = BuildTree(rootPath, files);
        var totalFiles = files.Count;
        var totalSymbols = files.Sum(f => f.outline.Symbols.Count);

        // Score importance
        ScoreImportance(root, totalSymbols);

        // Prune to budget
        var prunedRoot = PruneToBudget(root, maxEstimatedTokens);
        var estimatedTokens = EstimateTokens(prunedRoot);

        return new RepoMap
        {
            RootPath = rootPath,
            Root = prunedRoot,
            TotalFiles = totalFiles,
            TotalSymbols = totalSymbols,
            EstimatedTokens = estimatedTokens,
            MaxTokens = maxEstimatedTokens,
            BudgetExceeded = estimatedTokens > maxEstimatedTokens,
        };
    }

    /// <summary>
    /// Builds a real directory tree from file paths.
    /// </summary>
    private static RepoMapNode BuildTree(
        string rootPath,
        IReadOnlyList<(string relativePath, FileOutline outline)> files)
    {
        var rootName = string.IsNullOrEmpty(rootPath) ? "root" : Path.GetFileName(rootPath);
        var rootNode = new RepoMapNode
        {
            Name = rootName,
            Path = "",
            Type = RepoMapNodeType.Directory,
            SymbolCount = files.Sum(f => f.outline.Symbols.Count),
            Importance = 0,
        };

        // Build a real directory tree using path segments
        var dirLookup = new Dictionary<string, RepoMapNode>(StringComparer.Ordinal)
        {
            [""] = rootNode
        };

        var allNodes = new List<(RepoMapNode node, string parentPath, (string relativePath, FileOutline outline) file)>();

        foreach (var file in files)
        {
            var parts = file.relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            var currentPath = "";
            RepoMapNode? parentNode = rootNode;

            // Create directory nodes
            for (var i = 0; i < parts.Length - 1; i++)
            {
                var segment = parts[i];
                var path = currentPath + (string.IsNullOrEmpty(currentPath) ? "" : "/") + segment;

                if (!dirLookup.TryGetValue(path, out var dirNode))
                {
                    dirNode = new RepoMapNode
                    {
                        Name = segment,
                        Path = path + "/",
                        Type = RepoMapNodeType.Directory,
                        SymbolCount = 0,
                        Importance = 0,
                    };
                    dirLookup[path] = dirNode;

                    // Add to parent's children
                    var parent = dirLookup[currentPath];
                    parent.Children.Add(dirNode);
                }
                currentPath = path;
                parentNode = dirNode;
            }

            // Create file node
            var fileName = parts[^1];
            var keySymbols = file.outline.Symbols
                .Where(s => s.Kind is SymbolKind.Class or SymbolKind.Interface or SymbolKind.Function or SymbolKind.Method)
                .Take(MaxKeySymbolsPerFile)
                .ToList();

            var fileNode = new RepoMapNode
            {
                Name = fileName,
                Path = file.relativePath,
                Type = RepoMapNodeType.File,
                SymbolCount = file.outline.Symbols.Count,
                Importance = 0,
                KeySymbols = keySymbols,
            };

            parentNode!.Children.Add(fileNode);
        }

        // Use a mutable wrapper for Children since records are immutable but we used a List
        // Actually, RepoMapNode.Children is IReadOnlyList — we used List<RepoMapNode> internally above
        // but the record's init-only property means we created it with a List. That's fine —
        // Lists are mutable even when referenced as IReadOnlyList.

        return rootNode;
    }

    /// <summary>
    /// Scores importance for each node based on symbol count and type.
    /// </summary>
    private static void ScoreImportance(RepoMapNode node, int totalSymbols)
    {
        if (totalSymbols == 0) totalSymbols = 1;

        if (node.Type == RepoMapNodeType.File)
        {
            node.Importance = (double)node.SymbolCount / totalSymbols;
            // Boost files with key symbols (classes/interfaces)
            if (node.KeySymbols.Count > 0)
                node.Importance = Math.Min(1.0, node.Importance + 0.1);
        }
        else
        {
            // Directory: sum of children importance
            var childSum = 0.0;
            foreach (var child in node.Children)
            {
                ScoreImportance(child, totalSymbols);
                childSum += child.Importance;
            }
            node.Importance = node.Children.Count > 0 ? childSum / node.Children.Count : 0;
        }
    }

    /// <summary>
    /// Prunes the tree to fit within the token budget, keeping highest-importance nodes.
    /// </summary>
    private static RepoMapNode PruneToBudget(RepoMapNode node, int maxTokens)
    {
        var currentTokens = EstimateTokens(node);
        if (currentTokens <= maxTokens)
            return node;

        // Sort children by importance (descending), keep only what fits
        var sortedChildren = node.Children
            .OrderByDescending(c => c.Importance)
            .ToList();

        var kept = new List<RepoMapNode>();
        var usedTokens = EstimateTokens(new RepoMapNode
        {
            Name = node.Name,
            Path = node.Path,
            Type = node.Type,
            SymbolCount = node.SymbolCount,
            Importance = node.Importance,
        });

        foreach (var child in sortedChildren)
        {
            var childTokens = EstimateTokens(child);
            if (usedTokens + childTokens > maxTokens)
                break;
            kept.Add(PruneToBudget(child, maxTokens - usedTokens));
            usedTokens += childTokens;
        }

        return node with { Children = kept };
    }

    private static int EstimateTokens(RepoMapNode node)
    {
        // Rough estimate: ~4 chars per token
        var tokens = node.Name.Length / 4 + 2; // name + overhead
        if (node.Type == RepoMapNodeType.File)
            tokens += node.Path.Length / 4;

        foreach (var sym in node.KeySymbols)
            tokens += sym.Name.Length / 4 + 1;

        foreach (var child in node.Children)
            tokens += EstimateTokens(child);

        return tokens;
    }
}
