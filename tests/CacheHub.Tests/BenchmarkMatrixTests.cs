using CacheHub.Core.Benchmarks;
using CacheHub.Core.Benchmarks.Matrix;
using CacheHub.Core.Benchmarks.Tasks;
using Xunit;

namespace CacheHub.Tests;

/// <summary>
/// V7-W19: Tests for Benchmark Matrix Runner and Phase Gate evaluation.
/// </summary>
public class BenchmarkMatrixTests
{
    [Fact]
    public void RunRetrievalMatrix_ReturnsResultsForAllTasks()
    {
        var runner = new BenchmarkMatrixRunner();
        var result = runner.RunRetrievalMatrix(
            task => GetMockFiles(task),
            (task, path) => "mock content",
            (task, path) => "mock-hash",
            modelId: "test-model");

        Assert.Equal(BenchmarkTaskSet.Tasks.Count, result.Tasks.Count);
        Assert.Equal("test-model", result.ModelId);
        Assert.True(result.Summary.TotalTasks > 0);
    }

    [Fact]
    public void RunRetrievalMatrix_ComputesRecallAndTokenReduction()
    {
        var runner = new BenchmarkMatrixRunner();
        var result = runner.RunRetrievalMatrix(
            task => GetMockFiles(task),
            (task, path) => "mock content",
            (task, path) => "mock-hash");

        foreach (var taskResult in result.Tasks)
        {
            Assert.True(taskResult.FileRecallAt10 >= 0 && taskResult.FileRecallAt10 <= 1.0);
            Assert.True(taskResult.TokenReduction >= 0 && taskResult.TokenReduction <= 1.0);
            Assert.True(taskResult.SelectedFileCount >= 0);
        }
    }

    [Fact]
    public void RunRetrievalMatrix_PhaseGate_EvaluatesCorrectly()
    {
        var runner = new BenchmarkMatrixRunner();
        var result = runner.RunRetrievalMatrix(
            task => GetMockFiles(task),
            (task, path) => "mock",
            (task, path) => "hash");

        Assert.NotNull(result.PhaseGate);
        Assert.True(result.PhaseGate.ActualFileRecallAt10 >= 0);
        Assert.True(result.PhaseGate.ActualTokenReduction >= 0);
    }

    [Fact]
    public void MatrixPhaseGate_PassWhenAllThresholdsMet()
    {
        var summary = new MatrixSummary
        {
            TotalTasks = 10,
            MeanFileRecallAt10 = 0.95,
            MeanTokenReduction = 0.50,
            CacheHubSuccessRate = 0.90,
            BaselineSuccessRate = 0.85,
            MeanInputTokenReduction = 0.60,
            PositiveTokenTaskRatio = 0.80,
            TasksWithAgentResults = 10,
        };

        var gate = MatrixPhaseGate.Evaluate(summary);
        Assert.True(gate.Passed);
        Assert.Empty(gate.FailedGates);
    }

    [Fact]
    public void MatrixPhaseGate_FailWhenRecallTooLow()
    {
        var summary = new MatrixSummary
        {
            TotalTasks = 10,
            MeanFileRecallAt10 = 0.50, // below 0.90
            MeanTokenReduction = 0.50,
        };

        var gate = MatrixPhaseGate.Evaluate(summary);
        Assert.False(gate.Passed);
        Assert.Contains(gate.FailedGates, g => g.Contains("FileRecall"));
    }

    [Fact]
    public void MatrixPhaseGate_FailWhenTokenReductionTooLow()
    {
        var summary = new MatrixSummary
        {
            TotalTasks = 10,
            MeanFileRecallAt10 = 0.95,
            MeanTokenReduction = 0.10, // below 0.20
        };

        var gate = MatrixPhaseGate.Evaluate(summary);
        Assert.False(gate.Passed);
        Assert.Contains(gate.FailedGates, g => g.Contains("TokenReduction"));
    }

    [Fact]
    public void MatrixPhaseGate_FailWhenCacheHubSuccessBelowBaseline95Percent()
    {
        var summary = new MatrixSummary
        {
            TotalTasks = 10,
            MeanFileRecallAt10 = 0.95,
            MeanTokenReduction = 0.50,
            CacheHubSuccessRate = 0.70, // Baseline 0.90 * 0.95 = 0.855, 0.70 < 0.855
            BaselineSuccessRate = 0.90,
            MeanInputTokenReduction = 0.60,
            PositiveTokenTaskRatio = 0.80,
            TasksWithAgentResults = 10,
        };

        var gate = MatrixPhaseGate.Evaluate(summary);
        Assert.False(gate.Passed);
        Assert.Contains(gate.FailedGates, g => g.Contains("Success"));
    }

    [Fact]
    public void MatrixTaskResult_DerivedProperties_ComputeCorrectly()
    {
        var result = new MatrixTaskResult
        {
            TaskId = "test",
            RepositoryId = "repo",
            Language = "csharp",
            TaskDescription = "test task",
            FileRecallAt10 = 0.9,
            TokenReduction = 0.5,
            SelectedFileCount = 3,
            FullRepoTokenCount = 10000,
            SelectedTokenCount = 5000,
            CacheHubTaskCompleted = true,
            BaselineTaskCompleted = true,
            CacheHubInputTokens = 5000,
            BaselineInputTokens = 10000,
        };

        Assert.True(result.SuccessRateMaintained);
        Assert.Equal(0.5, result.InputTokenReduction);
    }

    [Fact]
    public void BenchmarkTaskSet_GetTasksForRepository_ReturnsCorrectTasks()
    {
        var tsTasks = BenchmarkTaskSet.GetTasksForRepository("sample-ts-auth");
        Assert.True(tsTasks.Count >= 2);
        Assert.All(tsTasks, t => Assert.Equal("sample-ts-auth", t.RepositoryId));
    }

    [Fact]
    public void BenchmarkTaskSet_AllTasksHaveRepositoryPath()
    {
        foreach (var task in BenchmarkTaskSet.Tasks)
        {
            Assert.NotNull(task.RepositoryPath);
            Assert.False(string.IsNullOrEmpty(task.RepositoryPath));
        }
    }

    [Fact]
    public void BenchmarkTaskSet_AllTasksHaveTestCommand()
    {
        foreach (var task in BenchmarkTaskSet.Tasks)
        {
            Assert.NotNull(task.TestCommand);
            Assert.False(string.IsNullOrEmpty(task.TestCommand));
        }
    }

    private static List<MatrixFileInfo> GetMockFiles(BenchmarkTask task)
    {
        var files = new List<MatrixFileInfo>();
        foreach (var req in task.RequiredFiles)
        {
            files.Add(new MatrixFileInfo
            {
                Path = req,
                NormalizedPath = req,
                Language = task.Language,
                Size = 1000,
            });
        }
        foreach (var help in task.HelpfulFiles)
        {
            files.Add(new MatrixFileInfo
            {
                Path = help,
                NormalizedPath = help,
                Language = task.Language,
                Size = 500,
            });
        }
        // Add some distractor files
        foreach (var dist in task.DistractorFiles.Take(2))
        {
            files.Add(new MatrixFileInfo
            {
                Path = dist,
                NormalizedPath = dist,
                Language = "text",
                Size = 200,
            });
        }
        return files;
    }
}
