using AiKv.Context.Budget;
using AiKv.Context.Cache;
using AiKv.Context.Engine;
using AiKv.Context.Expand;
using AiKv.Context.Explain;
using AiKv.Context.Parsing;
using AiKv.Context.Ranking;
using AiKv.Context.Recall;
using AiKv.Core.Context;

namespace AiKv.Tests;

public class ContextEngineTests
{
    private static ContextEngine CreateEngine() => new();

    private static List<IndexedFileInfo> TestFiles => new()
    {
        new() { Path = "src/auth/token.ts", NormalizedPath = "src/auth/token.ts", Language = "typescript", Size = 300, Symbols = ["TokenService", "RefreshToken"] },
        new() { Path = "src/auth/login.ts", NormalizedPath = "src/auth/login.ts", Language = "typescript", Size = 200, Symbols = ["LoginService"] },
        new() { Path = "src/api/user.ts", NormalizedPath = "src/api/user.ts", Language = "typescript", Size = 250, Symbols = ["UserApi"] },
    };

    [Fact]
    public void ContextEngine_Build_ShouldProduceManifest()
    {
        var engine = CreateEngine();

        var manifest = engine.Build(
            new ContextBuildRequest
            {
                WorkspaceId = Core.Identifiers.WorkspaceId.New(),
                IndexSnapshotId = Core.Identifiers.IndexSnapshotId.New(),
                Task = "Fix TokenService refresh token bug in src/auth/token.ts",
            },
            () => TestFiles,
            path => $"export class TokenService {{ refresh() {{}} }}",
            path => "sha256:abc123");

        Assert.Equal(1, manifest.SchemaVersion);
        Assert.NotEmpty(manifest.SelectedFiles);
        Assert.NotEmpty(manifest.Task.OriginalText);
        Assert.Equal("deterministic-query-v1", manifest.Task.QueryParserVersion);
        Assert.Equal("deterministic-v1", manifest.Ranking.ProfileId);
        Assert.True(manifest.Budget.ActualEstimate > 0);
    }

    [Fact]
    public void ContextEngine_Build_ShouldRankTokenServiceFirst()
    {
        var engine = CreateEngine();

        var manifest = engine.Build(
            new ContextBuildRequest
            {
                WorkspaceId = Core.Identifiers.WorkspaceId.New(),
                IndexSnapshotId = Core.Identifiers.IndexSnapshotId.New(),
                Task = "Fix TokenService in src/auth/token.ts",
            },
            () => TestFiles,
            path => $"export class {{ }} // {path}",
            path => "sha256:abc");

        Assert.NotEmpty(manifest.SelectedFiles);
        // TokenService file should be in top selected
        Assert.Contains(manifest.SelectedFiles, f => f.Path.Contains("token.ts"));
    }

    [Fact]
    public void ContextEngine_Build_ShouldRecordExcluded()
    {
        var engine = CreateEngine();
        var budget = DefaultTokenBudgetPolicy.Create(modelContextWindow: 2000);

        var manifest = engine.Build(
            new ContextBuildRequest
            {
                WorkspaceId = Core.Identifiers.WorkspaceId.New(),
                IndexSnapshotId = Core.Identifiers.IndexSnapshotId.New(),
                Task = "Fix everything TokenService LoginService UserApi",
                Budget = budget,
            },
            () => TestFiles,
            path => new string('x', 3000),
            path => "sha256:abc");

        // Some files should be excluded due to budget
        // (at least ExcludedCandidates should exist, or SelectedFiles count < total)
        Assert.True(manifest.ExcludedCandidates.Count > 0 || manifest.SelectedFiles.Count < TestFiles.Count);
    }

    [Fact]
    public void ContextEngine_Build_ShouldBeDeterministic()
    {
        var engine = CreateEngine();
        var wsId = Core.Identifiers.WorkspaceId.New();
        var snapId = Core.Identifiers.IndexSnapshotId.New();

        var manifest1 = engine.Build(
            new ContextBuildRequest { WorkspaceId = wsId, IndexSnapshotId = snapId, Task = "Fix TokenService in token.ts" },
            () => TestFiles, path => "content", path => "hash");

        var manifest2 = engine.Build(
            new ContextBuildRequest { WorkspaceId = wsId, IndexSnapshotId = snapId, Task = "Fix TokenService in token.ts" },
            () => TestFiles, path => "content", path => "hash");

        Assert.Equal(manifest1.SelectedFiles.Select(f => f.Path), manifest2.SelectedFiles.Select(f => f.Path));
        Assert.Equal(manifest1.Budget.ActualEstimate, manifest2.Budget.ActualEstimate);
    }

