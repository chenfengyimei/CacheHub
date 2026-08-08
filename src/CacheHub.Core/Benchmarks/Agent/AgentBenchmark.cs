using CacheHub.Core.Benchmarks;
using CacheHub.Core.Tokens;

namespace CacheHub.Core.Benchmarks.Agent;

/// <summary>
/// A model executor: given a full prompt, returns the model's response.
/// In production, this wraps Gateway/Provider calls. In tests, a mock is used.
/// </summary>
public interface IAgentModelExecutor
{
    string ModelId { get; }
    Task<AgentModelResponse> GenerateAsync(string systemPrompt, string userContent, CancellationToken ct = default);
}

/// <summary>
/// Response from a model execution.
/// </summary>
public sealed record AgentModelResponse
{
    public required string Content { get; init; }
    public required int PromptTokens { get; init; }
    public required int CompletionTokens { get; init; }
    public double? Cost { get; init; }
}

/// <summary>
/// Context package for agent benchmark: file snippets selected by ContextEngine.
/// </summary>
public sealed record AgentContextPackage
{
    public required string TaskDescription { get; init; }
    public required IReadOnlyList<string> SelectedFilePaths { get; init; }
    public required IReadOnlyList<string> FileSnippets { get; init; }
    public required int EstimatedTokens { get; init; }
}

