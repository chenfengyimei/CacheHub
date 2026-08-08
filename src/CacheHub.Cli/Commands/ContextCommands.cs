using System.Text.Json;
using CacheHub.Context.Cache;
using CacheHub.Context.Engine;
using CacheHub.Context.Export;
using CacheHub.Context.Payload;
using CacheHub.Context.Recall;  // V7-W13: RecallWiringFactory + RecallCallbacks
using CacheHub.Core.Context;
using CacheHub.Core.Feedback;
using CacheHub.Core.Identifiers;
using CacheHub.Core.Semantic;
using CacheHub.Core.Security;
using CacheHub.Core.Workspaces;
using CacheHub.Storage;
using CacheHub.Storage.Database;
using CacheHub.Storage.Database.Migrations;
using CacheHub.Storage.Query;
using CacheHub.Storage.Repositories;

namespace CacheHub.Cli.Commands;

/// <summary>
/// Helper for wiring SemanticReferenceRecall into ContextEngine's semanticSearch callback.
/// Reference-only: provides historical task/error/feedback context, does not reuse model answers.
/// </summary>
internal static class SemanticReferenceHelper
{
    private static readonly LocalHashEmbeddingProvider _embedding = new();
    private static PersistentVectorStore? _store;

    /// <summary>
    /// Gets or creates a persistent vector store at the app data path.
    /// </summary>
    private static PersistentVectorStore GetStore(string appDataRoot)
    {
        if (_store is not null) return _store;
        var dir = Path.Combine(appDataRoot, "semantic");
        Directory.CreateDirectory(dir);
        _store = new PersistentVectorStore(Path.Combine(dir, "references.json"));
        return _store;
    }

    /// <summary>
    /// Creates a semanticSearch callback for ContextEngine.Build.
    /// Returns null if no references exist (engine will skip SemanticRecallSource).
    /// </summary>
    public static Func<string, IReadOnlyList<SemanticHit>>? CreateSemanticSearch(
        string appDataRoot, string? workspaceId = null)
    {
        var store = GetStore(appDataRoot);
        if (store.Count == 0) return null;

        return queryText =>
        {
            var recall = new SemanticReferenceRecall(store, _embedding);
            var results = recall.RecallAsync(queryText, workspaceId, topK: 5, minSimilarity: 0.1)
                .GetAwaiter().GetResult();
            return results.Select(r => new SemanticHit
            {
                Content = r.Reference.Content,
                Similarity = r.Similarity,
                ReferenceType = r.Reference.Type.ToString(),
                TaskDescription = r.Reference.TaskDescription,
                HistoricalFiles = [.. r.Reference.SelectedFiles, .. r.Reference.FilesActuallyRead],
            }).ToList();
        };
    }

    /// <summary>
    /// Records a task reference for future semantic recall.
    /// V6: Now stores selectedFiles and filesActuallyRead for direct file recall.
    /// </summary>
    public static void RecordReference(
        string appDataRoot, string content, string? workspaceId,
        string? taskDescription, string? snapshotId,
        IReadOnlyList<string>? selectedFiles = null,
        IReadOnlyList<string>? filesActuallyRead = null,
        bool? taskCompleted = null)
    {
        var store = GetStore(appDataRoot);
        var recall = new SemanticReferenceRecall(store, _embedding);
        recall.RecordAsync(content, SemanticReferenceType.Task,
            workspaceId, taskDescription, taskCompleted,
            snapshotId, workspaceContentHash: null,
            selectedFiles, filesActuallyRead).GetAwaiter().GetResult();
    }
}

