using CacheHub.Storage;
using CacheHub.Storage.Database;
using CacheHub.Storage.Database.Migrations;
using CacheHub.Storage.Search;
using CacheHub.Core.Identifiers;

namespace CacheHub.Cli.Commands;

public static class SearchCommands
{
    private static readonly System.Text.Json.JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    public static async Task<int> HandleAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: cachehub search <query> [--workspace=<id>] [--limit=N] [--output=json]");
            return 1;
        }

        var query = args[0];
        var wsId = GetOpt(args, "--workspace");
        var limitStr = GetOpt(args, "--limit") ?? "50";
        var outputJson = HasFlag(args, "--output=json") || HasFlag(args, "--json");

        if (string.IsNullOrEmpty(wsId))
        {
            Console.Error.WriteLine("Error: --workspace=<id> is required");
            Console.Error.WriteLine("Usage: cachehub search <query> --workspace=<id> [--limit=N] [--output=json]");
            return 1;
        }

        if (!int.TryParse(limitStr, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var limit))
        {
            Console.Error.WriteLine($"Error: Invalid limit value: {limitStr}");
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

        // Get active snapshot for workspace
        var snapshotId = await GetActiveSnapshotIdAsync(factory, wsId);
        if (snapshotId is null)
        {
            Console.Error.WriteLine($"No active index snapshot found for workspace: {wsId}");
            Console.Error.WriteLine("Run 'cachehub index build --id=<workspace-id>' first.");
            return 1;
        }

        var fts = new Fts5Index(factory);
        var results = await fts.SearchAsync(snapshotId, query, limit);

        if (results.Count == 0)
        {
            if (outputJson)
                Console.WriteLine("[]");
            else
                Console.WriteLine("No results found.");
            return 0;
        }

        if (outputJson)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(results.Select(r => new
            {
                path = r.Path,
                language = r.Language,
                snippet = r.Snippet,
            }), _jsonOpts);
            Console.WriteLine(json);
        }
        else
        {
            Console.WriteLine($"Search results for \"{query}\" ({results.Count} matches):");
            Console.WriteLine();
            foreach (var r in results)
            {
                Console.WriteLine($"  [{r.Language}] {r.Path}");
                Console.WriteLine($"    {r.Snippet}");
                Console.WriteLine();
            }
        }

        return 0;
    }

    private static async Task<IndexSnapshotId?> GetActiveSnapshotIdAsync(SqliteConnectionFactory factory, string workspaceId)
    {
        await using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM index_snapshots WHERE workspace_id = $ws AND status IN ('Active', 'ActiveDegraded') LIMIT 1;";
        cmd.Parameters.AddWithValue("$ws", workspaceId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
            return IndexSnapshotId.Parse(reader.GetString(0));
        return null;
    }

    private static string? GetOpt(string[] args, string prefix) =>
        args.FirstOrDefault(a => a.StartsWith(prefix + "=", StringComparison.OrdinalIgnoreCase))?[(prefix.Length + 1)..];

    private static bool HasFlag(string[] args, string flag) =>
        args.Contains(flag, StringComparer.OrdinalIgnoreCase);
}