/// <summary>
/// Result of running tests on a patch.
/// </summary>
public sealed record AgentTestResult
{
    public required bool Success { get; init; }
    public required int Passed { get; init; }
    public required int Total { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Result of a single agent benchmark run (task -> model -> patch -> test -> cost).
/// </summary>
public sealed record AgentRunResult
{
    public required string TaskId { get; init; }
    public required int RunNumber { get; init; }
    public required bool TaskCompleted { get; init; }
    public required int Rounds { get; init; }
    public required int PromptTokens { get; init; }          // V6: Provider actual usage
    public required int CompletionTokens { get; init; }      // V6: Provider actual usage
    public required int LocalEstimatedContextTokens { get; init; }  // V6: local tokenizer estimate of assembled prompt (for comparison)
    public required double TotalCost { get; init; }
    public required int TestsPassed { get; init; }
    public required int TestsTotal { get; init; }
    public required int ExtraFilesRead { get; init; }
    public required IReadOnlyList<string> PatchApplied { get; init; }
    public required string? ErrorMessage { get; init; }
    public required TimeSpan Duration { get; init; }
}

/// <summary>
/// Result of a full agent benchmark across a task set.
/// </summary>
public sealed record AgentBenchmarkResult
{
    public required string ModelId { get; init; }
    public required IReadOnlyList<AgentRunResult> Runs { get; init; }
    public double SuccessRate => Runs.Count == 0 ? 0 : (double)Runs.Count(r => r.TaskCompleted) / Runs.Count;
    public long TotalPromptTokens => Runs.Sum(r => (long)r.PromptTokens); // V6: Provider actual usage
    public long TotalCompletionTokens => Runs.Sum(r => (long)r.CompletionTokens); // V6: Provider actual usage
    public long TotalLocalEstimatedContextTokens => Runs.Sum(r => (long)r.LocalEstimatedContextTokens); // V6: local estimate for comparison
    public long TotalTokens => TotalPromptTokens + TotalCompletionTokens;
    public double TotalCost => Runs.Sum(r => r.TotalCost);
    public double AvgRounds => Runs.Count == 0 ? 0 : Runs.Average(r => (double)r.Rounds);
    public double AvgInputTokensPerTask => Runs.Count == 0 ? 0 : Runs.Average(r => (double)r.PromptTokens);
    public double AvgTestPassRatio => Runs.Count == 0 ? 0 : Runs.Average(r => r.TestsTotal > 0 ? (double)r.TestsPassed / r.TestsTotal : 0);
}

/// <summary>
/// Agent benchmark runner.
/// Executes "task -> model -> patch -> test -> cost" for each task.
/// A single "round" = one model call + test evaluation.
/// </summary>
public sealed class AgentBenchmarkRunner
{
    private readonly IAgentModelExecutor _model;
    private readonly ITokenizer _tokenizer;
    private readonly int _maxRounds;

    public AgentBenchmarkRunner(IAgentModelExecutor model, ITokenizer tokenizer, int maxRounds = 3)
    {
        _model = model;
        _tokenizer = tokenizer;
        _maxRounds = maxRounds;
    }

    /// <summary>
    /// Runs a single task through the full agent loop.
    /// </summary>
    public async Task<AgentRunResult> RunTaskAsync(
        BenchmarkTask task,
        BenchmarkConfig config,
        Func<string, AgentContextPackage> contextBuilder,
        Func<string, Task<AgentTestResult>> testRunner,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var rounds = 0;
        var totalPrompt = 0;
        var totalCompletion = 0;
        var totalLocalEstimated = 0;
        var totalCost = 0.0;
        var lastTestsPassed = 0;
        var lastTestsTotal = 0;
        var taskCompleted = false;  // V7-W03: track per-round success, not cumulative
        var applyPatches = new List<string>();
        var errorMsg = (string?)null;

        while (rounds < _maxRounds)
        {
            rounds++;
            var context = contextBuilder(task.TaskDescription);
            var prompt = ComposePrompt(task.TaskDescription, context.FileSnippets);

            // V6: Local estimate of assembled context (for comparison against Provider actual)
            totalLocalEstimated += _tokenizer.CountTokens(prompt);

            AgentModelResponse response;
            try
            {
                response = await _model.GenerateAsync("You are a code assistant. Complete the task.", prompt, ct);
            }
            catch (Exception ex)
            {
                errorMsg = ex.Message;
                break;
            }

            // V6: Use Provider's actual usage tokens, not local tokenizer estimate
            totalPrompt += response.PromptTokens;
            totalCompletion += response.CompletionTokens;
            totalCost += response.Cost ?? 0;

            var patch = ExtractPatch(response.Content);
            applyPatches.Add(patch);

            var result = await testRunner(patch);
            // V7-W03: Track last round's test results (not cumulative) for reporting
            lastTestsPassed = result.Passed;
            lastTestsTotal = result.Total;

            if (result.Success)
            {
                taskCompleted = true;  // V7-W03: any round success = task completed
                break;
            }
        }

        sw.Stop();
        return new AgentRunResult
        {
            TaskId = task.Id,
            RunNumber = 0,
            TaskCompleted = taskCompleted,  // V7-W03: based on per-round success, not cumulative
            Rounds = rounds,
            PromptTokens = totalPrompt,
            CompletionTokens = totalCompletion,
            LocalEstimatedContextTokens = totalLocalEstimated,
            TotalCost = totalCost,
            TestsPassed = lastTestsPassed,   // V7-W03: last round's results
            TestsTotal = lastTestsTotal,     // V7-W03: last round's results
            ExtraFilesRead = 0,
            PatchApplied = applyPatches,
            ErrorMessage = errorMsg,
            Duration = sw.Elapsed,
        };
    }

    /// <summary>
    /// Runs all tasks in a benchmark set and aggregates results.
    /// </summary>
    public async Task<AgentBenchmarkResult> RunAllAsync(
        IReadOnlyList<BenchmarkTask> tasks,
        BenchmarkConfig config,
        Func<string, AgentContextPackage> contextBuilder,
        Func<string, Task<AgentTestResult>> testRunner,
        CancellationToken ct = default)
    {
        var results = new List<AgentRunResult>();
        foreach (var task in tasks)
        {
            var result = await RunTaskAsync(task, config, contextBuilder, testRunner, ct);
            results.Add(result);
        }
        return new AgentBenchmarkResult
        {
            ModelId = _model.ModelId,
            Runs = results,
        };
    }

    private static string ComposePrompt(string taskDescription, IReadOnlyList<string> fileSnippets)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Task: ").AppendLine(taskDescription);
        sb.AppendLine();
        sb.AppendLine("Context files:");
        foreach (var snippet in fileSnippets)
        {
            sb.AppendLine("---");
            sb.AppendLine(snippet);
        }
        return sb.ToString();
    }

    private static string ExtractPatch(string modelResponse)
    {
        // Best-effort: extract code blocks from the response
        var start = modelResponse.IndexOf("```", StringComparison.Ordinal);
        if (start < 0) return modelResponse;
        var end = modelResponse.IndexOf("```", start + 3, StringComparison.Ordinal);
        if (end < 0) return modelResponse;
        var inner = modelResponse.AsSpan(start + 3, end - start - 3).ToString();
        // Skip language identifier (e.g., "csharp\n")
        var nl = inner.IndexOf('\n');
        return nl >= 0 ? inner[(nl + 1)..] : inner;
    }
}

/// <summary>
/// A no-op model executor for testing: returns a fixed response without calling any model.
/// Useful for verifying the benchmark framework without a real model.
/// </summary>
public sealed class NullAgentModelExecutor : IAgentModelExecutor
{
    public string ModelId => "null-test";
    public Task<AgentModelResponse> GenerateAsync(string systemPrompt, string userContent, CancellationToken ct = default)
        => Task.FromResult(new AgentModelResponse
        {
            Content = "```\n// No changes\n```",
            PromptTokens = 0,
            CompletionTokens = 5,
            Cost = 0,
        });
}
