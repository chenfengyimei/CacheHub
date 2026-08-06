using CacheHub.Context.Budget;
using CacheHub.Context.Chunking;
using CacheHub.Context.Selection;

namespace CacheHub.Tests;

public class ChunkingBudgetSelectionTests
{
    [Fact]
    public void ChunkingStrategy_Chunk_Full_ShouldReturnSingleChunk()
    {
        var chunker = new ChunkingStrategy();
        var content = "line1\nline2\nline3";

        var chunks = chunker.Chunk("test.ts", content, Core.Context.SelectionMode.Full, 10000);

        Assert.Single(chunks);
        Assert.Equal(3, chunks[0].EndLine);
    }

    [Fact]
    public void ChunkingStrategy_Chunk_Chunks_ShouldSplitLargeContent()
    {
        var chunker = new ChunkingStrategy();
        var lines = Enumerable.Range(1, 1000).Select(i => $"line {i}: some content here");
        var content = string.Join('\n', lines);

        var chunks = chunker.Chunk("big.ts", content, Core.Context.SelectionMode.Chunks, 5000);

        Assert.True(chunks.Count > 1);
        // Overlap: second chunk should start before first ends
        Assert.True(chunks[1].StartLine <= chunks[0].EndLine);
    }

    [Fact]
    public void ChunkingStrategy_Chunk_Outline_ShouldExtractDeclarations()
    {
        var chunker = new ChunkingStrategy();
        var content = "import { x } from 'y';\npublic class Foo {\n  var bar = 1;\n  public void Baz() {}\n}\nvar unused = 0;";

        var chunks = chunker.Chunk("test.cs", content, Core.Context.SelectionMode.Outline, 5000);

        Assert.Single(chunks);
        Assert.Contains("class Foo", chunks[0].Content);
        Assert.Contains("Baz", chunks[0].Content);
        // Non-declaration lines should not be in outline
        Assert.DoesNotContain("var bar = 1", chunks[0].Content);
    }

    [Fact]
    public void ChunkingStrategy_EstimateTokens_ShouldBeContentBased()
    {
        var tokens = ChunkingStrategy.EstimateTokens("hello world this is a test");

        Assert.True(tokens > 0);
        // ~4 chars per token: 26 chars / 4 ≈ 6
        Assert.Equal(6, tokens);
    }

    [Fact]
    public void TokenBudget_Default_ShouldHaveValidRanges()
    {
        var budget = DefaultTokenBudgetPolicy.Create();

        Assert.True(budget.ContextTarget > 0);
        Assert.True(budget.ContextHardLimit > budget.ContextTarget);
        Assert.True(budget.SafetyMargin > 0);
        Assert.True(budget.EffectiveBudget > 0);
        Assert.True(budget.EffectiveBudget <= budget.ContextTarget);
    }

    [Fact]
    public void TokenBudget_FitsHardLimit_ShouldCheckCorrectly()
    {
        var budget = DefaultTokenBudgetPolicy.Create();

        Assert.True(budget.FitsHardLimit(1000));
        // FitsHardLimit checks against MaxAvailable (modelWindow - all reserved)
        Assert.False(budget.FitsHardLimit(budget.MaxAvailable + 1));
    }

    [Fact]
    public void TokenBudget_FitsEffective_ShouldCheckCorrectly()
    {
        var budget = DefaultTokenBudgetPolicy.Create();

        Assert.True(budget.FitsEffective(100));
        Assert.False(budget.FitsEffective(budget.EffectiveBudget + 1));
    }

    [Fact]
    public void SelectionEngine_Select_ShouldIncludeFiles()
    {
        var ranker = new Context.Ranking.RankingEngine();
        var selection = new SelectionEngine();
        var budget = DefaultTokenBudgetPolicy.Create(modelContextWindow: 128_000);

        var candidates = new List<Context.Ranking.RankedCandidate>
        {
            new() { Path = "a.ts", NormalizedPath = "a.ts", Language = "ts", Size = 100, Score = 0.9, Reasons = ["test"], Features = new Context.Ranking.FeatureScores() },
            new() { Path = "b.ts", NormalizedPath = "b.ts", Language = "ts", Size = 100, Score = 0.5, Reasons = ["test"], Features = new Context.Ranking.FeatureScores() },
        };

        var result = selection.Select(candidates, budget,
            path => $"export const x = 1; // {path}",
            path => "sha256:abc");

        Assert.NotEmpty(result.SelectedFiles);
        Assert.True(result.TotalEstimatedTokens > 0);
        Assert.False(result.BudgetExceeded);
    }

