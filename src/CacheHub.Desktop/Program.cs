using System.Security.Cryptography;
using System.Text.Json;
using CacheHub.Context.Engine;
using CacheHub.Context.Explain;
using CacheHub.Context.Parsing;
using CacheHub.Context.Recall;
using CacheHub.Context.Expand;
using CacheHub.Context.Payload;
using CacheHub.Core.Capabilities;
using CacheHub.Core.Context;
using CacheHub.Core.Errors;
using CacheHub.Core.Feedback;
using CacheHub.Core.Identifiers;
using CacheHub.Core.Paths;
using Microsoft.Data.Sqlite;
using CacheHub.Core.Parsing;
using CacheHub.Core.Workspaces;
using CacheHub.Indexing.Parsing;
using CacheHub.Storage;
using CacheHub.Storage.Database;
using CacheHub.Storage.Database.Migrations;
using CacheHub.Storage.Search;
using CacheHub.Storage.Repositories;

var builder = WebApplication.CreateBuilder(args);

// Security: generate a random access token for API authentication
// Tests can override via configuration "ApiToken" or env var CACHEHUB_API_TOKEN
var accessToken = builder.Configuration["ApiToken"]
    ?? Environment.GetEnvironmentVariable("CACHEHUB_API_TOKEN")
    ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

builder.Services.AddSingleton<AppDataDirectory>();
builder.Services.AddSingleton<SqliteConnectionFactory>(sp =>
{
    var appData = sp.GetRequiredService<AppDataDirectory>();
    appData.EnsureCreated();
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
    ]);
    runner.Migrate();
    return factory;
});
builder.Services.AddSingleton<IWorkspaceRepository, SqliteWorkspaceRepository>();
builder.Services.AddSingleton<IContextPackageRepository, SqliteContextPackageRepository>();
builder.Services.AddSingleton<IFeedbackRepository, SqliteFeedbackRepository>();
builder.Services.AddSingleton<ContextEngine>();

// Security: force loopback binding only
builder.WebHost.UseUrls("http://127.0.0.1:5099");

var app = builder.Build();

// Security: API authentication middleware — all /api/ routes require a valid bearer token
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value;
    if (path is not null && path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
    {
#if DEBUG
        // In debug mode, allow requests without token if no Authorization header is present
        // This is for development convenience — production builds always require the token
#endif
        var authHeader = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.Equals($"Bearer {accessToken}", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync($"{{\"error\":\"Unauthorized\",\"code\":\"AUTH_REQUIRED\",\"hint\":\"Use Authorization: Bearer <token> header. Token was printed to console on startup.\"}}");
            return;
        }

        // Host header validation — prevent DNS rebinding attacks
        var host = context.Request.Headers.Host.ToString();
        if (!host.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase) &&
            !host.StartsWith("localhost", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\":\"Forbidden\",\"code\":\"INVALID_HOST\",\"hint\":\"Only localhost/127.0.0.1 access is allowed.\"}");
            return;
        }
    }

    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();

// Security: resolve a relative path within a workspace root, rejecting traversal attempts.
static string? SafeResolvePath(string rootPath, string relativePath)
{
    if (string.IsNullOrEmpty(rootPath)) return null;
    if (string.IsNullOrEmpty(relativePath)) return null;
    var cleaned = relativePath.Replace('/', Path.DirectorySeparatorChar);
    if (cleaned.Contains("..")) return null;
    var fullPath = Path.GetFullPath(Path.Combine(rootPath, cleaned));
    var normalizedRoot = Path.GetFullPath(rootPath);
    return fullPath.StartsWith(normalizedRoot, CacheHub.Core.Paths.PathComparer.PhysicalPathComparison) ? fullPath : null;
}

static async Task<List<IndexedFileInfo>> GetIndexedFilesAsync(SqliteConnectionFactory factory, string workspaceId)
{
    var result = new List<IndexedFileInfo>();
    await using var conn = factory.CreateOpenConnection();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = """
        SELECT f.normalized_path, f.size, f.language, f.content_hash
        FROM files f
        INNER JOIN index_snapshots s ON f.snapshot_id = s.id
        WHERE s.workspace_id = $ws AND s.status = 'Active';
        """;
    cmd.Parameters.AddWithValue("$ws", workspaceId);
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        result.Add(new IndexedFileInfo
        {
            Path = reader.GetString(0),
            NormalizedPath = reader.GetString(0),
            Size = reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
            Language = reader.IsDBNull(2) ? "unknown" : reader.GetString(2),
            ContentHash = reader.IsDBNull(3) ? null : reader.GetString(3),
            Symbols = [],
        });
    }
    return result;
}

