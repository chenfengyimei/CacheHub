using AiKv.Core.Capabilities;
using AiKv.Core.Identifiers;
using AiKv.Core.Workspaces;
using AiKv.Storage;
using AiKv.Storage.Database;
using AiKv.Storage.Database.Migrations;
using AiKv.Storage.Repositories;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<AppDataDirectory>();
builder.Services.AddSingleton<SqliteConnectionFactory>(sp =>
{
    var appData = sp.GetRequiredService<AppDataDirectory>();
    appData.EnsureCreated();
    return new SqliteConnectionFactory(appData.GetWorkspaceDatabasePath("main"));
});
builder.Services.AddSingleton<IWorkspaceRepository, SqliteWorkspaceRepository>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// === API Routes ===

app.MapGet("/api/v1/capabilities", () =>
{
    return Results.Ok(new CapabilityDiscovery
    {
        Version = "0.1.0-beta",
        ProtocolVersion = "1.0",
        Capabilities = CapabilityFlags.With(
            Capability.WorkspaceImport,
            Capability.ContextBuild,
            Capability.ContextExpand,
            Capability.ContextExplain,
            Capability.FileExport,
            Capability.Cache),
        SchemaVersions = new Dictionary<string, int>
        {
            ["contextPackage"] = 1,
            ["capabilityDiscovery"] = 1,
        },
        Limitations = ["No Gateway", "No Semantic", "No LSP"],
    });
});

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
    var workspace = Workspace.Create(req.Name ?? new DirectoryInfo(req.Path).Name, req.Path);
    await repo.InsertAsync(workspace);
    return Results.Ok(new { id = workspace.Id.Value, name = workspace.Name, status = workspace.Status.ToString() });
});

app.MapGet("/api/v1/workspaces/{id}/status", async (string id, IWorkspaceRepository repo) =>
{
    var ws = await repo.FindByIdAsync(WorkspaceId.Parse(id));
    if (ws is null) return Results.NotFound(new { error = "Workspace not found" });
    return Results.Ok(new
    {
        id = ws.Id.Value,
        name = ws.Name,
        rootPath = ws.RootPath,
        status = ws.Status.ToString(),
        createdAt = ws.CreatedAt,
    });
});

app.MapDelete("/api/v1/workspaces/{id}", async (string id, IWorkspaceRepository repo) =>
{
    await repo.RemoveAsync(WorkspaceId.Parse(id));
    return Results.Ok(new { removed = true });
});

app.Run();

record ImportRequest(string Path, string? Name);
