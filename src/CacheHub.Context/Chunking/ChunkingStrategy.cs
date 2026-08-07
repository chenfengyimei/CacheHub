using CacheHub.Core.Context;
using CacheHub.Context.Ranking;
using CacheHub.Context.Recall;
using CacheHub.Core.Tokens;

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
    /// <summary>
    /// Anchor that produced this chunk (e.g., "symbol:AuthService", "fts:login", "gitdiff").
    /// </summary>
    public string? AnchorSource { get; init; }
}

/// <summary>
/// Chunking strategy v2: anchor-based chunking.
/// Instead of blindly splitting files by line window, it uses anchors
/// (symbol ranges, FTS hit lines, error stack lines) to select only
/// relevant chunks. Non-anchor modes (Full, Outline, Summary, Metadata)
/// remain unchanged.
/// </summary>
public sealed class ChunkingStrategy
{
    public const string Version = "chunking-v2";

    private const int DefaultChunkSize = 200; // lines
    private const int DefaultOverlap = 10; // lines
    private const int MaxChunkSize = 500;
    private const int AnchorContextLines = 15; // lines of context around an anchor
    private const int AnchorMaxLines = 100; // max lines per anchor chunk

    /// <summary>
    /// Chunks a file's content based on the selection mode and token budget.
    /// When tokenizer is provided, uses real token counting instead of chars/4 estimate.
    /// </summary>
    public IReadOnlyList<FileChunk> Chunk(
        string filePath,
        string content,
        SelectionMode mode,
        int maxTokens,
        IReadOnlyList<LineAnchor>? anchors = null,
        ITokenizer? tokenizer = null)
    {
        var lines = content.Split('\n');
        int CountTokens(string text) => tokenizer?.CountTokens(text) ?? EstimateTokens(text);
        var totalTokens = CountTokens(content);

        return mode switch
        {
            SelectionMode.Full => ChunkFull(filePath, content, lines, totalTokens, maxTokens, CountTokens),
            SelectionMode.Chunks when anchors is not null && anchors.Count > 0
                => ChunkByAnchors(filePath, lines, anchors, maxTokens, CountTokens),
            SelectionMode.Chunks => ChunkByLines(filePath, lines, maxTokens, CountTokens),
            SelectionMode.Outline => ChunkOutline(filePath, lines, CountTokens),
            SelectionMode.DeterministicSummary => ChunkSummary(filePath, lines, CountTokens),
            SelectionMode.Metadata => ChunkMetadata(filePath, lines, totalTokens, CountTokens),
            _ => ChunkByLines(filePath, lines, maxTokens, CountTokens),
        };
    }

    /// <summary>
    /// Anchor-based chunking: creates chunks centered around anchor lines.
    /// Each chunk includes AnchorContextLines before and after the anchor.
    /// Overlapping chunks are merged. Only anchored regions are included.
    /// </summary>
    private static List<FileChunk> ChunkByAnchors(
        string path, string[] lines, IReadOnlyList<LineAnchor> anchors, int maxTokens,
        Func<string, int> countTokens)
    {
        // Create expanded ranges around each anchor
        var ranges = new List<(int start, int end, string source)>();
        foreach (var anchor in anchors)
        {
            var start = Math.Max(0, anchor.StartLine - 1 - AnchorContextLines);
            var end = Math.Min(lines.Length - 1, anchor.EndLine - 1 + AnchorContextLines);
            // Cap to max lines
            if (end - start + 1 > AnchorMaxLines)
                end = start + AnchorMaxLines - 1;
            ranges.Add((start, end, anchor.AnchorType.ToString()));
        }

        // Sort and merge overlapping ranges
        ranges.Sort((a, b) => a.start.CompareTo(b.start));
        var merged = new List<(int start, int end, string source)>();
        foreach (var (start, end, source) in ranges)
        {
            if (merged.Count > 0 && start <= merged[^1].end + 1)
            {
                // Merge with previous
                var prev = merged[^1];
                merged[^1] = (prev.start, Math.Max(prev.end, end), prev.source + "+" + source);
            }
            else
            {
                merged.Add((start, end, source));
            }
        }

        // Apply budget: select chunks until budget exhausted
        var chunks = new List<FileChunk>();
        var usedTokens = 0;
        foreach (var (start, end, source) in merged)
        {
            var content = string.Join('\n', lines.Skip(start).Take(end - start + 1));
            var tokens = countTokens(content);

            if (usedTokens + tokens > maxTokens)
            {
                // Try to trim the chunk to fit
                var remaining = maxTokens - usedTokens;
                if (remaining <= 50) break; // Not enough budget for meaningful content
                var linesToFit = remaining / 10; // ~10 tokens per line
                if (linesToFit < 5) break;
                var actualEnd = Math.Min(end, start + linesToFit);
                content = string.Join('\n', lines.Skip(start).Take(actualEnd - start + 1));
                tokens = countTokens(content);
            }

            chunks.Add(new FileChunk
            {
                Path = path,
                StartLine = start + 1, // 1-based
                EndLine = end + 1,
                Content = content,
                EstimatedTokens = tokens,
                AnchorSource = source,
            });
            usedTokens += tokens;
        }

        return chunks;
    }

    private static List<FileChunk> ChunkFull(string path, string content, string[] lines, int totalTokens, int maxTokens, Func<string, int> countTokens)
    {
        if (totalTokens <= maxTokens)
        {
            return [new FileChunk { Path = path, StartLine = 1, EndLine = lines.Length, Content = content, EstimatedTokens = totalTokens }];
        }
        // If too large, fall back to chunking
        return ChunkByLines(path, lines, maxTokens, countTokens);
    }

    private static List<FileChunk> ChunkByLines(string path, string[] lines, int maxTokens, Func<string, int> countTokens)
    {
        var chunks = new List<FileChunk>();
        var chunkSize = DetermineChunkSize(lines.Length, maxTokens);
        var overlap = Math.Min(DefaultOverlap, chunkSize / 5);

        for (var i = 0; i < lines.Length; i += chunkSize - overlap)
        {
            var end = Math.Min(i + chunkSize, lines.Length);
            var content = string.Join('\n', lines.Skip(i).Take(end - i));
            var tokens = countTokens(content);

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

    private static List<FileChunk> ChunkOutline(string path, string[] lines, Func<string, int> countTokens)
    {
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
                EstimatedTokens = countTokens(content),
            },
        ];
    }

    private static List<FileChunk> ChunkSummary(string path, string[] lines, Func<string, int> countTokens)
    {
        var head = lines.Take(Math.Min(20, lines.Length)).ToList();
        var imports = lines.Where(l => l.Contains("import ", StringComparison.Ordinal) || l.Contains("using ", StringComparison.Ordinal)).Take(10).ToList();
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
                EstimatedTokens = countTokens(content),
            },
        ];
    }

    private static List<FileChunk> ChunkMetadata(string path, string[] lines, int totalTokens, Func<string, int> countTokens)
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
                EstimatedTokens = countTokens(content),
            },
        ];
    }

    private static int DetermineChunkSize(int totalLines, int maxTokens)
    {
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