static async Task<IndexSnapshotId?> GetActiveSnapshotIdAsync(SqliteConnectionFactory factory, string workspaceId)
{
    await using var conn = factory.CreateOpenConnection();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT id FROM index_snapshots WHERE workspace_id = $ws AND status = 'Active' LIMIT 1;";
    cmd.Parameters.AddWithValue("$ws", workspaceId);
    var result = await cmd.ExecuteScalarAsync();
    return result is string id ? IndexSnapshotId.Parse(id) : null;
}

static string ResolveFileHashFromDb(SqliteConnectionFactory factory, IndexSnapshotId snapshotId, string path)
{
    using var conn = factory.CreateOpenConnection();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT content_hash FROM files WHERE snapshot_id = $snap AND normalized_path = $path LIMIT 1;";
    cmd.Parameters.AddWithValue("$snap", snapshotId.Value);
    cmd.Parameters.AddWithValue("$path", path);
    var result = cmd.ExecuteScalar();
    if (result is string hash && !string.IsNullOrEmpty(hash) && hash != "pending")
        return hash;
    return "sha256:pending";
}

// === Capabilities ===
app.MapGet("/api/v1/capabilities", () => Results.Ok(new CapabilityDiscovery
{
    Version = "0.2.0-prealpha",
    ProtocolVersion = "1.0",
    Capabilities = CapabilityFlags.With(
        Capability.WorkspaceImport, Capability.ContextBuild,
        Capability.ContextExpand, Capability.ContextExplain,
        Capability.ContextFeedback, Capability.FileExport,
        Capability.Cache, Capability.Gateway),
    SchemaVersions = new Dictionary<string, int>
    {
        ["contextPackage"] = 1,
        ["capabilityDiscovery"] = 1,
    },
    Limitations = ["No Semantic", "No LSP"],
}));

// === Workspaces ===
app.MapGet("/api/v1/workspaces", async (IWorkspaceRepository repo) =>
{
    var workspaces = await repo.ListAllAsync();
    return Results.Ok(workspaces.Select(w => new
    {
        id = w.Id.Value,
        name = w.Name,
        rootPath = w.RootPath,
        status = w.Status.ToString(),
        createdAt = w.CreatedAt,
    }));
});

app.MapPost("/api/v1/workspaces/import", async (ImportRequest req, IWorkspaceRepository repo) =>
{
    if (string.IsNullOrEmpty(req.Path))
        return Results.BadRequest(ErrorEnvelope.From(ErrorCode.InvalidArgument, "Path is required"));

    var workspace = Workspace.CreateValidated(req.Name ?? new DirectoryInfo(req.Path).Name, req.Path);
    await repo.InsertAsync(workspace);
    return Results.Ok(new { id = workspace.Id.Value, name = workspace.Name, status = workspace.Status.ToString() });
});

app.MapGet("/api/v1/workspaces/{id}/status", async (string id, IWorkspaceRepository repo) =>
{
    var ws = await repo.FindByIdAsync(WorkspaceId.Parse(id));
    if (ws is null) return Results.NotFound(ErrorEnvelope.From(ErrorCode.WorkspaceNotFound, "Workspace not found"));
    return Results.Ok(new { id = ws.Id.Value, name = ws.Name, rootPath = ws.RootPath, status = ws.Status.ToString() });
});

app.MapDelete("/api/v1/workspaces/{id}", async (string id, IWorkspaceRepository repo) =>
{
    await repo.RemoveAsync(WorkspaceId.Parse(id));
    return Results.Ok(new { removed = true });
});

