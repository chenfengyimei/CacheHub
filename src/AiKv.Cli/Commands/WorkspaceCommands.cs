using AiKv.Core.Workspaces;
using AiKv.Storage;
using AiKv.Storage.Database;
using AiKv.Storage.Database.Migrations;
using AiKv.Storage.Repositories;

namespace AiKv.Cli.Commands;

public static class WorkspaceCommands
{
    public static async Task<int> HandleAsync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        return args[0] switch
        {
            "workspace" => await HandleWorkspaceAsync(args.AsSpan(1).ToArray()),
            "capabilities" => HandleCapabilities(),
            _ => PrintUsage(),
        };
    }

    private static async Task<int> HandleWorkspaceAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: aikv workspace <import|status|list|remove> [options]");
            return 1;
        }

        return args[0] switch
        {
            "import" => await HandleWorkspaceImportAsync(args.AsSpan(1).ToArray()),
            "list" => await HandleWorkspaceListAsync(),
            "status" => await HandleWorkspaceStatusAsync(args.AsSpan(1).ToArray()),
            "remove" => await HandleWorkspaceRemoveAsync(args.AsSpan(1).ToArray()),
            _ => 1,
        };
    }

    private static async Task<int> HandleWorkspaceImportAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Error: path argument is required");
            Console.Error.WriteLine("Usage: aikv workspace import <path> [--name <name>]");
            return 1;
        }

        var path = args[0];
        var name = args.FirstOrDefault(a => a.StartsWith("--name="))?["--name=".Length..]
                   ?? new DirectoryInfo(path).Name;

        var appData = new AppDataDirectory();
        appData.EnsureCreated();
        var dbPath = appData.GetWorkspaceDatabasePath("main");
        var factory = new SqliteConnectionFactory(dbPath);
        var runner = new MigrationRunner(factory, dbPath, [new Migration0001Initial()]);
        runner.Migrate();
        var repo = new SqliteWorkspaceRepository(factory);
        var workspace = Workspace.Create(name, path);
        await repo.InsertAsync(workspace);

        Console.WriteLine($"Workspace imported: {workspace.Id.Value}");
        Console.WriteLine($"  Name: {workspace.Name}");
        Console.WriteLine($"  Path: {workspace.RootPath}");
        Console.WriteLine($"  Status: {workspace.Status}");
        return 0;
    }

    private static async Task<int> HandleWorkspaceListAsync()
    {
        var appData = new AppDataDirectory();
        var dbPath = appData.GetWorkspaceDatabasePath("main");
        var factory = new SqliteConnectionFactory(dbPath);
        var repo = new SqliteWorkspaceRepository(factory);
        var workspaces = await repo.ListAllAsync();

        if (workspaces.Count == 0)
        {
            Console.WriteLine("No workspaces registered.");
            return 0;
        }

        Console.WriteLine($"{"ID",-36}  {"Status",-12}  {"Name"}");
        Console.WriteLine(new string('-', 70));
        foreach (var ws in workspaces)
        {
            Console.WriteLine($"{ws.Id.Value,-36}  {ws.Status,-12}  {ws.Name}");
        }
        return 0;
    }

    private static async Task<int> HandleWorkspaceStatusAsync(string[] args)
    {
        var id = args.FirstOrDefault(a => a.StartsWith("--id="))?["--id=".Length..];
        if (string.IsNullOrEmpty(id))
        {
            Console.Error.WriteLine("Error: --id=<workspace-id> is required");
            return 1;
        }

        var appData = new AppDataDirectory();
        var dbPath = appData.GetWorkspaceDatabasePath("main");
        var factory = new SqliteConnectionFactory(dbPath);
        var repo = new SqliteWorkspaceRepository(factory);
        var ws = await repo.FindByIdAsync(Core.Identifiers.WorkspaceId.Parse(id));

        if (ws is null)
        {
            Console.Error.WriteLine($"Workspace not found: {id}");
            return 1;
        }

        Console.WriteLine($"ID: {ws.Id.Value}");
        Console.WriteLine($"Name: {ws.Name}");
        Console.WriteLine($"Path: {ws.RootPath}");
        Console.WriteLine($"Status: {ws.Status}");
        Console.WriteLine($"Created: {ws.CreatedAt:O}");
        return 0;
    }

    private static async Task<int> HandleWorkspaceRemoveAsync(string[] args)
    {
        var id = args.FirstOrDefault(a => a.StartsWith("--id="))?["--id=".Length..];
        if (string.IsNullOrEmpty(id))
        {
            Console.Error.WriteLine("Error: --id=<workspace-id> is required");
            return 1;
        }

        var appData = new AppDataDirectory();
        var dbPath = appData.GetWorkspaceDatabasePath("main");
        var factory = new SqliteConnectionFactory(dbPath);
        var repo = new SqliteWorkspaceRepository(factory);
        await repo.RemoveAsync(Core.Identifiers.WorkspaceId.Parse(id));

        Console.WriteLine($"Workspace removed: {id}");
        Console.WriteLine("Note: Only AI_KV data was removed. Source code is untouched.");
        return 0;
    }

    private static int HandleCapabilities()
    {
        Console.WriteLine($"{{");
        Console.WriteLine($"  \"version\": \"0.1.0-alpha\",");
        Console.WriteLine($"  \"protocolVersion\": \"1.0\",");
        Console.WriteLine($"  \"capabilities\": {{");
        Console.WriteLine($"    \"workspaceImport\": true,");
        Console.WriteLine($"    \"contextBuild\": false,");
        Console.WriteLine($"    \"contextExpand\": false,");
        Console.WriteLine($"    \"contextFeedback\": false,");
        Console.WriteLine($"    \"gateway\": false,");
        Console.WriteLine($"    \"semantic\": false,");
        Console.WriteLine($"    \"lsp\": false");
        Console.WriteLine($"  }}");
        Console.WriteLine($"}}");
        return 0;
    }

    private static int PrintUsage()
    {
        Console.WriteLine("AI_KV - Local code context infrastructure");
        Console.WriteLine();
        Console.WriteLine("Usage: aikv <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  capabilities              Show available capabilities");
        Console.WriteLine("  workspace import <path>   Import a local directory as a workspace");
        Console.WriteLine("  workspace list            List all workspaces");
        Console.WriteLine("  workspace status --id=<id> Show workspace status");
        Console.WriteLine("  workspace remove --id=<id> Remove a workspace");
        return 1;
    }
}
