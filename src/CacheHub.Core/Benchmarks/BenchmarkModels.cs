using System.Text.Json.Serialization;

namespace CacheHub.Core.Benchmarks;

/// <summary>
/// A benchmark task definition.
/// </summary>
public sealed record BenchmarkTask
{
    public required string Id { get; init; }
    public required string RepositoryId { get; init; }
    public required string Language { get; init; }
    public required string TaskDescription { get; init; }
    public required string CommitHash { get; init; }
    public IReadOnlyList<string> RequiredFiles { get; init; } = [];
    public IReadOnlyList<string> HelpfulFiles { get; init; } = [];
    public IReadOnlyList<string> DistractorFiles { get; init; } = [];

    /// <summary>V7-W17: Local fixture path for the repository (relative to project root or absolute).</summary>
    public string? RepositoryPath { get; init; }

    /// <summary>V7-W17: Test command to run for --real-test verification (e.g., "npm test", "pytest", "go test").</summary>
    public string? TestCommand { get; init; }

    /// <summary>V7-W17: Arguments for the test command.</summary>
    public string? TestCommandArgs { get; init; }
}

/// <summary>
/// Ground truth annotation for a benchmark task.
/// Required = must be present for task success.
/// Helpful = useful but not necessary.
/// Distractor = contains keywords but irrelevant.
/// </summary>
public sealed record GroundTruth
{
    public required string TaskId { get; init; }
    public required IReadOnlyList<string> RequiredFiles { get; init; }
    public required IReadOnlyList<string> HelpfulFiles { get; init; }
    public required IReadOnlyList<string> DistractorFiles { get; init; }
}

/// <summary>
/// Configuration for a benchmark run.
/// </summary>
public sealed record BenchmarkConfig
{
    public required string ModelId { get; init; }
    public required string AgentId { get; init; }
    public required string SystemPrompt { get; init; }
    public required int RunsPerTask { get; init; } = 3;
    public required bool ResetBetweenRuns { get; init; } = true;
    public required bool ShareBuildCache { get; init; }
    public string? ToolPermissions { get; init; }
    public string? TestEnvironment { get; init; }
}

/// <summary>
/// Metrics for a single task run.
/// </summary>
public sealed record TaskMetrics
{
    public required string TaskId { get; init; }
    public required int RunNumber { get; init; }
    public required bool TaskCompleted { get; init; }
    public required int TotalInputTokens { get; init; }
    public required int TotalOutputTokens { get; init; }
    public required int Rounds { get; init; }
    public required double FileRecallAt10 { get; init; }
    public required double SymbolRecallAt10 { get; init; }
    public required double ContextPrecision { get; init; }
    public required bool MissingContext { get; init; }
    public required bool StaleContext { get; init; }
    public required IReadOnlyList<string> SelectedFiles { get; init; }
    public required IReadOnlyList<string> ActuallyReadFiles { get; init; }
}

/// <summary>
/// Aggregated metrics for a task across multiple runs.
/// </summary>
public sealed record AggregatedMetrics
{
    public required string TaskId { get; init; }
    public double MeanFileRecall { get; init; }
    public double StdDevFileRecall { get; init; }
    public double WorstFileRecall { get; init; }
    public double MeanPrecision { get; init; }
    public double SuccessRate { get; init; }
    public double MeanRounds { get; init; }
    public double MeanInputTokens { get; init; }
    public double MissingContextRate { get; init; }
    public double StaleContextRate { get; init; }
    public int RunCount { get; init; }
}

/// <summary>
/// Failure attribution category.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FailureCategory
{
    Retrieval,
    Ranking,
    Budget,
    AgentNonCompliance,
    ModelRandom,
    Environment,
    None,
}

/// <summary>
/// Attributed failure for a task.
/// </summary>
public sealed record FailureAttribution
{
    public required string TaskId { get; init; }
    public required FailureCategory Category { get; init; }
    public required string Description { get; init; }
}

/// <summary>
/// Phase gate thresholds.
/// </summary>
public sealed record PhaseGateThresholds
{
    public double MinFileRecallAt10 { get; init; } = 0.90;
    public double MaxMissingContextFailureRate { get; init; } = 0.10;
    public double MinTestPassRatio { get; init; } = 0.95;
    public double MinMeanTokenReduction { get; init; } = 0.20;
    public double MinPositiveTokenTaskRatio { get; init; } = 0.60;
    public double MaxStaleContextErrorRate { get; init; } = 0.01;
}

/// <summary>
/// Phase gate evaluation result.
/// </summary>
public sealed record PhaseGateResult
{
    public required bool Passed { get; init; }
    public required double ActualFileRecallAt10 { get; init; }
    public required double ActualMissingContextRate { get; init; }
    public required double ActualTestPassRatio { get; init; }
    public required double ActualMeanTokenReduction { get; init; }
    public required double ActualPositiveTokenTaskRatio { get; init; }
    public required double ActualStaleContextErrorRate { get; init; }
    public required IReadOnlyList<string> FailedGates { get; init; }
}