// === Index Build (API-P1-003: GUI 索引任务闭环) ===
app.MapPost("/api/v1/workspaces/{id}/index", async (string id, IWorkspaceRepository repo, SqliteConnectionFactory factory) =>
{
    var ws = await repo.FindByIdAsync(WorkspaceId.Parse(id));
    if (ws is null) return Results.NotFound(ErrorEnvelope.From(ErrorCode.WorkspaceNotFound, "Workspace not found"));

    var snapshotId = IndexSnapshotId.New();

    // Insert snapshot as Building
    await using var initConn = factory.CreateOpenConnection();
    using var snapCmd = initConn.CreateCommand();
    snapCmd.CommandText = "INSERT INTO index_snapshots (id, workspace_id, status, file_count) VALUES ($id, $ws, 'Building', 0);";
    snapCmd.Parameters.AddWithValue("$id", snapshotId.Value);
    snapCmd.Parameters.AddWithValue("$ws", ws.Id.Value);
    await snapCmd.ExecuteNonQueryAsync();
    await initConn.DisposeAsync();

    // Run indexing in background
    _ = Task.Run(async () =>
    {
        try
        {
            var ignoreEngine = new CacheHub.Indexing.IgnoreRules.IgnoreRuleEngine()
                .WithDefaults()
                .WithGitIgnore(System.IO.Path.Combine(ws.RootPath, ".gitignore"))
                .WithCacheHubIgnore(System.IO.Path.Combine(ws.RootPath, ".cachehubignore"));

            var enumerator = new CacheHub.Indexing.Scanning.DirectoryEnumerator();

            // Collect files first, then batch-write in a single transaction
            var filesToIndex = new List<(string relativePath, string fullPath, long size, string language, bool isBinary, string hash, string content)>();

            await foreach (var file in enumerator.EnumerateAsync(ws.RootPath))
            {
                if (file.IsDirectory) continue;
                var relativePath = CacheHub.Core.Paths.PathNormalizer.GetRelativePath(ws.RootPath, file.Path);
                if (ignoreEngine.IsIgnored(relativePath)) continue;

                var typeInfo = CacheHub.Indexing.Detection.FileTypeDetector.Detect(file.Path, file.Size);
                if (!typeInfo.ShouldIndex) continue;

                var hash = await CacheHub.Indexing.Hashing.FileHasher.HashAsync(file.Path, file.Size);
                var content = await System.IO.File.ReadAllTextAsync(file.Path);

                filesToIndex.Add((relativePath, file.Path, file.Size, typeInfo.Language, typeInfo.IsBinary, hash.Hash, content));
            }

            // Batch write: single connection, single transaction for atomicity
            await using var batchConn = factory.CreateOpenConnection();
            await using var batchTx = await batchConn.BeginTransactionAsync();

            try
            {
                foreach (var (relativePath, fullPath, size, language, isBinary, hash, content) in filesToIndex)
                {
                    var parser = SelectParser(relativePath);
                    var parseResult = parser.Parse(content, relativePath);

                    using var fileCmd = batchConn.CreateCommand();
                    fileCmd.Transaction = (SqliteTransaction)batchTx;
                    fileCmd.CommandText = """
                        INSERT INTO files (id, snapshot_id, path, normalized_path, size, content_hash, language, is_binary, status, hash_kind, parser_id, parser_version)
                        VALUES ($id, $snap, $path, $norm, $size, $hash, $lang, $bin, 'Indexed', $hashKind, $parserId, $parserVer);
                        """;
                    var fileId = Guid.NewGuid().ToString("N");
                    fileCmd.Parameters.AddWithValue("$id", fileId);
                    fileCmd.Parameters.AddWithValue("$snap", snapshotId.Value);
                    fileCmd.Parameters.AddWithValue("$path", relativePath);
                    fileCmd.Parameters.AddWithValue("$norm", relativePath);
                    fileCmd.Parameters.AddWithValue("$size", size);
                    fileCmd.Parameters.AddWithValue("$hash", hash);
                    fileCmd.Parameters.AddWithValue("$lang", language);
                    fileCmd.Parameters.AddWithValue("$bin", isBinary ? 1 : 0);
                    fileCmd.Parameters.AddWithValue("$hashKind", hash.StartsWith("fp:", StringComparison.Ordinal) ? "fingerprint" : "full");
                    fileCmd.Parameters.AddWithValue("$parserId", parser.Id);
                    fileCmd.Parameters.AddWithValue("$parserVer", parser.Version);
                    await fileCmd.ExecuteNonQueryAsync();

                    foreach (var symbol in parseResult.Symbols)
                    {
                        using var symCmd = batchConn.CreateCommand();
                        symCmd.Transaction = (SqliteTransaction)batchTx;
                        symCmd.CommandText = """
                            INSERT INTO file_symbols (id, file_id, snapshot_id, name, kind, start_line, end_line, modifier, confidence)
                            VALUES ($id, $fid, $snap, $name, $kind, $sl, $el, $mod, $conf);
                            """;
                        symCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
                        symCmd.Parameters.AddWithValue("$fid", fileId);
                        symCmd.Parameters.AddWithValue("$snap", snapshotId.Value);
                        symCmd.Parameters.AddWithValue("$name", symbol.Name);
                        symCmd.Parameters.AddWithValue("$kind", symbol.Kind.ToString());
                        symCmd.Parameters.AddWithValue("$sl", symbol.StartLine);
                        symCmd.Parameters.AddWithValue("$el", symbol.EndLine);
                        symCmd.Parameters.AddWithValue("$mod", (object?)symbol.Modifier ?? DBNull.Value);
                        symCmd.Parameters.AddWithValue("$conf", "syntactic");
                        await symCmd.ExecuteNonQueryAsync();
                    }

                    foreach (var import in parseResult.Imports)
                    {
                        using var impCmd = batchConn.CreateCommand();
                        impCmd.Transaction = (SqliteTransaction)batchTx;
                        impCmd.CommandText = """
                            INSERT INTO file_imports (id, file_id, snapshot_id, module, imported_name, line)
                            VALUES ($id, $fid, $snap, $mod, $name, $line);
                            """;
                        impCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
                        impCmd.Parameters.AddWithValue("$fid", fileId);
                        impCmd.Parameters.AddWithValue("$snap", snapshotId.Value);
                        impCmd.Parameters.AddWithValue("$mod", import.Module);
                        impCmd.Parameters.AddWithValue("$name", (object?)import.ImportedName ?? DBNull.Value);
                        impCmd.Parameters.AddWithValue("$line", import.Line);
                        await impCmd.ExecuteNonQueryAsync();
                    }

                    foreach (var relation in parseResult.Relations)
                    {
                        using var relCmd = batchConn.CreateCommand();
                        relCmd.Transaction = (SqliteTransaction)batchTx;
                        relCmd.CommandText = """
                            INSERT INTO file_relations (id, file_id, snapshot_id, source_symbol, target_symbol, relation_type, confidence, line)
                            VALUES ($id, $fid, $snap, $src, $tgt, $rt, $conf, $line);
                            """;
                        relCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
                        relCmd.Parameters.AddWithValue("$fid", fileId);
                        relCmd.Parameters.AddWithValue("$snap", snapshotId.Value);
                        relCmd.Parameters.AddWithValue("$src", relation.Relation);
                        relCmd.Parameters.AddWithValue("$tgt", relation.TargetName);
                        relCmd.Parameters.AddWithValue("$rt", relation.RelationType.ToString());
                        relCmd.Parameters.AddWithValue("$conf", relation.Source.ToString());
                        relCmd.Parameters.AddWithValue("$line", (object?)relation.Confidence ?? DBNull.Value);
                        await relCmd.ExecuteNonQueryAsync();
                    }
                }

                await batchTx.CommitAsync();
            }
            catch (Exception)
            {
                await batchTx.RollbackAsync();
                throw;
            }

            // FTS indexing (separate — FTS5 virtual tables don't support DDL in DML transactions)
            var fts = new CacheHub.Storage.Search.Fts5Index(factory);
            foreach (var (relativePath, _, _, language, _, hash, content) in filesToIndex)
            {
                await fts.IndexFileAsync(snapshotId, relativePath, relativePath, content, language, hash);
            }

            // Activate snapshot (workspace-scoped)
            await using var activateConn = factory.CreateOpenConnection();
            await using var activateTx = await activateConn.BeginTransactionAsync();
            using var supCmd = activateConn.CreateCommand();
            supCmd.Transaction = (SqliteTransaction)activateTx;
            supCmd.CommandText = "UPDATE index_snapshots SET status = 'Superseded' WHERE status = 'Active' AND workspace_id = $ws;";
            supCmd.Parameters.AddWithValue("$ws", ws.Id.Value);
            await supCmd.ExecuteNonQueryAsync();

            using var setActiveCmd = activateConn.CreateCommand();
            setActiveCmd.Transaction = (SqliteTransaction)activateTx;
            setActiveCmd.CommandText = "UPDATE index_snapshots SET status = 'Active', file_count = $count, completed_at = datetime('now') WHERE id = $id;";
            setActiveCmd.Parameters.AddWithValue("$count", filesToIndex.Count);
            setActiveCmd.Parameters.AddWithValue("$id", snapshotId.Value);
            await setActiveCmd.ExecuteNonQueryAsync();
            await activateTx.CommitAsync();

            await repo.UpdateStatusAsync(ws.Id, WorkspaceStatus.Ready);
        }
        catch (Exception)
        {
            // Mark snapshot as failed
            await using var failConn = factory.CreateOpenConnection();
            using var failCmd = failConn.CreateCommand();
            failCmd.CommandText = "UPDATE index_snapshots SET status = 'Failed' WHERE id = $id;";
            failCmd.Parameters.AddWithValue("$id", snapshotId.Value);
            await failCmd.ExecuteNonQueryAsync();
        }
    });

    return Results.Ok(new
    {
        workspaceId = ws.Id.Value,
        snapshotId = snapshotId.Value,
        status = "Building",
        message = "Index build started. Poll /api/v1/workspaces/{id}/status for progress.",
    });
});

