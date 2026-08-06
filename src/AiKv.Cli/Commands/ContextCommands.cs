using System.Text.Json;
using AiKv.Context.Engine;
using AiKv.Context.Parsing;
using AiKv.Context.Recall;
using AiKv.Core.Context;
using AiKv.Core.Identifiers;
using AiKv.Core.Workspaces;
using AiKv.Storage;
using AiKv.Storage.Database;
using AiKv.Storage.Database.Migrations;
using AiKv.Storage.Repositories;

namespace AiKv.Cli.Commands;

/// <summary>
/// Handles `aikv context build/inspect/export/expand` commands.
/// All commands support --output=json.
/// </summary>
public static class ContextCommands
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    public static async Task<int> HandleAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: aikv context <build|inspect|export|expand> [options]");
            return 1;
        }

        return args[0] switch
        {
            "build" => await BuildAsync(args.AsSpan(1).ToArray()),
            "inspect" => await InspectAsync(args.AsSpan(1).ToArray()),
            "export" => await ExportAsync(args.AsSpan(1).ToArray()),
            "expand" => await ExpandAsync(args.AsSpan(1).ToArray()),
            _ => 1,
        };
    }

    private static async Task<int> BuildAsync(string[] args)
    {
        var wsId = GetOpt(args, "--workspace");
        var task = GetOpt(args, "--task");
        var outputJson = HasFlag(args, "--output=json") || HasFlag(args, "--json");

        if (string.IsNullOrEmpty(wsId) || string.IsNullOrEmpty(task))
        {
            Console.Error.WriteLine("Error: --workspace=<id> and --task=<text> are required");
            return 1;
        }

        var appData = new AppDataDirectory();
        var dbPath = appData.GetWorkspaceDatabasePath("main");
        var factory = new SqliteConnectionFactory(dbPath);
        var runner = new MigrationRunner(factory, dbPath,
        [
            new Migration0001Initial(),
            new Migration0002Fts5(),
        ]);
        runner.Migrate();

        var repo = new SqliteWorkspaceRepository(factory);
        var workspace = await repo.FindByIdAsync(WorkspaceId.Parse(wsId));
        if (workspace is null)
        {
            Console.Error.WriteLine($"Workspace not found: {wsId}");
            return 1;
        }

        Console.Error.WriteLine($"Building context for: {workspace.Name}");
        Console.Error.WriteLine($"  Task: {task}");

        var engine = new ContextEngine();
        var snapshotId = IndexSnapshotId.New();

        // Collect indexed files from FTS
        var indexedFiles = GetIndexedFiles(factory, snapshotId);

        var manifest = engine.Build(
            new ContextBuildRequest
            {
                WorkspaceId = workspace.Id,
                IndexSnapshotId = snapshotId,
                Task = task,
            },
            () => indexedFiles,
            path => File.Exists(path) ? File.ReadAllText(path) : "",
            path => "sha256:pending");

        if (outputJson)
        {
            var json = JsonSerializer.Serialize(manifest, JsonOpts);
            Console.WriteLine(json);
        }
        else
        {
            Console.WriteLine($"Context Package: {manifest.Id.Value}");
            Console.WriteLine($"  Schema: v{manifest.SchemaVersion}");
            Console.WriteLine($"  Task: {manifest.Task.OriginalText}");
            Console.WriteLine($"  Ranking: {manifest.Ranking.ProfileId} v{manifest.Ranking.ProfileVersion}");
            Console.WriteLine($"  Budget: {manifest.Budget.ActualEstimate} / {manifest.Budget.ContextTarget} (hard limit: {manifest.Budget.ContextHardLimit})");
            Console.WriteLine($"  Selected Files ({manifest.SelectedFiles.Count}):");
            foreach (var f in manifest.SelectedFiles)
                Console.WriteLine($"    [{f.Mode,-10}] {f.Score:F2}  {f.Path}");
            Console.WriteLine($"  Excluded ({manifest.ExcludedCandidates.Count}):");
            foreach (var e in manifest.ExcludedCandidates)
                Console.WriteLine($"    [{e.Score:F2}] {e.Path} — {e.Reason}");
        }

        return 0;
    }

    private static Task<int> InspectAsync(string[] args)
    {
        var ctxId = GetOpt(args, "--id");
        if (string.IsNullOrEmpty(ctxId))
        {
            Console.Error.WriteLine("Error: --id=<context-id> is required");
            return Task.FromResult(1);
        }

        Console.Error.WriteLine($"Context inspect not yet persisted. Context ID: {ctxId}");
        Console.WriteLine($"{{ \"id\": \"{ctxId}\", \"status\": \"not_persisted\" }}");
        return Task.FromResult(0);
    }

    private static Task<int> ExportAsync(string[] args)
    {
        var ctxId = GetOpt(args, "--id");
        var format = GetOpt(args, "--format") ?? "markdown";

        if (string.IsNullOrEmpty(ctxId))
        {
            Console.Error.WriteLine("Error: --id=<context-id> is required");
            return Task.FromResult(1);
        }

        Console.Error.WriteLine($"Export not yet fully implemented. Context ID: {ctxId}, Format: {format}");
        Console.WriteLine($"# Context Package {ctxId}");
        Console.WriteLine();
        Console.WriteLine("> Export is a placeholder in the current version.");
        return Task.FromResult(0);
    }

    private static Task<int> ExpandAsync(string[] args)
    {
        var ctxId = GetOpt(args, "--id");
        var symbol = GetOpt(args, "--symbol");

        if (string.IsNullOrEmpty(ctxId))
        {
            Console.Error.WriteLine("Error: --id=<context-id> is required");
            return Task.FromResult(1);
        }

        Console.Error.WriteLine($"Expand: ctx={ctxId}, symbol={symbol}");
        Console.WriteLine($"{{ \"contextId\": \"{ctxId}\", \"expandedSymbol\": \"{symbol ?? "none"}\", \"status\": \"not_persisted\" }}");
        return Task.FromResult(0);
    }

    private static List<Context.Recall.IndexedFileInfo> GetIndexedFiles(SqliteConnectionFactory factory, IndexSnapshotId snapshotId)
    {
        // In a full implementation, this would query the FTS5 + files tables.
        // For now, return empty list — the Context Engine handles empty gracefully.
        return [];
    }

    private static string? GetOpt(string[] args, string prefix) =>
        args.FirstOrDefault(a => a.StartsWith(prefix + "=", StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

    private static bool HasFlag(string[] args, string flag) =>
        args.Contains(flag, StringComparer.OrdinalIgnoreCase);
}
