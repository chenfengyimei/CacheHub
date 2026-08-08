using System.Text.Json;
using CacheHub.Context.Engine;
using CacheHub.Core.Benchmarks;
using CacheHub.Core.Benchmarks.Agent;
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

using CacheHub.Core.Benchmarks.Matrix;

namespace CacheHub.Cli.Commands;

/// <summary>
/// Persisted benchmark run result for Report() aggregation.
/// </summary>
public sealed class BenchmarkRunRecord
{
    public string TaskId { get; set; } = "";
    public string TaskDescription { get; set; } = "";
    public string Language { get; set; } = "";
    public DateTime RunTimestamp { get; set; }
    public int SelectedFilesCount { get; set; }
    public int RequiredFilesCount { get; set; }
    public int RequiredHit { get; set; }
    public double RecallAt10 { get; set; }
    public double TokenReduction { get; set; }
    public int SelectedTokens { get; set; }
    public int FullRepoTokens { get; set; }
    public int TotalIndexedFiles { get; set; }
    public List<string> Top10Paths { get; set; } = [];
    public List<string> SelectedPaths { get; set; } = [];
}

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
            Console.WriteLine("Usage: cachehub benchmark <list|run|agent|report> [options]");
            Console.WriteLine();
            Console.WriteLine("Commands:");
            Console.WriteLine("  list    List available benchmark tasks");
            Console.WriteLine("  run     Run a retrieval benchmark (Recall/Token) against a real workspace");
            Console.WriteLine("  agent   Run a real Agent Benchmark (task→model→patch→test→cost) via Gateway");
            Console.WriteLine("  matrix  Run Benchmark Matrix across all fixture repositories (V7-W18)");
            Console.WriteLine("  report  Generate aggregated report from all retrieval benchmark runs");
            Console.WriteLine();
            Console.WriteLine("Agent options:");
            Console.WriteLine("  --id=<workspace-id>     Workspace with active index (required)");
            Console.WriteLine("  --task=<task-id>        Specific task (default: all tasks)");
            Console.WriteLine("  --model=<model>         Model id (default: gpt-4o-mini)");
            Console.WriteLine("  --gateway-url=<url>     Gateway URL (default: http://127.0.0.1:5218)");
            Console.WriteLine("  --rounds=<n>            Max model rounds per task (default: 2)");
            Console.WriteLine("  --compare               Run CacheHub vs Baseline side-by-side");
            Console.WriteLine("  --real-test             Apply patch to git worktree + run real build/test");
            Console.WriteLine("                          (SuccessRate from actual test exit code)");
            Console.WriteLine("  --test-command=<cmd>    Build/test command for --real-test (default: dotnet test)");
            Console.WriteLine("  --price=<in,out>        Override model pricing in USD per 1M tokens,");
            Console.WriteLine("                          e.g. --price=3.0,15.0 for Claude (review #17)");
            return 1;
        }

        return args[0] switch
        {
            "list" => List(),
            "run" => Run(args.AsSpan(1).ToArray()),
            "agent" => Agent(args.AsSpan(1).ToArray()),
            "matrix" => Matrix(args.AsSpan(1).ToArray()),
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
            new Migration0011SnapshotGitState(),
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
        var engine = new ContextEngine(tokenizers, cache: ContextCommands.CreateContextCache(factory));

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
            },
            semanticSearch: SemanticReferenceHelper.CreateSemanticSearch(appData.Root, workspace.Id.Value),
            fileSymbolsProvider: path =>
            {
                var results = querySvc.GetFileSymbolsAsync(activeSnapshotId, path).GetAwaiter().GetResult();
                return results.Select(r => new Context.Recall.SymbolHit
                {
                    NormalizedPath = path,
                    Name = r.Name,
                    Kind = r.Kind,
                    StartLine = r.StartLine,
                    EndLine = r.EndLine,
                    ExactMatch = true,
                }).ToList();
            },
            reverseRelationSearch: target =>
            {
                var results = querySvc.GetFilesByRelationTargetAsync(activeSnapshotId, target).GetAwaiter().GetResult();
                return results.Select(r => new Context.Recall.RelationHit
                {
                    TargetName = r.TargetName,
                    RelationType = r.RelationType,
                    Relation = r.Relation,
                    Confidence = r.Confidence,
                    SourcePath = r.NormalizedPath,
                }).ToList();
            });

        // Compute real metrics
        var gt = BenchmarkTaskSet.GetGroundTruth(taskId);
        var selectedPaths = manifest.SelectedFiles.Select(f => f.Path).ToList();

        // Calculate Recall@10: only consider the top-10 selected files (by ranking order)
        var top10Paths = selectedPaths.Take(10).ToList();
        var requiredHit = gt.RequiredFiles.Count(rf =>
            top10Paths.Any(sp => sp.Contains(rf, StringComparison.OrdinalIgnoreCase) ||
                rf.Contains(sp, StringComparison.OrdinalIgnoreCase)));

        var recallAt10 = gt.RequiredFiles.Count > 0
            ? (double)requiredHit / gt.RequiredFiles.Count
            : 0;

        // Calculate token reduction using the SAME tokenizer for both baseline and selected
        // This ensures apples-to-apples comparison (no chars/4 vs CodeTokenizer mismatch)
        var tokenizer = tokenizers.Default;
        var fullRepoTokens = 0;
        foreach (var file in indexedFileInfos)
        {
            var content = ResolveFileContent(workspace.RootPath, file.NormalizedPath);
            if (!string.IsNullOrEmpty(content))
                fullRepoTokens += tokenizer.CountTokens(content);
        }
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
                top10Paths,
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
            Console.WriteLine($"  Recall@10: {recallAt10:P1} (top-10 of {selectedPaths.Count} selected)");
            Console.WriteLine($"  Token reduction: {tokenReduction:P1} ({selectedTokens} / {fullRepoTokens})");
            Console.WriteLine($"  Selected paths:");
            foreach (var p in selectedPaths)
                Console.WriteLine($"    - {p}");
        }

        // Persist run result for Report() aggregation
        PersistRunResult(new BenchmarkRunRecord
        {
            TaskId = task.Id,
            TaskDescription = task.TaskDescription,
            Language = task.Language,
            RunTimestamp = DateTime.UtcNow,
            SelectedFilesCount = selectedPaths.Count,
            RequiredFilesCount = gt.RequiredFiles.Count,
            RequiredHit = requiredHit,
            RecallAt10 = Math.Round(recallAt10, 4),
            TokenReduction = Math.Round(tokenReduction, 4),
            SelectedTokens = selectedTokens,
            FullRepoTokens = fullRepoTokens,
            TotalIndexedFiles = indexedFileInfos.Count,
            Top10Paths = top10Paths,
            SelectedPaths = selectedPaths,
        });

        return 0;
    }

    /// <summary>
    /// `benchmark agent`: runs a real Agent Benchmark (task→model→patch→test→cost)
    /// through the Gateway. Measures SuccessRate, Token usage, Rounds, and Cost —
    /// the data that proves CacheHub's value ("same success, fewer tokens").
    /// Requires a running Gateway (cachehub gateway start).
    /// </summary>
    private static int Agent(string[] args)
    {
        var taskId = GetOpt(args, "--task");
        var wsId = GetOpt(args, "--id");
        var gatewayUrl = GetOpt(args, "--gateway-url") ?? "http://127.0.0.1:5218";
        var gatewayToken = GetOpt(args, "--gateway-token")
            ?? Environment.GetEnvironmentVariable("CACHEHUB_GATEWAY_TOKEN")
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? "";
        var model = GetOpt(args, "--model") ?? "gpt-4o-mini";
        var maxRounds = int.TryParse(GetOpt(args, "--rounds"), out var r) ? r : 2;
        var runsPerTask = int.TryParse(GetOpt(args, "--runs-per-task"), out var rpt) ? rpt : 1; // V8-P1-03
        var compare = HasFlag(args, "--compare");
        // V6: Real test — apply patch to git worktree and run build/test command
        var realTest = HasFlag(args, "--real-test");
        var testCommandStr = GetOpt(args, "--test-command") ?? "dotnet test";
        // V6: Split into (command, args) so --test-command actually works (was previously ignored)
        var testCommandParts = testCommandStr.Split(' ', 2, StringSplitOptions.TrimEntries);
        var testCmd = testCommandParts.Length >= 1 ? testCommandParts[0] : "dotnet";
        var testArgs = testCommandParts.Length >= 2 ? testCommandParts[1] : "test -c Release";
        // V6: Optional per-run pricing override "inputPer1M,outputPer1M" (review #17)
        var priceOverride = GetOpt(args, "--price");

        if (string.IsNullOrEmpty(wsId))
        {
            Console.Error.WriteLine("Error: --id=<workspace-id> is required");
            return 1;
        }
        if (string.IsNullOrEmpty(gatewayUrl))
        {
            Console.Error.WriteLine("Error: --gateway-url=<url> is required");
            return 1;
        }
        if (string.IsNullOrEmpty(gatewayToken))
        {
            Console.Error.WriteLine("Warning: No gateway token. Set CACHEHUB_GATEWAY_TOKEN or OPENAI_API_KEY.");
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
            new Migration0011SnapshotGitState(),
        ]);
        runner.Migrate();

        var wsRepo = new SqliteWorkspaceRepository(factory);
        var workspace = wsRepo.FindByIdAsync(WorkspaceId.Parse(wsId)).GetAwaiter().GetResult();
        if (workspace is null)
        {
            Console.Error.WriteLine($"Workspace not found: {wsId}");
            return 1;
        }

        var tokenizers = TokenizerRegistry.CreateWithDefaults();
        var tokenizer = tokenizers.Default;

        // Parse optional --price "inPer1M,outPer1M"
        double? overrideIn = null, overrideOut = null;
        if (!string.IsNullOrEmpty(priceOverride))
        {
            var parts = priceOverride.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 &&
                double.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out var inP) &&
                double.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out var outP))
            {
                overrideIn = inP;
                overrideOut = outP;
                Console.Error.WriteLine($"  Using custom pricing: ${overrideIn.Value:F2}/1M input, ${overrideOut.Value:F2}/1M output");
            }
            else
            {
                Console.Error.WriteLine("  Warning: Invalid --price format (expected \"inputPer1M,outputPer1M\"). Using built-in pricing.");
            }
        }

        var executor = new GatewayAgentModelExecutor(gatewayUrl, gatewayToken, model, overrideIn, overrideOut);
        var agentRunner = new Core.Benchmarks.Agent.AgentBenchmarkRunner(executor, tokenizer, maxRounds);

        // CacheHub branch: use REAL ContextEngine to build context (NOT RequiredFiles).
        // This proves CacheHub's recall/ranking/selection can find the right files.
        AgentContextPackage BuildCacheHubContext(string taskDescription)
        {
            var querySvc = new SqliteIndexQueryService(factory);
            var activeSnapshotId = querySvc.GetActiveSnapshotIdAsync(workspace.Id.Value).GetAwaiter().GetResult();
            if (activeSnapshotId is null)
                return new Core.Benchmarks.Agent.AgentContextPackage
                {
                    TaskDescription = taskDescription,
                    SelectedFilePaths = [],
                    FileSnippets = [],
                    EstimatedTokens = 0,
                };

            var indexedFiles = querySvc.GetIndexedFilesBySnapshotAsync(activeSnapshotId).GetAwaiter().GetResult();
            var indexedFileInfos = indexedFiles.Select(f => new Context.Recall.IndexedFileInfo
            {
                Path = f.NormalizedPath,
                NormalizedPath = f.NormalizedPath,
                Language = f.Language,
                Size = f.Size,
                ContentHash = f.ContentHash,
            }).ToList();

            var engine = new ContextEngine(tokenizers, cache: ContextCommands.CreateContextCache(factory));
            var manifest = engine.Build(
                new ContextBuildRequest
                {
                    WorkspaceId = workspace.Id,
                    IndexSnapshotId = activeSnapshotId,
                    Task = taskDescription,
                },
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
                },
                semanticSearch: SemanticReferenceHelper.CreateSemanticSearch(appData.Root, workspace.Id.Value),
                fileSymbolsProvider: path =>
                {
                    var results = querySvc.GetFileSymbolsAsync(activeSnapshotId, path).GetAwaiter().GetResult();
                    return results.Select(r => new Context.Recall.SymbolHit
                    {
                        NormalizedPath = path,
                        Name = r.Name,
                        Kind = r.Kind,
                        StartLine = r.StartLine,
                        EndLine = r.EndLine,
                        ExactMatch = true,
                    }).ToList();
                },
                reverseRelationSearch: target =>
                {
                    var results = querySvc.GetFilesByRelationTargetAsync(activeSnapshotId, target).GetAwaiter().GetResult();
                    return results.Select(r => new Context.Recall.RelationHit
                    {
                        TargetName = r.TargetName,
                        RelationType = r.RelationType,
                        Relation = r.Relation,
                        Confidence = r.Confidence,
                        SourcePath = r.NormalizedPath,
                    }).ToList();
                });

            var paths = manifest.SelectedFiles.Select(f => f.Path).ToList();

            // V6: Use real PayloadGenerator for chunked/compressed payload (respects anchor ranges)
            var payloadGenerator = new CacheHub.Context.Payload.PayloadGenerator();
            var (_, payloadEnforcer) = CacheHub.Core.Security.SecurityPolicyResolver.CreateEnforcer();
            var payloadContent = payloadGenerator.GenerateMarkdown(manifest,
                path => ResolveFileContent(workspace.RootPath, path), payloadEnforcer);

            return new Core.Benchmarks.Agent.AgentContextPackage
            {
                TaskDescription = taskDescription,
                SelectedFilePaths = paths,
                FileSnippets = [payloadContent],
                EstimatedTokens = manifest.Budget.ActualEstimate,
            };
        }

        // Baseline branch: give the model the ENTIRE repository content.
        // Represents "AI agent without CacheHub" — reads the full codebase each time.
        // Baseline branch: give the model the ENTIRE repository content.
        // Represents "AI agent without CacheHub" — reads the full codebase each time.
        Core.Benchmarks.Agent.AgentContextPackage? baselineCache = null;
        AgentContextPackage BuildBaselineContext(string taskDescription)
        {
            if (baselineCache is not null) return baselineCache;

            // V6 (review #18): Baseline should be a fair, safe "read whole repo" proxy.
            // V7-W07: Use relative paths for context labels + stable sort for reproducibility.
            var baselinePaths = Directory.EnumerateFiles(workspace.RootPath, "*", SearchOption.AllDirectories)
                .Where(p =>
                    !p.Contains("\\.git\\", StringComparison.OrdinalIgnoreCase) &&
                    !p.Contains("/.git/", StringComparison.OrdinalIgnoreCase) &&
                    !p.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase) &&
                    !p.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase) &&
                    !p.Contains("/bin/", StringComparison.OrdinalIgnoreCase) &&
                    !p.Contains("/obj/", StringComparison.OrdinalIgnoreCase) &&
                    !p.Contains("node_modules", StringComparison.OrdinalIgnoreCase) &&
                    !p.Contains("\\vendor\\", StringComparison.OrdinalIgnoreCase) &&
                    !p.Contains("/vendor/", StringComparison.OrdinalIgnoreCase) &&
                    !BaselineFileFilter.IsExcluded(p))
                .Select(p => Path.GetRelativePath(workspace.RootPath, p).Replace('\\', '/'))  // V7-W07: relative path
                .OrderBy(p => p, StringComparer.Ordinal)  // V7-W07: stable sort for reproducibility
                .Take(500)
                .ToList();

            var snippets = new List<string>();
            foreach (var relPath in baselinePaths)
            {
                try
                {
                    var fullPath = Path.Combine(workspace.RootPath, relPath.Replace('/', Path.DirectorySeparatorChar));
                    // V7-W07: Use relative path as label (not just filename) so model can distinguish files
                    snippets.Add($"// ---- {relPath} ----\n{File.ReadAllText(fullPath)}");
                }
                catch { }
            }
            baselineCache = new Core.Benchmarks.Agent.AgentContextPackage
            {
                TaskDescription = taskDescription,
                SelectedFilePaths = baselinePaths,
                FileSnippets = snippets,
                EstimatedTokens = tokenizer.CountTokens(string.Join("\n", snippets)),
            };
            return baselineCache;
        }

        // Test runner: evaluate the model's patch.
        // V6: With --real-test, apply the patch to a temp git worktree and run the real test command.
        // Success comes from actual build/test exit code, not just "contains code".
        Core.Benchmarks.Agent.GitWorktreePatchTester? worktreeTester = null;
        if (realTest)
        {
            try
            {
                Console.Error.WriteLine("  Using real git worktree test (--real-test)...");
                worktreeTester = new Core.Benchmarks.Agent.GitWorktreePatchTester();
                var wtPath = worktreeTester.CreateWorktree(workspace.RootPath, "HEAD");
                Console.Error.WriteLine($"  Worktree: {wtPath}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  Warning: Could not create worktree for real testing: {ex.Message}");
                Console.Error.WriteLine("  Falling back to substantial-code evaluation.");
                worktreeTester = null;
            }
        }

        var finalWorktree = worktreeTester;
        Task<Core.Benchmarks.Agent.AgentTestResult> EvaluatePatch(string patch)
        {
            // Real test path: apply patch to worktree and run real test command
            if (finalWorktree is not null)
            {
                try
                {
                    // Reset the worktree between patches (apply patch is cumulative-safe)
                    finalWorktree.Reset();
                    // Recreate a fresh worktree for each attempt
                    var wtPath = finalWorktree.CreateWorktree(workspace.RootPath, "HEAD");
                    _ = wtPath;
                    var applied = finalWorktree.ApplyPatch(patch);
                    if (!applied)
                    {
                        return Task.FromResult(new Core.Benchmarks.Agent.AgentTestResult
                        {
                            Success = false,
                            Passed = 0,
                            Total = 1,
                            ErrorMessage = "Patch failed to apply cleanly (git apply)",
                        });
                    }
                    return Task.FromResult(finalWorktree.RunTests(testCmd, testArgs));
                }
                catch (Exception ex)
                {
                    return Task.FromResult(new Core.Benchmarks.Agent.AgentTestResult
                    {
                        Success = false,
                        Passed = 0,
                        Total = 1,
                        ErrorMessage = ex.Message,
                    });
                }
            }

            // Fallback: substantive-code heuristic
            var valid = !string.IsNullOrWhiteSpace(patch)
                && patch.Trim() != "```"
                && ContainsSubstantiveCode(patch);
            return Task.FromResult(new Core.Benchmarks.Agent.AgentTestResult
            {
                Success = valid,
                Passed = valid ? 1 : 0,
                Total = 1,
                ErrorMessage = valid ? null : "Patch contains no substantive code changes",
            });
        }

        var config = new BenchmarkConfig
        {
            ModelId = model,
            AgentId = "cachehub-cli-agent",
            SystemPrompt = "cachehub",
            RunsPerTask = runsPerTask, // V8-P1-03: expose --runs-per-task
            ResetBetweenRuns = true,
            ShareBuildCache = false,
        };

        Console.Error.WriteLine($"Agent Benchmark — model={model}, gateway={gatewayUrl}");
        Console.Error.WriteLine($"  Tasks: {BenchmarkTaskSet.Tasks.Count}, maxRounds={maxRounds}");
        Console.Error.WriteLine($"  Use --task=<id> to run a single task, or no --task for all.");

        var tasks = string.IsNullOrEmpty(taskId)
            ? BenchmarkTaskSet.Tasks
            : BenchmarkTaskSet.Tasks.Where(t => t.Id == taskId).ToList();

        if (tasks.Count == 0)
        {
            Console.Error.WriteLine($"Task not found: {taskId}");
            return 1;
        }

        // Run synchronously (gateway call needed)
        try
        {
            var result = agentRunner.RunAllAsync(tasks, config,
                    desc => BuildCacheHubContext(desc),
                    patch => EvaluatePatch(patch))
                .GetAwaiter().GetResult();

            Console.WriteLine(JsonSerializer.Serialize(new
            {
                realModel = model,
                gatewayUrl,
                mode = compare ? "compare" : "cachehub-context",
                tasksRun = result.Runs.Count,
                successRate = Math.Round(result.SuccessRate, 4),
                totalPromptTokens = result.TotalPromptTokens,
                totalCompletionTokens = result.TotalCompletionTokens,
                totalTokens = result.TotalTokens,
                totalLocalEstimatedContextTokens = result.TotalLocalEstimatedContextTokens,
                totalCostUsd = Math.Round(result.TotalCost, 6),
                avgRounds = Math.Round(result.AvgRounds, 2),
                avgInputTokensPerTask = result.AvgInputTokensPerTask,
                avgTestPassRatio = Math.Round(result.AvgTestPassRatio, 4),
                runs = result.Runs.Select(run => new
                {
                    taskId = run.TaskId,
                    completed = run.TaskCompleted,
                    rounds = run.Rounds,
                    promptTokens = run.PromptTokens,
                    completionTokens = run.CompletionTokens,
                    localEstimatedContextTokens = run.LocalEstimatedContextTokens,
                    costUsd = Math.Round(run.TotalCost, 6),
                    testsPassed = run.TestsPassed,
                    testsTotal = run.TestsTotal,
                }),
            }, _jsonOpts));

            // If --compare, also run the baseline (full repo) branch for the same tasks
            // and emit a side-by-side comparison to prove CacheHub's value.
            if (compare)
            {
                var baselineResult = agentRunner.RunAllAsync(tasks, config,
                        desc => BuildBaselineContext(desc),
                        patch => EvaluatePatch(patch))
                    .GetAwaiter().GetResult();

                Console.Error.WriteLine("\n===== CacheHub vs Baseline (without CacheHub) =====");
                Console.Error.WriteLine($"{"Metric",-26} {"CacheHub",-12} {"Baseline",-12}");
                Console.Error.WriteLine($"{"Success rate",-26} {result.SuccessRate:P1,-12} {baselineResult.SuccessRate:P1,-12}");
                Console.Error.WriteLine($"{"Total input tokens",-26} {result.TotalPromptTokens,-12} {baselineResult.TotalPromptTokens,-12}");
                Console.Error.WriteLine($"{"Total tokens",-26} {result.TotalTokens,-12} {baselineResult.TotalTokens,-12}");
                Console.Error.WriteLine($"{"Local est. context tokens",-26} {result.TotalLocalEstimatedContextTokens,-12} {baselineResult.TotalLocalEstimatedContextTokens,-12}");
                Console.Error.WriteLine($"{"Avg rounds",-26} {result.AvgRounds:F2,-12} {baselineResult.AvgRounds:F2,-12}");
                Console.Error.WriteLine($"{"Total cost (USD)",-26} {result.TotalCost:F6,-12} {baselineResult.TotalCost:F6,-12}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Agent Benchmark failed: {ex.Message}");
            Console.Error.WriteLine("  Is the Gateway running? Start it with 'cachehub gateway start --provider-url=...'");
            return 1;
        }
        finally
        {
            // V6: Clean up git worktree used for real build/test verification
            (worktreeTester as IDisposable)?.Dispose();
            worktreeTester = null;
        }
    }

    private static int Report()
    {
        // Load real benchmark run results from persisted JSON files
        var appData = new AppDataDirectory();
        var benchDir = Path.Combine(appData.Root, "benchmarks");
        if (!Directory.Exists(benchDir))
        {
            Console.Error.WriteLine("No benchmark runs found. Use 'cachehub benchmark run --task=<id> --id=<ws>' first.");
            return 1;
        }

        var runFiles = Directory.GetFiles(benchDir, "run-*.json", SearchOption.TopDirectoryOnly);
        if (runFiles.Length == 0)
        {
            Console.Error.WriteLine("No benchmark runs found. Use 'cachehub benchmark run --task=<id> --id=<ws>' first.");
            return 1;
        }

        // Load all run results
        var runs = new List<BenchmarkRunRecord>();
        foreach (var file in runFiles)
        {
            try
            {
                var json = File.ReadAllText(file);
                var run = JsonSerializer.Deserialize<BenchmarkRunRecord>(json, _jsonOpts);
                if (run is not null) runs.Add(run);
            }
            catch { /* skip corrupt files */ }
        }

        if (runs.Count == 0)
        {
            Console.Error.WriteLine("No valid benchmark runs found.");
            return 1;
        }

        // Aggregate by task
        var aggregated = new List<AggregatedMetrics>();
        var failures = new List<FailureAttribution>();

        foreach (var task in BenchmarkTaskSet.Tasks)
        {
            var taskRuns = runs.Where(r => r.TaskId == task.Id).ToList();
            if (taskRuns.Count == 0) continue;

            var gt = BenchmarkTaskSet.GetGroundTruth(task.Id);
            var recallValues = taskRuns.Select(r => (double)r.RecallAt10).ToList();
            var tokenValues = taskRuns.Select(r => (double)r.SelectedTokens).ToList();

            var meanRecall = recallValues.Average();
            var agg = new AggregatedMetrics
            {
                TaskId = task.Id,
                MeanFileRecall = meanRecall,
                MissingContextRate = 1.0 - meanRecall,
                // V5: This is Retrieval Quality (not Agent Task Success) — accurately named
                SuccessRate = meanRecall >= 0.8 ? 1.0 : meanRecall, // Retrieval Quality proxy
                StaleContextRate = 0,
                MeanInputTokens = (long)tokenValues.Average(),
                RunCount = taskRuns.Count,
            };
            aggregated.Add(agg);

            // Failure attribution based on real results
            if (meanRecall < 0.3)
            {
                failures.Add(new FailureAttribution
                {
                    TaskId = task.Id,
                    Category = FailureCategory.Retrieval,
                    Description = $"Low recall ({meanRecall:F2}): required files not found in top-10",
                });
            }
            else if (meanRecall < 0.8)
            {
                var missingFiles = gt.RequiredFiles
                    .Where(rf => !taskRuns.Any(r => r.Top10Paths.Any(sp =>
                        sp.Contains(rf, StringComparison.OrdinalIgnoreCase) ||
                        rf.Contains(sp, StringComparison.OrdinalIgnoreCase))))
                    .ToList();
                failures.Add(new FailureAttribution
                {
                    TaskId = task.Id,
                    Category = FailureCategory.Ranking,
                    Description = $"Partial recall ({meanRecall:F2}): missing {missingFiles.Count} required file(s): {string.Join(", ", missingFiles)}",
                });
            }
        }

        // Phase gate evaluation: use the actual aggregated metrics as both actual and baseline
        // (no fake baseline — compare against the threshold directly)
        var phaseGate = MetricsCalculator.EvaluatePhaseGate(aggregated, aggregated, new PhaseGateThresholds());

        var config = new BenchmarkConfig
        {
            ModelId = "real-benchmark",
            AgentId = "cachehub-cli",
            SystemPrompt = "benchmark",
            RunsPerTask = 1,
            ResetBetweenRuns = true,
            ShareBuildCache = false,
        };

        var report = ReportGenerator.GenerateJson(config, aggregated, failures, phaseGate);
        Console.WriteLine(report);
        Console.Error.WriteLine($"\nReport based on {runs.Count} real benchmark run(s) across {aggregated.Count} task(s).");
        return 0;
    }

    // V7-W18: Benchmark Matrix — runs retrieval benchmark across all fixture repos
    private static int Matrix(string[] args)
    {
        var jsonOutput = HasFlag(args, "--json") || HasFlag(args, "--output=json");
        var repoRoot = GetOpt(args, "--repo-root") ?? FindRepoRoot();
        var language = GetOpt(args, "--lang"); // filter by language

        Console.Error.WriteLine("CacheHub Benchmark Matrix — Retrieval Mode");
        Console.Error.WriteLine($"  Repository root: {repoRoot}");

        var tasks = string.IsNullOrEmpty(language)
            ? BenchmarkTaskSet.Tasks
            : BenchmarkTaskSet.Tasks.Where(t => t.Language == language).ToList();

        Console.Error.WriteLine($"  Tasks: {tasks.Count}" + (language is not null ? $" (lang={language})" : ""));

        // V8-P0-04: Use real ContextEngine for retrieval (blind to Ground Truth)
        var matrixRunner = new BenchmarkMatrixRunner();
        var result = matrixRunner.RunRetrievalMatrix(
            task => GetFixtureFiles(repoRoot, task),
            (task, path) => ReadFixtureFile(repoRoot, task, path),
            (task, path) => ReadFixtureHash(repoRoot, task, path),
            task => BuildContextForMatrixTask(repoRoot, task),
            modelId: "retrieval-matrix",
            tasks: tasks);

        // Console report
        if (!jsonOutput)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(GenerateMatrixConsoleReport(result));
        }
        else
        {
            Console.WriteLine(JsonSerializer.Serialize(result, _jsonOpts));
        }

        // Persist result
        try
        {
            var appData = new AppDataDirectory();
            var benchDir = Path.Combine(appData.Root, "benchmarks");
            Directory.CreateDirectory(benchDir);
            var fileName = $"matrix-{DateTime.UtcNow:yyyyMMddHHmmss}.json";
            File.WriteAllText(Path.Combine(benchDir, fileName),
                JsonSerializer.Serialize(result, _jsonOpts));
            Console.Error.WriteLine($"  Saved: {Path.Combine(benchDir, fileName)}");
        }
        catch { }

        // V8-P0-05: Incomplete gate returns exit code 2 (distinct from Passed=0 and Failed=1)
        return result.PhaseGate.Status switch
        {
            MatrixGateStatus.Passed => 0,
            MatrixGateStatus.Failed => 1,
            MatrixGateStatus.Incomplete => 2,
            _ => 1,
        };
    }

    private static string FindRepoRoot()
    {
        var dir = Environment.CurrentDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "CacheHub.sln")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return Environment.CurrentDirectory;
    }

    private static List<MatrixFileInfo> GetFixtureFiles(string repoRoot, BenchmarkTask task)
    {
        var fixturePath = task.RepositoryPath ?? ".";
        var fullPath = Path.IsPathRooted(fixturePath)
            ? fixturePath
            : Path.Combine(repoRoot, fixturePath);

        if (!Directory.Exists(fullPath))
        {
            Console.Error.WriteLine($"  ⚠ Fixture not found: {fullPath} (task {task.Id})");
            return [];
        }

        var files = new List<MatrixFileInfo>();
        foreach (var file in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories))
        {
            if (file.Contains("\\.git\\", StringComparison.OrdinalIgnoreCase) ||
                file.Contains("/.git/", StringComparison.OrdinalIgnoreCase) ||
                file.Contains("\\node_modules\\", StringComparison.OrdinalIgnoreCase))
                continue;

            var relPath = Path.GetRelativePath(fullPath, file).Replace('\\', '/');
            var info = new FileInfo(file);
            var ext = info.Extension.ToLowerInvariant();
            var lang = ext switch
            {
                ".cs" => "csharp",
                ".ts" or ".tsx" => "typescript",
                ".js" => "javascript",
                ".py" => "python",
                ".go" => "go",
                ".rs" => "rust",
                ".md" => "markdown",
                _ => "text",
            };

            files.Add(new MatrixFileInfo
            {
                Path = relPath,
                NormalizedPath = relPath,
                Language = lang,
                Size = (int)info.Length,
            });
        }
        return files;
    }

    private static string ReadFixtureFile(string repoRoot, BenchmarkTask task, string relativePath)
    {
        var fixturePath = task.RepositoryPath ?? ".";
        var fullPath = Path.IsPathRooted(fixturePath)
            ? Path.Combine(fixturePath, relativePath.Replace('/', Path.DirectorySeparatorChar))
            : Path.Combine(repoRoot, fixturePath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(fullPath) ? File.ReadAllText(fullPath) : "";
    }

    private static string ReadFixtureHash(string repoRoot, BenchmarkTask task, string relativePath)
    {
        var content = ReadFixtureFile(repoRoot, task, relativePath);
        if (string.IsNullOrEmpty(content)) return "empty";
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant()[..16];
    }

    private static string GenerateMatrixConsoleReport(MatrixResult result)
    {
        var lines = new List<string>
        {
            "=== CacheHub Benchmark Matrix Report ===",
            $"Generated: {result.GeneratedAt:O}",
            $"Model: {result.ModelId}",
            $"Tasks: {result.Summary.TotalTasks}",
            "",
            $"  Mean File Recall@10:  {result.Summary.MeanFileRecallAt10,6:P2}",
            $"  Mean Token Reduction: {result.Summary.MeanTokenReduction,6:P2}",
        };

        if (result.Summary.CacheHubSuccessRate.HasValue)
        {
            lines.Add($"  CacheHub Success:     {result.Summary.CacheHubSuccessRate,6:P1}");
            lines.Add($"  Baseline Success:     {result.Summary.BaselineSuccessRate,6:P1}");
            lines.Add($"  Input Token Reduction:{result.Summary.MeanInputTokenReduction,6:P2}");
            lines.Add($"  Positive Token Tasks: {result.Summary.PositiveTokenTaskRatio,6:P1}");
        }

        lines.Add("");
        lines.Add($"  Phase Gate: {result.PhaseGate.Status.ToString().ToUpperInvariant()}" +
            (result.PhaseGate.Status == MatrixGateStatus.Incomplete ? " (no Agent data)" : ""));

        if (result.PhaseGate.FailedGates.Count > 0)
            lines.AddRange(result.PhaseGate.FailedGates.Select(g => $"    - {g}"));

        lines.Add("");
        lines.Add("=== Per-Task Results ===");
        lines.Add($"{"Task",-10} {"Lang",-5} {"Recall@10",-10} {"Tok.Red",-10} {"Files",-6}");
        lines.Add(new string('-', 50));

        foreach (var task in result.Tasks)
        {
            lines.Add($"{task.TaskId,-10} {task.Language,-5} {task.FileRecallAt10,8:P1}   {task.TokenReduction,8:P1}   {task.SelectedFileCount,-6}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// V8-P0-04: Builds context for a matrix task using the real ContextEngine.
    /// This callback is blind to Ground Truth — it only receives the task description and fixture files.
    /// </summary>
    private static Core.Context.ContextPackageManifest BuildContextForMatrixTask(string repoRoot, BenchmarkTask task)
    {
        var fixturePath = task.RepositoryPath ?? ".";
        var fullPath = Path.IsPathRooted(fixturePath)
            ? fixturePath
            : Path.Combine(repoRoot, fixturePath);

        var files = GetFixtureFiles(repoRoot, task);
        var indexedFiles = files.Select(f => new Context.Recall.IndexedFileInfo
        {
            Path = f.NormalizedPath,
            NormalizedPath = f.NormalizedPath,
            Language = f.Language,
            Size = f.Size,
            ContentHash = "sha256:pending",
        }).ToList();

        var tokenizers = Core.Tokens.TokenizerRegistry.CreateWithDefaults();
        var (secPolicy, _) = Core.Security.SecurityPolicyResolver.CreateEnforcer();
        var engine = new ContextEngine(tokenizers, secPolicy, cache: null);

        var snapshotId = Core.Identifiers.IndexSnapshotId.New();
        var manifest = engine.Build(
            new ContextBuildRequest
            {
                WorkspaceId = Core.Identifiers.WorkspaceId.New(),
                IndexSnapshotId = snapshotId,
                Task = task.TaskDescription,
            },
            () => indexedFiles,
            path => ReadFixtureFile(repoRoot, task, path),
            _ => "sha256:pending");

        return manifest;
    }

    private static string ResolveFileContent(string rootPath, string relativePath)
    {
        // V8-P0-03: Use SafePathResolver for consistent path security
        var fullPath = new CacheHub.Core.Paths.SafePathResolver(rootPath).ResolveFile(relativePath);
        return fullPath is not null ? File.ReadAllText(fullPath) : "";
    }

    private static void PersistRunResult(BenchmarkRunRecord run)
    {
        try
        {
            var appData = new AppDataDirectory();
            var benchDir = Path.Combine(appData.Root, "benchmarks");
            Directory.CreateDirectory(benchDir);
            var fileName = $"run-{run.TaskId}-{DateTime.UtcNow:yyyyMMddHHmmssfff}.json";
            var filePath = Path.Combine(benchDir, fileName);
            File.WriteAllText(filePath, JsonSerializer.Serialize(run, _jsonOpts));
        }
        catch { /* best effort — don't fail the benchmark if persistence fails */ }
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

    /// <summary>
    /// Checks if a patch contains substantive code changes (not just comments/TODOs/whitespace).
    /// A valid patch must have at least one line that looks like real code:
    /// assignment, function call, control flow, return, declaration, etc.
    /// </summary>
    private static bool ContainsSubstantiveCode(string patch)
    {
        var codeLines = patch.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !l.StartsWith("//", StringComparison.Ordinal) && !l.StartsWith('#') && !l.StartsWith("/*", StringComparison.Ordinal) && !l.StartsWith('*'))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (codeLines.Count == 0) return false;

        // At least one line must contain a code-like pattern
        return codeLines.Any(l =>
            l.Contains('=', StringComparison.Ordinal) ||
            l.Contains('(', StringComparison.Ordinal) ||
            l.Contains('{', StringComparison.Ordinal) ||
            l.Contains('}', StringComparison.Ordinal) ||
            l.StartsWith("return", StringComparison.Ordinal) ||
            l.StartsWith("if ", StringComparison.Ordinal) ||
            l.StartsWith("for ", StringComparison.Ordinal) ||
            l.StartsWith("while ", StringComparison.Ordinal) ||
            l.StartsWith("def ", StringComparison.Ordinal) ||
            l.StartsWith("func ", StringComparison.Ordinal) ||
            l.StartsWith("fn ", StringComparison.Ordinal) ||
            l.StartsWith("public ", StringComparison.Ordinal) ||
            l.StartsWith("private ", StringComparison.Ordinal) ||
            l.StartsWith("protected ", StringComparison.Ordinal) ||
            l.StartsWith("internal ", StringComparison.Ordinal) ||
            l.StartsWith("class ", StringComparison.Ordinal) ||
            l.StartsWith("struct ", StringComparison.Ordinal) ||
            l.StartsWith("enum ", StringComparison.Ordinal) ||
            l.StartsWith("interface ", StringComparison.Ordinal) ||
            l.StartsWith("import ", StringComparison.Ordinal) ||
            l.StartsWith("using ", StringComparison.Ordinal) ||
            l.StartsWith("export ", StringComparison.Ordinal) ||
            l.StartsWith("const ", StringComparison.Ordinal) ||
            l.StartsWith("let ", StringComparison.Ordinal) ||
            l.StartsWith("var ", StringComparison.Ordinal));
    }
}
