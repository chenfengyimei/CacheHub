using AiKv.Context.Engine;
using AiKv.Context.Parsing;
using AiKv.Context.Ranking;
using AiKv.Context.Recall;
using AiKv.Core.Benchmarks;
using AiKv.Core.Benchmarks.Engine;
using AiKv.Core.Benchmarks.Tasks;
using AiKv.Core.Caching;
using AiKv.Core.Context;
using AiKv.Core.Detection;
using AiKv.Core.Feedback;
using AiKv.Core.Identifiers;
using AiKv.Core.Paths;
using AiKv.Core.Security;
using AiKv.Core.Tokens;
using AiKv.Core.Workspaces;
using Budget = AiKv.Context.Budget;

namespace AiKv.Tests;

/// <summary>
/// End-to-end integration tests that verify multiple modules work together.
/// </summary>
public class EndToEndTests
{
    [Fact]
    public void E2E_TaskParser_To_Recall_To_Ranking_To_Selection()
    {
        // 1. Parse task
        var parser = new TaskParser();
        var task = parser.Parse("Fix TokenService refresh token bug in src/auth/token.ts");

        Assert.NotEmpty(task.ExtractedSymbols);
        Assert.NotEmpty(task.ExtractedPaths);

        // 2. Recall candidates
        var indexedFiles = new List<IndexedFileInfo>
        {
            new() { Path = "src/auth/token.ts", NormalizedPath = "src/auth/token.ts", Language = "typescript", Size = 500, Symbols = ["TokenService", "RefreshToken"] },
            new() { Path = "src/auth/login.ts", NormalizedPath = "src/auth/login.ts", Language = "typescript", Size = 300, Symbols = ["LoginService"] },
            new() { Path = "README.md", NormalizedPath = "README.md", Language = "markdown", Size = 1000, Symbols = [] },
        };

        var pipeline = new RecallPipeline();
        var candidates = pipeline.Recall(task, indexedFiles);

        Assert.NotEmpty(candidates);
        Assert.Contains(candidates, c => c.NormalizedPath.Contains("token.ts"));

        // 3. Rank
        var ranker = new RankingEngine();
        var ranked = ranker.Rank(candidates, DefaultRankingProfile.Create(), task);

        Assert.NotEmpty(ranked);
        Assert.Contains("token.ts", ranked[0].NormalizedPath);

        // 4. Select within budget
        var selection = new Context.Selection.SelectionEngine();
        var result = selection.Select(ranked, Budget.DefaultTokenBudgetPolicy.Create(),
            path => "export class TokenService { refresh() {} }",
            path => "sha256:abc");

        Assert.NotEmpty(result.SelectedFiles);
        Assert.False(result.BudgetExceeded);
    }

    [Fact]
    public void E2E_SecurityScanner_BlocksSecrets_InContextContent()
    {
        var enforcer = new SecurityPolicyEnforcer();

        // Simulate a file with a secret
        var filePath = "config/settings.ts";
        var content = "const apiKey = 'sk-1234567890abcdefghijklmnopqrstuv';";

        var (allowed, scan, reason) = enforcer.CheckBeforeSend(filePath, content);

        // Path is allowed but content scan should fail
        Assert.False(allowed);
        Assert.NotNull(scan);
        Assert.False(scan!.Passed);
    }

    [Fact]
    public void E2E_Tokenizer_Integrates_With_Budget()
    {
        var registry = new TokenizerRegistry();
        registry.Register("gpt-4", new CodeTokenizer());

        var content = "export function hello(name: string): void { console.log(`Hello ${name}`); }";
        var tokens = registry.CountTokens("gpt-4", content);

        var budget = Budget.DefaultTokenBudgetPolicy.Create();

        Assert.True(tokens > 0);
        Assert.True(budget.FitsEffective(tokens));
    }

