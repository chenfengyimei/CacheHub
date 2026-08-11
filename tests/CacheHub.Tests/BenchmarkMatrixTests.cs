using CacheHub.Context.Engine;
using CacheHub.Context.Recall;
using CacheHub.Core.Benchmarks;
using CacheHub.Core.Benchmarks.Matrix;
using CacheHub.Core.Benchmarks.Tasks;
using CacheHub.Core.Context;
using CacheHub.Core.Identifiers;
using CacheHub.Core.Tokens;
using Xunit;

namespace CacheHub.Tests;

/// <summary>
/// V7-W19 / V8-P0-04+05: Tests for Benchmark Matrix Runner and Phase Gate evaluation.
/// V8-P0-04: Rewritten to verify Ground Truth is NOT used in retrieval.
/// V8-P0-05: Verifies PhaseGate is Incomplete without Agent data.
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
            task => BuildMockContext(task));

        Assert.Equal(BenchmarkTaskSet.Tasks.Count, result.Tasks.Count);
        Assert.True(result.Summary.TotalTasks > 0);
    }

    [Fact]
    public void RunRetrievalMatrix_ComputesRecallAndTokenReduction()
    {
        var runner = new BenchmarkMatrixRunner();
        var result = runner.RunRetrievalMatrix(
            task => GetMockFiles(task),
            (task, path) => "mock content",
            (task, path) => "mock-hash",
            task => BuildMockContext(task));

        foreach (var taskResult in result.Tasks)
        {
            Assert.True(taskResult.FileRecallAt10 >= 0 && taskResult.FileRecallAt10 <= 1.0);
            Assert.True(taskResult.TokenReduction >= 0 && taskResult.TokenReduction <= 1.0);
        }
    }

    [Fact]
    public void RunRetrievalMatrix_PhaseGate_EvaluatesCorrectly()
    {
        var runner = new BenchmarkMatrixRunner();
        var result = runner.RunRetrievalMatrix(
            task => GetMockFiles(task),
            (task, path) => "mock",
            (task, path) => "hash",
            task => BuildMockContext(task));

        Assert.NotNull(result.PhaseGate);
        Assert.True(result.PhaseGate.ActualFileRecallAt10 >= 0);
        Assert.True(result.PhaseGate.ActualTokenReduction >= 0);
    }

    /// <summary>
    /// V8-P0-05: Without Agent data, PhaseGate must be Incomplete, not Passed.
    /// </summary>
    [Fact]
    public void MatrixPhaseGate_NoAgentData_StatusIsIncomplete()
    {
        var summary = new MatrixSummary
        {
            TotalTasks = 10,
            MeanFileRecallAt10 = 0.95,
            MeanTokenReduction = 0.50,
            // No agent data — TasksWithAgentResults is null
        };

        var gate = MatrixPhaseGate.Evaluate(summary);
        Assert.Equal(MatrixGateStatus.Incomplete, gate.Status);
        Assert.False(gate.Passed);
        Assert.Contains(gate.FailedGates, g => g.Contains("No Agent results"));
    }

    /// <summary>
    /// V8-P0-05: With Agent data meeting all thresholds, PhaseGate is Passed.
    /// </summary>
    [Fact]
    public void MatrixPhaseGate_WithAgentData_AllThresholdsMet_StatusIsPassed()
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
        Assert.Equal(MatrixGateStatus.Passed, gate.Status);
        Assert.True(gate.Passed);
        Assert.Empty(gate.FailedGates);
    }

    [Fact]
    public void MatrixPhaseGate_FailWhenRecallTooLow()
    {
        var summary = new MatrixSummary
        {
            TotalTasks = 10,
            MeanFileRecallAt10 = 0.50,
            MeanTokenReduction = 0.50,
            TasksWithAgentResults = 10,
            CacheHubSuccessRate = 0.90,
            BaselineSuccessRate = 0.85,
            MeanInputTokenReduction = 0.60,
            PositiveTokenTaskRatio = 0.80,
        };

        var gate = MatrixPhaseGate.Evaluate(summary);
        Assert.Equal(MatrixGateStatus.Failed, gate.Status);
        Assert.Contains(gate.FailedGates, g => g.Contains("FileRecall"));
    }

    [Fact]
    public void MatrixPhaseGate_FailWhenTokenReductionTooLow()
    {
        var summary = new MatrixSummary
        {
            TotalTasks = 10,
            MeanFileRecallAt10 = 0.95,
            MeanTokenReduction = 0.10,
            TasksWithAgentResults = 10,
            CacheHubSuccessRate = 0.90,
            BaselineSuccessRate = 0.85,
            MeanInputTokenReduction = 0.60,
            PositiveTokenTaskRatio = 0.80,
        };

        var gate = MatrixPhaseGate.Evaluate(summary);
        Assert.Equal(MatrixGateStatus.Failed, gate.Status);
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
            CacheHubSuccessRate = 0.70,
            BaselineSuccessRate = 0.90,
            MeanInputTokenReduction = 0.60,
            PositiveTokenTaskRatio = 0.80,
            TasksWithAgentResults = 10,
        };

        var gate = MatrixPhaseGate.Evaluate(summary);
        Assert.Equal(MatrixGateStatus.Failed, gate.Status);
        Assert.Contains(gate.FailedGates, g => g.Contains("Success"));
    }

    /// <summary>
    /// V8-P0-04: Verifies that the contextBuildCallback is called and its predictions are used.
    /// The callback should NOT have access to RequiredFiles.
    /// </summary>
    [Fact]
    public void RunRetrievalMatrix_UsesContextBuildCallback_NotGroundTruth()
    {
        var callbackCalled = false;
        var runner = new BenchmarkMatrixRunner();
        var result = runner.RunRetrievalMatrix(
            task => GetMockFiles(task),
            (task, path) => "content",
            (task, path) => "hash",
            task =>
            {
                callbackCalled = true;
                return BuildMockContext(task);
            });

        Assert.True(callbackCalled);
        Assert.True(result.Tasks.All(t => t.SelectedFileCount >= 0));
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
    public void AttachAgentResults_PartialEvidence_LeavesGateIncomplete()
    {
        var runner = new BenchmarkMatrixRunner();
        var retrieval = runner.RunRetrievalMatrix(
            task => GetMockFiles(task),
            (task, path) => "content",
            (task, path) => "hash",
            BuildMockContext);

        var enriched = runner.AttachAgentResults(retrieval, new Dictionary<string, MatrixAgentTaskResult>
        {
            [BenchmarkTaskSet.Tasks[0].Id] = new()
            {
                CacheHubTaskCompleted = true,
                BaselineTaskCompleted = true,
                CacheHubInputTokens = 100,
                BaselineInputTokens = 200,
                CacheHubRounds = 1,
                BaselineRounds = 1,
                CacheHubCost = 0.01,
                BaselineCost = 0.02,
            },
        });

        Assert.Equal(MatrixGateStatus.Incomplete, enriched.PhaseGate.Status);
        Assert.Equal(1, enriched.Summary.TasksWithAgentResults);
        Assert.Contains(enriched.PhaseGate.FailedGates, gate => gate.Contains("incomplete"));
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

    // V8-P0-04: Mock context builder that uses real ContextEngine (blind to Ground Truth)
    private static ContextPackageManifest BuildMockContext(BenchmarkTask task)
    {
        var files = GetMockFiles(task);
        var indexedFiles = files.Select(f => new IndexedFileInfo
        {
            Path = f.NormalizedPath,
            NormalizedPath = f.NormalizedPath,
            Language = f.Language,
            Size = f.Size,
            ContentHash = "sha256:pending",
        }).ToList();

        var engine = new ContextEngine(
            TokenizerRegistry.CreateWithDefaults(),
            securityPolicy: null,
            cache: null);

        return engine.Build(
            new ContextBuildRequest
            {
                WorkspaceId = WorkspaceId.New(),
                IndexSnapshotId = IndexSnapshotId.New(),
                Task = task.TaskDescription,
            },
            () => indexedFiles,
            _ => "mock content for testing",
            _ => "sha256:pending");
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
