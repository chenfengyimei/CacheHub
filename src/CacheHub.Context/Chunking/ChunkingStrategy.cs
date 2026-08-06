using CacheHub.Core.Context;
using CacheHub.Context.Ranking;

namespace CacheHub.Context.Chunking;

/// <summary>
/// A chunk of a file with line range and estimated tokens.
/// </summary>
public sealed record FileChunk
{
    public required string Path { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required string Content { get; init; }
    public required int EstimatedTokens { get; init; }
}

/// <summary>
/// Chunking strategy: splits files into syntax-aware chunks when possible.
/// Version 1: line-window based with overlap control.
/// </summary>
public sealed class ChunkingStrategy
{
    public const string Version = "chunking-v1";

    private const int DefaultChunkSize = 200; // lines
    private const int DefaultOverlap = 10; // lines
    private const int MaxChunkSize = 500;

    /// <summary>
    /// Chunks a file's content based on the selection mode and token budget.
    /// </summary>
    public IReadOnlyList<FileChunk> Chunk(
        string filePath,
        string content,
        SelectionMode mode,
        int maxTokens)
    {
        var lines = content.Split('\n');
        var totalTokens = EstimateTokens(content);

        return mode switch
        {
            SelectionMode.Full => ChunkFull(filePath, content, lines, totalTokens, maxTokens),
            SelectionMode.Chunks => ChunkByLines(filePath, lines, maxTokens),
            SelectionMode.Outline => ChunkOutline(filePath, lines),
            SelectionMode.DeterministicSummary => ChunkSummary(filePath, lines),
            SelectionMode.Metadata => ChunkMetadata(filePath, lines, totalTokens),
            _ => ChunkByLines(filePath, lines, maxTokens),
        };
    }

    private static List<FileChunk> ChunkFull(string path, string content, string[] lines, int totalTokens, int maxTokens)
    {
        if (totalTokens <= maxTokens)
        {
            return [new FileChunk { Path = path, StartLine = 1, EndLine = lines.Length, Content = content, EstimatedTokens = totalTokens }];
        }
        // If too large, fall back to chunking
        return ChunkByLines(path, lines, maxTokens);
    }

    private static List<FileChunk> ChunkByLines(string path, string[] lines, int maxTokens)
    {
        var chunks = new List<FileChunk>();
        var chunkSize = DetermineChunkSize(lines.Length, maxTokens);
        var overlap = Math.Min(DefaultOverlap, chunkSize / 5);

        for (var i = 0; i < lines.Length; i += chunkSize - overlap)
        {
            var end = Math.Min(i + chunkSize, lines.Length);
            var content = string.Join('\n', lines.Skip(i).Take(end - i));
            var tokens = EstimateTokens(content);

            chunks.Add(new FileChunk
            {
                Path = path,
                StartLine = i + 1,
                EndLine = end,
                Content = content,
                EstimatedTokens = tokens,
            });

            if (end >= lines.Length) break;
        }
        return chunks;
    }

    private static List<FileChunk> ChunkOutline(string path, string[] lines)
    {
        // Extract only class/function/interface/namespace declarations
        var outlineLines = new List<string>();
        var lineNums = new List<int>();

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (IsDeclarationLine(trimmed))
            {
                outlineLines.Add($"{i + 1}: {trimmed}");
                lineNums.Add(i + 1);
            }
        }

        if (outlineLines.Count == 0) return [];

        var content = string.Join('\n', outlineLines);
        return
        [
            new FileChunk
            {
                Path = path,
                StartLine = lineNums.FirstOrDefault(),
                EndLine = lineNums.LastOrDefault(),
                Content = content,
                EstimatedTokens = EstimateTokens(content),
            },
        ];
    }

    private static List<FileChunk> ChunkSummary(string path, string[] lines)
    {
        // Deterministic summary: first N lines + imports + last N lines
        var head = lines.Take(Math.Min(20, lines.Length)).ToList();
        var imports = lines.Where(l => l.Contains("import ") || l.Contains("using ")).Take(10).ToList();
        var tail = lines.Skip(Math.Max(0, lines.Length - 10)).Take(10).ToList();

        var content = string.Join('\n', head.Concat(imports).Concat(tail).Distinct());
        return
        [
            new FileChunk
            {
                Path = path,
                StartLine = 1,
                EndLine = lines.Length,
                Content = content,
                EstimatedTokens = EstimateTokens(content),
            },
        ];
    }

    private static List<FileChunk> ChunkMetadata(string path, string[] lines, int totalTokens)
    {
        var content = $"Path: {path}\nLines: {lines.Length}\nEstTokens: {totalTokens}";
        return
        [
            new FileChunk
            {
                Path = path,
                StartLine = 0,
                EndLine = 0,
                Content = content,
                EstimatedTokens = EstimateTokens(content),
            },
        ];
    }

    private static int DetermineChunkSize(int totalLines, int maxTokens)
    {
        // Rough: ~4 chars per token, ~40 chars per line
        var tokensPerLine = 10;
        var linesForBudget = maxTokens / tokensPerLine;
        return Math.Clamp(linesForBudget, 50, MaxChunkSize);
    }

    private static bool IsDeclarationLine(string line) =>
        line.StartsWith("public ", StringComparison.Ordinal) ||
        line.StartsWith("private ", StringComparison.Ordinal) ||
        line.StartsWith("protected ", StringComparison.Ordinal) ||
        line.StartsWith("internal ", StringComparison.Ordinal) ||
        line.StartsWith("export ", StringComparison.Ordinal) ||
        line.StartsWith("class ", StringComparison.Ordinal) ||
        line.StartsWith("interface ", StringComparison.Ordinal) ||
        line.StartsWith("enum ", StringComparison.Ordinal) ||
        line.StartsWith("struct ", StringComparison.Ordinal) ||
        line.StartsWith("namespace ", StringComparison.Ordinal) ||
        line.StartsWith("def ", StringComparison.Ordinal) ||
        line.StartsWith("async def ", StringComparison.Ordinal) ||
        line.StartsWith("function ", StringComparison.Ordinal);

    /// <summary>
    /// Rough token estimation: ~4 chars per token.
    /// </summary>
    public static int EstimateTokens(string text) => text.Length / 4;
}
