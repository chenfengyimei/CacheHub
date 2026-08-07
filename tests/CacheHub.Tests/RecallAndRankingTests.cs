using CacheHub.Context.Ranking;
using CacheHub.Context.Recall;

namespace CacheHub.Tests;

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
        new IndexedFileInfo
        {
            Path = "D:/proj/src/auth/appsettings.json",
            NormalizedPath = "src/auth/appsettings.json",
            Language = "json",
            Size = 150,
            Symbols = [],
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
    public void DefaultRankingProfile_ShouldHaveVersion5()
    {
        var profile = DefaultRankingProfile.Create();

        Assert.Equal("deterministic-v2", profile.Id);
        Assert.Equal(5, profile.Version);
        Assert.True(profile.Weights.SymbolMatch > 0);
        Assert.True(profile.Weights.ImportRelation > 0);
        Assert.True(profile.Weights.TestRelation > 0);
        Assert.True(profile.Weights.ConfigRelation > 0);
    }

    [Fact]
    public void FeatureWeights_Sum_ShouldBeOne()
    {
        var weights = new FeatureWeights();
        Assert.Equal(1.0, weights.Sum, precision: 2);
    }

    [Fact]
    public void RecallPipeline_ShouldExpandByImportRelation()
    {
        var parser = new Context.Parsing.TaskParser();
        var task = parser.Parse("Fix TokenService");
        var pipeline = new RecallPipeline();
        var files = CreateIndexedFiles();

        // token.ts has TokenService; importSearch says login.ts imports it
        var candidates = pipeline.Recall(task, files,
            symbolSearch: sym => sym == "TokenService" ? ["src/auth/token.ts"] : [],
            importSearch: sym => sym == "TokenService" ? ["src/auth/login.ts"] : []);

        Assert.Contains(candidates, c => c.NormalizedPath.Contains("login.ts"));
        Assert.Contains(candidates.Where(c => c.NormalizedPath.Contains("login.ts")),
            c => c.Sources.Contains(RecallSource.ImportRelation));
    }

    [Fact]
    public void RecallPipeline_ShouldDiscoverTestRelation()
    {
        var parser = new Context.Parsing.TaskParser();
        var task = parser.Parse("Fix TokenService");
        var pipeline = new RecallPipeline();
        var files = CreateIndexedFiles();

        var candidates = pipeline.Recall(task, files,
            symbolSearch: sym => sym == "TokenService" ? ["src/auth/token.ts"] : []);

        // auth.test.ts should be discovered as test relation to token.ts
        Assert.Contains(candidates, c => c.NormalizedPath.Contains("auth.test.ts"));
        Assert.Contains(candidates.Where(c => c.NormalizedPath.Contains("auth.test.ts")),
            c => c.Sources.Contains(RecallSource.TestRelation));
    }

    [Fact]
    public void RecallPipeline_ShouldDiscoverConfigRelation()
    {
        var parser = new Context.Parsing.TaskParser();
        var pipeline = new RecallPipeline();
        var files = new List<IndexedFileInfo>
        {
            new() { Path = "D:/proj/src/auth/token.ts", NormalizedPath = "src/auth/token.ts", Language = "typescript", Size = 500, Symbols = ["TokenService"] },
            new() { Path = "D:/proj/src/auth/appsettings.json", NormalizedPath = "src/auth/appsettings.json", Language = "json", Size = 100, Symbols = [] },
        };
        var task = parser.Parse("Fix TokenService");
        var candidates = pipeline.Recall(task, files,
            symbolSearch: sym => sym == "TokenService" ? ["src/auth/token.ts"] : []);

        Assert.Contains(candidates, c => c.NormalizedPath.Contains("appsettings.json"));
        Assert.Contains(candidates.Where(c => c.NormalizedPath.Contains("appsettings.json")),
            c => c.Sources.Contains(RecallSource.ConfigRelation));
    }

    [Fact]
    public void RecallPipeline_ShouldFallbackWhenNoCandidates()
    {
        var parser = new Context.Parsing.TaskParser();
        var task = parser.Parse("zzzzz nonexistent");
        var pipeline = new RecallPipeline();
        var files = new List<IndexedFileInfo>
        {
            new() { Path = "D:/proj/Program.cs", NormalizedPath = "Program.cs", Language = "csharp", Size = 100, Symbols = [] },
            new() { Path = "D:/proj/src/large.ts", NormalizedPath = "src/large.ts", Language = "typescript", Size = 99999, Symbols = [] },
        };

        var candidates = pipeline.Recall(task, files);

        Assert.NotEmpty(candidates);
        Assert.Contains(candidates, c => c.Sources.Contains(RecallSource.DirectoryFallback));
    }

    [Fact]
    public void RecallPipeline_ShouldRecordSourceEvidence()
    {
        var parser = new Context.Parsing.TaskParser();
        var task = parser.Parse("Fix TokenService in src/auth/token.ts");
        var pipeline = new RecallPipeline();

        var candidates = pipeline.Recall(task, CreateIndexedFiles(),
            ftsSearch: kw => kw == "token" ? [new FtsMatch("src/auth/token.ts", "typescript", "TokenService token")] : []);

        var tokenCandidate = candidates.First(c => c.NormalizedPath.Contains("token.ts"));
        Assert.NotEmpty(tokenCandidate.Evidence);
        Assert.Contains(tokenCandidate.Evidence, e => e.Source == RecallSource.Symbol);
        Assert.Contains(tokenCandidate.Evidence, e => e.Source == RecallSource.FilePath);
    }

    [Fact]
    public void RecallPipeline_ShouldRespectMaxCandidates()
    {
        var parser = new Context.Parsing.TaskParser();
        var task = parser.Parse("Fix bug in auth");
        var pipeline = new RecallPipeline();
        var files = CreateIndexedFiles();

        var candidates = pipeline.Recall(task, files, options: new RecallOptions { MaxCandidates = 2 });

        Assert.True(candidates.Count <= 2);
    }

    [Fact]
    public void RankingEngine_ShouldScoreImportRelationLowerThanDirectMatch()
    {
        var parser = new Context.Parsing.TaskParser();
        var task = parser.Parse("Fix TokenService");
        var pipeline = new RecallPipeline();
        var ranker = new RankingEngine();

        var candidates = pipeline.Recall(task, CreateIndexedFiles(),
            symbolSearch: sym => sym == "TokenService" ? ["src/auth/token.ts"] : [],
            importSearch: sym => sym == "TokenService" ? ["src/api/user.ts"] : []);

        var ranked = ranker.Rank(candidates, DefaultRankingProfile.Create(), task);
        var tokenRank = ranked.First(r => r.NormalizedPath.Contains("token.ts"));
        var userRank = ranked.FirstOrDefault(r => r.NormalizedPath.Contains("user.ts"));

        if (userRank is not null)
        {
            Assert.True(tokenRank.Score > userRank.Score,
                $"Direct symbol match ({tokenRank.Score}) should rank higher than import relation ({userRank.Score})");
        }
    }
}
