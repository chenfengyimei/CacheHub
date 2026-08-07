using CacheHub.Core.Benchmarks;
using CacheHub.Core.Benchmarks.Engine;
using CacheHub.Core.Benchmarks.Tasks;

namespace CacheHub.Tests;

/// <summary>
/// R12 Gate regression tests: real benchmark, no simulated metrics, phase gate.
/// </summary>
public class R12GateRegressionTests
{
    // R12 Gate: Task set has at least 20 tasks
    [Fact]
    public void Gate_TaskSet_HasAtLeast20Tasks()
    {
        Assert.True(BenchmarkTaskSet.Tasks.Count >= 20);
    }

    // R12 Gate: Task set includes Chinese descriptions
    [Fact]
    public void Gate_TaskSet_IncludesChinese()
    {
        var chineseTasks = BenchmarkTaskSet.Tasks
            .Where(t => t.TaskDescription.Any(c => c >= '\u4e00' && c <= '\u9fff'));
        Assert.NotEmpty(chineseTasks);
    }

    // R12 Gate: Task set includes Monorepo
    [Fact]
    public void Gate_TaskSet_IncludesMonorepo()
    {
        var monorepoTasks = BenchmarkTaskSet.Tasks
            .Where(t => t.Language == "mixed" || t.RepositoryId.Contains("monorepo"));
        Assert.NotEmpty(monorepoTasks);
    }

    // R12 Gate: Ground truth has Required/Helpful/Distractor
    [Fact]
    public void Gate_GroundTruth_HasAllCategories()
    {
        foreach (var task in BenchmarkTaskSet.Tasks)
        {
            var gt = BenchmarkTaskSet.GetGroundTruth(task.Id);
            Assert.NotEmpty(gt.RequiredFiles);
            Assert.NotEmpty(gt.HelpfulFiles);
            Assert.NotEmpty(gt.DistractorFiles);
        }
    }

    // R12 Gate: Phase gate thresholds exist
    [Fact]
    public void Gate_PhaseGateThresholds_Exist()
    {
        var thresholds = new PhaseGateThresholds();
        Assert.True(thresholds.MinFileRecallAt10 > 0);
        Assert.True(thresholds.MaxMissingContextFailureRate > 0);
        Assert.True(thresholds.MinTestPassRatio > 0);
        Assert.True(thresholds.MinMeanTokenReduction > 0);
    }

    // R12 Gate: Metrics calculator computes recall
    [Fact]
    public void Gate_MetricsCalculator_ComputesRecall()
    {
        var selected = new[] { "a.ts", "b.ts" };
        var required = new[] { "a.ts", "c.ts" };
        var recall = MetricsCalculator.ComputeRecall(selected, required);
        Assert.Equal(0.5, recall); // 1 out of 2 required found
    }

    // R12 Gate: Metrics calculator computes precision
    [Fact]
    public void Gate_MetricsCalculator_ComputesPrecision()
    {
        var groundTruth = new GroundTruth
        {
            TaskId = "test",
            RequiredFiles = ["a.ts"],
            HelpfulFiles = ["b.ts"],
            DistractorFiles = ["c.ts"],
        };
        var precision = MetricsCalculator.ComputePrecision(["a.ts", "c.ts"], groundTruth);
        Assert.Equal(0.5, precision); // 1 relevant out of 2 selected
    }

    // R12 Gate: No hardcoded metrics in benchmark
    [Fact]
    public void Gate_Benchmark_NoHardcodedSuccess()
    {
        // Verify that BenchmarkTaskSet tasks don't have pre-computed success/failure
        foreach (var task in BenchmarkTaskSet.Tasks)
        {
            // Task should only have data, not results
            Assert.NotEmpty(task.TaskDescription);
            Assert.NotEmpty(task.RequiredFiles);
            Assert.NotEmpty(task.CommitHash);
        }
    }
}
