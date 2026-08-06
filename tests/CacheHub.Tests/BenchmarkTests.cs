using CacheHub.Core.Benchmarks;
using CacheHub.Core.Benchmarks.Engine;
using CacheHub.Core.Benchmarks.Reporting;

namespace CacheHub.Tests;

public class BenchmarkTests
{
    private static GroundTruth CreateGroundTruth() => new()
    {
        TaskId = "task-001",
        RequiredFiles = ["src/auth/token.ts", "src/auth/refresh.ts"],
        HelpfulFiles = ["src/auth/types.ts"],
        DistractorFiles = ["docs/auth.md", "src/legacy/auth.ts"],
    };

    [Fact]
    public void ComputeRecall_ShouldReturn1_WhenAllRequiredInSelected()
    {
        var selected = new HashSet<string> { "src/auth/token.ts", "src/auth/refresh.ts", "other.ts" };
        var required = new HashSet<string> { "src/auth/token.ts", "src/auth/refresh.ts" };

        var recall = MetricsCalculator.ComputeRecall(selected, required);

        Assert.Equal(1.0, recall);
    }

    [Fact]
    public void ComputeRecall_ShouldReturn0_WhenNoRequiredInSelected()
    {
        var selected = new HashSet<string> { "other.ts", "unrelated.ts" };
        var required = new HashSet<string> { "src/auth/token.ts" };

        var recall = MetricsCalculator.ComputeRecall(selected, required);

        Assert.Equal(0.0, recall);
    }

    [Fact]
    public void ComputeRecall_ShouldReturnHalf_WhenOneOfTwoRequired()
    {
        var selected = new HashSet<string> { "src/auth/token.ts", "other.ts" };
        var required = new HashSet<string> { "src/auth/token.ts", "src/auth/refresh.ts" };

        var recall = MetricsCalculator.ComputeRecall(selected, required);

        Assert.Equal(0.5, recall);
    }

    [Fact]
    public void ComputePrecision_ShouldPenalizeDistractors()
    {
        var selected = new HashSet<string> { "src/auth/token.ts", "docs/auth.md", "src/legacy/auth.ts" };
        var gt = CreateGroundTruth();

        var precision = MetricsCalculator.ComputePrecision(selected, gt);

        // Only 1 out of 3 is required/helpful → precision = 1/3
        Assert.True(precision < 0.5);
    }

    [Fact]
    public void ComputePrecision_ShouldReturn1_WhenAllRelevant()
    {
        var selected = new HashSet<string> { "src/auth/token.ts", "src/auth/refresh.ts" };
        var gt = CreateGroundTruth();

        var precision = MetricsCalculator.ComputePrecision(selected, gt);

        Assert.Equal(1.0, precision);
    }

    [Fact]
    public void Aggregate_ShouldComputeMeanAndStdDev()
    {
        var runs = new List<TaskMetrics>
        {
            CreateMetrics(0.8, 0.7, true, 10000),
            CreateMetrics(0.9, 0.8, true, 12000),
            CreateMetrics(0.7, 0.6, false, 9000),
        };

        var agg = MetricsCalculator.Aggregate("task-001", runs);

        Assert.Equal(3, agg.RunCount);
        Assert.True(agg.MeanFileRecall > 0.79 && agg.MeanFileRecall < 0.81);
        Assert.True(agg.StdDevFileRecall > 0);
        Assert.Equal(0.7, agg.WorstFileRecall);
        Assert.True(agg.SuccessRate > 0.66 && agg.SuccessRate < 0.67);
    }

    [Fact]
    public void EvaluatePhaseGate_ShouldPass_WhenMetricsMeetThresholds()
    {
        var tasks = new List<AggregatedMetrics>
        {
            new() { TaskId = "t1", MeanFileRecall = 0.95, MissingContextRate = 0.05, SuccessRate = 0.98, StaleContextRate = 0.0, MeanInputTokens = 8000 },
            new() { TaskId = "t2", MeanFileRecall = 0.92, MissingContextRate = 0.08, SuccessRate = 0.97, StaleContextRate = 0.0, MeanInputTokens = 9000 },
        };
        var baseline = new List<AggregatedMetrics>
        {
            new() { TaskId = "t1", MeanFileRecall = 0.90, MissingContextRate = 0.1, SuccessRate = 0.95, StaleContextRate = 0.0, MeanInputTokens = 12000 },
            new() { TaskId = "t2", MeanFileRecall = 0.88, MissingContextRate = 0.15, SuccessRate = 0.93, StaleContextRate = 0.0, MeanInputTokens = 14000 },
        };
        var thresholds = new PhaseGateThresholds();

        var result = MetricsCalculator.EvaluatePhaseGate(tasks, baseline, thresholds);

        Assert.True(result.Passed);
        Assert.Empty(result.FailedGates);
    }

