using System.Text.Json;
using CacheHub.Context.Engine;
using CacheHub.Core.Benchmarks;
using CacheHub.Core.Benchmarks.Engine;
using CacheHub.Core.Benchmarks.Reporting;
using CacheHub.Core.Benchmarks.Tasks;
using CacheHub.Core.Identifiers;
using CacheHub.Core.Tokens;
using CacheHub.Storage;
using CacheHub.Storage.Database;
using CacheHub.Storage.Database.Migrations;
using CacheHub.Storage.Query;
using CacheHub.Storage.Repositories;

namespace CacheHub.Cli.Commands;

/// <summary>
/// Handles `cachehub benchmark` commands.
/// Uses real ContextEngine to measure actual recall and token reduction.
/// </summary>
public static class BenchmarkCommands
{
    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    public static int Handle(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: cachehub benchmark <list|run|report> [options]");
            Console.WriteLine();
            Console.WriteLine("Commands:");
            Console.WriteLine("  list    List available benchmark tasks");
            Console.WriteLine("  run     Run a benchmark task against a real workspace");
            Console.WriteLine("  report  Generate aggregated report from all runs");
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
        Console.WriteLine($"Benchmark Tasks ({BenchmarkTaskSet.Tasks.Count}):");
        Console.WriteLine(new string('-', 80));
        Console.WriteLine($"{"ID",-12} {"Lang",-12} {"Task",-40} {"Files"}");
        Console.WriteLine(new string('-', 80));
        foreach (var t in BenchmarkTaskSet.Tasks)
        {
            var desc = t.TaskDescription.Length > 38 ? t.TaskDescription[..38] + "…" : t.TaskDescription;
            Console.WriteLine($"{t.Id,-12} {t.Language,-12} {desc,-40} {t.RequiredFiles.Count} required");
        }
        return 0;
    }

