using AiKv.Indexing.Detection;

namespace AiKv.Cli.Commands;

public static class DetectCommands
{
    private static readonly System.Text.Json.JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    public static int Handle(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: aikv detect <path> [--plan]");
            return 1;
        }

        var path = args[0];
        var showPlan = args.Contains("--plan", StringComparer.OrdinalIgnoreCase);
        var outputJson = args.Contains("--output=json", StringComparer.OrdinalIgnoreCase) ||
                         args.Contains("--json", StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(path))
        {
            Console.Error.WriteLine($"Error: Directory not found: {path}");
            return 1;
        }

        var engine = new ProjectDetectionEngine();
        var result = engine.Detect(path);

        if (showPlan)
        {
            var plan = engine.GeneratePlan(result);

            if (outputJson)
            {
                var json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    rootPath = plan.RootPath,
                    components = plan.DetectedComponents,
                    actions = plan.Actions.Select(a => new
                    {
                        command = a.Command,
                        purpose = a.Purpose,
                        workingDirectory = a.WorkingDirectory,
                        requiresNetwork = a.RequiresNetwork,
                        writesToDisk = a.WritesToDisk,
                        mayRunScripts = a.MayRunScripts,
                        approval = a.Approval.ToString(),
                        risks = a.Risks,
                    }),
                    missingTools = plan.MissingTools,
                }, _jsonOpts);
                Console.WriteLine(json);
            }
            else
            {
                Console.WriteLine($"Project: {result.RootPath}");
                Console.WriteLine($"  Monorepo: {result.IsMonorepo}");
                Console.WriteLine($"  Components ({result.Components.Count}):");
                foreach (var comp in result.Components)
                    Console.WriteLine($"    {comp.Language} ({comp.Framework ?? comp.BuildSystem ?? "unknown"}) — {comp.Path}");
                Console.WriteLine();
                Console.WriteLine($"Initialization Plan ({plan.Actions.Count} actions):");
                foreach (var action in plan.Actions)
                {
                    Console.WriteLine($"  [{action.Approval}] {action.Command}");
                    Console.WriteLine($"    Purpose: {action.Purpose}");
                    Console.WriteLine($"    Network: {action.RequiresNetwork}, Writes: {action.WritesToDisk}, Scripts: {action.MayRunScripts}");
                    if (action.Risks.Count > 0)
                        Console.WriteLine($"    Risks: {string.Join(", ", action.Risks)}");
                }
            }
        }
        else
        {
            if (outputJson)
            {
                var json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    rootPath = result.RootPath,
                    isMonorepo = result.IsMonorepo,
                    components = result.Components.Select(c => new
                    {
                        id = c.Id,
                        path = c.Path,
                        language = c.Language,
                        framework = c.Framework,
                        buildSystem = c.BuildSystem,
                        packageManager = c.PackageManager,
                        confidence = c.Confidence,
                    }),
                    languageStats = result.LanguageStats,
                }, _jsonOpts);
                Console.WriteLine(json);
            }
            else
            {
                Console.WriteLine($"Project: {result.RootPath}");
                Console.WriteLine($"  Monorepo: {result.IsMonorepo}");
                Console.WriteLine($"  Components ({result.Components.Count}):");
                foreach (var comp in result.Components)
                {
                    Console.WriteLine($"    {comp.Language} ({comp.Framework ?? comp.BuildSystem ?? "unknown"})");
                    Console.WriteLine($"      Path: {comp.Path}");
                    Console.WriteLine($"      Evidence: {string.Join(", ", comp.Evidence)}");
                }
                if (result.LanguageStats.Count > 0)
                {
                    Console.WriteLine("  Language Stats:");
                    foreach (var kv in result.LanguageStats)
                        Console.WriteLine($"    {kv.Key}: {kv.Value}");
                }
            }
        }

        return 0;
    }
}