    [Fact]
    public void EvaluatePhaseGate_ShouldFail_WhenRecallTooLow()
    {
        var tasks = new List<AggregatedMetrics>
        {
            new() { TaskId = "t1", MeanFileRecall = 0.70, MissingContextRate = 0.05, SuccessRate = 0.98, StaleContextRate = 0.0, MeanInputTokens = 8000 },
        };
        var baseline = new List<AggregatedMetrics>
        {
            new() { TaskId = "t1", MeanFileRecall = 0.70, MissingContextRate = 0.05, SuccessRate = 0.98, StaleContextRate = 0.0, MeanInputTokens = 12000 },
        };

        var result = MetricsCalculator.EvaluatePhaseGate(tasks, baseline, new PhaseGateThresholds());

        Assert.False(result.Passed);
        Assert.Contains(result.FailedGates, g => g.Contains("Recall"));
    }

    [Fact]
    public void AttributeFailure_ShouldCategorizeRetrieval()
    {
        var metrics = CreateMetrics(0.2, 0.5, false, 5000);
        var gt = CreateGroundTruth();

        var attr = MetricsCalculator.AttributeFailure(metrics, gt);

        Assert.Equal(FailureCategory.Retrieval, attr.Category);
    }

    [Fact]
    public void AttributeFailure_ShouldCategorizeRanking_WhenRecallOkButPrecisionLow()
    {
        var metrics = CreateMetrics(0.8, 0.1, false, 5000);
        var gt = CreateGroundTruth();

        var attr = MetricsCalculator.AttributeFailure(metrics, gt);

        Assert.Equal(FailureCategory.Ranking, attr.Category);
    }

    [Fact]
    public void AttributeFailure_ShouldReturnNone_WhenTaskCompleted()
    {
        var metrics = CreateMetrics(0.9, 0.8, true, 5000);
        var gt = CreateGroundTruth();

        var attr = MetricsCalculator.AttributeFailure(metrics, gt);

        Assert.Equal(FailureCategory.None, attr.Category);
    }

    [Fact]
    public void ReportGenerator_GenerateJson_ShouldContainRequiredFields()
    {
        var config = new BenchmarkConfig
        {
            ModelId = "test-model",
            AgentId = "test-agent",
            SystemPrompt = "You are a helpful assistant",
            RunsPerTask = 3,
            ResetBetweenRuns = true,
            ShareBuildCache = false,
        };
        var tasks = new List<AggregatedMetrics>
        {
            new() { TaskId = "t1", MeanFileRecall = 0.95, MissingContextRate = 0.05, SuccessRate = 0.98, StaleContextRate = 0.0, MeanInputTokens = 8000, RunCount = 3 },
        };
        var failures = new List<FailureAttribution>();
        var phaseGate = new PhaseGateResult
        {
            Passed = true,
            ActualFileRecallAt10 = 0.95,
            ActualMissingContextRate = 0.05,
            ActualTestPassRatio = 0.98,
            ActualMeanTokenReduction = 0.33,
            ActualPositiveTokenTaskRatio = 1.0,
            ActualStaleContextErrorRate = 0.0,
            FailedGates = [],
        };

        var json = ReportGenerator.GenerateJson(config, tasks, failures, phaseGate);

        Assert.Contains("generatedAt", json);
        Assert.Contains("phaseGatePassed", json);
        Assert.Contains("meanFileRecallAt10", json);
        Assert.Contains("limitations", json);
    }

    [Fact]
    public void PhaseGateThresholds_Default_ShouldHaveExpectedValues()
    {
        var t = new PhaseGateThresholds();

        Assert.Equal(0.90, t.MinFileRecallAt10);
        Assert.Equal(0.10, t.MaxMissingContextFailureRate);
        Assert.Equal(0.95, t.MinTestPassRatio);
        Assert.Equal(0.20, t.MinMeanTokenReduction);
        Assert.Equal(0.60, t.MinPositiveTokenTaskRatio);
    }

    private static TaskMetrics CreateMetrics(double recall, double precision, bool completed, int tokens) => new()
    {
        TaskId = "task-001",
        RunNumber = 1,
        TaskCompleted = completed,
        TotalInputTokens = tokens,
        TotalOutputTokens = 500,
        Rounds = 3,
        FileRecallAt10 = recall,
        SymbolRecallAt10 = recall,
        ContextPrecision = precision,
        MissingContext = recall < 0.5,
        StaleContext = false,
        SelectedFiles = [],
        ActuallyReadFiles = [],
    };
}
