using System.Text.Json;
using CacheHub.Core.Benchmarks.Tasks;
using CacheHub.Core.Context;
using CacheHub.Core.Tokens;

namespace CacheHub.Core.Benchmarks.Matrix;

/// <summary>
/// V7-W18: Simple file info for matrix retrieval (avoids cross-project dependency).
/// </summary>
public sealed record MatrixFileInfo
{
    public required string Path { get; init; }
    public required string NormalizedPath { get; init; }
    public required string Language { get; init; }
    public required int Size { get; init; }
}

/// <summary>
/// V8-P0-04: Phase gate status.
/// </summary>
public enum MatrixGateStatus
{
    Passed,
    Failed,
    Incomplete
}

/// <summary>
/// V7-W18 / V8-P0-04: Benchmark Matrix Runner.
/// V8-P0-04: Rewritten to eliminate Ground Truth leakage.
/// Retrieval now uses a real ContextEngine callback; RequiredFiles/HelpfulFiles/DistractorFiles
/// are ONLY used in the evaluation phase, never in the retrieval/selection phase.
/// </summary>
public sealed class BenchmarkMatrixRunner
{
    private readonly ITokenizer _tokenizer;

    public BenchmarkMatrixRunner(ITokenizer? tokenizer = null)
    {
        _tokenizer = tokenizer ?? new CodeTokenizer();
    }

    /// <summary>
    /// Runs the retrieval-only benchmark matrix (no model calls needed).
    /// V8-P0-04: Uses contextBuildCallback to get real ContextEngine predictions.
    /// Ground Truth (RequiredFiles/HelpfulFiles/DistractorFiles) is ONLY used in evaluation, never in retrieval.
    /// </summary>
    /// <param name="indexedFilesProvider">Provides indexed files for a task.</param>
    /// <param name="contentProvider">Provides file content for a task and path.</param>
    /// <param name="hashProvider">Provides file hash for a task and path.</param>
    /// <param name="contextBuildCallback">V8-P0-04: Calls real ContextEngine to build context for a task.
    /// Returns the manifest with SelectedFiles. This callback must NOT have access to Ground Truth.</param>
    /// <param name="modelId">Model identifier for the report.</param>
    /// <param name="tasks">Optional filtered task list. If null, uses all tasks.</param>
    public MatrixResult RunRetrievalMatrix(
        Func<BenchmarkTask, IReadOnlyList<MatrixFileInfo>> indexedFilesProvider,
        Func<BenchmarkTask, string, string> contentProvider,
        Func<BenchmarkTask, string, string> hashProvider,
        Func<BenchmarkTask, ContextPackageManifest> contextBuildCallback,
        string modelId = "retrieval-only",
        IReadOnlyList<BenchmarkTask>? tasks = null)
    {
        var taskList = tasks ?? BenchmarkTaskSet.Tasks;
        var results = new List<MatrixTaskResult>();

        foreach (var task in taskList)
        {
            var indexedFiles = indexedFilesProvider(task);

            // V8-P0-04: Call real ContextEngine to get predictions (blind to Ground Truth)
            var manifest = contextBuildCallback(task);
            var selectedPaths = manifest.SelectedFiles.Select(f => f.Path).ToList();

            // V8-P0-04: Evaluation phase — NOW we can use Ground Truth to score predictions
            var recall = EvaluateRecall(task, selectedPaths);
            var tokenReduction = EvaluateTokenReduction(task, indexedFiles, selectedPaths, contentProvider);

            results.Add(new MatrixTaskResult
            {
                TaskId = task.Id,
                RepositoryId = task.RepositoryId,
                Language = task.Language,
                TaskDescription = task.TaskDescription,
                FileRecallAt10 = recall,
                TokenReduction = tokenReduction.Reduction,
                SelectedFileCount = tokenReduction.SelectedFileCount,
                FullRepoTokenCount = tokenReduction.FullRepoTokens,
                SelectedTokenCount = tokenReduction.SelectedTokens,
            });
        }

        return BuildResult(results, modelId);
    }