    [Fact]
    public void SelectionEngine_Select_ShouldNeverExceedHardLimit()
    {
        var selection = new SelectionEngine();
        // Use appropriate reserved values for a small model window
        var budget = DefaultTokenBudgetPolicy.Create(
            modelContextWindow: 8000,
            agentReservedTokens: 500,
            responseReservedTokens: 500,
            safetyMargin: 500);

        var candidates = Enumerable.Range(0, 40).Select(i => new Context.Ranking.RankedCandidate
        {
            Path = $"file{i}.ts",
            NormalizedPath = $"file{i}.ts",
            Language = "ts",
            Size = 1000,
            Score = Math.Max(0.05, 0.9 - i * 0.02),
            Reasons = ["test"],
            Features = new Context.Ranking.FeatureScores(),
        }).ToList();

        var result = selection.Select(candidates, budget,
            path => string.Join('\n', Enumerable.Range(0, 300).Select(n => $"public void Method{n}() {{ }}")),
            path => "sha256:abc");

        // Invariant: total estimated tokens must stay within the available budget.
        Assert.True(result.TotalEstimatedTokens <= budget.MaxAvailable);
    }

    [Fact]
    public void SelectionEngine_Select_ShouldAssignModes()
    {
        var selection = new SelectionEngine();
        var budget = DefaultTokenBudgetPolicy.Create();

        var smallFile = new Context.Ranking.RankedCandidate
        {
            Path = "small.ts",
            NormalizedPath = "small.ts",
            Language = "ts",
            Size = 50,
            Score = 0.9,
            Reasons = ["high"],
            Features = new Context.Ranking.FeatureScores(),
        };

        var result = selection.Select([smallFile], budget,
            path => "small content",
            path => "sha256:abc");

        Assert.Single(result.SelectedFiles);
        Assert.Equal(Core.Context.SelectionMode.Full, result.SelectedFiles[0].Mode);
    }

    // === Anchor-based chunking tests (R2-W005) ===

    [Fact]
    public void ChunkingStrategy_Chunk_WithAnchors_ShouldOnlyReturnAnchorChunks()
    {
        var chunker = new ChunkingStrategy();
        var lines = Enumerable.Range(1, 500).Select(i => $"line {i}: content");
        var content = string.Join('\n', lines);

        // Anchor at line 100 and line 300
        var anchors = new List<LineAnchor>
        {
            new(100, "fts:keyword"),
            new(300, "symbol:AuthService"),
        };

        var chunks = chunker.Chunk("test.ts", content, Core.Context.SelectionMode.Chunks, 10000, anchors);

        // Should return only anchor-based chunks (not all 500 lines)
        Assert.True(chunks.Count >= 1);
        Assert.True(chunks.Count <= 3); // Should be 1-2 merged chunks

        // Each chunk should be around 30 lines (anchor ± 15)
        foreach (var chunk in chunks)
        {
            Assert.True(chunk.EndLine - chunk.StartLine <= 100);
            Assert.NotNull(chunk.AnchorSource);
        }
    }

    [Fact]
    public void ChunkingStrategy_Chunk_AnchorsMerge_WhenOverlapping()
    {
        var chunker = new ChunkingStrategy();
        var lines = Enumerable.Range(1, 200).Select(i => $"line {i}");
        var content = string.Join('\n', lines);

        // Two close anchors that should merge
        var anchors = new List<LineAnchor>
        {
            new(50, "fts:a"),
            new(60, "fts:b"),
        };

        var chunks = chunker.Chunk("test.ts", content, Core.Context.SelectionMode.Chunks, 10000, anchors);

        Assert.Single(chunks); // Should merge into one
        Assert.Contains("a", chunks[0].AnchorSource);
        Assert.Contains("b", chunks[0].AnchorSource);
    }

    [Fact]
    public void ChunkingStrategy_Chunk_AnchorsRespectBudget()
    {
        var chunker = new ChunkingStrategy();
        var lines = Enumerable.Range(1, 1000).Select(i => $"line {i}: some content here for testing");
        var content = string.Join('\n', lines);

        // Many anchors with small budget
        var anchors = Enumerable.Range(1, 10).Select(i => new LineAnchor(i * 100, $"fts:{i}")).ToList();

        var chunks = chunker.Chunk("test.ts", content, Core.Context.SelectionMode.Chunks, 500, anchors);

        // Should not exceed the budget (roughly)
        var totalTokens = chunks.Sum(c => c.EstimatedTokens);
        Assert.True(totalTokens <= 600, $"Expected <= 600 tokens, got {totalTokens}");
    }

    [Fact]
    public void ChunkingStrategy_Chunk_NoAnchors_FallsBackToLineChunking()
    {
        var chunker = new ChunkingStrategy();
        var content = string.Join('\n', Enumerable.Range(1, 500).Select(i => $"line {i}"));

        // No anchors: should fall back to line-based chunking
        var chunks = chunker.Chunk("test.ts", content, Core.Context.SelectionMode.Chunks, 5000, null);

        Assert.NotEmpty(chunks);
        // Line-based chunks don't have AnchorSource
        Assert.All(chunks, c => Assert.Null(c.AnchorSource));
    }

    [Fact]
    public void ChunkingStrategy_Version_ShouldBeV2()
    {
        Assert.Equal("chunking-v2", ChunkingStrategy.Version);
    }
}