// === Context ===
app.MapPost("/api/v1/context/build", async (ContextBuildApiRequest req, ContextEngine engine, SqliteConnectionFactory factory, IWorkspaceRepository repo, IContextPackageRepository ctxRepo) =>
{
    var ws = await repo.FindByIdAsync(WorkspaceId.Parse(req.WorkspaceId));
    if (ws is null) return Results.NotFound(ErrorEnvelope.From(ErrorCode.WorkspaceNotFound, "Workspace not found"));

    var activeSnapshotId = await GetActiveSnapshotIdAsync(factory, req.WorkspaceId);
    if (activeSnapshotId is null)
        return Results.BadRequest(ErrorEnvelope.From(ErrorCode.IndexNotFound, "No active index snapshot. Run 'cachehub index build' first."));

    var indexedFiles = await GetIndexedFilesAsync(factory, req.WorkspaceId);

    var manifest = engine.Build(
        new ContextBuildRequest
        {
            WorkspaceId = ws.Id,
            IndexSnapshotId = activeSnapshotId,
            Task = req.Task,
        },
        () => indexedFiles,
        path =>
        {
            var fullPath = SafeResolvePath(ws.RootPath, path);
            return fullPath is not null && File.Exists(fullPath) ? File.ReadAllTextAsync(fullPath).GetAwaiter().GetResult() : "";
        },
        path => ResolveFileHashFromDb(factory, activeSnapshotId, path),
        ftsSearch: keyword =>
        {
            var querySvc = new CacheHub.Storage.Query.SqliteIndexQueryService(factory);
            var results = querySvc.SearchFtsAsync(activeSnapshotId, keyword, 50).GetAwaiter().GetResult();
            return results.Select(r => new CacheHub.Context.Recall.FtsMatch(r.Path, r.Language, r.Snippet)).ToList();
        },
        symbolSearch: symbol =>
        {
            var querySvc = new CacheHub.Storage.Query.SqliteIndexQueryService(factory);
            var results = querySvc.SearchSymbolsAsync(activeSnapshotId, symbol).GetAwaiter().GetResult();
            return results.Select(r => r.NormalizedPath).ToList();
        },
        importSearch: symbol =>
        {
            var querySvc = new CacheHub.Storage.Query.SqliteIndexQueryService(factory);
            var results = querySvc.GetFilesByImportedSymbolAsync(activeSnapshotId, symbol).GetAwaiter().GetResult();
            return results.ToList();
        },
        symbolSearchDetailed: symbol =>
        {
            var querySvc = new CacheHub.Storage.Query.SqliteIndexQueryService(factory);
            var results = querySvc.SearchSymbolsAsync(activeSnapshotId, symbol).GetAwaiter().GetResult();
            return results.Select(r => new CacheHub.Context.Recall.SymbolHit
            {
                NormalizedPath = r.NormalizedPath,
                Name = r.Name,
                Kind = r.Kind,
                StartLine = r.StartLine,
                EndLine = r.EndLine,
                ExactMatch = r.ExactMatch,
            }).ToList();
        });

    await ctxRepo.SaveAsync(manifest);

    return Results.Ok(manifest);
});

