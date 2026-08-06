using CacheHub.Core.Benchmarks;

namespace CacheHub.Core.Benchmarks.Engine;

/// <summary>
/// Computes benchmark metrics from task results.
/// </summary>
public static class MetricsCalculator
{
    /// <summary>
    /// Computes metrics for a single task run.
    /// </summary>
    public static TaskMetrics ComputeTaskMetrics(
        string taskId,
        int runNumber,
        bool taskCompleted,
        int inputTokens,
        int outputTokens,
        int rounds,
        IReadOnlyList<string> selectedFiles,
        IReadOnlyList<string> actuallyRead,
        GroundTruth groundTruth)
    {
        var topK = selectedFiles.Take(10).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var readSet = actuallyRead.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var fileRecall = ComputeRecall(topK, groundTruth.RequiredFiles);
        var symbolRecall = ComputeRecall(readSet, groundTruth.RequiredFiles);
        var precision = ComputePrecision(topK, groundTruth);

        var missingContext = !groundTruth.RequiredFiles.All(r =>
            readSet.Contains(r) || selectedFiles.Any(s => s.Equals(r, StringComparison.OrdinalIgnoreCase)));

        return new TaskMetrics
        {
            TaskId = taskId,
            RunNumber = runNumber,
            TaskCompleted = taskCompleted,
            TotalInputTokens = inputTokens,
            TotalOutputTokens = outputTokens,
            Rounds = rounds,
            FileRecallAt10 = fileRecall,
            SymbolRecallAt10 = symbolRecall,
            ContextPrecision = precision,
            MissingContext = missingContext,
            StaleContext = false, // Requires version comparison (later)
            SelectedFiles = selectedFiles,
            ActuallyReadFiles = actuallyRead,
        };
    }

    /// <summary>
    /// Aggregates metrics across multiple runs of the same task.
    /// </summary>
    public static AggregatedMetrics Aggregate(string taskId, IReadOnlyList<TaskMetrics> runs)
    {
        if (runs.Count == 0)
            return new AggregatedMetrics
            {
                TaskId = taskId,
                RunCount = 0,
                MeanFileRecall = 0,
                StdDevFileRecall = 0,
                WorstFileRecall = 0,
                MeanPrecision = 0,
                SuccessRate = 0,
                MeanRounds = 0,
                MeanInputTokens = 0,
                MissingContextRate = 0,
                StaleContextRate = 0,
            };

        var recalls = runs.Select(r => r.FileRecallAt10).ToArray();
        var precisions = runs.Select(r => r.ContextPrecision).ToArray();
        var tokens = runs.Select(r => (double)r.TotalInputTokens).ToArray();
        var rounds = runs.Select(r => (double)r.Rounds).ToArray();

        return new AggregatedMetrics
        {
            TaskId = taskId,
            MeanFileRecall = Mean(recalls),
            StdDevFileRecall = StdDev(recalls),
            WorstFileRecall = recalls.Min(),
            MeanPrecision = Mean(precisions),
            SuccessRate = (double)runs.Count(r => r.TaskCompleted) / runs.Count,
            MeanRounds = Mean(rounds),
            MeanInputTokens = Mean(tokens),
            MissingContextRate = (double)runs.Count(r => r.MissingContext) / runs.Count,
            StaleContextRate = (double)runs.Count(r => r.StaleContext) / runs.Count,
            RunCount = runs.Count,
        };
    }

    /// <summary>
    /// Computes Recall@K: fraction of required files in top-K.
    /// </summary>
    public static double ComputeRecall(IEnumerable<string> selected, IEnumerable<string> required)
    {
        var requiredList = required.ToList();
        if (requiredList.Count == 0) return 1.0;
        var hits = requiredList.Count(r => selected.Any(s => s.Equals(r, StringComparison.OrdinalIgnoreCase)));
        return (double)hits / requiredList.Count;
    }

    /// <summary>
    /// Computes Precision: fraction of selected files that are required or helpful (not distractor).
    /// </summary>
    public static double ComputePrecision(IEnumerable<string> selected, GroundTruth groundTruth)
    {
        var selectedList = selected.ToList();
        if (selectedList.Count == 0) return 0.0;
        var relevant = selectedList.Count(s =>
            groundTruth.RequiredFiles.Any(r => r.Equals(s, StringComparison.OrdinalIgnoreCase)) ||
            groundTruth.HelpfulFiles.Any(h => h.Equals(s, StringComparison.OrdinalIgnoreCase)));
        return (double)relevant / selectedList.Count;
    }