    [Fact]
    public void E2E_Benchmark_TaskSet_Metrics_Pipeline()
    {
        // 1. Get a benchmark task
        var task = BenchmarkTaskSet.Tasks.First(t => t.Id == "bench-001");
        var gt = BenchmarkTaskSet.GetGroundTruth(task.Id);

        // 2. Simulate AI_KV selecting files (pretend it selected the right files)
        var selectedFiles = new HashSet<string>(task.RequiredFiles, StringComparer.OrdinalIgnoreCase);

        // 3. Compute metrics
        var recall = MetricsCalculator.ComputeRecall(selectedFiles, gt.RequiredFiles);
        var precision = MetricsCalculator.ComputePrecision(selectedFiles, gt);

        Assert.Equal(1.0, recall); // All required files selected
        Assert.True(precision > 0.5); // No distractors selected
    }

    [Fact]
    public void E2E_Benchmark_PhaseGate_Evaluation()
    {
        // Simulate aggregated metrics for all tasks
        var taskMetrics = BenchmarkTaskSet.Tasks.Select(t => new AggregatedMetrics
        {
            TaskId = t.Id,
            MeanFileRecall = 0.95,
            MissingContextRate = 0.05,
            SuccessRate = 0.98,
            StaleContextRate = 0.0,
            MeanInputTokens = 8000,
            RunCount = 3,
        }).ToList();

        var baselineMetrics = BenchmarkTaskSet.Tasks.Select(t => new AggregatedMetrics
        {
            TaskId = t.Id,
            MeanFileRecall = 0.90,
            MissingContextRate = 0.10,
            SuccessRate = 0.95,
            StaleContextRate = 0.0,
            MeanInputTokens = 12000,
            RunCount = 3,
        }).ToList();

        var result = MetricsCalculator.EvaluatePhaseGate(taskMetrics, baselineMetrics, new PhaseGateThresholds());

        // With 95% recall, 5% missing, 98% success, 33% token reduction → should pass
        Assert.True(result.Passed);
        Assert.Empty(result.FailedGates);
    }

    [Fact]
    public void E2E_Feedback_RoundTrip()
    {
        // Create feedback
        var feedback = new ContextFeedback
        {
            ContextPackageId = "ctx-001",
            ClientId = "test-agent",
            FilesActuallyRead = ["src/auth/token.ts"],
            TaskCompleted = true,
            MissingContextReported = false,
            TotalWorkflowInputTokens = 15000,
        };

        // Serialize
        var json = System.Text.Json.JsonSerializer.Serialize(feedback);

        // Parse back
        var parsed = ContextFeedback.ParseJson(json);

        Assert.NotNull(parsed);
        Assert.Equal("ctx-001", parsed!.ContextPackageId);
        Assert.True(parsed.TaskCompleted);
        Assert.Single(parsed.FilesActuallyRead);
    }

    [Fact]
    public void E2E_Cache_Integration_With_ParserCache()
    {
        // Parse a file, cache it, then verify cache hit
        var parserCache = new Indexing.Parsing.Cache.ParserCache();
        var parser = new Indexing.Parsing.CSharpRegexParser();

        var content = "public class UserService { public void GetUser() {} }";
        var hash = "sha256:abc";

        // First parse — miss
        var result1 = parserCache.GetOrParse(content, "UserService.cs", hash, parser);
        Assert.Equal(1, parserCache.Count);

        // Second parse — hit
        var result2 = parserCache.GetOrParse(content, "UserService.cs", hash, parser);
        Assert.Same(result1, result2);

        // Invalidate
        parserCache.Invalidate(hash);
        Assert.Equal(0, parserCache.Count);
    }

    [Fact]
    public void E2E_Workspace_Creation_And_Path_Validation()
    {
        var ws = Workspace.Create("test-project", @"C:\projects\test");

        Assert.Equal("test-project", ws.Name);
        Assert.DoesNotContain("\\", ws.RootPath);
        Assert.False(string.IsNullOrEmpty(ws.RootPathHash));

        // Path normalizer should validate paths within workspace
        Assert.True(PathNormalizer.IsWithinRoot(ws.RootPath, ws.RootPath + "/src/app.ts"));
        Assert.False(PathNormalizer.IsWithinRoot(ws.RootPath, @"C:\other\project"));
    }
}
