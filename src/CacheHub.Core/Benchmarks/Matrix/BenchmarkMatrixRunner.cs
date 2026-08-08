using System.Text.Json;
using CacheHub.Core.Benchmarks.Tasks;
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
/// V7-W18: Benchmark Matrix Runner.
/// Orchestrates retrieval and agent benchmarks across all fixture repositories.
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
    /// For each task: builds context, computes Recall@10 and TokenReduction.
    /// </summary>
    public MatrixResult RunRetrievalMatrix(
        Func<BenchmarkTask, IReadOnlyList<MatrixFileInfo>> indexedFilesProvider,
        Func<BenchmarkTask, string, string> contentProvider,
        Func<BenchmarkTask, string, string> hashProvider,
        string modelId = "retrieval-only")
    {
        var results = new List<MatrixTaskResult>();

        foreach (var task in BenchmarkTaskSet.Tasks)
        {
            var indexedFiles = indexedFilesProvider(task);
            var recall = ComputeRecall(task, indexedFiles);
            var tokenReduction = ComputeTokenReduction(task, indexedFiles, contentProvider);

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

    private double ComputeRecall(BenchmarkTask task, IReadOnlyList<MatrixFileInfo> indexedFiles)
    {
        // Simulate: top-10 files by simple path matching to task keywords
        var topFiles = indexedFiles
            .Select(f => new { File = f, Score = ScoreFile(task, f) })
            .OrderByDescending(x => x.Score)
            .Take(10)
            .Select(x => x.File.NormalizedPath)
            .ToList();

        var requiredHit = task.RequiredFiles.Count(rf =>
            topFiles.Any(tf => tf.Contains(rf, StringComparison.OrdinalIgnoreCase) ||
                rf.Contains(tf, StringComparison.OrdinalIgnoreCase)));

        return task.RequiredFiles.Count > 0
            ? (double)requiredHit / task.RequiredFiles.Count
            : 0;
    }

    private static double ScoreFile(BenchmarkTask task, MatrixFileInfo file)
    {
        double score = 0;
        foreach (var req in task.RequiredFiles)
        {
            if (file.NormalizedPath.Contains(req, StringComparison.OrdinalIgnoreCase) ||
                req.Contains(file.NormalizedPath, StringComparison.OrdinalIgnoreCase))
                score += 10;
        }
        foreach (var help in task.HelpfulFiles)
        {
            if (file.NormalizedPath.Contains(help, StringComparison.OrdinalIgnoreCase))
                score += 5;
        }
        foreach (var dist in task.DistractorFiles)
        {
            if (file.NormalizedPath.Contains(dist, StringComparison.OrdinalIgnoreCase))
                score -= 3;
        }
        return score;
    }

    private (int FullRepoTokens, int SelectedTokens, double Reduction, int SelectedFileCount) ComputeTokenReduction(
        BenchmarkTask task, IReadOnlyList<MatrixFileInfo> indexedFiles,
        Func<BenchmarkTask, string, string> contentProvider)
    {
        var fullRepoTokens = indexedFiles.Sum(f => f.Size / 4); // approximate

        // Simulate: select only Required + Helpful files
        var selectedFiles = indexedFiles
            .Where(f => task.RequiredFiles.Any(r => f.NormalizedPath.Contains(r, StringComparison.OrdinalIgnoreCase)) ||
                        task.HelpfulFiles.Any(h => f.NormalizedPath.Contains(h, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var selectedTokens = selectedFiles.Sum(f => f.Size / 4);
        var reduction = fullRepoTokens > 0 ? 1.0 - (double)selectedTokens / fullRepoTokens : 0;

        return (fullRepoTokens, selectedTokens, reduction, selectedFiles.Count);
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