    /// <summary>
    /// Evaluates phase gate against thresholds.
    /// </summary>
    public static PhaseGateResult EvaluatePhaseGate(
        IReadOnlyList<AggregatedMetrics> allTasks,
        IReadOnlyList<AggregatedMetrics> baselineTasks,
        PhaseGateThresholds thresholds)
    {
        var actualRecall = allTasks.Any() ? allTasks.Average(t => t.MeanFileRecall) : 0;
        var actualMissingRate = allTasks.Any() ? allTasks.Average(t => t.MissingContextRate) : 1;
        var actualSuccessRate = allTasks.Any() ? allTasks.Average(t => t.SuccessRate) : 0;
        var actualStaleRate = allTasks.Any() ? allTasks.Average(t => t.StaleContextRate) : 1;

        var baselineTokens = baselineTasks.Any() ? baselineTasks.Average(t => t.MeanInputTokens) : 0;
        var cachehubTokens = allTasks.Any() ? allTasks.Average(t => t.MeanInputTokens) : 0;
        var tokenReduction = baselineTokens > 0 ? (baselineTokens - cachehubTokens) / baselineTokens : 0;

        var positiveTokenTasks = allTasks.Count(t =>
            baselineTasks.FirstOrDefault(b => b.TaskId == t.TaskId)?.MeanInputTokens is var baselineToken
            && baselineToken > 0
            && t.MeanInputTokens < baselineToken);
        var positiveRatio = allTasks.Count > 0 ? (double)positiveTokenTasks / allTasks.Count : 0;

        var failedGates = new List<string>();
        if (actualRecall < thresholds.MinFileRecallAt10)
            failedGates.Add($"File Recall@10: {actualRecall:F2} < {thresholds.MinFileRecallAt10}");
        if (actualMissingRate > thresholds.MaxMissingContextFailureRate)
            failedGates.Add($"Missing Context Rate: {actualMissingRate:F2} > {thresholds.MaxMissingContextFailureRate}");
        if (actualSuccessRate < thresholds.MinTestPassRatio)
            failedGates.Add($"Success Rate: {actualSuccessRate:F2} < {thresholds.MinTestPassRatio}");
        if (tokenReduction < thresholds.MinMeanTokenReduction)
            failedGates.Add($"Token Reduction: {tokenReduction:F2} < {thresholds.MinMeanTokenReduction}");
        if (positiveRatio < thresholds.MinPositiveTokenTaskRatio)
            failedGates.Add($"Positive Token Task Ratio: {positiveRatio:F2} < {thresholds.MinPositiveTokenTaskRatio}");
        if (actualStaleRate > thresholds.MaxStaleContextErrorRate)
            failedGates.Add($"Stale Context Rate: {actualStaleRate:F2} > {thresholds.MaxStaleContextErrorRate}");

        return new PhaseGateResult
        {
            Passed = failedGates.Count == 0,
            ActualFileRecallAt10 = actualRecall,
            ActualMissingContextRate = actualMissingRate,
            ActualTestPassRatio = actualSuccessRate,
            ActualMeanTokenReduction = tokenReduction,
            ActualPositiveTokenTaskRatio = positiveRatio,
            ActualStaleContextErrorRate = actualStaleRate,
            FailedGates = failedGates,
        };
    }

    /// <summary>
    /// Attributes a failure to a category based on metrics.
    /// </summary>
    public static FailureAttribution AttributeFailure(TaskMetrics metrics, GroundTruth groundTruth)
    {
        if (metrics.TaskCompleted) return new FailureAttribution
        {
            TaskId = metrics.TaskId,
            Category = FailureCategory.None,
            Description = "Task completed successfully",
        };

        if (metrics.FileRecallAt10 < 0.3)
            return new FailureAttribution
            {
                TaskId = metrics.TaskId,
                Category = FailureCategory.Retrieval,
                Description = $"Low recall ({metrics.FileRecallAt10:F2}): required files not found",
            };

        if (metrics.ContextPrecision < 0.3)
            return new FailureAttribution
            {
                TaskId = metrics.TaskId,
                Category = FailureCategory.Ranking,
                Description = $"Low precision ({metrics.ContextPrecision:F2}): too many irrelevant files",
            };

        if (metrics.MissingContext)
            return new FailureAttribution
            {
                TaskId = metrics.TaskId,
                Category = FailureCategory.Budget,
                Description = "Required files were excluded due to budget",
            };

        return new FailureAttribution
        {
            TaskId = metrics.TaskId,
            Category = FailureCategory.ModelRandom,
            Description = "Task failed despite good context; likely model randomness",
        };
    }

    private static double Mean(double[] values) =>
        values.Length == 0 ? 0 : values.Average();

    private static double StdDev(double[] values)
    {
        if (values.Length < 2) return 0;
        var mean = values.Average();
        var sumSq = values.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sumSq / values.Length);
    }
}