app.MapGet("/api/v1/context/{id}", async (string id, IContextPackageRepository ctxRepo) =>
{
    var manifest = await ctxRepo.FindByIdAsync(ContextPackageId.Parse(id));
    if (manifest is null) return Results.NotFound(ErrorEnvelope.From(ErrorCode.ContextPackageNotFound, "Context package not found"));
    return Results.Ok(manifest);
});

app.MapPost("/api/v1/context/{id}/expand", async (string id, ExpandApiRequest req, IContextPackageRepository ctxRepo, IWorkspaceRepository wsRepo) =>
{
    var manifest = await ctxRepo.FindByIdAsync(ContextPackageId.Parse(id));
    if (manifest is null) return Results.NotFound(ErrorEnvelope.From(ErrorCode.ContextPackageNotFound, "Context package not found"));

    var ws = await wsRepo.FindByIdAsync(manifest.WorkspaceId);
    if (ws is null) return Results.NotFound(ErrorEnvelope.From(ErrorCode.WorkspaceNotFound, "Workspace not found"));

    var targetPath = req.File ?? req.Symbol ?? "";
    var fullPath = SafeResolvePath(ws.RootPath, targetPath);

    if (fullPath is null)
        return Results.BadRequest(ErrorEnvelope.From(ErrorCode.InvalidArgument, "Invalid file path"));

    if (!File.Exists(fullPath))
        return Results.NotFound(ErrorEnvelope.From(ErrorCode.InvalidArgument, $"File not found: {targetPath}"));

    var content = await File.ReadAllTextAsync(fullPath);
    var expander = new ContextExpander();
    var result = expander.ExpandByFile(id, targetPath, content, req.Reason ?? "API expand");

    return Results.Ok(new
    {
        contextId = id,
        addedItems = result.AddedItems.Select(i => new { path = i.Path, mode = i.Mode.ToString(), content = i.Content.Length }),
        additionalTokens = result.AdditionalTokens,
        reason = result.Reason,
    });
});

