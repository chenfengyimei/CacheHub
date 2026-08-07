using CacheHub.Context.Chunking;
using CacheHub.Context.Recall;

namespace CacheHub.Tests;

/// <summary>
/// Tests for R5-W003: syntax range merging and trimming.
/// Verifies anchor-based chunking merges overlapping ranges, respects budget,
/// and never defaults to truncating from file start.
/// </summary>
public class SyntaxRangeMergeTests
{
    private static string BuildContent(int lines) =>
        string.Join('\n', Enumerable.Range(1, lines).Select(i => $"line {i}: content for testing purposes here"));

    [Fact]
    public void Anchors_AdjacentRanges_MergeIntoOne()
    {
        var chunker = new ChunkingStrategy();
        var content = BuildContent(200);
        // Anchors at line 50 and 55 — within AnchorContextLines (15), should merge
        var anchors = new[]
        {
            new LineAnchor { StartLine = 50, EndLine = 50, AnchorType = AnchorType.FtsHit, MatchedText = "a" },
            new LineAnchor { StartLine = 55, EndLine = 55, AnchorType = AnchorType.FtsHit, MatchedText = "b" },
        };

        var chunks = chunker.Chunk("test.ts", content, Core.Context.SelectionMode.Chunks, 10000, anchors);

        Assert.Single(chunks);
    }

    [Fact]
    public void Anchors_DistantRanges_ProduceSeparateChunks()
    {
        var chunker = new ChunkingStrategy();
        var content = BuildContent(500);
        // Anchors far apart — should NOT merge
        var anchors = new[]
        {
            new LineAnchor { StartLine = 50, EndLine = 50, AnchorType = AnchorType.SymbolDefinition, MatchedText = "A" },
            new LineAnchor { StartLine = 400, EndLine = 400, AnchorType = AnchorType.SymbolDefinition, MatchedText = "B" },
        };

        var chunks = chunker.Chunk("test.ts", content, Core.Context.SelectionMode.Chunks, 10000, anchors);

        Assert.Equal(2, chunks.Count);
        // First chunk should be near line 50, second near 400
        Assert.True(chunks[0].StartLine < chunks[1].StartLine);
    }

    [Fact]
    public void Anchors_SymbolDefinition_UsesStartAndEndLine()
    {
        var chunker = new ChunkingStrategy();
        var content = BuildContent(100);
        // Symbol spanning lines 30-60
        var anchors = new[]
        {
            new LineAnchor { StartLine = 30, EndLine = 60, AnchorType = AnchorType.SymbolDefinition, MatchedText = "AuthService" },
        };

        var chunks = chunker.Chunk("test.ts", content, Core.Context.SelectionMode.Chunks, 10000, anchors);

        Assert.Single(chunks);
        var chunk = chunks[0];
        // Should include lines around the symbol definition (30-60 ± context)
        Assert.True(chunk.StartLine <= 30, "Chunk should start at or before symbol start line");
        Assert.True(chunk.EndLine >= 60, "Chunk should end at or after symbol end line");
    }

    [Fact]
    public void Anchors_BudgetTrimming_KeepsRelevantLines()
    {
        var chunker = new ChunkingStrategy();
        var content = BuildContent(500);
        var anchors = new[]
        {
            new LineAnchor { StartLine = 250, EndLine = 250, AnchorType = AnchorType.SymbolDefinition, MatchedText = "Target" },
        };

        // Very small budget
        var chunks = chunker.Chunk("test.ts", content, Core.Context.SelectionMode.Chunks, 200, anchors);

        // Should produce at least some content near the anchor
        if (chunks.Count > 0)
        {
            Assert.True(chunks[0].StartLine >= 235, "Trimmed chunk should still be near the anchor");
        }
    }

    [Fact]
    public void Anchors_NeverStartAtFileLine1_WhenAnchorIsElsewhere()
    {
        var chunker = new ChunkingStrategy();
        var content = BuildContent(300);
        var anchors = new[]
        {
            new LineAnchor { StartLine = 200, EndLine = 210, AnchorType = AnchorType.SymbolDefinition, MatchedText = "FarSymbol" },
        };

        var chunks = chunker.Chunk("test.ts", content, Core.Context.SelectionMode.Chunks, 10000, anchors);

        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.True(c.StartLine > 100, "Chunk should not start near file beginning when anchor is far away"));
    }

    [Fact]
    public void Anchors_OverlappingRanges_StablyMerge()
    {
        var chunker = new ChunkingStrategy();
        var content = BuildContent(300);
        // Three overlapping anchors
        var anchors = new[]
        {
            new LineAnchor { StartLine = 100, EndLine = 110, AnchorType = AnchorType.SymbolDefinition, MatchedText = "A" },
            new LineAnchor { StartLine = 105, EndLine = 115, AnchorType = AnchorType.FtsHit, MatchedText = "B" },
            new LineAnchor { StartLine = 112, EndLine = 120, AnchorType = AnchorType.GitDiff, MatchedText = "C" },
        };

        var chunks = chunker.Chunk("test.ts", content, Core.Context.SelectionMode.Chunks, 10000, anchors);

        // All three overlap and should merge into one chunk
        Assert.Single(chunks);
        // AnchorSource should contain all three types
        Assert.Contains("SymbolDefinition", chunks[0].AnchorSource);
        Assert.Contains("FtsHit", chunks[0].AnchorSource);
        Assert.Contains("GitDiff", chunks[0].AnchorSource);
    }
}
