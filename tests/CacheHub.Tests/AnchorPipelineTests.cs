using CacheHub.Context.Budget;
using CacheHub.Context.Chunking;
using CacheHub.Context.Ranking;
using CacheHub.Context.Recall;
using CacheHub.Context.Selection;

namespace CacheHub.Tests;

/// <summary>
/// Tests for R5-W001 (统一 LineAnchor 数据模型) and R5-W002 (Anchor 贯穿召回到分块).
/// Verifies that anchors flow from recall → ranking → selection → chunking,
/// and that Chunks mode prioritizes regions around anchors, not the file start.
/// </summary>
public class AnchorPipelineTests
{
    private static string BuildLargeFile(int totalLines)
    {
        var lines = Enumerable.Range(1, totalLines)
            .Select(i => $"line {i}: some content here used for testing tokens");
        return string.Join('\n', lines);
    }

    [Fact]
    public void LineAnchor_IsUnifiedSingleType()
    {
        // R5-W001: There must be exactly ONE LineAnchor type, with StartLine/EndLine/AnchorType
        var anchor = new LineAnchor
        {
            StartLine = 10,
            EndLine = 20,
            AnchorType = AnchorType.SymbolDefinition,
            MatchedText = "AuthService",
            Confidence = 1.0,
        };

        Assert.Equal(10, anchor.StartLine);
        Assert.Equal(20, anchor.EndLine);
        Assert.Equal(AnchorType.SymbolDefinition, anchor.AnchorType);
        Assert.Equal("AuthService", anchor.MatchedText);
    }

    [Fact]
    public void RankedCandidate_CarriesAnchorsFromRecall()
    {
        // R5-W002: anchors from CandidateFile must flow into RankedCandidate
        var candidate = new CandidateFile
        {
            Path = "src/auth.ts",
            NormalizedPath = "src/auth.ts",
            Language = "typescript",
            Size = 100,
            Anchors =
            [
                new LineAnchor { StartLine = 5, EndLine = 20, AnchorType = AnchorType.SymbolDefinition, MatchedText = "AuthService" },
            ],
        };

        var candidates = new[] { candidate };
        var ranker = new RankingEngine();
        var profile = DefaultRankingProfile.Create();
        var task = new Context.Parsing.TaskParser().Parse("Fix AuthService");

        var ranked = ranker.Rank(candidates, profile, task, null);

        Assert.Single(ranked);
        Assert.NotEmpty(ranked[0].Anchors);
        Assert.Equal("AuthService", ranked[0].Anchors[0].MatchedText);
    }

    [Fact]
    public void SelectionEngine_ChunksAroundAnchors_NotFileStart()
    {
        // R5-W002 acceptance: Chunks mode must prioritize around anchors, not start at line 1
        var content = BuildLargeFile(200);
        var ranked = new[]
        {
            new RankedCandidate
            {
                Path = "src/auth.ts",
                NormalizedPath = "src/auth.ts",
                Language = "typescript",
                Size = content.Length,
                Score = 0.8,
                Reasons = ["符号匹配"],
                Features = new FeatureScores { SymbolMatch = 1.0 },
                Anchors =
                [
                    new LineAnchor { StartLine = 150, EndLine = 160, AnchorType = AnchorType.SymbolDefinition, MatchedText = "AuthService" },
                ],
            },
        };

        var selection = new SelectionEngine();
        // High budget so the high-score file is selected as Chunks mode
        var budget = new CacheHub.Context.Budget.TokenBudget
        {
            ModelContextWindow = 128_000,
            AgentReservedTokens = 1000,
            ResponseReservedTokens = 1000,
            ContextTarget = 50_000,
            ContextHardLimit = 60_000,
            SafetyMargin = 1000,
        };

        var result = selection.Select(
            ranked,
            budget,
            _ => content,
            _ => "sha256:test");

        Assert.NotEmpty(result.SelectedFiles);
        var selected = result.SelectedFiles[0];
        Assert.Equal(Core.Context.SelectionMode.Chunks, selected.Mode);
        // The selected ranges should center around the anchor (line 400-420), NOT start at line 1
        Assert.NotNull(selected.Ranges);
        Assert.Contains(selected.Ranges, r => r.StartLine >= 135 && r.StartLine <= 150);
        Assert.DoesNotContain(selected.Ranges, r => r.StartLine <= 3 && r.EndLine <= 50);
    }

    [Fact]
    public void ChunkingStrategy_AnchorSourceReflectsAnchorType()
    {
        var chunker = new ChunkingStrategy();
        var content = BuildLargeFile(100);
        var anchors = new[] { new LineAnchor { StartLine = 50, EndLine = 55, AnchorType = AnchorType.FtsHit, MatchedText = "login" } };

        var chunks = chunker.Chunk("test.ts", content, Core.Context.SelectionMode.Chunks, 10000, anchors);

        Assert.NotEmpty(chunks);
        Assert.Contains("FtsHit", chunks[0].AnchorSource);
        // Chunk should be near line 50, not at the file start
        Assert.True(chunks[0].StartLine >= 30, "Anchor chunk should be near the anchor, not at file start");
    }
}