app.MapPost("/api/v1/context/{id}/feedback", async (string id, FeedbackApiRequest req, IFeedbackRepository fbRepo) =>
{
    var feedback = new ContextFeedback
    {
        ContextPackageId = id,
        ClientId = req.ClientId,
        FilesActuallyRead = req.FilesActuallyRead ?? [],
        TaskCompleted = req.TaskCompleted,
        MissingContextReported = req.MissingContextReported,
    };

    await fbRepo.SaveAsync(feedback);

    return Results.Ok(new { received = true, saved = true, contextId = id, clientId = feedback.ClientId });
});

// === Context List ===
app.MapGet("/api/v1/workspaces/{id}/contexts", async (string id, IContextPackageRepository ctxRepo) =>
{
    var list = await ctxRepo.ListByWorkspaceAsync(WorkspaceId.Parse(id));
    return Results.Ok(list.Select(m => new
    {
        id = m.Id.Value,
        task = m.Task.OriginalText,
        budget = m.Budget.ActualEstimate,
        engine = m.ContextEngineVersion,
        createdAt = m.CreatedAt,
    }));
});

// === Context Explain ===
app.MapGet("/api/v1/context/{id}/explain", async (string id, IContextPackageRepository ctxRepo) =>
{
    var manifest = await ctxRepo.FindByIdAsync(ContextPackageId.Parse(id));
    if (manifest is null) return Results.NotFound(ErrorEnvelope.From(ErrorCode.ContextPackageNotFound, "Context package not found"));

    var explanations = ContextExplainer.Explain(manifest);
    var misses = ContextExplainer.DetectPotentialMisses(manifest);
    var budgetSummary = ContextExplainer.BudgetSummary(manifest);

    return Results.Ok(new
    {
        contextId = id,
        explanations = explanations.Select(e => new
        {
            path = e.Path,
            selected = e.Selected,
            score = e.Score,
            reasons = e.Reasons,
            exclusionReason = e.ExclusionReason,
        }),
        potentialMisses = misses,
        budgetSummary,
    });
});

