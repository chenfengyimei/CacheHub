using CacheHub.Core.Identifiers;
using CacheHub.Core.Indexing;
using CacheHub.Core.Jobs;
using CacheHub.Core.Paths;
using CacheHub.Core.Workspaces;
using CacheHub.Indexing.Detection;
using CacheHub.Indexing.Hashing;
using CacheHub.Indexing.IgnoreRules;
using CacheHub.Indexing.Reconciliation;
using CacheHub.Indexing.Scanning;
using CacheHub.Indexing.States;
using CacheHub.Storage;
using CacheHub.Storage.Database;
using CacheHub.Storage.Database.Migrations;
using CacheHub.Storage.Repositories;
using CacheHub.Storage.Search;
using Microsoft.Data.Sqlite;

namespace CacheHub.Cli.Commands;

public static class IndexCommands
{
    public static async Task<int> HandleAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: cachehub index <build|refresh|status|verify> [options]");
            return 1;
        }

        return args[0] switch
        {
            "build" => await BuildAsync(args.AsSpan(1).ToArray()),
            "refresh" => await RefreshAsync(args.AsSpan(1).ToArray()),
            "status" => await StatusAsync(args.AsSpan(1).ToArray()),
            "verify" => await VerifyAsync(args.AsSpan(1).ToArray()),
            _ => PrintUsage(),
        };
    }

    private static async Task<int> BuildAsync(string[] args)
    {
        var wsId = GetOption(args, "--id");
        if (string.IsNullOrEmpty(wsId))
        {
            Console.Error.WriteLine("Error: --id=<workspace-id> is required");
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
        ]);
        runner.Migrate();

        var repo = new SqliteWorkspaceRepository(factory);
        var workspace = await repo.FindByIdAsync(WorkspaceId.Parse(wsId));
        if (workspace is null)
        {
            Console.Error.WriteLine($"Workspace not found: {wsId}");
            return 1;
        }

        Console.WriteLine($"Building index for: {workspace.Name}");
        Console.WriteLine($"  Root: {workspace.RootPath}");

        // Create snapshot
        var snapshotId = IndexSnapshotId.New();
        await InsertSnapshotAsync(factory, snapshotId, workspace.Id);

        // Build ignore rules
        var ignoreEngine = new IgnoreRuleEngine()
            .WithDefaults()
            .WithGitIgnore(Path.Combine(workspace.RootPath, ".gitignore"))
            .WithCacheHubIgnore(Path.Combine(workspace.RootPath, ".cachehubignore"));

        Console.WriteLine($"  Ignore rules hash: {ignoreEngine.GetRulesHash()}");

        // Enumerate files
        var enumerator = new DirectoryEnumerator();
        var fts = new Fts5Index(factory);
        var fileCount = 0;
        var failedCount = 0;
        var ignoredCount = 0;

        await foreach (var file in enumerator.EnumerateAsync(workspace.RootPath))
        {
            if (file.IsDirectory) continue;

            var relativePath = PathNormalizer.GetRelativePath(workspace.RootPath, file.Path);
            if (ignoreEngine.IsIgnored(relativePath))
            {
                ignoredCount++;
                continue;
            }

            try
            {
                var typeInfo = FileTypeDetector.Detect(file.Path, file.Size);
                if (!typeInfo.ShouldIndex)
                {
                    ignoredCount++;
                    continue;
                }

                var hash = await FileHasher.HashAsync(file.Path, file.Size);
                var content = await File.ReadAllTextAsync(file.Path);

                await fts.IndexFileAsync(
                    snapshotId, relativePath, relativePath,
                    content, typeInfo.Language,
                    hash.IsFullHash ? hash.Hash : "pending");

                await InsertFileAsync(
                    factory, snapshotId, relativePath, relativePath,
                    file.Size, hash.IsFullHash ? hash.Hash : "pending",
                    typeInfo.Language, typeInfo.IsBinary);

                fileCount++;
                if (fileCount % 1000 == 0)
                    Console.WriteLine($"  Indexed {fileCount} files...");
            }
            catch (Exception ex)
            {
                failedCount++;
                Console.Error.WriteLine($"  Failed: {relativePath} - {ex.Message}");
            }
        }

        // Activate snapshot
        await ActivateSnapshotAsync(factory, snapshotId, fileCount);
        await repo.UpdateStatusAsync(workspace.Id, WorkspaceStatus.Ready);

        Console.WriteLine($"Index build complete:");
        Console.WriteLine($"  Indexed: {fileCount}");
        Console.WriteLine($"  Ignored: {ignoredCount}");
        Console.WriteLine($"  Failed: {failedCount}");
        Console.WriteLine($"  Snapshot: {snapshotId.Value}");
        return 0;
    }

    private static Task<int> RefreshAsync(string[] args)
    {
        var wsId = GetOption(args, "--id");
        if (string.IsNullOrEmpty(wsId))
        {
            Console.Error.WriteLine("Error: --id=<workspace-id> is required");
            return Task.FromResult(1);
        }

        Console.WriteLine($"Refresh: Not yet implemented. Use 'index build' to rebuild.");
        return Task.FromResult(0);
    }

    private static async Task<int> StatusAsync(string[] args)
    {
        var wsId = GetOption(args, "--id");
        if (string.IsNullOrEmpty(wsId))
        {
            Console.Error.WriteLine("Error: --id=<workspace-id> is required");
            return 1;
        }

        var appData = new AppDataDirectory();
        var dbPath = appData.GetWorkspaceDatabasePath("main");
        var factory = new SqliteConnectionFactory(dbPath);
        var repo = new SqliteWorkspaceRepository(factory);
        var workspace = await repo.FindByIdAsync(WorkspaceId.Parse(wsId));

        if (workspace is null)
        {
            Console.Error.WriteLine($"Workspace not found: {wsId}");
            return 1;
        }

        Console.WriteLine($"Workspace: {workspace.Name}");
        Console.WriteLine($"  Status: {workspace.Status}");
        Console.WriteLine($"  Root: {workspace.RootPath}");

        var activeSnapshot = await GetActiveSnapshotAsync(factory, workspace.Id);
        if (activeSnapshot is not null)
        {
            Console.WriteLine($"  Active Snapshot: {activeSnapshot.Value.id}");
            Console.WriteLine($"  Files: {activeSnapshot.Value.fileCount}");
        }
        else
        {
            Console.WriteLine("  No active snapshot");
        }
        return 0;
    }

    private static async Task<int> VerifyAsync(string[] args)
    {
        var wsId = GetOption(args, "--id");
        if (string.IsNullOrEmpty(wsId))
        {
            Console.Error.WriteLine("Error: --id=<workspace-id> is required");
            return 1;
        }

        var appData = new AppDataDirectory();
        var dbPath = appData.GetWorkspaceDatabasePath("main");
        var factory = new SqliteConnectionFactory(dbPath);
        var repo = new SqliteWorkspaceRepository(factory);
        var workspace = await repo.FindByIdAsync(WorkspaceId.Parse(wsId));

        if (workspace is null)
        {
            Console.Error.WriteLine($"Workspace not found: {wsId}");
            return 1;
        }

        // Run consistency check
        var indexedFiles = await GetIndexedFileSizesAsync(factory, workspace.Id);
        var result = ConsistencyReconciler.Reconcile(workspace.RootPath, indexedFiles);

        Console.WriteLine($"Verification result:");
        Console.WriteLine($"  Total checked: {result.TotalChecked}");
        Console.WriteLine($"  Added: {result.AddedFiles}");
        Console.WriteLine($"  Modified: {result.ModifiedFiles}");
        Console.WriteLine($"  Deleted: {result.DeletedFiles}");
        Console.WriteLine($"  Unchanged: {result.UnchangedFiles}");
        Console.WriteLine($"  Consistent: {result.IsConsistent}");
        return result.IsConsistent ? 0 : 1;
    }

    private static int PrintUsage()
    {
        Console.WriteLine("Usage: cachehub index <build|refresh|status|verify> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  build --id=<id>      Build a new index snapshot");
        Console.WriteLine("  refresh --id=<id>    Refresh the current index");
        Console.WriteLine("  status --id=<id>     Show index status");
        Console.WriteLine("  verify --id=<id>     Verify index consistency");
        return 1;
    }

    private static string? GetOption(string[] args, string prefix)
        => args.FirstOrDefault(a => a.StartsWith(prefix + "=", StringComparison.OrdinalIgnoreCase))?[(prefix.Length + 1)..];

    private static async Task InsertSnapshotAsync(SqliteConnectionFactory factory, IndexSnapshotId snapshotId, WorkspaceId workspaceId)
    {
        await using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO index_snapshots (id, workspace_id, status, file_count)
            VALUES ($id, $ws, 'Building', 0);
            """;
        cmd.Parameters.AddWithValue("$id", snapshotId.Value);
        cmd.Parameters.AddWithValue("$ws", workspaceId.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task InsertFileAsync(
        SqliteConnectionFactory factory,
        IndexSnapshotId snapshotId,
        string path,
        string normalizedPath,
        long size,
        string contentHash,
        string language,
        bool isBinary)
    {
        await using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO files (id, snapshot_id, path, normalized_path, size, content_hash, language, is_binary, status)
            VALUES ($id, $snap, $path, $norm, $size, $hash, $lang, $bin, 'Indexed');
            """;
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("$snap", snapshotId.Value);
        cmd.Parameters.AddWithValue("$path", path);
        cmd.Parameters.AddWithValue("$norm", normalizedPath);
        cmd.Parameters.AddWithValue("$size", size);
        cmd.Parameters.AddWithValue("$hash", contentHash);
        cmd.Parameters.AddWithValue("$lang", language);
        cmd.Parameters.AddWithValue("$bin", isBinary ? 1 : 0);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task ActivateSnapshotAsync(SqliteConnectionFactory factory, IndexSnapshotId snapshotId, int fileCount)
    {
        await using var conn = factory.CreateOpenConnection();
        using var deactivateCmd = conn.CreateCommand();
        deactivateCmd.CommandText = "UPDATE index_snapshots SET status = 'Superseded' WHERE status = 'Active';";
        await deactivateCmd.ExecuteNonQueryAsync();

        using var activateCmd = conn.CreateCommand();
        activateCmd.CommandText =
            """
            UPDATE index_snapshots SET status = 'Active', file_count = $count, completed_at = datetime('now')
            WHERE id = $id;
            """;
        activateCmd.Parameters.AddWithValue("$id", snapshotId.Value);
        activateCmd.Parameters.AddWithValue("$count", fileCount);
        await activateCmd.ExecuteNonQueryAsync();
    }

    private static async Task<(string id, int fileCount)?> GetActiveSnapshotAsync(SqliteConnectionFactory factory, WorkspaceId workspaceId)
    {
        await using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, file_count FROM index_snapshots WHERE workspace_id = $ws AND status = 'Active' LIMIT 1;";
        cmd.Parameters.AddWithValue("$ws", workspaceId.Value);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
            return (reader.GetString(0), reader.GetInt32(1));
        return null;
    }

    private static async Task<Dictionary<string, long>> GetIndexedFileSizesAsync(SqliteConnectionFactory factory, WorkspaceId workspaceId)
    {
        var result = new Dictionary<string, long>();
        await using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT f.normalized_path, f.size FROM files f INNER JOIN index_snapshots s ON f.snapshot_id = s.id WHERE s.workspace_id = $ws AND s.status = 'Active';";
        cmd.Parameters.AddWithValue("$ws", workspaceId.Value);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result[reader.GetString(0)] = reader.GetInt64(1);
        }
        return result;
    }
}
