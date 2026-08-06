using System.Text.Json;
using CacheHub.Context.Engine;
using CacheHub.Context.Explain;
using CacheHub.Context.Parsing;
using CacheHub.Context.Recall;
using CacheHub.Context.Expand;
using CacheHub.Context.Payload;
using CacheHub.Core.Capabilities;
using CacheHub.Core.Context;
using CacheHub.Core.Feedback;
using CacheHub.Core.Identifiers;
using CacheHub.Core.Paths;
using CacheHub.Core.Workspaces;
using CacheHub.Storage;
using CacheHub.Storage.Database;
using CacheHub.Storage.Database.Migrations;
using CacheHub.Storage.Search;
using CacheHub.Storage.Repositories;

var builder = WebApplication.CreateBuilder(args);

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
    ]);
    runner.Migrate();
    return factory;
});
builder.Services.AddSingleton<IWorkspaceRepository, SqliteWorkspaceRepository>();
builder.Services.AddSingleton<IContextPackageRepository, SqliteContextPackageRepository>();
builder.Services.AddSingleton<IFeedbackRepository, SqliteFeedbackRepository>();
builder.Services.AddSingleton<ContextEngine>();

var app = builder.Build();

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
    return fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) ? fullPath : null;
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
        return Results.BadRequest(new { error = "Path is required" });

    var workspace = Workspace.Create(req.Name ?? new DirectoryInfo(req.Path).Name, req.Path);
    await repo.InsertAsync(workspace);
    return Results.Ok(new { id = workspace.Id.Value, name = workspace.Name, status = workspace.Status.ToString() });
});

app.MapGet("/api/v1/workspaces/{id}/status", async (string id, IWorkspaceRepository repo) =>
{
    var ws = await repo.FindByIdAsync(WorkspaceId.Parse(id));
    if (ws is null) return Results.NotFound(new { error = "Workspace not found" });
    return Results.Ok(new { id = ws.Id.Value, name = ws.Name, rootPath = ws.RootPath, status = ws.Status.ToString() });
});

app.MapDelete("/api/v1/workspaces/{id}", async (string id, IWorkspaceRepository repo) =>
{
    await repo.RemoveAsync(WorkspaceId.Parse(id));
    return Results.Ok(new { removed = true });
});

// === Context ===
app.MapPost("/api/v1/context/build", async (ContextBuildApiRequest req, ContextEngine engine, SqliteConnectionFactory factory, IWorkspaceRepository repo, IContextPackageRepository ctxRepo) =>
{
    var ws = await repo.FindByIdAsync(WorkspaceId.Parse(req.WorkspaceId));
    if (ws is null) return Results.NotFound(new { error = "Workspace not found" });

    var activeSnapshotId = await GetActiveSnapshotIdAsync(factory, req.WorkspaceId);
    if (activeSnapshotId is null)
        return Results.BadRequest(new { error = "No active index snapshot. Run 'cachehub index build' first.", code = "INDEX_NOT_READY" });

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
            return fullPath is not null && File.Exists(fullPath) ? File.ReadAllText(fullPath) : "";
        },
        path => ResolveFileHashFromDb(factory, activeSnapshotId, path));

    await ctxRepo.SaveAsync(manifest);

    return Results.Ok(manifest);
});

app.MapGet("/api/v1/context/{id}", async (string id, IContextPackageRepository ctxRepo) =>
{
    var manifest = await ctxRepo.FindByIdAsync(ContextPackageId.Parse(id));
    if (manifest is null) return Results.NotFound(new { error = "Context package not found" });
    return Results.Ok(manifest);
});

app.MapPost("/api/v1/context/{id}/expand", async (string id, ExpandApiRequest req, IContextPackageRepository ctxRepo, IWorkspaceRepository wsRepo) =>
{
    var manifest = await ctxRepo.FindByIdAsync(ContextPackageId.Parse(id));
    if (manifest is null) return Results.NotFound(new { error = "Context package not found" });

    var ws = await wsRepo.FindByIdAsync(manifest.WorkspaceId);
    if (ws is null) return Results.NotFound(new { error = "Workspace not found" });

    var targetPath = req.File ?? req.Symbol ?? "";
    var fullPath = SafeResolvePath(ws.RootPath, targetPath);

    if (fullPath is null)
        return Results.BadRequest(new { error = "Invalid file path" });

    if (!File.Exists(fullPath))
        return Results.NotFound(new { error = $"File not found: {targetPath}" });

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
    if (manifest is null) return Results.NotFound(new { error = "Context package not found" });

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
    if (ws is null) return Results.NotFound(new { error = "Workspace not found" });

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
app.MapGet("/api/v1/search", async (string q, string? workspace, int? limit, SqliteConnectionFactory factory) =>
{
    if (string.IsNullOrWhiteSpace(q))
        return Results.BadRequest(new { error = "Query parameter 'q' is required" });

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

// === Outline ===
app.MapGet("/api/v1/outline", (string path) =>
{
    if (string.IsNullOrWhiteSpace(path))
        return Results.BadRequest(new { error = "Query parameter 'path' is required" });

    if (!File.Exists(path))
        return Results.NotFound(new { error = $"File not found: {path}" });

    var content = File.ReadAllText(path);
    var ext = Path.GetExtension(path).ToLowerInvariant();

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
    if (manifest is null) return Results.NotFound(new { error = "Context package not found" });

    var ws = await wsRepo.FindByIdAsync(manifest.WorkspaceId);
    if (ws is null) return Results.NotFound(new { error = "Workspace not found" });

    var generator = new PayloadGenerator();
    var payload = generator.Generate(manifest, path =>
    {
        var fullPath = SafeResolvePath(ws.RootPath, path);
        return fullPath is not null && File.Exists(fullPath) ? File.ReadAllText(fullPath) : "";
    });

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

app.Run();

record ImportRequest(string Path, string? Name);
record ContextBuildApiRequest(string WorkspaceId, string Task);
record ExpandApiRequest(string? Symbol, string? File, string? Reason);
record FeedbackApiRequest(string? ClientId, bool TaskCompleted, bool MissingContextReported, IReadOnlyList<string>? FilesActuallyRead);