    /// <summary>
    /// Merges verified CacheHub-vs-baseline agent runs into a retrieval Matrix
    /// result and re-evaluates the release gate. Missing task evidence remains
    /// null so a partially executed Matrix cannot appear complete.
    /// </summary>
    public MatrixResult AttachAgentResults(
        MatrixResult retrievalResult,
        IReadOnlyDictionary<string, MatrixAgentTaskResult> agentResults)
    {
        var enriched = retrievalResult.Tasks.Select(task =>
        {
            if (!agentResults.TryGetValue(task.TaskId, out var agent))
                return task;

            return task with
            {
                CacheHubTaskCompleted = agent.CacheHubTaskCompleted,
                BaselineTaskCompleted = agent.BaselineTaskCompleted,
                CacheHubInputTokens = agent.CacheHubInputTokens,
                BaselineInputTokens = agent.BaselineInputTokens,
                CacheHubRounds = agent.CacheHubRounds,
                BaselineRounds = agent.BaselineRounds,
                CacheHubCost = agent.CacheHubCost,
                BaselineCost = agent.BaselineCost,
            };
        }).ToList();

        return BuildResult(enriched, retrievalResult.ModelId);
    }

    /// <summary>
    /// V8-P0-04: Evaluates Recall@10 using Ground Truth against ContextEngine predictions.
    /// This is the ONLY place where RequiredFiles is used — after retrieval is complete.
    /// </summary>
    private static double EvaluateRecall(BenchmarkTask task, IReadOnlyList<string> selectedPaths)
    {
        if (task.RequiredFiles.Count == 0)
            return 0;

        // Check how many RequiredFiles appear in the top-10 selected paths
        var top10 = selectedPaths.Take(10).ToList();
        var hitCount = task.RequiredFiles.Count(rf =>
            top10.Any(sp => PathEquals(sp, rf)) ||
            top10.Any(sp => sp.Contains(rf, StringComparison.OrdinalIgnoreCase) ||
                rf.Contains(sp, StringComparison.OrdinalIgnoreCase)));

        return (double)hitCount / task.RequiredFiles.Count;
    }

    /// <summary>
    /// V8-P0-04: Evaluates token reduction based on ContextEngine-selected files (not Ground Truth).
    /// </summary>
    private (int FullRepoTokens, int SelectedTokens, double Reduction, int SelectedFileCount) EvaluateTokenReduction(
        BenchmarkTask task, IReadOnlyList<MatrixFileInfo> indexedFiles,
        List<string> selectedPaths,
        Func<BenchmarkTask, string, string> contentProvider)
    {
        var fullRepoTokens = indexedFiles.Sum(f => _tokenizer.CountTokens(contentProvider(task, f.NormalizedPath)));

        // V8-P0-04: Use ContextEngine-selected paths, NOT RequiredFiles
        var selectedTokens = selectedPaths.Sum(p =>
        {
            var file = indexedFiles.FirstOrDefault(f => PathEquals(f.NormalizedPath, p));
            return file is not null ? _tokenizer.CountTokens(contentProvider(task, file.NormalizedPath)) : 0;
        });

        var reduction = fullRepoTokens > 0 ? 1.0 - (double)selectedTokens / fullRepoTokens : 0;

        return (fullRepoTokens, selectedTokens, reduction, selectedPaths.Count);
    }

    /// <summary>
    /// Path equality check that handles separator differences.
    /// </summary>
    private static bool PathEquals(string a, string b)
    {
        var na = a.Replace('\\', '/').TrimEnd('/');
        var nb = b.Replace('\\', '/').TrimEnd('/');
        return string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
    }

    private MatrixResult BuildResult(List<MatrixTaskResult> results, string modelId)
    {
        var summary = new MatrixSummary
        {
            TotalTasks = results.Count,
            MeanFileRecallAt10 = results.Count > 0 ? results.Average(r => r.FileRecallAt10) : 0,
            MeanTokenReduction = results.Count > 0 ? results.Average(r => r.TokenReduction) : 0,
        };

        // Agent metrics (if available)
        var agentResults = results.Where(r => r.CacheHubTaskCompleted.HasValue).ToList();
        if (agentResults.Count > 0)
        {
            summary = summary with
            {
                TasksWithAgentResults = agentResults.Count,
                CacheHubSuccessRate = agentResults.Count(r => r.CacheHubTaskCompleted == true) / (double)agentResults.Count,
                BaselineSuccessRate = agentResults.Count(r => r.BaselineTaskCompleted == true) / (double)agentResults.Count,
                MeanInputTokenReduction = agentResults
                    .Where(r => r.InputTokenReduction.HasValue)
                    .Average(r => r.InputTokenReduction!.Value),
                PositiveTokenTaskRatio = agentResults.Count(r => r.InputTokenReduction > 0) / (double)agentResults.Count,
            };
        }

        return new MatrixResult
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            ModelId = modelId,
            Tasks = results,
            Summary = summary,
            PhaseGate = MatrixPhaseGate.Evaluate(summary),
        };
    }
}
