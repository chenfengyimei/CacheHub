using CacheHub.Storage;
using CacheHub.Storage.Database;
using CacheHub.Storage.Database.Migrations;
using CacheHub.Storage.Repositories;

namespace CacheHub.Cli.Commands;

public static class StatsCommands
{
    private static readonly System.Text.Json.JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    public static async Task<int> HandleAsync(string[] args)
    {
        var outputJson = args.Contains("--output=json", StringComparer.OrdinalIgnoreCase) ||
                         args.Contains("--json", StringComparer.OrdinalIgnoreCase);

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
        ]);
        runner.Migrate();

        var wsRepo = new SqliteWorkspaceRepository(factory);
        var ctxRepo = new SqliteContextPackageRepository(factory);

        var workspaces = await wsRepo.ListAllAsync();
        var totalContexts = 0;
        var totalEstimatedTokens = 0;

        foreach (var ws in workspaces)
        {
            var contexts = await ctxRepo.ListByWorkspaceAsync(ws.Id);
            totalContexts += contexts.Count;
            totalEstimatedTokens += contexts.Sum(c => c.Budget.ActualEstimate);
        }

        var stats = new
        {
            workspaces = workspaces.Count,
            contextPackages = totalContexts,
            totalEstimatedTokens,
            dataDir = appData.Root,
            dbPath,
            workspaceStatuses = workspaces.GroupBy(w => w.Status.ToString())
                .Select(g => new { status = g.Key, count = g.Count() }),
        };

        if (outputJson)
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(stats, _jsonOpts));
        }
        else
        {
            Console.WriteLine("CacheHub Statistics");
            Console.WriteLine(new string('=', 40));
            Console.WriteLine($"Workspaces:          {stats.workspaces}");
            Console.WriteLine($"Context Packages:    {stats.contextPackages}");
            Console.WriteLine($"Total Est. Tokens:   {stats.totalEstimatedTokens:N0}");
            Console.WriteLine($"Data Directory:      {stats.dataDir}");
            Console.WriteLine();
            Console.WriteLine("Workspace Statuses:");
            foreach (var s in stats.workspaceStatuses)
                Console.WriteLine($"  {s.status,-12}  {s.count}");
        }

        return 0;
    }
}
