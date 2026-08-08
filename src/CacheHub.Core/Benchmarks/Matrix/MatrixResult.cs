using CacheHub.Core.Benchmarks;

namespace CacheHub.Core.Benchmarks.Matrix;

/// <summary>
/// V7-W18: Result of a single task in the Benchmark Matrix.
/// </summary>
public sealed record MatrixTaskResult
{
    public required string TaskId { get; init; }
    public required string RepositoryId { get; init; }
    public required string Language { get; init; }
    public required string TaskDescription { get; init; }

    // Retrieval metrics
    public double FileRecallAt10 { get; init; }
    public double TokenReduction { get; init; }
    public int SelectedFileCount { get; init; }
    public int FullRepoTokenCount { get; init; }
    public int SelectedTokenCount { get; init; }

    // Agent metrics (if --agent flag)
    public bool? CacheHubTaskCompleted { get; init; }
    public bool? BaselineTaskCompleted { get; init; }
    public int? CacheHubInputTokens { get; init; }
    public int? BaselineInputTokens { get; init; }
    public int? CacheHubRounds { get; init; }
    public int? BaselineRounds { get; init; }
    public double? CacheHubCost { get; init; }
    public double? BaselineCost { get; init; }

    // Derived
    public bool? SuccessRateMaintained => CacheHubTaskCompleted.HasValue && BaselineTaskCompleted.HasValue
        ? CacheHubTaskCompleted.Value || !BaselineTaskCompleted.Value
        : null;
    public double? InputTokenReduction => CacheHubInputTokens.HasValue && BaselineInputTokens.HasValue && BaselineInputTokens > 0
        ? 1.0 - (double)CacheHubInputTokens.Value / BaselineInputTokens.Value
        : null;
}

/// <summary>
/// V7-W18: Aggregated result of the entire Benchmark Matrix.
/// </summary>
public sealed record MatrixResult
{
    public required DateTimeOffset GeneratedAt { get; init; }
    public required string ModelId { get; init; }
    public required IReadOnlyList<MatrixTaskResult> Tasks { get; init; }
    public required MatrixSummary Summary { get; init; }
    public required MatrixPhaseGate PhaseGate { get; init; }
}

/// <summary>
/// V7-W18: Summary statistics across all tasks.
/// </summary>
public sealed record MatrixSummary
{
    public required int TotalTasks { get; init; }
    public required double MeanFileRecallAt10 { get; init; }
    public required double MeanTokenReduction { get; init; }

    // Agent metrics (if --agent flag)
    public int? TasksWithAgentResults { get; init; }
    public double? CacheHubSuccessRate { get; init; }
    public double? BaselineSuccessRate { get; init; }
    public double? MeanInputTokenReduction { get; init; }
    public double? PositiveTokenTaskRatio { get; init; }
}

/// <summary>
/// V7-W18 / V8-P0-05: Phase gate evaluation for the Benchmark Matrix.
/// V8-P0-05: When no Agent results exist, gate status = Incomplete (not Passed).
/// </summary>
public sealed record MatrixPhaseGate
{
    public required bool Passed { get; init; }

    /// <summary>V8-P0-05: Gate status — Passed, Failed, or Incomplete (no Agent data).</summary>
    public required MatrixGateStatus Status { get; init; }

    public required double ActualFileRecallAt10 { get; init; }
    public required double ActualTokenReduction { get; init; }
    public required double? ActualCacheHubSuccessRate { get; init; }
    public required double? ActualBaselineSuccessRate { get; init; }
    public required double? ActualInputTokenReduction { get; init; }
    public required double? ActualPositiveTokenTaskRatio { get; init; }
    public required IReadOnlyList<string> FailedGates { get; init; }

    public static MatrixPhaseGate Evaluate(MatrixSummary summary)
    {
        var gates = new List<string>();

        // V8-P0-05: If no Agent results, gate is Incomplete — not Passed.
        // Retrieval-only metrics (Recall@10, TokenReduction) are still evaluated for information,
        // but the gate cannot Pass without Agent data.
        var hasAgentResults = summary.TasksWithAgentResults is > 0;

        if (summary.MeanFileRecallAt10 < 0.90)
            gates.Add($"FileRecall@10 {summary.MeanFileRecallAt10:F2} < 0.90");

        if (summary.MeanTokenReduction < 0.20)
            gates.Add($"TokenReduction {summary.MeanTokenReduction:F2} < 0.20");

        if (hasAgentResults && summary.CacheHubSuccessRate.HasValue && summary.BaselineSuccessRate.HasValue)
        {
            if (summary.CacheHubSuccessRate < summary.BaselineSuccessRate * 0.95)
                gates.Add($"CacheHub Success {summary.CacheHubSuccessRate:F2} < Baseline×95% {summary.BaselineSuccessRate * 0.95:F2}");

            if (summary.MeanInputTokenReduction < 0.20)
                gates.Add($"InputTokenReduction {summary.MeanInputTokenReduction:F2} < 0.20");

            if (summary.PositiveTokenTaskRatio < 0.60)
                gates.Add($"PositiveTokenTaskRatio {summary.PositiveTokenTaskRatio:F2} < 0.60");
        }
        else if (!hasAgentResults)
        {
            // V8-P0-05: No agent results = gate is incomplete
            gates.Add("No Agent results — Agent Product Gate not evaluated");
        }

        // V8-P0-05: Determine status
        var status = !hasAgentResults
            ? MatrixGateStatus.Incomplete
            : gates.Count == 0
                ? MatrixGateStatus.Passed
                : MatrixGateStatus.Failed;

        return new MatrixPhaseGate
        {
            Passed = status == MatrixGateStatus.Passed,
            Status = status,
            ActualFileRecallAt10 = summary.MeanFileRecallAt10,
            ActualTokenReduction = summary.MeanTokenReduction,
            ActualCacheHubSuccessRate = summary.CacheHubSuccessRate,
            ActualBaselineSuccessRate = summary.BaselineSuccessRate,
            ActualInputTokenReduction = summary.MeanInputTokenReduction,
            ActualPositiveTokenTaskRatio = summary.PositiveTokenTaskRatio,
            FailedGates = gates,
        };
    }
}
