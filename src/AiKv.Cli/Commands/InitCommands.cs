using AiKv.Core.Configuration;
using AiKv.Core.Security;
using AiKv.Storage;
using AiKv.Storage.Database;
using AiKv.Storage.Database.Migrations;

namespace AiKv.Cli.Commands;

public static class InitCommands
{
    public static async Task<int> HandleAsync(string[] args)
    {
        var path = args.FirstOrDefault() ?? Environment.CurrentDirectory;
        var outputJson = args.Contains("--output=json", StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(path))
        {
            Console.Error.WriteLine($"Error: Directory not found: {path}");
            return 1;
        }

        var steps = new List<(string step, bool success, string detail)>();

        // 1. Initialize data directory
        var appData = new AppDataDirectory();
        appData.EnsureCreated();
        steps.Add(("data-dir", true, appData.Root));

        // 2. Run database migrations
        var dbPath = appData.GetWorkspaceDatabasePath("main");
        var factory = new SqliteConnectionFactory(dbPath);
        var runner = new MigrationRunner(factory, dbPath,
        [
            new Migration0001Initial(),
            new Migration0002Fts5(),
            new Migration0003ContextPackages(),
            new Migration0004Feedback(),
        ]);
        var applied = runner.Migrate();
        steps.Add(("database", true, $"v{runner.GetCurrentVersion()}, {applied} migrations"));

        // 3. Create default config if not exists
        var configManager = new ConfigManager();
        if (!configManager.Exists)
        {
            configManager.Save(new AiKvConfig
            {
                DefaultBudget = new BudgetConfig(),
                Security = new SecurityConfig(),
                Indexing = new IndexingConfig(),
            });
            steps.Add(("config", true, $"Created: {configManager.ConfigPath}"));
        }
        else
        {
            steps.Add(("config", true, $"Exists: {configManager.ConfigPath}"));
        }

        // 4. Create .aikvignore if not exists
        var aikvignorePath = System.IO.Path.Combine(path, ".aikvignore");
        if (!File.Exists(aikvignorePath))
        {
            var examplePath = System.IO.Path.Combine(AppContext.BaseDirectory, ".aikvignore.example");
            var content = File.Exists(examplePath)
                ? await File.ReadAllTextAsync(examplePath)
                : GetDefaultAikvignore();
            await File.WriteAllTextAsync(aikvignorePath, content);
            steps.Add(("aikvignore", true, $"Created: {aikvignorePath}"));
        }
        else
        {
            steps.Add(("aikvignore", true, $"Exists: {aikvignorePath}"));
        }

        // 5. Check if path has a project to import
        var hasProject = Directory.GetFiles(path, "*.csproj", SearchOption.TopDirectoryOnly).Length > 0 ||
                         File.Exists(System.IO.Path.Combine(path, "package.json")) ||
                         File.Exists(System.IO.Path.Combine(path, "go.mod")) ||
                         File.Exists(System.IO.Path.Combine(path, "Cargo.toml")) ||
                         File.Exists(System.IO.Path.Combine(path, "pyproject.toml"));

        if (hasProject)
        {
            steps.Add(("project-detected", true, "Project files found — ready to import"));
        }
        else
        {
            steps.Add(("project-detected", false, "No recognized project files found"));
        }

        // 6. Verify integration
        var integrationOk = true;
        try
        {
            var wsRepo = new Storage.Repositories.SqliteWorkspaceRepository(factory);
            var ws = await wsRepo.ListAllAsync();
            steps.Add(("integration", true, $"{ws.Count} workspace(s) registered"));
        }
        catch (Exception ex)
        {
            integrationOk = false;
            steps.Add(("integration", false, ex.Message));
        }

        if (outputJson)
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                path,
                steps = steps.Select(s => new { step = s.step, success = s.success, detail = s.detail }),
                ready = integrationOk,
            }, _jsonOpts));
        }
        else
        {
            Console.WriteLine($"AI_KV Initialization: {path}");
            Console.WriteLine(new string('=', 50));
            foreach (var (step, success, detail) in steps)
            {
                var icon = success ? "✓" : "⚠";
                Console.WriteLine($"  {icon} {step,-20} {detail}");
            }
            Console.WriteLine();
            Console.WriteLine(integrationOk ? "✅ AI_KV is ready to use!" : "⚠ Some steps need attention.");
            Console.WriteLine();
            Console.WriteLine("Next steps:");
            Console.WriteLine("  aikv workspace import " + path);
            Console.WriteLine("  aikv index build --id=<workspace-id>");
            Console.WriteLine("  aikv context build --workspace=<id> --task=\"<your task>\"");
        }

        return integrationOk ? 0 : 1;
    }

    private static string GetDefaultAikvignore() => """
        # .aikvignore — AI_KV ignore rules
        # Merged with: system defaults > .gitignore > .aikvignore > user rules

        # Build outputs
        bin/
        obj/
        dist/
        build/
        target/

        # Dependencies
        node_modules/
        __pycache__/
        .venv/

        # IDE
        .vs/
        .vscode/
        .idea/
        *.user

        # AI_KV internal
        .aikv/
        """;

    private static readonly System.Text.Json.JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };
}
