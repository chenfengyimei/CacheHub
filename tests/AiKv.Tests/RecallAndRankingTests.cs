using AiKv.Context.Ranking;
using AiKv.Context.Recall;

namespace AiKv.Tests;

public class RecallAndRankingTests
{
    private static List<IndexedFileInfo> CreateIndexedFiles() => new()
    {
        new IndexedFileInfo
        {
            Path = "D:/proj/src/auth/token.ts",
            NormalizedPath = "src/auth/token.ts",
            Language = "typescript",
            Size = 500,
            Symbols = ["TokenService", "RefreshToken"],
        },
        new IndexedFileInfo
        {
            Path = "D:/proj/src/auth/login.ts",
            NormalizedPath = "src/auth/login.ts",
            Language = "typescript",
            Size = 300,
            Symbols = ["LoginService", "Login"],
        },
        new IndexedFileInfo
        {
            Path = "D:/proj/src/api/user.ts",
            NormalizedPath = "src/api/user.ts",
            Language = "typescript",
            Size = 400,
            Symbols = ["UserApi"],
        },
        new IndexedFileInfo
        {
            Path = "D:/proj/tests/auth.test.ts",
            NormalizedPath = "tests/auth.test.ts",
            Language = "typescript",
            Size = 250,
            Symbols = ["AuthTest"],
        },
    };

    [Fact]
    public void RecallPipeline_ShouldRecallByPath()
    {
        var parser = new Context.Parsing.TaskParser();
        var task = parser.Parse("Fix bug in src/auth/token.ts");
        var pipeline = new RecallPipeline();

        var candidates = pipeline.Recall(task, CreateIndexedFiles());

        Assert.NotEmpty(candidates);
        Assert.Contains(candidates, c => c.NormalizedPath.Contains("token.ts"));
    }

    [Fact]
    public void RecallPipeline_ShouldRecallBySymbol()
    {
        var parser = new Context.Parsing.TaskParser();
        var task = parser.Parse("Update LoginService");
        var pipeline = new RecallPipeline();

        var candidates = pipeline.Recall(task, CreateIndexedFiles());

        Assert.Contains(candidates, c => c.NormalizedPath.Contains("login.ts"));
    }

    [Fact]
    public void RecallPipeline_ShouldRecallByGitDiff()
    {
        var parser = new Context.Parsing.TaskParser();
        var task = parser.Parse("Fix the issue");
        var pipeline = new RecallPipeline();

        var candidates = pipeline.Recall(task, CreateIndexedFiles(), gitDiffFiles: ["user.ts"]);

        Assert.Contains(candidates, c => c.NormalizedPath.Contains("user.ts"));
        Assert.Contains(candidates.Where(c => c.NormalizedPath.Contains("user.ts")), c => c.Sources.Contains(RecallSource.GitDiff));
    }

    [Fact]
    public void RecallPipeline_ShouldIncludeCurrentFile()
    {
        var parser = new Context.Parsing.TaskParser();
        var task = parser.Parse("Debug the issue");
        var pipeline = new RecallPipeline();

        var candidates = pipeline.Recall(task, CreateIndexedFiles(), currentFile: "src/auth/login.ts");

        Assert.Contains(candidates, c => c.NormalizedPath.Contains("login.ts"));
    }

    [Fact]
    public void RankingEngine_Rank_ShouldSortByScore()
    {
        var parser = new Context.Parsing.TaskParser();
        var task = parser.Parse("Fix TokenService in token.ts");
        var pipeline = new RecallPipeline();
        var ranker = new RankingEngine();

        var candidates = pipeline.Recall(task, CreateIndexedFiles());
        var ranked = ranker.Rank(candidates, DefaultRankingProfile.Create(), task);

        Assert.NotEmpty(ranked);
        // TokenService file should rank highest
        Assert.Contains("token.ts", ranked[0].NormalizedPath);
    }

    [Fact]
    public void RankingEngine_Rank_ShouldProduceStableOrder()
    {
        var parser = new Context.Parsing.TaskParser();
        var task = parser.Parse("fix TokenService");
        var pipeline = new RecallPipeline();
        var ranker = new RankingEngine();
        var profile = DefaultRankingProfile.Create();

        var candidates = pipeline.Recall(task, CreateIndexedFiles());
        var ranked1 = ranker.Rank(candidates, profile, task);
        var ranked2 = ranker.Rank(candidates, profile, task);

        Assert.Equal(
            ranked1.Select(r => r.NormalizedPath),
            ranked2.Select(r => r.NormalizedPath));
    }

    [Fact]
    public void RankingEngine_Rank_ShouldIncludeReasons()
    {
        var parser = new Context.Parsing.TaskParser();
        var task = parser.Parse("Update TokenService in src/auth/token.ts");
        var pipeline = new RecallPipeline();
        var ranker = new RankingEngine();

        var candidates = pipeline.Recall(task, CreateIndexedFiles());
        var ranked = ranker.Rank(candidates, DefaultRankingProfile.Create(), task);

        var top = ranked.First(r => r.NormalizedPath.Contains("token.ts"));
        Assert.NotEmpty(top.Reasons);
    }

    [Fact]
    public void RankingEngine_Rank_EmptyCandidates_ShouldReturnEmpty()
    {
        var parser = new Context.Parsing.TaskParser();
        var task = parser.Parse("zzzzz nonexistent");
        var ranker = new RankingEngine();

        var ranked = ranker.Rank([], DefaultRankingProfile.Create(), task);

        Assert.Empty(ranked);
    }

    [Fact]
    public void CandidateAggregator_Deduplicate_ShouldMergeSources()
    {
        var candidates = new List<CandidateFile>
        {
            new() { Path = "a.ts", NormalizedPath = "src/a.ts", Language = "ts", Size = 100, Sources = [RecallSource.FilePath] },
            new() { Path = "a.ts", NormalizedPath = "src/a.ts", Language = "ts", Size = 100, Sources = [RecallSource.Symbol] },
        };

        var deduped = CandidateAggregator.Deduplicate(candidates);

        Assert.Single(deduped);
        Assert.Equal(2, deduped[0].Sources.Count);
    }

    [Fact]
    public void DefaultRankingProfile_ShouldHaveVersion3()
    {
        var profile = DefaultRankingProfile.Create();

        Assert.Equal("deterministic-v1", profile.Id);
        Assert.Equal(3, profile.Version);
        Assert.True(profile.Weights.SymbolMatch > 0);
    }

    [Fact]
    public void FeatureWeights_Sum_ShouldBeOne()
    {
        var weights = new FeatureWeights();
        Assert.Equal(1.0, weights.Sum, precision: 2);
    }
}
