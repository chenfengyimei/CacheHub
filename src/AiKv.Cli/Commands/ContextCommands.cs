using System.Text.Json;
using AiKv.Context.Engine;
using AiKv.Context.Recall;
using AiKv.Core.Context;
using AiKv.Core.Feedback;
using AiKv.Core.Identifiers;
using AiKv.Core.Workspaces;
using AiKv.Storage;
using AiKv.Storage.Database;
using AiKv.Storage.Database.Migrations;
using AiKv.Storage.Repositories;

namespace AiKv.Cli.Commands;

public static class ContextCommands
{
    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    public static async Task<int> HandleAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: aikv context <build|inspect|export|expand|feedback> [options]");
            return 1;
        }

        return args[0] switch
        {
            "build" => await BuildAsync(args.AsSpan(1).ToArray()),
            "inspect" => await InspectAsync(args.AsSpan(1).ToArray()),
            "export" => await ExportAsync(args.AsSpan(1).ToArray()),
            "expand" => await ExpandAsync(args.AsSpan(1).ToArray()),
            "feedback" => await FeedbackAsync(args.AsSpan(1).ToArray()),
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

        var (factory, workspace) = await ResolveWorkspaceAsync(wsId);
        if (workspace is null) return 1;

        Console.Error.WriteLine($"Building context for: {workspace.Name}");
        Console.Error.WriteLine($"  Task: {task}");

        var engine = new ContextEngine();
        var snapshotId = IndexSnapshotId.New();
        var indexedFiles = await GetIndexedFilesAsync(factory, wsId);

        var manifest = engine.Build(
            new ContextBuildRequest
            {
                WorkspaceId = workspace.Id,
                IndexSnapshotId = snapshotId,
                Task = task,
            },
            () => indexedFiles,
            path => ResolveFileContent(workspace.RootPath, path),
            path => "sha256:pending");

        if (outputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(manifest, _jsonOpts));
        }
        else
        {
            Console.WriteLine($"Context Package: {manifest.Id.Value}");
            Console.WriteLine($"  Schema: v{manifest.SchemaVersion}");
            Console.WriteLine($"  Task: {manifest.Task.OriginalText}");
            Console.WriteLine($"  Ranking: {manifest.Ranking.ProfileId} v{manifest.Ranking.ProfileVersion}");
            Console.WriteLine($"  Budget: {manifest.Budget.ActualEstimate} / {manifest.Budget.ContextTarget} (hard: {manifest.Budget.ContextHardLimit})");
            Console.WriteLine($"  Selected ({manifest.SelectedFiles.Count}):");
            foreach (var f in manifest.SelectedFiles)
                Console.WriteLine($"    [{f.Mode,-20}] {f.Score:F2}  {f.Path}");
            if (manifest.ExcludedCandidates.Count > 0)
            {
                Console.WriteLine($"  Excluded ({manifest.ExcludedCandidates.Count}):");
                foreach (var e in manifest.ExcludedCandidates)
                    Console.WriteLine($"    [{e.Score:F2}] {e.Path} — {e.Reason}");
            }
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

        if (format == "json")
            Console.WriteLine($"{{ \"id\": \"{ctxId}\", \"format\": \"json\", \"status\": \"placeholder\" }}");
        else
        {
            Console.WriteLine($"# Context Package {ctxId}");
            Console.WriteLine();
            Console.WriteLine("> Export is a placeholder in the current version.");
        }
        return Task.FromResult(0);
    }

    private static Task<int> ExpandAsync(string[] args)
    {
        var ctxId = GetOpt(args, "--id");
        var symbol = GetOpt(args, "--symbol");
        var file = GetOpt(args, "--file");
        if (string.IsNullOrEmpty(ctxId))
        {
            Console.Error.WriteLine("Error: --id=<context-id> is required");
            return Task.FromResult(1);
        }

        var detail = symbol is not null ? $"symbol={symbol}" : $"file={file}";
        Console.WriteLine($"{{ \"contextId\": \"{ctxId}\", \"expanded\": \"{detail}\", \"status\": \"not_persisted\" }}");
        return Task.FromResult(0);
    }

    private static async Task<int> FeedbackAsync(string[] args)
    {
        var ctxId = GetOpt(args, "--id");
        var filePath = GetOpt(args, "--file");
        if (string.IsNullOrEmpty(ctxId) || string.IsNullOrEmpty(filePath))
        {
            Console.Error.WriteLine("Error: --id=<context-id> and --file=<path> are required");
            return 1;
        }

        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"Error: File not found: {filePath}");
            return 1;
        }

        var json = await File.ReadAllTextAsync(filePath);
        var feedback = ContextFeedback.ParseJson(json);
        if (feedback is null)
        {
            Console.Error.WriteLine("Error: Invalid feedback JSON");
            return 1;
        }

        Console.Error.WriteLine($"Feedback received for context: {ctxId}");
        Console.Error.WriteLine($"  Client: {feedback.ClientId ?? "unknown"}");
        Console.Error.WriteLine($"  Files read: {feedback.FilesActuallyRead.Count}");
        Console.Error.WriteLine($"  Task completed: {feedback.TaskCompleted}");
        Console.Error.WriteLine($"  Missing context: {feedback.MissingContextReported}");
        Console.WriteLine($"{{ \"received\": true, \"contextId\": \"{ctxId}\" }}");
        return 0;
    }

    private static async Task<(SqliteConnectionFactory factory, Workspace? workspace)> ResolveWorkspaceAsync(string wsId)
    {
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
        var ws = await repo.FindByIdAsync(WorkspaceId.Parse(wsId));
        if (ws is null)
            Console.Error.WriteLine($"Workspace not found: {wsId}");

        return (factory, ws);
    }

    private static async Task<List<IndexedFileInfo>> GetIndexedFilesAsync(SqliteConnectionFactory factory, string workspaceId)
    {
        var result = new List<IndexedFileInfo>();
        await using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT f.normalized_path, f.size, f.language
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
                Language = reader.IsDBNull(2) ? "unknown" : reader.GetString(2),
                Size = reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
                Symbols = [],
            });
        }
        return result;
    }

    private static string ResolveFileContent(string rootPath, string relativePath)
    {
        var fullPath = Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(fullPath) ? File.ReadAllText(fullPath) : "";
    }

    private static string? GetOpt(string[] args, string prefix) =>
        args.FirstOrDefault(a => a.StartsWith(prefix + "=", StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

    private static bool HasFlag(string[] args, string flag) =>
        args.Contains(flag, StringComparer.OrdinalIgnoreCase);
}
