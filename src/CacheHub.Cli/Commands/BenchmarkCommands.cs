using CacheHub.Core.Benchmarks;
using CacheHub.Core.Benchmarks.Engine;
using CacheHub.Core.Benchmarks.Reporting;
using CacheHub.Core.Benchmarks.Tasks;

namespace CacheHub.Cli.Commands;

public static class BenchmarkCommands
{
    private static readonly System.Text.Json.JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    public static int Handle(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: cachehub benchmark <list|run|report> [options]");
            return 1;
        }

        return args[0] switch
        {
            "list" => List(),
            "run" => Run(args.AsSpan(1).ToArray()),
            "report" => Report(),
            _ => 1,
        };
    }

    private static int List()
    {
        Console.WriteLine("⚠️ DEMO ONLY — Benchmark tasks are simulated, not real experiments.");
        Console.WriteLine("   Results from 'benchmark run' and 'benchmark report' cannot be used as");
        Console.WriteLine("   evidence of Token optimization or task success rate.");
        Console.WriteLine();
        Console.WriteLine($"Benchmark Tasks ({BenchmarkTaskSet.Tasks.Count}):");
        Console.WriteLine(new string('-', 80));
        Console.WriteLine($"{"ID",-12} {"Lang",-12} {"Task",-40} {"Files"}");
        Console.WriteLine(new string('-', 80));
        foreach (var t in BenchmarkTaskSet.Tasks)
        {
            Console.WriteLine($"{t.Id,-12} {t.Language,-12} {t.TaskDescription[..Math.Min(38, t.TaskDescription.Length)] + ("…").PadRight(40)} {t.RequiredFiles.Count} required");
        }
        return 0;
    }

    private static int Run(string[] args)
    {
        Console.Error.WriteLine("⚠️ DEMO ONLY — This benchmark uses simulated data, not a real Context Engine run.");
        Console.Error.WriteLine("   Selected files are set to RequiredFiles (ground truth), not actually selected by ContextEngine.");
        Console.Error.WriteLine("   Results CANNOT be used as evidence of Token optimization or phase gate approval.");
        Console.Error.WriteLine();

        var taskId = args.FirstOrDefault(a => a.StartsWith("--task=", StringComparison.OrdinalIgnoreCase))?["--task=".Length..];
        var outputJson = args.Contains("--output=json", StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(taskId))
        {
            Console.Error.WriteLine("Error: --task=<task-id> is required");
            Console.Error.WriteLine("Available tasks: " + string.Join(", ", BenchmarkTaskSet.Tasks.Select(t => t.Id)));
            return 1;
        }

        var task = BenchmarkTaskSet.Tasks.FirstOrDefault(t => t.Id == taskId);
        if (task is null)
        {
            Console.Error.WriteLine($"Task not found: {taskId}");
            return 1;
        }

        var gt = BenchmarkTaskSet.GetGroundTruth(taskId);

        // SIMULATED: CacheHub "selects" the required files (ideal case, NOT real)
        var selectedFiles = task.RequiredFiles.ToList();
        var metrics = MetricsCalculator.ComputeTaskMetrics(
            taskId, 1, true, 8000, 2000, 3, selectedFiles, selectedFiles, gt);

        if (outputJson)
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                demo = true,
                warning = "Simulated data — not a real benchmark. Cannot be used for phase gate approval.",
                taskId = metrics.TaskId,
                run = metrics.RunNumber,
                completed = metrics.TaskCompleted,
                recall = Math.Round(metrics.FileRecallAt10, 4),
                precision = Math.Round(metrics.ContextPrecision, 4),
                tokens = metrics.TotalInputTokens,
                rounds = metrics.Rounds,
            }, _jsonOpts));
        }
        else
        {
            Console.WriteLine($"⚠️ DEMO Benchmark Run: {taskId}");
            Console.WriteLine($"  (Simulated — selectedFiles = RequiredFiles, NOT real Context Engine)");
            Console.WriteLine($"  Task: {task.TaskDescription}");
            Console.WriteLine($"  Language: {task.Language}");
            Console.WriteLine($"  Completed: {metrics.TaskCompleted}");
            Console.WriteLine($"  Recall@10: {metrics.FileRecallAt10:F4}");
            Console.WriteLine($"  Precision: {metrics.ContextPrecision:F4}");
            Console.WriteLine($"  Tokens: {metrics.TotalInputTokens}");
            Console.WriteLine($"  Rounds: {metrics.Rounds}");
            Console.WriteLine($"  Required files: {task.RequiredFiles.Count}");
            Console.WriteLine($"  Helpful files: {task.HelpfulFiles.Count}");
            Console.WriteLine($"  Distractor files: {task.DistractorFiles.Count}");
        }

        return 0;
    }

    private static int Report()
    {
        Console.Error.WriteLine("⚠️ DEMO ONLY — This report uses simulated baseline and metrics.");
        Console.Error.WriteLine("   Phase gate status is NOT valid. Real benchmark requires R4 implementation.");
        Console.Error.WriteLine();

        // Generate a report using simulated data — phase gate is NOT valid
        var taskMetrics = BenchmarkTaskSet.Tasks.Select(t =>
        {
            var gt = BenchmarkTaskSet.GetGroundTruth(t.Id);
            return MetricsCalculator.ComputeTaskMetrics(t.Id, 1, true, 8000, 2000, 3,
                t.RequiredFiles.ToList(), t.RequiredFiles.ToList(), gt);
        }).ToList();

        var aggregated = taskMetrics.Select(m => MetricsCalculator.Aggregate(m.TaskId, [m])).ToList();

        // Simulated baseline — NOT real
        var baseline = BenchmarkTaskSet.Tasks.Select(t => new AggregatedMetrics
        {
            TaskId = t.Id,
            MeanFileRecall = 0.90,
            MissingContextRate = 0.10,
            SuccessRate = 0.95,
            StaleContextRate = 0,
            MeanInputTokens = 12000,
            RunCount = 1,
        }).ToList();

        var phaseGate = MetricsCalculator.EvaluatePhaseGate(aggregated, baseline, new PhaseGateThresholds());

        var config = new BenchmarkConfig
        {
            ModelId = "demo-model",
            AgentId = "cachehub-demo",
            SystemPrompt = "benchmark-demo",
            RunsPerTask = 1,
            ResetBetweenRuns = true,
            ShareBuildCache = false,
        };

        var failures = taskMetrics
            .Select(m => MetricsCalculator.AttributeFailure(m, BenchmarkTaskSet.GetGroundTruth(m.TaskId)))
            .ToList();

        var report = ReportGenerator.GenerateJson(config, aggregated, failures, phaseGate);

        // Prepend demo warning to the JSON output
        Console.WriteLine("{\"demo\": true, \"warning\": \"Simulated data — phase gate is NOT valid. Real benchmark requires R4 implementation.\", \"report\": " + report + "}");
        return 0;
    }
}
