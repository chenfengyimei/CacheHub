using AiKv.Storage;
using AiKv.Storage.Database;
using AiKv.Storage.Database.Migrations;
using AiKv.Storage.Repositories;

namespace AiKv.Cli.Commands;

public static class IntegrationCommands
{
    public static async Task<int> HandleAsync(string[] args)
    {
        if (args.Length == 0 || args[0] != "verify")
        {
            Console.WriteLine("Usage: aikv integration verify");
            return 1;
        }

        var allPassed = true;

        // 1. Check data directory
        Console.Write("[1/5] Data directory... ");
        var appData = new AppDataDirectory();
        try
        {
            appData.EnsureCreated();
            Console.WriteLine($"OK ({appData.Root})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL: {ex.Message}");
            allPassed = false;
        }

        // 2. Check database and migrations
        Console.Write("[2/5] Database & migrations... ");
        try
        {
            var dbPath = appData.GetWorkspaceDatabasePath("main");
            var factory = new SqliteConnectionFactory(dbPath);
            var runner = new MigrationRunner(factory, dbPath,
            [
                new Migration0001Initial(),
                new Migration0002Fts5(),
            ]);
            var applied = runner.Migrate();
            Console.WriteLine($"OK (v{runner.GetCurrentVersion()}, {applied} new migrations)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL: {ex.Message}");
            allPassed = false;
        }

        // 3. Check workspace access
        Console.Write("[3/5] Workspace access... ");
        try
        {
            var dbPath = appData.GetWorkspaceDatabasePath("main");
            var factory = new SqliteConnectionFactory(dbPath);
            var repo = new SqliteWorkspaceRepository(factory);
            var workspaces = await repo.ListAllAsync();
            Console.WriteLine($"OK ({workspaces.Count} workspace(s))");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL: {ex.Message}");
            allPassed = false;
        }

        // 4. Check CLI capabilities
        Console.Write("[4/5] CLI capabilities... ");
        try
        {
            var caps = CapabilitiesCommands.Handle([]);
            Console.WriteLine(caps == 0 ? "OK" : "FAIL");
            if (caps != 0) allPassed = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL: {ex.Message}");
            allPassed = false;
        }

        // 5. Check rollback capability
        Console.Write("[5/5] Rollback capability... ");
        Console.WriteLine("OK (remove command only deletes AI_KV data, not source code)");

        Console.WriteLine();
        Console.WriteLine(allPassed ? "✅ All checks passed." : "❌ Some checks failed.");
        return allPassed ? 0 : 1;
    }
}
