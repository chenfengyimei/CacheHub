using CacheHub.Context.Explain;
using CacheHub.Core.Context;
using CacheHub.Core.Identifiers;
using CacheHub.Storage;
using CacheHub.Storage.Database;
using CacheHub.Storage.Database.Migrations;
using CacheHub.Storage.Repositories;

namespace CacheHub.Cli.Commands;

public static partial class ExplainCommands
{
    public static async Task<int> HandleAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: cachehub explain --id=<context-id> [--output=json]");
            return 1;
        }

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

        var repo = new SqliteContextPackageRepository(factory);
        var manifest = await repo.FindByIdAsync(ContextPackageId.Parse(ctxId));

        if (manifest is null)
        {
            Console.Error.WriteLine($"Context package not found: {ctxId}");
            return 1;
        }

        var explanations = ContextExplainer.Explain(manifest);
        var misses = ContextExplainer.DetectPotentialMisses(manifest);
        var budgetSummary = ContextExplainer.BudgetSummary(manifest);

        if (outputJson)
        {
            var result = new
            {
                contextId = ctxId,
                budgetSummary,
                potentialMisses = misses,
                files = explanations.Select(e => new
                {
                    path = e.Path,
                    selected = e.Selected,
                    score = e.Score,
                    reasons = e.Reasons,
                    exclusionReason = e.ExclusionReason,
                }),
            };
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result, _jsonOpts));
        }
        else
        {
            Console.WriteLine($"Context Explain: {ctxId}");
            Console.WriteLine($"  Budget: {budgetSummary}");
            Console.WriteLine();

            if (misses.Count > 0)
            {
                Console.WriteLine("Potential Misses (high-score excluded files):");
                foreach (var m in misses)
                    Console.WriteLine($"  ⚠ {m}");
                Console.WriteLine();
            }

            Console.WriteLine("File Explanations:");
            foreach (var e in explanations)
            {
                var status = e.Selected ? "✓ SELECTED" : "✗ EXCLUDED";
                Console.WriteLine($"  [{status}] {e.Path} (score: {e.Score:F2})");
                if (e.Reasons.Count > 0)
                    Console.WriteLine($"      Reasons: {string.Join(", ", e.Reasons)}");
                if (e.ExclusionReason is not null)
                    Console.WriteLine($"      Excluded: {e.ExclusionReason}");
            }
        }

        return 0;
    }

    private static readonly System.Text.Json.JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    private static string? GetOpt(string[] args, string prefix) =>
        args.FirstOrDefault(a => a.StartsWith(prefix + "=", StringComparison.OrdinalIgnoreCase))?[(prefix.Length + 1)..];

    private static bool HasFlag(string[] args, string flag) =>
        args.Contains(flag, StringComparer.OrdinalIgnoreCase);
}
