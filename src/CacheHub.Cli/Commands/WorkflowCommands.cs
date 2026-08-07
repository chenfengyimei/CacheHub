using System.Text.Json;
using CacheHub.Context.Cache;
using CacheHub.Context.Engine;
using CacheHub.Context.Payload;
using CacheHub.Core.Context;
using CacheHub.Core.Identifiers;
using CacheHub.Core.Configuration;
using CacheHub.Core.Security;
using CacheHub.Core.Tokens;
using CacheHub.Core.Workflow;
using CacheHub.Storage;
using CacheHub.Storage.Database;
using CacheHub.Storage.Database.Migrations;
using CacheHub.Storage.Query;
using CacheHub.Storage.Repositories;
using Microsoft.Data.Sqlite;

namespace CacheHub.Cli.Commands;

/// <summary>
/// Handles `cachehub workflow` commands.
/// Unified Context → Gateway workflow: builds context, assembles prompt, optionally calls Gateway.
/// </summary>
public static class WorkflowCommands
{
    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    public static async Task<int> HandleAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: cachehub workflow <command>");
            Console.Error.WriteLine("Commands:");
            Console.Error.WriteLine("  completion  Build context + assemble prompt + optional Gateway call");
            return 1;
        }

        return args[0] switch
        {
            "completion" => await CompletionAsync(args.AsSpan(1).ToArray()),
            _ => UnknownCommand(args[0]),
        };
    }

    private static async Task<int> CompletionAsync(string[] args)
    {
        var wsId = GetOpt(args, "--id");
        var task = GetOpt(args, "--task");
        var model = GetOpt(args, "--model");
        var callGateway = HasFlag(args, "--call-gateway");
        var currentFile = GetOpt(args, "--current-file");

        if (string.IsNullOrEmpty(wsId))
        {
            Console.Error.WriteLine("Error: --id=<workspace-id> is required");
            return 1;
        }

        if (string.IsNullOrEmpty(task))
        {
            Console.Error.WriteLine("Error: --task=\"task description\" is required");
            return 1;
        }

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
        var workspace = await wsRepo.FindByIdAsync(WorkspaceId.Parse(wsId));
        if (workspace is null)
        {
            Console.Error.WriteLine($"Workspace not found: {wsId}");
            return 1;
        }

        var querySvc = new SqliteIndexQueryService(factory);
        var activeSnapshotId = await querySvc.GetActiveSnapshotIdAsync(workspace.Id.Value);
        if (activeSnapshotId is null)
        {
            Console.Error.WriteLine("Error: No active index snapshot. Run 'cachehub index build' first.");
            return 1;
        }

        var tokenizers = TokenizerRegistry.CreateWithDefaults();
        var cache = ContextCommands.CreateContextCache(factory);
        var (secPolicy, secEnforcer) = SecurityPolicyResolver.CreateEnforcer();
        var engine = new ContextEngine(tokenizers, secPolicy, cache);

        var indexedFiles = await querySvc.GetIndexedFilesBySnapshotAsync(activeSnapshotId);
        var indexedFileInfos = indexedFiles.Select(f => new Context.Recall.IndexedFileInfo
        {
            Path = f.NormalizedPath,
            NormalizedPath = f.NormalizedPath,
            Language = f.Language,
            Size = f.Size,
            ContentHash = f.ContentHash,
        }).ToList();

        var request = new ContextBuildRequest
        {
            WorkspaceId = workspace.Id,
            IndexSnapshotId = activeSnapshotId,
            Task = task,
            ModelId = model,
            CurrentFile = currentFile,
            SecurityPolicyVersion = secPolicy.Version,
        };

        var manifest = engine.Build(
            request,
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
            semanticSearch: SemanticReferenceHelper.CreateSemanticSearch(appData.Root, workspace.Id.Value));

        var ctxRepo = new SqliteContextPackageRepository(factory);
        await ctxRepo.SaveAsync(manifest);

        var promptAssembly = new PromptAssemblyService();
        var payloadGenerator = new PayloadGenerator();
        var enforcer = secEnforcer;
        var payloadContent = payloadGenerator.GenerateMarkdown(manifest, path => ResolveFileContent(workspace.RootPath, path), enforcer);
        var (systemPrompt, userContent) = promptAssembly.Assemble(manifest, payloadContent);

        if (callGateway && !string.IsNullOrEmpty(model))
        {
            // V5-W02 (P0): Hard-block gateway call if security policy is Offline
            if (!secEnforcer.IsCloudSendAllowed())
            {
                Console.Error.WriteLine("  ⛔ Gateway call blocked: security policy is Offline mode.");
                Console.Error.WriteLine("  To enable cloud send, change security.mode in .cachehub-config.json.");
            }
            else
            {
            var gatewayUrl = GetOpt(args, "--gateway-url") ?? "http://127.0.0.1:5218";
            var gatewayToken = GetOpt(args, "--gateway-token")
                ?? Environment.GetEnvironmentVariable("CACHEHUB_GATEWAY_TOKEN")
                ?? "";

            try
            {
                var (responseContent, usageTokens) = await CallGatewayAsync(
                    gatewayUrl, gatewayToken, model, systemPrompt, userContent);

                var gatewayOutput = new
                {
                    manifest = new
                    {
                        id = manifest.Id.Value,
                        workspaceId = manifest.WorkspaceId.Value,
                        task = manifest.Task.OriginalText,
                        selectedFiles = manifest.SelectedFiles.Count,
                        actualTokens = manifest.Budget.ActualEstimate,
                        targetTokens = manifest.Budget.ContextTarget,
                    },
                    systemPrompt,
                    userContent = userContent.Length > 500 ? userContent[..500] + "..." : userContent,
                    gatewayCalled = true,
                    modelResponse = responseContent,
                    totalLifecycleTokens = manifest.Budget.ActualEstimate + usageTokens,
                };

                Console.WriteLine(JsonSerializer.Serialize(gatewayOutput, _jsonOpts));
                Console.Error.WriteLine($"  Context: {manifest.SelectedFiles.Count} files, {manifest.Budget.ActualEstimate} tokens");
                Console.Error.WriteLine($"  Gateway: called {gatewayUrl}, response {responseContent.Length} chars");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  Gateway call failed: {ex.Message}");
                Console.Error.WriteLine("  Returning context without model response.");
            }
            } // end else (cloud send allowed)
        }

        var output = new
        {
            manifest = new
            {
                id = manifest.Id.Value,
                workspaceId = manifest.WorkspaceId.Value,
                task = manifest.Task.OriginalText,
                selectedFiles = manifest.SelectedFiles.Count,
                actualTokens = manifest.Budget.ActualEstimate,
                targetTokens = manifest.Budget.ContextTarget,
            },
            systemPrompt,
            userContent = userContent.Length > 500 ? userContent[..500] + "..." : userContent,
            gatewayCalled = false,
            totalLifecycleTokens = manifest.Budget.ActualEstimate,
        };

        Console.WriteLine(JsonSerializer.Serialize(output, _jsonOpts));
        Console.Error.WriteLine($"  Context: {manifest.SelectedFiles.Count} files, {manifest.Budget.ActualEstimate} tokens");
        Console.Error.WriteLine($"  Prompt: system={systemPrompt.Length} chars, user={userContent.Length} chars");

        return 0;
    }

    private static string ResolveFileContent(string rootPath, string relativePath)
    {
        var fullPath = Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(fullPath) ? File.ReadAllText(fullPath) : "";
    }

    /// <summary>
    /// Calls the Gateway's /v1/chat/completions endpoint with the assembled prompt.
    /// Returns (response content, total usage tokens).
    /// </summary>
    private static async Task<(string content, int usageTokens)> CallGatewayAsync(
        string gatewayUrl, string gatewayToken, string model,
        string systemPrompt, string userContent)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        var requestBody = JsonSerializer.Serialize(new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userContent },
            },
        });

        using var msg = new HttpRequestMessage(HttpMethod.Post, $"{gatewayUrl.TrimEnd('/')}/v1/chat/completions");
        msg.Content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");
        if (!string.IsNullOrEmpty(gatewayToken))
            msg.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", gatewayToken);

        var resp = await http.SendAsync(msg);
        var body = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Gateway returned {resp.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";

        var usageTokens = 0;
        if (doc.RootElement.TryGetProperty("usage", out var usage))
        {
            usageTokens = usage.TryGetProperty("total_tokens", out var t) ? t.GetInt32() : 0;
        }

        return (content, usageTokens);
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

    private static string? GetOpt(string[] args, string prefix)
    {
        foreach (var arg in args)
        {
            if (arg.StartsWith(prefix + "=", StringComparison.OrdinalIgnoreCase))
                return arg[(prefix.Length + 1)..];
        }
        return null;
    }

    private static bool HasFlag(string[] args, string flag)
        => args.Contains(flag, StringComparer.OrdinalIgnoreCase);

    private static int UnknownCommand(string cmd)
    {
        Console.Error.WriteLine($"Unknown workflow command: {cmd}");
        return 1;
    }
}