// === File Export ===
app.MapPost("/api/v1/workspaces/{id}/export", async (string id, IWorkspaceRepository repo) =>
{
    var ws = await repo.FindByIdAsync(WorkspaceId.Parse(id));
    if (ws is null) return Results.NotFound(ErrorEnvelope.From(ErrorCode.WorkspaceNotFound, "Workspace not found"));

    var appData = new AppDataDirectory();
    var exportDir = Path.Combine(appData.Root, "exports", id);
    Directory.CreateDirectory(exportDir);

    var workspaceJson = new { id = ws.Id.Value, name = ws.Name, rootPath = ws.RootPath, status = ws.Status.ToString() };
    var jsonPath = Path.Combine(exportDir, "workspace.json");
    await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(workspaceJson, new JsonSerializerOptions { WriteIndented = true }));

    var repomapPath = Path.Combine(exportDir, "repomap.md");
    await File.WriteAllTextAsync(repomapPath, $"# Repository Map: {ws.Name}\n\n> Export is a placeholder.\n");

    return Results.Ok(new
    {
        workspaceId = id,
        exportedTo = exportDir,
        files = new[] { "workspace.json", "repomap.md" },
    });
});

// === Search ===
app.MapGet("/api/v1/search", async (string? q, string? workspace, int? limit, SqliteConnectionFactory factory) =>
{
    if (string.IsNullOrWhiteSpace(q))
        return Results.BadRequest(ErrorEnvelope.From(ErrorCode.InvalidArgument, "Query parameter 'q' is required"));

    var wsId = workspace ?? "";
    IndexSnapshotId? snapshotId = null;

    if (!string.IsNullOrEmpty(wsId))
    {
        await using var conn = factory.CreateOpenConnection();
        using var snapCmd = conn.CreateCommand();
        snapCmd.CommandText = "SELECT id FROM index_snapshots WHERE workspace_id = $ws AND status = 'Active' LIMIT 1;";
        snapCmd.Parameters.AddWithValue("$ws", wsId);
        await using var snapReader = await snapCmd.ExecuteReaderAsync();
        if (await snapReader.ReadAsync())
            snapshotId = IndexSnapshotId.Parse(snapReader.GetString(0));
    }

    if (snapshotId is null)
        return Results.Ok(new { results = Array.Empty<object>(), message = "No active index snapshot found" });

    var fts = new Fts5Index(factory);
    var results = await fts.SearchAsync(snapshotId, q, limit ?? 50);

    return Results.Ok(new
    {
        query = q,
        count = results.Count,
        results = results.Select(r => new { path = r.Path, language = r.Language, snippet = r.Snippet }),
    });
});

// === Outline === (SECURITY: workspace-scoped, no arbitrary absolute paths)
app.MapGet("/api/v1/outline", async (string workspaceId, string path, IWorkspaceRepository wsRepo) =>
{
    if (string.IsNullOrWhiteSpace(workspaceId))
        return Results.BadRequest(ErrorEnvelope.From(ErrorCode.InvalidArgument, "Query parameter 'workspaceId' is required"));
    if (string.IsNullOrWhiteSpace(path))
        return Results.BadRequest(ErrorEnvelope.From(ErrorCode.InvalidArgument, "Query parameter 'path' (relative file path) is required"));

    var ws = await wsRepo.FindByIdAsync(WorkspaceId.Parse(workspaceId));
    if (ws is null)
        return Results.NotFound(ErrorEnvelope.From(ErrorCode.WorkspaceNotFound, "Workspace not found"));

    var resolver = new SafePathResolver(ws.RootPath);
    var resolvedPath = resolver.ResolveFile(path);
    if (resolvedPath is null)
        return Results.BadRequest(ErrorEnvelope.From(ErrorCode.PathTraversalDetected, "File not found within workspace or path traversal detected"));

    var content = File.ReadAllText(resolvedPath);
    var ext = Path.GetExtension(resolvedPath).ToLowerInvariant();

    CacheHub.Core.Parsing.ICodeParser? parser = ext switch
    {
        ".cs" => new CacheHub.Indexing.Parsing.CSharpRegexParser(),
        ".ts" or ".tsx" or ".js" or ".jsx" => new CacheHub.Indexing.Parsing.TypeScriptRegexParser(),
        ".py" => new CacheHub.Indexing.Parsing.PythonRegexParser(),
        ".md" or ".markdown" => new CacheHub.Indexing.Parsing.MarkdownParser(),
        _ => new CacheHub.Indexing.Parsing.TextParser(),
    };

    var result = parser.Parse(content, path);
    var outline = CacheHub.Core.Parsing.Outline.DeterministicOutlineGenerator.Generate(result, path);

    return Results.Ok(new
    {
        file = outline.FilePath,
        language = outline.Language,
        parser = outline.ParserId,
        symbols = outline.Symbols.Select(s => new
        {
            name = s.Name,
            kind = s.Kind.ToString(),
            startLine = s.StartLine,
            endLine = s.EndLine,
            modifier = s.Modifier,
        }),
        imports = outline.Imports.Select(i => new { module = i.Module, name = i.ImportedName, line = i.Line }),
    });
});