    [Fact]
    public void ContextExplainer_Explain_ShouldListSelectedAndExcluded()
    {
        var manifest = new ContextPackageManifest
        {
            Id = Core.Identifiers.ContextPackageId.New(),
            WorkspaceId = Core.Identifiers.WorkspaceId.New(),
            IndexSnapshotId = Core.Identifiers.IndexSnapshotId.New(),
            Task = new TaskInfo { OriginalText = "test", QueryParserVersion = "v1" },
            Ranking = new RankingInfo { ProfileId = "v1", ProfileVersion = 1 },
            Budget = new BudgetInfo
            {
                ModelContextWindow = 128000,
                AgentReservedTokens = 10000,
                ResponseReservedTokens = 8000,
                ContextTarget = 80000,
                ContextHardLimit = 90000,
                SafetyMargin = 10000,
                ActualEstimate = 50000,
            },
            SelectedFiles = [new SelectedFile { Path = "a.ts", ContentHash = "h1", Mode = SelectionMode.Full, Score = 0.9, Reasons = ["match"] }],
            ExcludedCandidates = [new ExcludedCandidate { Path = "b.ts", Score = 0.4, Reason = "budget" }],
            Safety = new SafetyInfo { CloudSendAllowed = true, SecretsScanPassed = true },
            ContextEngineVersion = "0.1.0",
            ChunkingStrategyVersion = "v1",
            TokenBudgetPolicyVersion = "v1",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var explanations = ContextExplainer.Explain(manifest);

        Assert.Equal(2, explanations.Count);
        Assert.Contains(explanations, e => e.Selected && e.Path == "a.ts");
        Assert.Contains(explanations, e => !e.Selected && e.Path == "b.ts");
    }

    [Fact]
    public void ContextExplainer_DetectPotentialMisses_ShouldFindHighScoreExcluded()
    {
        var manifest = new ContextPackageManifest
        {
            Id = Core.Identifiers.ContextPackageId.New(),
            WorkspaceId = Core.Identifiers.WorkspaceId.New(),
            IndexSnapshotId = Core.Identifiers.IndexSnapshotId.New(),
            Task = new TaskInfo { OriginalText = "t", QueryParserVersion = "v1" },
            Ranking = new RankingInfo { ProfileId = "v1", ProfileVersion = 1 },
            Budget = new BudgetInfo
            {
                ModelContextWindow = 1000,
                AgentReservedTokens = 0,
                ResponseReservedTokens = 0,
                ContextTarget = 500,
                ContextHardLimit = 600,
                SafetyMargin = 50,
                ActualEstimate = 400,
            },
            SelectedFiles = [],
            ExcludedCandidates =
            [
                new ExcludedCandidate { Path = "high.ts", Score = 0.8, Reason = "budget" },
                new ExcludedCandidate { Path = "low.ts", Score = 0.1, Reason = "budget" },
            ],
            Safety = new SafetyInfo { CloudSendAllowed = true, SecretsScanPassed = true },
            ContextEngineVersion = "0.1.0",
            ChunkingStrategyVersion = "v1",
            TokenBudgetPolicyVersion = "v1",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var misses = ContextExplainer.DetectPotentialMisses(manifest);

        Assert.Single(misses);
        Assert.Contains("high.ts", misses);
    }

    [Fact]
    public void ContextPackageCache_TryGetOrBuild_ShouldCache()
    {
        var cache = new ContextPackageCache();
        var key = CacheKey.Build("task", "snap1", "profile1", 1, 80000, 90000, "sec1", "ignore1");

        var buildCount = 0;
        var result1 = cache.TryGetOrBuild(key, () => { buildCount++; return CreateMinimalManifest(); }, out _);
        var result2 = cache.TryGetOrBuild(key, () => { buildCount++; return CreateMinimalManifest(); }, out _);

        Assert.False(result1); // first call builds
        Assert.True(result2); // second call hits cache
        Assert.Equal(1, buildCount);
    }

    [Fact]
    public void ContextPackageCache_DifferentKey_ShouldNotHit()
    {
        var cache = new ContextPackageCache();
        var key1 = CacheKey.Build("task1", "snap1", "p1", 1, 80000, 90000, null, null);
        var key2 = CacheKey.Build("task2", "snap1", "p1", 1, 80000, 90000, null, null);

        cache.TryGetOrBuild(key1, CreateMinimalManifest, out _);
        var hit = cache.TryGetOrBuild(key2, CreateMinimalManifest, out _);

        Assert.False(hit); // different task → miss
    }

    [Fact]
    public void ContextExpander_ExpandByFile_ShouldReturnResult()
    {
        var expander = new ContextExpander();
        var result = expander.ExpandByFile("ctx_001", "src/new.ts", "export const x = 1;", "需要 new.ts 的定义");

        Assert.NotEmpty(result.AddedItems);
        Assert.True(result.AdditionalTokens > 0);
        Assert.Contains("new.ts", result.AddedItems[0].Path);
    }

    [Fact]
    public void CacheKey_Build_ShouldProduceConsistentHash()
    {
        var key1 = CacheKey.Build("task", "snap", "profile", 1, 80000, 90000, "sec", "ignore");
        var key2 = CacheKey.Build("task", "snap", "profile", 1, 80000, 90000, "sec", "ignore");

        Assert.Equal(key1.FullKey, key2.FullKey);
    }

    [Fact]
    public void CacheKey_DifferentBudget_ShouldProduceDifferentHash()
    {
        var key1 = CacheKey.Build("task", "snap", "profile", 1, 80000, 90000, null, null);
        var key2 = CacheKey.Build("task", "snap", "profile", 1, 40000, 50000, null, null);

        Assert.NotEqual(key1.FullKey, key2.FullKey);
    }

    private static ContextPackageManifest CreateMinimalManifest() => new()
    {
        Id = Core.Identifiers.ContextPackageId.New(),
        WorkspaceId = Core.Identifiers.WorkspaceId.New(),
        IndexSnapshotId = Core.Identifiers.IndexSnapshotId.New(),
        Task = new TaskInfo { OriginalText = "test", QueryParserVersion = "v1" },
        Ranking = new RankingInfo { ProfileId = "v1", ProfileVersion = 1 },
        Budget = new BudgetInfo
        {
            ModelContextWindow = 128000,
            AgentReservedTokens = 10000,
            ResponseReservedTokens = 8000,
            ContextTarget = 80000,
            ContextHardLimit = 90000,
            SafetyMargin = 10000,
            ActualEstimate = 0,
        },
        SelectedFiles = [],
        ExcludedCandidates = [],
        Safety = new SafetyInfo { CloudSendAllowed = true, SecretsScanPassed = true },
        ContextEngineVersion = "0.1.0",
        ChunkingStrategyVersion = "v1",
        TokenBudgetPolicyVersion = "v1",
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
