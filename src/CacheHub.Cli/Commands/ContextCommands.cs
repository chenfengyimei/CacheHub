using System.Text.Json;
using CacheHub.Context.Engine;
using CacheHub.Context.Export;
using CacheHub.Context.Payload;
using CacheHub.Context.Recall;
using CacheHub.Core.Context;
using CacheHub.Core.Feedback;
using CacheHub.Core.Identifiers;
using CacheHub.Core.Security;
using CacheHub.Core.Workspaces;
using CacheHub.Storage;
using CacheHub.Storage.Database;
using CacheHub.Storage.Database.Migrations;
using CacheHub.Storage.Query;
using CacheHub.Storage.Repositories;

namespace CacheHub.Cli.Commands;

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

        if (string.IsNullOrEmpty(wsId) || string.IsNullOrEmpty(task))
        {
            Console.Error.WriteLine("Error: --workspace=<id> and --task=<text> are required");
            return 1;
        }

        var (factory, workspace) = await ResolveWorkspaceAsync(wsId);
        if (workspace is null) return 1;

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

        // Build security policy from config
        Core.Security.SecurityPolicy? secPolicy = null;
        if (config.Security is not null)
        {
            secPolicy = new Core.Security.SecurityPolicy
            {
                Version = "config-v1",
                Mode = config.Security.Mode,
                EnableSecretScan = config.Security.EnableSecretScan,
                BlockedExtensions = config.Security.BlockedExtensions is not null
                    ? new HashSet<string>(config.Security.BlockedExtensions, StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            };
        }

        // Build tokenizer registry
        var tokenizers = new Core.Tokens.TokenizerRegistry();

        var engine = new ContextEngine(tokenizers, secPolicy);

        // Query the real Active Snapshot from the database (not a random ID)
        var activeSnapshot = await GetActiveSnapshotAsync(factory, workspace.Id.Value);
        if (activeSnapshot is null)
        {
            Console.Error.WriteLine("Error: No active index snapshot found. Run 'cachehub index build --id=<workspace-id>' first.");
            return 1;
        }

        var snapshotId = activeSnapshot.Value.SnapshotId;
        var indexedFiles = await GetIndexedFilesAsync(factory, wsId, snapshotId);

        var manifest = engine.Build(
            new ContextBuildRequest
            {
                WorkspaceId = workspace.Id,
                IndexSnapshotId = snapshotId,
                Task = task,
                GitDiffFiles = gitDiffFiles,
                ModelId = effectiveModel,
            },
            () => indexedFiles,
            path => ResolveFileContent(workspace.RootPath, path),
            path => ResolveFileHash(factory, snapshotId, path, workspace.RootPath),
            ftsSearch: keyword =>
            {
                var querySvc = new SqliteIndexQueryService(factory);
                var results = querySvc.SearchFtsAsync(snapshotId, keyword, 50).GetAwaiter().GetResult();
                return results.Select(r => new FtsMatch(r.Path, r.Language, r.Snippet)).ToList();
            },
            symbolSearch: symbol =>
            {
                var querySvc = new SqliteIndexQueryService(factory);
                var results = querySvc.SearchSymbolsAsync(snapshotId, symbol).GetAwaiter().GetResult();
                return results.Select(r => r.NormalizedPath).ToList();
            },
            importSearch: symbol =>
            {
                var querySvc = new SqliteIndexQueryService(factory);
                var results = querySvc.GetFilesByImportedSymbolAsync(snapshotId, symbol).GetAwaiter().GetResult();
                return results.ToList();
            },
            symbolSearchDetailed: symbol =>
            {
                var querySvc = new SqliteIndexQueryService(factory);
                var results = querySvc.SearchSymbolsAsync(snapshotId, symbol).GetAwaiter().GetResult();
                return results.Select(r => new SymbolHit
                {
                    NormalizedPath = r.NormalizedPath,
                    Name = r.Name,
                    Kind = r.Kind,
                    StartLine = r.StartLine,
                    EndLine = r.EndLine,
                    ExactMatch = r.ExactMatch,
                }).ToList();
            });

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
            var enforcer = new SecurityPolicyEnforcer();
            var exportDir = await exporter.ExportAsync(
                manifest,
                path => ResolveFileContent(ws.RootPath, path),
                manifest.WorkspaceId.Value,
                enforcer);

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
            var enforcer = new SecurityPolicyEnforcer();
            var markdown = generator.GenerateMarkdown(manifest, path => ResolveFileContent(ws.RootPath, path), enforcer);
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

    private static async Task<(IndexSnapshotId SnapshotId, int FileCount)?> GetActiveSnapshotAsync(SqliteConnectionFactory factory, string workspaceId)
    {
        await using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, file_count FROM index_snapshots WHERE workspace_id = $ws AND status = 'Active' LIMIT 1;";
        cmd.Parameters.AddWithValue("$ws", workspaceId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
            return (IndexSnapshotId.Parse(reader.GetString(0)), reader.GetInt32(1));
        return null;
    }

    private static string ResolveFileHash(SqliteConnectionFactory factory, IndexSnapshotId snapshotId, string path, string? rootPath = null)
    {
        // Try to read the hash from the files table first
        using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT content_hash FROM files WHERE snapshot_id = $snap AND normalized_path = $path LIMIT 1;";
        cmd.Parameters.AddWithValue("$snap", snapshotId.Value);
        cmd.Parameters.AddWithValue("$path", path);
        var result = cmd.ExecuteScalar();
        if (result is string hash && !string.IsNullOrEmpty(hash) && hash != "pending" && !hash.StartsWith("fp:", StringComparison.Ordinal))
            return hash;

        // DB hash is pending/fingerprint: compute real SHA-256 from file on disk
        if (rootPath is not null)
        {
            var fullPath = System.IO.Path.Combine(rootPath, path.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(fullPath))
            {
                try
                {
                    return CacheHub.Indexing.Hashing.FileHasher.ComputeFullHashAsync(fullPath).GetAwaiter().GetResult();
                }
                catch { /* fall through to pending */ }
            }
        }
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
}