// === Stats ===
app.MapGet("/api/v1/stats", async (SqliteConnectionFactory factory) =>
{
    var wsRepo = new SqliteWorkspaceRepository(factory);
    var ctxRepo = new SqliteContextPackageRepository(factory);

    var workspaces = await wsRepo.ListAllAsync();
    var totalContexts = 0;
    var totalTokens = 0;

    foreach (var ws in workspaces)
    {
        var contexts = await ctxRepo.ListByWorkspaceAsync(ws.Id);
        totalContexts += contexts.Count;
        totalTokens += contexts.Sum(c => c.Budget.ActualEstimate);
    }

    return Results.Ok(new
    {
        workspaces = workspaces.Count,
        contextPackages = totalContexts,
        totalEstimatedTokens = totalTokens,
        statuses = workspaces.GroupBy(w => w.Status.ToString())
            .Select(g => new { status = g.Key, count = g.Count() }),
    });
});

// === Payload ===
app.MapGet("/api/v1/context/{id}/payload", async (string id, IContextPackageRepository ctxRepo, IWorkspaceRepository wsRepo) =>
{
    var manifest = await ctxRepo.FindByIdAsync(ContextPackageId.Parse(id));
    if (manifest is null) return Results.NotFound(ErrorEnvelope.From(ErrorCode.ContextPackageNotFound, "Context package not found"));

    var ws = await wsRepo.FindByIdAsync(manifest.WorkspaceId);
    if (ws is null) return Results.NotFound(ErrorEnvelope.From(ErrorCode.WorkspaceNotFound, "Workspace not found"));

    var generator = new PayloadGenerator();
    var enforcer = new CacheHub.Core.Security.SecurityPolicyEnforcer();
    var payload = generator.Generate(manifest, path =>
    {
        var fullPath = SafeResolvePath(ws.RootPath, path);
        return fullPath is not null && File.Exists(fullPath) ? File.ReadAllTextAsync(fullPath).GetAwaiter().GetResult() : "";
    }, enforcer);

    return Results.Ok(new
    {
        contextPackageId = payload.ContextPackageId,
        format = payload.Format.ToString(),
        totalEstimatedTokens = payload.TotalEstimatedTokens,
        items = payload.Items.Select(i => new
        {
            path = i.Path,
            mode = i.Mode.ToString(),
            content = i.Content,
            startLine = i.StartLine,
            endLine = i.EndLine,
        }),
    });
});

// Print access token to console for user
Console.WriteLine("============================================================");
Console.WriteLine("CacheHub Local API started on http://127.0.0.1:5099");
Console.WriteLine($"Access Token: {accessToken}");
Console.WriteLine("All API requests require: Authorization: Bearer <token>");
Console.WriteLine("============================================================");

static ICodeParser SelectParser(string filePath)
{
    var ext = Path.GetExtension(filePath).ToLowerInvariant();
    return ext switch
    {
        ".cs" => new CSharpRegexParser(),
        ".ts" or ".tsx" or ".js" or ".jsx" => new TypeScriptRegexParser(),
        ".py" => new PythonRegexParser(),
        ".md" or ".markdown" => new MarkdownParser(),
        _ => new TextParser(),
    };
}

app.Run();

record ImportRequest(string Path, string? Name);
record ContextBuildApiRequest(string WorkspaceId, string Task);
record ExpandApiRequest(string? Symbol, string? File, string? Reason);
record FeedbackApiRequest(string? ClientId, bool TaskCompleted, bool MissingContextReported, IReadOnlyList<string>? FilesActuallyRead);

// Make Program class accessible for integration testing
public partial class Program { }