public static class ContextCommands
{
    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    public static async Task<int> HandleAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: cachehub context <build|inspect|list|export|expand|feedback> [options]");
            return 1;
        }

        return args[0] switch
        {
            "build" => await BuildAsync(args.AsSpan(1).ToArray()),
            "inspect" => await InspectAsync(args.AsSpan(1).ToArray()),
            "list" => await ListAsync(args.AsSpan(1).ToArray()),
            "export" => await ExportAsync(args.AsSpan(1).ToArray()),
            "expand" => await ExpandAsync(args.AsSpan(1).ToArray()),
            "feedback" => await FeedbackAsync(args.AsSpan(1).ToArray()),
            _ => 1,
        };
    }

    private static async Task<int> BuildAsync(string[] args)
    {
        var wsId = GetOpt(args, "--workspace");
        var task = GetOpt(args, "--task");
        var outputJson = HasFlag(args, "--output=json") || HasFlag(args, "--json");
        var useGitDiff = HasFlag(args, "--git-diff");
        var modelId = GetOpt(args, "--model");
        var allowStale = HasFlag(args, "--allow-stale"); // V8-P0-01: explicit opt-in for stale context

        if (string.IsNullOrEmpty(wsId) || string.IsNullOrEmpty(task))
        {
            Console.Error.WriteLine("Error: --workspace=<id> and --task=<text> are required");
            return 1;
        }

        var (factory, workspace) = await ResolveWorkspaceAsync(wsId);
        if (workspace is null) return 1;

        var appData = new AppDataDirectory();

        Console.Error.WriteLine($"Building context for: {workspace.Name}");
        Console.Error.WriteLine($"  Task: {task}");

        // Load config for default model
        var config = new Core.Configuration.ConfigManager().Load();
        var effectiveModel = modelId ?? config.DefaultModel;

        // Git diff integration
        List<string>? gitDiffFiles = null;
        if (useGitDiff)
        {
            var gitDiff = new Context.Integration.GitDiffProvider();
            gitDiffFiles = (await gitDiff.GetChangedFilesAsync(workspace.RootPath)).ToList();
            Console.Error.WriteLine($"  Git diff files: {gitDiffFiles.Count}");
        }

        // V5-W02 (P0): Use unified SecurityPolicyResolver for consistent policy across all entry points
        var secPolicy = Core.Security.SecurityPolicyResolver.Resolve(new Core.Configuration.ConfigManager());

        // Build tokenizer registry
        var tokenizers = Core.Tokens.TokenizerRegistry.CreateWithDefaults();
        var cache = CreateContextCache(factory);

        var engine = new ContextEngine(tokenizers, secPolicy, cache);

        // Query the real Active Snapshot from the database (not a random ID)
        var activeSnapshot = await GetActiveSnapshotAsync(factory, workspace.Id.Value);
        if (activeSnapshot is null)
        {
            Console.Error.WriteLine("Error: No active index snapshot found. Run 'cachehub index build --id=<workspace-id>' first.");
            return 1;
        }

        var snapshotId = activeSnapshot.Value.SnapshotId;
        var indexedFiles = await GetIndexedFilesAsync(factory, wsId, snapshotId);

        // V7-W02 / V8-P0-01: Stale detection — default: reject build when stale
        // V8-P1-01: Pass fileFilter so fingerprint scope = index scope (excludes node_modules/bin/obj)
        var staleResult = await CacheHub.Core.Indexing.StaleDetector.CheckAsync(
            workspace.RootPath, activeSnapshot.Value.WorkspaceFingerprint,
            fileFilter: CreateFingerprintFilter(workspace.RootPath));
        if (!staleResult.IsFresh)
        {
            if (!allowStale)
            {
                // V8-P0-01: Default behavior — reject build to prevent stale context
                Console.Error.WriteLine($"  ✗ CONTEXT_STALE: {staleResult.Message}");
                Console.Error.WriteLine("  Run 'cachehub index refresh --id=<workspace-id>' to update the index.");
                Console.Error.WriteLine("  Or use --allow-stale to override (stale cache will be skipped).");
                return 3; // distinct exit code for stale context
            }
            // --allow-stale: warn but proceed, cache will be skipped
            Console.Error.WriteLine($"  ⚠ WARNING (stale, --allow-stale): {staleResult.Message}");
            Console.Error.WriteLine("  Cache lookup will be skipped for this build.");
        }
        else if (staleResult.NoFingerprint)
        {
            Console.Error.WriteLine($"  ℹ {staleResult.Message}");
        }

        // V7-W13: Use RecallWiringFactory for standard callbacks (FTS/Symbol/Import/SymbolDetailed/Relation/ReverseRelation/FileSymbols)
        var recallFactory = new RecallWiringFactory(factory);
        var callbacks = recallFactory.Create(snapshotId);

        var manifest = engine.Build(
            new ContextBuildRequest
            {
                WorkspaceId = workspace.Id,
                IndexSnapshotId = snapshotId,
                Task = task,
                GitDiffFiles = gitDiffFiles,
                ModelId = effectiveModel,
                SecurityPolicyVersion = secPolicy.Version,
                RepositoryCommit = activeSnapshot.Value.RepositoryCommit,
                Branch = activeSnapshot.Value.Branch,
                IsDirty = activeSnapshot.Value.IsDirty,
                WorkspaceFingerprint = activeSnapshot.Value.WorkspaceFingerprint,
                CurrentWorkspaceFingerprint = staleResult.CurrentFingerprint, // V8-P0-01: real-time fingerprint for cache key
                AllowStale = allowStale, // V8-P0-01: explicit stale opt-in
            },
            () => indexedFiles,
            path => ResolveFileContent(workspace.RootPath, path),
            path => ResolveFileHash(factory, snapshotId, path, workspace.RootPath),
            ftsSearch: callbacks.FtsSearch,
            symbolSearch: callbacks.SymbolSearch,
            importSearch: callbacks.ImportSearch,
            symbolSearchDetailed: callbacks.SymbolSearchDetailed,
            relationSearch: callbacks.RelationSearch,
            semanticSearch: SemanticReferenceHelper.CreateSemanticSearch(appData.Root, workspace.Id.Value),
            reverseRelationSearch: callbacks.ReverseRelationSearch,
            fileSymbolsProvider: callbacks.FileSymbolsProvider);

        // Persist manifest
        var ctxRepo = new SqliteContextPackageRepository(factory);
        await ctxRepo.SaveAsync(manifest);
        Console.Error.WriteLine($"  Saved: {manifest.Id.Value}");

        if (outputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(manifest, _jsonOpts));
        }
        else
        {
            Console.WriteLine($"Context Package: {manifest.Id.Value}");
            Console.WriteLine($"  Schema: v{manifest.SchemaVersion}");
            Console.WriteLine($"  Task: {manifest.Task.OriginalText}");
            Console.WriteLine($"  Ranking: {manifest.Ranking.ProfileId} v{manifest.Ranking.ProfileVersion}");
            Console.WriteLine($"  Budget: {manifest.Budget.ActualEstimate} / {manifest.Budget.ContextTarget} (hard: {manifest.Budget.ContextHardLimit})");
            Console.WriteLine($"  Selected ({manifest.SelectedFiles.Count}):");
            foreach (var f in manifest.SelectedFiles)
                Console.WriteLine($"    [{f.Mode,-20}] {f.Score:F2}  {f.Path}");
            if (manifest.ExcludedCandidates.Count > 0)
            {
                Console.WriteLine($"  Excluded ({manifest.ExcludedCandidates.Count}):");
                foreach (var e in manifest.ExcludedCandidates)
                    Console.WriteLine($"    [{e.Score:F2}] {e.Path} — {e.Reason}");
            }
        }

        return 0;
    }

    private static async Task<int> InspectAsync(string[] args)
    {
        var ctxId = GetOpt(args, "--id");
        var outputJson = HasFlag(args, "--output=json") || HasFlag(args, "--json");
        if (string.IsNullOrEmpty(ctxId))
        {
            Console.Error.WriteLine("Error: --id=<context-id> is required");
            return 1;
        }

        var appData = new AppDataDirectory();
        var dbPath = appData.GetWorkspaceDatabasePath("main");
        var factory = new SqliteConnectionFactory(dbPath);
        var repo = new SqliteContextPackageRepository(factory);
        var manifest = await repo.FindByIdAsync(ContextPackageId.Parse(ctxId));

        if (manifest is null)
        {
            Console.Error.WriteLine($"Context package not found: {ctxId}");
            return 1;
        }

        if (outputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(manifest, _jsonOpts));
        }
        else
        {
            Console.WriteLine($"Context Package: {manifest.Id.Value}");
            Console.WriteLine($"  Schema: v{manifest.SchemaVersion}");
            Console.WriteLine($"  Task: {manifest.Task.OriginalText}");
            Console.WriteLine($"  Ranking: {manifest.Ranking.ProfileId} v{manifest.Ranking.ProfileVersion}");
            Console.WriteLine($"  Budget: {manifest.Budget.ActualEstimate} / {manifest.Budget.ContextTarget}");
            Console.WriteLine($"  Engine: {manifest.ContextEngineVersion}");
            Console.WriteLine($"  Created: {manifest.CreatedAt:O}");
            Console.WriteLine($"  CloudSend: {manifest.Safety.CloudSendAllowed}");
            Console.WriteLine($"  SecretsScan: {manifest.Safety.SecretsScanPassed}");
        }

        return 0;
    }

    private static async Task<int> ListAsync(string[] args)
    {
        var wsId = GetOpt(args, "--workspace");
        var outputJson = HasFlag(args, "--output=json") || HasFlag(args, "--json");
        if (string.IsNullOrEmpty(wsId))
        {
            Console.Error.WriteLine("Error: --workspace=<id> is required");
            return 1;
        }

        var appData = new AppDataDirectory();
        var dbPath = appData.GetWorkspaceDatabasePath("main");
        var factory = new SqliteConnectionFactory(dbPath);
        var repo = new SqliteContextPackageRepository(factory);
        var list = await repo.ListByWorkspaceAsync(WorkspaceId.Parse(wsId));

        if (outputJson)
        {
            var json = JsonSerializer.Serialize(list.Select(m => new
            {
                id = m.Id.Value,
                task = m.Task.OriginalText,
                budget = m.Budget.ActualEstimate,
                engine = m.ContextEngineVersion,
                createdAt = m.CreatedAt,
            }), _jsonOpts);
            Console.WriteLine(json);
        }
        else
        {
            if (list.Count == 0)
            {
                Console.WriteLine("No context packages found.");
                return 0;
            }

            Console.WriteLine($"{"ID",-36}  {"Tokens",-8}  {"Engine",-10}  {"Created"}");
            Console.WriteLine(new string('-', 90));
            foreach (var m in list)
            {
                Console.WriteLine($"{m.Id.Value,-36}  {m.Budget.ActualEstimate,-8}  {m.ContextEngineVersion,-10}  {m.CreatedAt:yyyy-MM-dd HH:mm}");
            }
        }

        return 0;
    }

    private static async Task<int> ExportAsync(string[] args)
    {
        var ctxId = GetOpt(args, "--id");
        var format = GetOpt(args, "--format") ?? "markdown";
        var outputJson = HasFlag(args, "--output=json") || HasFlag(args, "--json");
        if (string.IsNullOrEmpty(ctxId))
        {
            Console.Error.WriteLine("Error: --id=<context-id> is required");
            return 1;
        }

        var appData = new AppDataDirectory();
        var dbPath = appData.GetWorkspaceDatabasePath("main");
        var factory = new SqliteConnectionFactory(dbPath);
        var repo = new SqliteContextPackageRepository(factory);
        var manifest = await repo.FindByIdAsync(ContextPackageId.Parse(ctxId));

        if (manifest is null)
        {
            Console.Error.WriteLine($"Context package not found: {ctxId}");
            return 1;
        }

        var wsRepo = new SqliteWorkspaceRepository(factory);
        var ws = await wsRepo.FindByIdAsync(manifest.WorkspaceId);

        if (format == "json" || outputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(manifest, _jsonOpts));
        }
        else if (format == "file")
        {
            // Export to .cachehub/ directory using FileExporter
            if (ws is null)
            {
                Console.Error.WriteLine("Workspace not found for file export");
                return 1;
            }

            var exportAppData = new AppDataDirectory();
            var exporter = new FileExporter(exportAppData);
            // V7-W06: Use SecurityPolicyResolver.CreateEnforcer instead of new SecurityPolicyEnforcer()
            var (_, exportEnforcer) = SecurityPolicyResolver.CreateEnforcer();
            var exportDir = await exporter.ExportAsync(
                manifest,
                path => ResolveFileContent(ws.RootPath, path),
                manifest.WorkspaceId.Value,
                exportEnforcer);

            Console.Error.WriteLine($"Exported to: {exportDir}");
            Console.WriteLine($"{{ \"exportDir\": \"{exportDir.Replace('\\', '/')}\", \"files\": [\"workspace.json\", \"latest-context.manifest.json\", \"latest-context.md\", \"repomap.md\"] }}");
        }
        else
        {
            // Markdown to stdout using PayloadGenerator
            if (ws is null)
            {
                Console.Error.WriteLine("Workspace not found for markdown export");
                return 1;
            }
            var generator = new PayloadGenerator();
            // V7-W06: Use SecurityPolicyResolver.CreateEnforcer instead of new SecurityPolicyEnforcer()
            var (_, mdEnforcer) = SecurityPolicyResolver.CreateEnforcer();
            var markdown = generator.GenerateMarkdown(manifest, path => ResolveFileContent(ws.RootPath, path), mdEnforcer);
            Console.WriteLine(markdown);
        }

        return 0;
    }

    private static async Task<int> ExpandAsync(string[] args)
    {
        var ctxId = GetOpt(args, "--id");
        var symbol = GetOpt(args, "--symbol");
        var file = GetOpt(args, "--file");
        var reason = GetOpt(args, "--reason");
        var outputJson = HasFlag(args, "--output=json") || HasFlag(args, "--json");

        if (string.IsNullOrEmpty(ctxId))
        {
            Console.Error.WriteLine("Error: --id=<context-id> is required");
            return 1;
        }

        if (string.IsNullOrEmpty(symbol) && string.IsNullOrEmpty(file))
        {
            Console.Error.WriteLine("Error: --symbol=<name> or --file=<path> is required");
            return 1;
        }

        var appData = new AppDataDirectory();
        var dbPath = appData.GetWorkspaceDatabasePath("main");
        var factory = new SqliteConnectionFactory(dbPath);
        var ctxRepo = new SqliteContextPackageRepository(factory);
        var wsRepo = new SqliteWorkspaceRepository(factory);

        var manifest = await ctxRepo.FindByIdAsync(ContextPackageId.Parse(ctxId));
        if (manifest is null)
        {
            Console.Error.WriteLine($"Context package not found: {ctxId}");
            return 1;
        }

        var ws = await wsRepo.FindByIdAsync(manifest.WorkspaceId);
        if (ws is null)
        {
            Console.Error.WriteLine("Workspace not found");
            return 1;
        }

        var expander = new Context.Expand.ContextExpander();

        // Handle --symbol: search file_symbols table, NOT treat as file path
        if (!string.IsNullOrEmpty(symbol) && string.IsNullOrEmpty(file))
        {
            // Search for the symbol in the index
            var symbolMatches = await SearchSymbolsAsync(factory, manifest.IndexSnapshotId, symbol);
            if (symbolMatches.Count == 0)
            {
                Console.Error.WriteLine($"Symbol not found in index: {symbol}");
                return 1;
            }

            var symbolResult = expander.ExpandBySymbol(
                ctxId,
                symbolMatches,
                path => ResolveFileContent(ws.RootPath, path),
                reason ?? $"Symbol: {symbol}");

            if (outputJson)
            {
                Console.WriteLine(JsonSerializer.Serialize(symbolResult, _jsonOpts));
            }
            else
            {
                Console.WriteLine($"Expansion for: {ctxId}");
                Console.WriteLine($"  Type: symbol");
                Console.WriteLine($"  Symbol: {symbol}");
                Console.WriteLine($"  Matches: {symbolMatches.Count} files");
                Console.WriteLine($"  Tokens: {symbolResult.AdditionalTokens}");
                Console.WriteLine($"  Reason: {symbolResult.Reason}");
                Console.WriteLine($"  Items: {symbolResult.AddedItems.Count}");
            }
            return 0;
        }

        // Handle --file (existing behavior)
        var targetFile = file!;

        // Resolve path safely without reading file content
        if (targetFile.Contains(".."))
        {
            Console.Error.WriteLine($"Error: Invalid file path: {targetFile}");
            return 1;
        }
        var fullPath = Path.GetFullPath(Path.Combine(ws.RootPath, targetFile.Replace('/', Path.DirectorySeparatorChar)));
        var normalizedRoot = Path.GetFullPath(ws.RootPath);
        if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"Error: Path outside workspace root: {targetFile}");
            return 1;
        }

        if (!File.Exists(fullPath))
        {
            Console.Error.WriteLine($"File not found: {targetFile}");
            return 1;
        }

        var content = await File.ReadAllTextAsync(fullPath);
        var result = expander.ExpandByFile(ctxId, targetFile, content, reason ?? $"Expanded: {symbol ?? file}");

        if (outputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, _jsonOpts));
        }
        else
        {
            Console.WriteLine($"Expansion for: {ctxId}");
            Console.WriteLine($"  File: {targetFile}");
            Console.WriteLine($"  Tokens: {result.AdditionalTokens}");
            Console.WriteLine($"  Reason: {result.Reason}");
            Console.WriteLine($"  Items: {result.AddedItems.Count}");
        }

        return 0;
    }

    private static async Task<int> FeedbackAsync(string[] args)
    {
        var ctxId = GetOpt(args, "--id");
        var filePath = GetOpt(args, "--file");
        if (string.IsNullOrEmpty(ctxId) || string.IsNullOrEmpty(filePath))
        {
            Console.Error.WriteLine("Error: --id=<context-id> and --file=<path> are required");
            return 1;
        }

        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"Error: File not found: {filePath}");
            return 1;
        }

        var json = await File.ReadAllTextAsync(filePath);
        var feedback = ContextFeedback.ParseJson(json);
        if (feedback is null)
        {
            Console.Error.WriteLine("Error: Invalid feedback JSON");
            return 1;
        }

        // R3-W008: Validate that CLI --id matches feedback JSON's ContextPackageId
        if (!string.IsNullOrEmpty(feedback.ContextPackageId) &&
            !string.Equals(ctxId, feedback.ContextPackageId, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"Error: --id={ctxId} does not match feedback JSON ContextPackageId={feedback.ContextPackageId}");
            return 1;
        }

        // Validate that the context package exists
        var appData = new AppDataDirectory();
        var dbPath = appData.GetWorkspaceDatabasePath("main");
        var factory = new SqliteConnectionFactory(dbPath);
        var ctxRepo = new SqliteContextPackageRepository(factory);
        var manifest = await ctxRepo.FindByIdAsync(ContextPackageId.Parse(ctxId));
        if (manifest is null)
        {
            Console.Error.WriteLine($"Error: Context package not found: {ctxId}");
            return 1;
        }

        // Persist feedback
        var feedbackRepo = new SqliteFeedbackRepository(factory);
        await feedbackRepo.SaveAsync(feedback);

        // V5-W09: Record semantic reference on successful task completion
        if (feedback.TaskCompleted)
        {
            try
            {
                SemanticReferenceHelper.RecordReference(
                    appData.Root,
                    manifest.Task.OriginalText,
                    manifest.WorkspaceId.Value,
                    manifest.Task.OriginalText,
                    manifest.IndexSnapshotId.Value,
                    selectedFiles: manifest.SelectedFiles.Select(f => f.Path).ToList(),
                    filesActuallyRead: feedback.FilesActuallyRead,
                    taskCompleted: true);
                Console.Error.WriteLine("  Semantic reference recorded for future recall.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  Warning: Failed to record semantic reference: {ex.Message}");
            }
        }

        Console.Error.WriteLine($"Feedback saved for context: {ctxId}");
        Console.Error.WriteLine($"  Workspace: {manifest.WorkspaceId.Value}");
        Console.Error.WriteLine($"  Client: {feedback.ClientId ?? "unknown"}");
        Console.Error.WriteLine($"  Files read: {feedback.FilesActuallyRead.Count}");
        Console.Error.WriteLine($"  Task completed: {feedback.TaskCompleted}");
        Console.Error.WriteLine($"  Missing context: {feedback.MissingContextReported}");
        Console.WriteLine($"{{ \"received\": true, \"contextId\": \"{ctxId}\", \"workspaceId\": \"{manifest.WorkspaceId.Value}\" }}");
        return 0;
    }

    private static async Task<(SqliteConnectionFactory factory, Workspace? workspace)> ResolveWorkspaceAsync(string wsId)
    {
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

        var repo = new SqliteWorkspaceRepository(factory);
        var ws = await repo.FindByIdAsync(WorkspaceId.Parse(wsId));
        if (ws is null)
            Console.Error.WriteLine($"Workspace not found: {wsId}");

        return (factory, ws);
    }

    private static async Task<List<IndexedFileInfo>> GetIndexedFilesAsync(SqliteConnectionFactory factory, string workspaceId, IndexSnapshotId snapshotId)
    {
        var result = new List<IndexedFileInfo>();
        await using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT f.normalized_path, f.size, f.language, f.content_hash
            FROM files f
            WHERE f.snapshot_id = $snap;
            """;
        cmd.Parameters.AddWithValue("$snap", snapshotId.Value);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new IndexedFileInfo
            {
                Path = reader.GetString(0),
                NormalizedPath = reader.GetString(0),
                Language = reader.IsDBNull(2) ? "unknown" : reader.GetString(2),
                Size = reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
                Symbols = [],
                ContentHash = reader.IsDBNull(3) ? null : reader.GetString(3),
            });
        }
        return result;
    }

    private static async Task<(IndexSnapshotId SnapshotId, int FileCount, string? RepositoryCommit, string? Branch, bool IsDirty, string? WorkspaceFingerprint)?> GetActiveSnapshotAsync(SqliteConnectionFactory factory, string workspaceId)
    {
        await using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, file_count, repository_commit, branch, is_dirty, workspace_fingerprint FROM index_snapshots WHERE workspace_id = $ws AND status IN ('Active', 'ActiveDegraded') LIMIT 1;";
        cmd.Parameters.AddWithValue("$ws", workspaceId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var commit = reader.IsDBNull(2) ? null : reader.GetString(2);
            var branch = reader.IsDBNull(3) ? null : reader.GetString(3);
            var isDirty = !reader.IsDBNull(4) && reader.GetBoolean(4);
            var fingerprint = reader.IsDBNull(5) ? null : reader.GetString(5);
            return (IndexSnapshotId.Parse(reader.GetString(0)), reader.GetInt32(1), commit, branch, isDirty, fingerprint);
        }
        return null;
    }

    private static string ResolveFileHash(SqliteConnectionFactory factory, IndexSnapshotId snapshotId, string path, string? rootPath = null)
    {
        // V7-W22: Always compute hash from disk first when file exists.
        // This ensures ContentHash matches the actual content being read from disk,
        // preventing "old Hash + new Content" inconsistency in Context Package.
        // DB hash is only used as fallback when the file no longer exists on disk.
        if (rootPath is not null)
        {
            var fullPath = System.IO.Path.Combine(rootPath, path.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(fullPath))
            {
                try
                {
                    return CacheHub.Indexing.Hashing.FileHasher.ComputeFullHashAsync(fullPath).GetAwaiter().GetResult();
                }
                catch { /* fall through to DB hash */ }
            }
        }

        // Fallback: read hash from files table (file may have been deleted from disk)
        using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT content_hash FROM files WHERE snapshot_id = $snap AND normalized_path = $path LIMIT 1;";
        cmd.Parameters.AddWithValue("$snap", snapshotId.Value);
        cmd.Parameters.AddWithValue("$path", path);
        var result = cmd.ExecuteScalar();
        if (result is string hash && !string.IsNullOrEmpty(hash) && hash != "pending" && !hash.StartsWith("fp:", StringComparison.Ordinal))
            return hash;

        return "sha256:pending";
    }

    private static async Task<List<Context.Expand.SymbolMatch>> SearchSymbolsAsync(SqliteConnectionFactory factory, IndexSnapshotId snapshotId, string symbolName)
    {
        var results = new List<Context.Expand.SymbolMatch>();
        await using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT s.name, s.kind, s.start_line, s.end_line, f.normalized_path
            FROM file_symbols s
            INNER JOIN files f ON s.file_id = f.id
            WHERE s.snapshot_id = $snap AND s.name LIKE '%' || $name || '%'
            ORDER BY s.start_line
            LIMIT 20;
            """;
        cmd.Parameters.AddWithValue("$snap", snapshotId.Value);
        cmd.Parameters.AddWithValue("$name", symbolName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new Context.Expand.SymbolMatch
            {
                SymbolName = reader.GetString(0),
                SymbolKind = reader.GetString(1),
                StartLine = reader.GetInt32(2),
                EndLine = reader.GetInt32(3),
                FilePath = reader.GetString(4),
            });
        }
        return results;
    }

    private static string ResolveFileContent(string rootPath, string relativePath)
    {
        if (relativePath.Contains("..")) return "";
        var fullPath = Path.GetFullPath(Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var normalizedRoot = Path.GetFullPath(rootPath);
        if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)) return "";
        return File.Exists(fullPath) ? File.ReadAllText(fullPath) : "";
    }

    private static string? GetOpt(string[] args, string prefix) =>
        args.FirstOrDefault(a => a.StartsWith(prefix + "=", StringComparison.OrdinalIgnoreCase))?[(prefix.Length + 1)..];

    private static bool HasFlag(string[] args, string flag) =>
        args.Contains(flag, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// V8-P1-01: Creates a file filter for fingerprint scope.
    /// Only indexed files (not ignored, not binary) participate in the workspace fingerprint.
    /// This prevents node_modules/bin/obj changes from causing unnecessary stale detection.
    /// </summary>
    internal static Func<string, bool> CreateFingerprintFilter(string workspaceRoot)
    {
        var ignoreEngine = new CacheHub.Indexing.IgnoreRules.IgnoreRuleEngine()
            .WithDefaults()
            .WithGitIgnore(Path.Combine(workspaceRoot, ".gitignore"))
            .WithCacheHubIgnore(Path.Combine(workspaceRoot, ".cachehubignore"));

        return relativePath =>
        {
            if (ignoreEngine.IsIgnored(relativePath)) return false;
            // Check if the file type should be indexed
            var fullPath = Path.Combine(workspaceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var typeInfo = CacheHub.Indexing.Detection.FileTypeDetector.Detect(fullPath, new FileInfo(fullPath).Length);
            return typeInfo.ShouldIndex;
        };
    }

    /// <summary>
    /// Creates a ContextPackageCache with persistent SQLite backend when available.
    /// Survives CLI process restart: context build on the same workspace+task
    /// hits cache without re-running the full Recall→Ranking→Selection pipeline.
    /// </summary>
    internal static ContextPackageCache CreateContextCache(SqliteConnectionFactory workspaceFactory)
    {
        try
        {
            var appData = new AppDataDirectory();
            var cacheDbPath = Path.Combine(appData.Root, "context-cache", "cache.db");
            Directory.CreateDirectory(Path.GetDirectoryName(cacheDbPath)!);
            var cacheFactory = new SqliteConnectionFactory(cacheDbPath);
            var runner = new MigrationRunner(cacheFactory, cacheDbPath,
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
            var store = new CacheHub.Storage.Caching.SqliteCacheStore(cacheFactory,
                Path.Combine(appData.Root, "context-cache", "blobs"));
            return new ContextPackageCache(store);
        }
        catch
        {
            // Fallback to in-memory if SQLite cache can't be initialized
            return new ContextPackageCache();
        }
    }
}
