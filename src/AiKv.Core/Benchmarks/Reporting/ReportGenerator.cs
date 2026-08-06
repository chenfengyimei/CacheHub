using System.Text.Json;
using AiKv.Core.Benchmarks;
using AiKv.Core.Benchmarks.Engine;

namespace AiKv.Core.Benchmarks.Reporting;

/// <summary>
/// Generates a public benchmark report.
/// </summary>
public static class ReportGenerator
{
    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };
    /// <summary>
    /// Generates a JSON report from aggregated metrics and phase gate result.
    /// </summary>
    public static string GenerateJson(
        BenchmarkConfig config,
        IReadOnlyList<AggregatedMetrics> taskMetrics,
        IReadOnlyList<FailureAttribution> failures,
        PhaseGateResult phaseGate)
    {
        var report = new
        {
            generatedAt = DateTimeOffset.UtcNow.ToString("O"),
            config = new
            {
                modelId = config.ModelId,
                agentId = config.AgentId,
                runsPerTask = config.RunsPerTask,
                resetBetweenRuns = config.ResetBetweenRuns,
            },
            summary = new
            {
                totalTasks = taskMetrics.Count,
                phaseGatePassed = phaseGate.Passed,
                meanFileRecallAt10 = taskMetrics.Any() ? taskMetrics.Average(t => t.MeanFileRecall) : 0,
                meanSuccessRate = taskMetrics.Any() ? taskMetrics.Average(t => t.SuccessRate) : 0,
                meanInputTokens = taskMetrics.Any() ? taskMetrics.Average(t => t.MeanInputTokens) : 0,
                missingContextRate = taskMetrics.Any() ? taskMetrics.Average(t => t.MissingContextRate) : 0,
            },
            phaseGate = new
            {
                passed = phaseGate.Passed,
                fileRecallAt10 = phaseGate.ActualFileRecallAt10,
                missingContextRate = phaseGate.ActualMissingContextRate,
                testPassRatio = phaseGate.ActualTestPassRatio,
                tokenReduction = phaseGate.ActualMeanTokenReduction,
                positiveTokenTaskRatio = phaseGate.ActualPositiveTokenTaskRatio,
                staleContextErrorRate = phaseGate.ActualStaleContextErrorRate,
                failedGates = phaseGate.FailedGates,
            },
            tasks = taskMetrics.Select(t => new
            {
                taskId = t.TaskId,
                meanFileRecall = Math.Round(t.MeanFileRecall, 4),
                stdDevFileRecall = Math.Round(t.StdDevFileRecall, 4),
                worstFileRecall = Math.Round(t.WorstFileRecall, 4),
                meanPrecision = Math.Round(t.MeanPrecision, 4),
                successRate = Math.Round(t.SuccessRate, 4),
                meanRounds = Math.Round(t.MeanRounds, 2),
                meanInputTokens = Math.Round(t.MeanInputTokens, 0),
                missingContextRate = Math.Round(t.MissingContextRate, 4),
                staleContextRate = Math.Round(t.StaleContextRate, 4),
                runCount = t.RunCount,
            }),
            failures = failures.Where(f => f.Category != FailureCategory.None).Select(f => new
            {
                taskId = f.TaskId,
                category = f.Category.ToString().ToLowerInvariant(),
                description = f.Description,
            }),
            limitations = new[]
            {
                "Tokenizer is rough estimate (chars/4)",
                "Model randomness may affect results",
                "Results are specific to the configured model and agent",
            },
        };

        return JsonSerializer.Serialize(report, _jsonOpts);
    }
}