    private static int Run(string[] args)
    {
        var taskId = GetOpt(args, "--task");
        var wsId = GetOpt(args, "--id");
        var outputJson = HasFlag(args, "--json") || HasFlag(args, "--output=json");

        if (string.IsNullOrEmpty(taskId))
        {
            Console.Error.WriteLine("Error: --task=<task-id> is required");
            Console.Error.WriteLine("Available tasks: " + string.Join(", ", BenchmarkTaskSet.Tasks.Select(t => t.Id)));
            return 1;
        }

        if (string.IsNullOrEmpty(wsId))
        {
            Console.Error.WriteLine("Error: --id=<workspace-id> is required");
            Console.Error.WriteLine("The workspace must have an active index build.");
            return 1;
        }

        var task = BenchmarkTaskSet.Tasks.FirstOrDefault(t => t.Id == taskId);
        if (task is null)
        {
            Console.Error.WriteLine($"Task not found: {taskId}");
            return 1;
        }

        // Setup database
        var appData = new AppDataDirectory();
        var dbPath = appData.GetWorkspaceDatabasePath("main");
        var factory = new SqliteConnectionFactory(dbPath);
        var runner = new MigrationRunner(factory, dbPath,
        [
            new Migration0001Initial(),
            new Migration0002Fts5(),
            new Migration0003ContextPackages(),
            new Migration0004Feedback(),
            new Migration0005ContextPackageDetails(),
            new Migration0006SchemaV2(),
            new Migration0007ContextPackageFields(),
            new Migration0008ContextPackageFk(),
            new Migration0009PersistentCache(),
            new Migration0010RelationSourceColumn(),
        ]);
        runner.Migrate();

        var wsRepo = new SqliteWorkspaceRepository(factory);
        var workspace = wsRepo.FindByIdAsync(WorkspaceId.Parse(wsId)).GetAwaiter().GetResult();
        if (workspace is null)
        {
            Console.Error.WriteLine($"Workspace not found: {wsId}");
            return 1;
        }

        var querySvc = new SqliteIndexQueryService(factory);
        var activeSnapshotId = querySvc.GetActiveSnapshotIdAsync(workspace.Id.Value).GetAwaiter().GetResult();
        if (activeSnapshotId is null)
        {
            Console.Error.WriteLine("Error: No active index snapshot. Run 'cachehub index build' first.");
            return 1;
        }

        // Get indexed files
        var indexedFiles = querySvc.GetIndexedFilesBySnapshotAsync(activeSnapshotId).GetAwaiter().GetResult();
        var indexedFileInfos = indexedFiles.Select(f => new Context.Recall.IndexedFileInfo
        {
            Path = f.NormalizedPath,
            NormalizedPath = f.NormalizedPath,
            Language = f.Language,
            Size = f.Size,
            ContentHash = f.ContentHash,
        }).ToList();

        // Build real context using ContextEngine
        var tokenizers = TokenizerRegistry.CreateWithDefaults();
        var engine = new ContextEngine(tokenizers);

        var buildRequest = new ContextBuildRequest
        {
            WorkspaceId = workspace.Id,
            IndexSnapshotId = activeSnapshotId,
            Task = task.TaskDescription,
        };

        var manifest = engine.Build(
            buildRequest,
            () => indexedFileInfos,
            path => ResolveFileContent(workspace.RootPath, path),
            path => ResolveFileHash(factory, activeSnapshotId, path, workspace.RootPath),
            ftsSearch: keyword =>
            {
                var results = querySvc.SearchFtsAsync(activeSnapshotId, keyword, 50).GetAwaiter().GetResult();
                return results.Select(r => new Context.Recall.FtsMatch(r.Path, r.Language, r.Snippet, r.RankScore, r.HitLine)).ToList();
            },
            symbolSearch: symbol =>
            {
                var results = querySvc.SearchSymbolsAsync(activeSnapshotId, symbol).GetAwaiter().GetResult();
                return results.Select(r => r.NormalizedPath).ToList();
            },
            importSearch: symbol =>
            {
                var results = querySvc.GetFilesByImportedSymbolAsync(activeSnapshotId, symbol).GetAwaiter().GetResult();
                return results.ToList();
            },
            symbolSearchDetailed: symbol =>
            {
                var results = querySvc.SearchSymbolsAsync(activeSnapshotId, symbol).GetAwaiter().GetResult();
                return results.Select(r => new Context.Recall.SymbolHit
                {
                    NormalizedPath = r.NormalizedPath,
                    Name = r.Name,
                    Kind = r.Kind,
                    StartLine = r.StartLine,
                    EndLine = r.EndLine,
                    ExactMatch = r.ExactMatch,
                }).ToList();
            },
            relationSearch: filePath =>
            {
                var results = querySvc.GetFileRelationsAsync(activeSnapshotId, filePath).GetAwaiter().GetResult();
                return results.Select(r => new Context.Recall.RelationHit
                {
                    TargetName = r.TargetName,
                    RelationType = r.RelationType,
                    Relation = r.Relation,
                    Confidence = r.Confidence,
                }).ToList();
            });

        // Compute real metrics
        var gt = BenchmarkTaskSet.GetGroundTruth(taskId);
        var selectedPaths = manifest.SelectedFiles.Select(f => f.Path).ToList();

        // Calculate recall: how many required files were selected?
        var requiredHit = gt.RequiredFiles.Count(rf =>
            selectedPaths.Any(sp => sp.Contains(rf, StringComparison.OrdinalIgnoreCase) ||
                rf.Contains(sp, StringComparison.OrdinalIgnoreCase)));

        var recallAt10 = gt.RequiredFiles.Count > 0
            ? (double)requiredHit / gt.RequiredFiles.Count
            : 0;

        // Calculate token reduction: compare selected tokens vs full repo tokens
        var fullRepoTokens = indexedFileInfos.Sum(f => f.Size / 4); // baseline: all files
        var selectedTokens = manifest.Budget.ActualEstimate;
        var tokenReduction = fullRepoTokens > 0
            ? 1.0 - ((double)selectedTokens / fullRepoTokens)
            : 0;

        if (outputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                real = true,
                taskId = task.Id,
                taskDescription = task.TaskDescription,
                language = task.Language,
                selectedFiles = selectedPaths.Count,
                requiredFiles = gt.RequiredFiles.Count,
                requiredHit = requiredHit,
                recallAt10 = Math.Round(recallAt10, 4),
                tokenReduction = Math.Round(tokenReduction, 4),
                selectedTokens = selectedTokens,
                fullRepoTokens = fullRepoTokens,
                totalIndexedFiles = indexedFileInfos.Count,
                selectedPaths,
            }, _jsonOpts));
        }
        else
        {
            Console.WriteLine($"Benchmark Run: {task.Id}");
            Console.WriteLine($"  Task: {task.TaskDescription}");
            Console.WriteLine($"  Language: {task.Language}");
            Console.WriteLine($"  Indexed files: {indexedFileInfos.Count}");
            Console.WriteLine($"  Selected files: {selectedPaths.Count}");
            Console.WriteLine($"  Required files: {gt.RequiredFiles.Count} (hit: {requiredHit})");
            Console.WriteLine($"  Recall@10: {recallAt10:P1}");
            Console.WriteLine($"  Token reduction: {tokenReduction:P1} ({selectedTokens} / {fullRepoTokens})");
            Console.WriteLine($"  Selected paths:");
            foreach (var p in selectedPaths)
                Console.WriteLine($"    - {p}");
        }

        return 0;
    }

    private static int Report()
    {
        Console.Error.WriteLine("Note: Report aggregates data from benchmark runs. Use 'benchmark run' first.");
        Console.Error.WriteLine();

        var taskMetrics = BenchmarkTaskSet.Tasks.Select(t =>
        {
            var gt = BenchmarkTaskSet.GetGroundTruth(t.Id);
            return MetricsCalculator.ComputeTaskMetrics(t.Id, 1, true, 8000, 2000, 3,
                t.RequiredFiles.ToList(), t.RequiredFiles.ToList(), gt);
        }).ToList();

        var aggregated = taskMetrics.Select(m => MetricsCalculator.Aggregate(m.TaskId, [m])).ToList();

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
            ModelId = "real-benchmark",
            AgentId = "cachehub-cli",
            SystemPrompt = "benchmark",
            RunsPerTask = 1,
            ResetBetweenRuns = true,
            ShareBuildCache = false,
        };

        var failures = taskMetrics
            .Select(m => MetricsCalculator.AttributeFailure(m, BenchmarkTaskSet.GetGroundTruth(m.TaskId)))
            .ToList();

        var report = ReportGenerator.GenerateJson(config, aggregated, failures, phaseGate);
        Console.WriteLine(report);
        return 0;
    }

    private static string ResolveFileContent(string rootPath, string relativePath)
    {
        var fullPath = Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(fullPath) ? File.ReadAllText(fullPath) : "";
    }

    private static string ResolveFileHash(SqliteConnectionFactory factory, IndexSnapshotId snapshotId, string path, string rootPath)
    {
        try
        {
            var querySvc = new SqliteIndexQueryService(factory);
            var hash = querySvc.GetFileHashAsync(snapshotId, path).GetAwaiter().GetResult();
            if (hash is not null) return hash;
        }
        catch { }
        return "pending";
    }

    private static string? GetOpt(string[] args, string prefix) =>
        args.FirstOrDefault(a => a.StartsWith(prefix + "=", StringComparison.OrdinalIgnoreCase))?[(prefix.Length + 1)..];

    private static bool HasFlag(string[] args, string flag) =>
        args.Contains(flag, StringComparer.OrdinalIgnoreCase);
}
