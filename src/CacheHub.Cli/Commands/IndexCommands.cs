using CacheHub.Core.Identifiers;
using CacheHub.Core.Indexing;
using CacheHub.Core.Jobs;
using CacheHub.Core.Paths;
using CacheHub.Core.Parsing;
using CacheHub.Core.Workspaces;
using CacheHub.Indexing.Detection;
using CacheHub.Indexing.Hashing;
using CacheHub.Indexing.IgnoreRules;
using CacheHub.Indexing.Parsing;
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
        new Migration0006SchemaV2(),
        new Migration0007ContextPackageFields(),
        new Migration0008ContextPackageFk(),
        new Migration0009PersistentCache(),
        new Migration0010RelationSourceColumn(),
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

        // Enumerate files first (in memory) so we can batch-write in a single transaction
        var enumerator = new DirectoryEnumerator();
        var fts = new Fts5Index(factory);
        var fileCount = 0;
        var failedCount = 0;
        var ignoredCount = 0;
        var filesToIndex = new List<(string relativePath, string fullPath, long size, string language, bool isBinary, string hash, string content)>();

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

                filesToIndex.Add((relativePath, file.Path, file.Size, typeInfo.Language, typeInfo.IsBinary,
                    hash.Hash, content));

                fileCount++;
                if (fileCount % 1000 == 0)
                    Console.WriteLine($"  Scanned {fileCount} files...");
            }
            catch (Exception ex)
            {
                failedCount++;
                Console.Error.WriteLine($"  Failed: {relativePath} - {ex.Message}");
            }
        }

        // Batch write: single connection, single transaction for atomicity
        await using var batchConn = factory.CreateOpenConnection();
        await using var batchTx = await batchConn.BeginTransactionAsync();

        try
        {
            // Insert all files + parser results in the same transaction
            foreach (var (relativePath, fullPath, size, language, isBinary, hash, content) in filesToIndex)
            {
                // Select parser by extension
                var parser = SelectParser(relativePath);
                var parseResult = parser.Parse(content, relativePath);

                // Insert into files table (with parser info)
                using var fileCmd = batchConn.CreateCommand();
                fileCmd.Transaction = (SqliteTransaction)batchTx;
                fileCmd.CommandText =
                    """
                    INSERT INTO files (id, snapshot_id, path, normalized_path, size, content_hash, language, is_binary, status, hash_kind, parser_id, parser_version)
                    VALUES ($id, $snap, $path, $norm, $size, $hash, $lang, $bin, 'Indexed', $hashKind, $parserId, $parserVer);
                    """;
                var fileId = Guid.NewGuid().ToString("N");
                fileCmd.Parameters.AddWithValue("$id", fileId);
                fileCmd.Parameters.AddWithValue("$snap", snapshotId.Value);
                fileCmd.Parameters.AddWithValue("$path", relativePath);
                fileCmd.Parameters.AddWithValue("$norm", relativePath);
                fileCmd.Parameters.AddWithValue("$size", size);
                fileCmd.Parameters.AddWithValue("$hash", hash);
                fileCmd.Parameters.AddWithValue("$lang", language);
                fileCmd.Parameters.AddWithValue("$bin", isBinary ? 1 : 0);
                fileCmd.Parameters.AddWithValue("$hashKind", hash.StartsWith("fp:", StringComparison.Ordinal) ? "fingerprint" : "full");
                fileCmd.Parameters.AddWithValue("$parserId", parser.Id);
                fileCmd.Parameters.AddWithValue("$parserVer", parser.Version);
                await fileCmd.ExecuteNonQueryAsync();

                // Insert symbols
                foreach (var symbol in parseResult.Symbols)
                {
                    using var symCmd = batchConn.CreateCommand();
                    symCmd.Transaction = (SqliteTransaction)batchTx;
                    symCmd.CommandText =
                        """
                        INSERT INTO file_symbols (id, file_id, snapshot_id, name, kind, start_line, end_line, modifier, confidence)
                        VALUES ($id, $fid, $snap, $name, $kind, $sl, $el, $mod, $conf);
                        """;
                    symCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
                    symCmd.Parameters.AddWithValue("$fid", fileId);
                    symCmd.Parameters.AddWithValue("$snap", snapshotId.Value);
                    symCmd.Parameters.AddWithValue("$name", symbol.Name);
                    symCmd.Parameters.AddWithValue("$kind", symbol.Kind.ToString());
                    symCmd.Parameters.AddWithValue("$sl", symbol.StartLine);
                    symCmd.Parameters.AddWithValue("$el", symbol.EndLine);
                    symCmd.Parameters.AddWithValue("$mod", (object?)symbol.Modifier ?? DBNull.Value);
                    symCmd.Parameters.AddWithValue("$conf", "syntactic");
                    await symCmd.ExecuteNonQueryAsync();
                }

                // Insert imports
                foreach (var import in parseResult.Imports)
                {
                    using var impCmd = batchConn.CreateCommand();
                    impCmd.Transaction = (SqliteTransaction)batchTx;
                    impCmd.CommandText =
                        """
                        INSERT INTO file_imports (id, file_id, snapshot_id, module, imported_name, line)
                        VALUES ($id, $fid, $snap, $mod, $name, $line);
                        """;
                    impCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
                    impCmd.Parameters.AddWithValue("$fid", fileId);
                    impCmd.Parameters.AddWithValue("$snap", snapshotId.Value);
                    impCmd.Parameters.AddWithValue("$mod", import.Module);
                    impCmd.Parameters.AddWithValue("$name", (object?)import.ImportedName ?? DBNull.Value);
                    impCmd.Parameters.AddWithValue("$line", import.Line);
                    await impCmd.ExecuteNonQueryAsync();
                }

                // Insert relations (heuristic calls/references)
                foreach (var relation in parseResult.Relations)
                {
                    using var relCmd = batchConn.CreateCommand();
                    relCmd.Transaction = (SqliteTransaction)batchTx;
                    relCmd.CommandText =
                        """
                        INSERT INTO file_relations (id, file_id, snapshot_id, source_symbol, target_symbol, relation_type, confidence, line, source)
                        VALUES ($id, $fid, $snap, $src, $tgt, $rt, $conf, $line, $source);
                        """;
                    relCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
                    relCmd.Parameters.AddWithValue("$fid", fileId);
                    relCmd.Parameters.AddWithValue("$snap", snapshotId.Value);
                    relCmd.Parameters.AddWithValue("$src", string.IsNullOrEmpty(relation.SourceSymbol) ? relation.Relation : relation.SourceSymbol);
                    relCmd.Parameters.AddWithValue("$tgt", relation.TargetName);
                    relCmd.Parameters.AddWithValue("$rt", relation.RelationType.ToString());
                    relCmd.Parameters.AddWithValue("$conf", relation.Confidence.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    relCmd.Parameters.AddWithValue("$line", relation.Line > 0 ? relation.Line : DBNull.Value);
                    relCmd.Parameters.AddWithValue("$source", relation.Source);
                    await relCmd.ExecuteNonQueryAsync();
                }
            }

            // Commit file metadata
            await batchTx.CommitAsync();
        }
        catch (Exception ex)
        {
            await batchTx.RollbackAsync();
            // Clean up the failed snapshot
            await DeleteSnapshotAsync(factory, snapshotId);
            Console.Error.WriteLine($"Error: Batch write failed, snapshot cleaned up: {ex.Message}");
            return 1;
        }

        // FTS indexing (separate transaction — FTS5 virtual tables don't support DDL in DML transactions)
        var ftsBuildFailed = false;
        try
        {
            foreach (var (relativePath, _, _, language, _, hash, content) in filesToIndex)
            {
                await fts.IndexFileAsync(snapshotId, relativePath, relativePath, content, language, hash);
            }
        }
        catch (Exception ex)
        {
            ftsBuildFailed = true;
            Console.Error.WriteLine($"Warning: FTS indexing partially failed: {ex.Message}");
        }

        // Activate snapshot — use Degraded status if FTS failed (FTS is a core recall source)
        var snapshotStatus = ftsBuildFailed ? "ActiveDegraded" : "Active";
        await ActivateSnapshotAsync(factory, snapshotId, workspace.Id, fileCount, snapshotStatus);
        await repo.UpdateStatusAsync(workspace.Id, ftsBuildFailed ? WorkspaceStatus.Indexing : WorkspaceStatus.Ready);

        Console.WriteLine($"Index build complete:");
        Console.WriteLine($"  Indexed: {fileCount}");
        Console.WriteLine($"  Ignored: {ignoredCount}");
        Console.WriteLine($"  Failed: {failedCount}");
        Console.WriteLine($"  Snapshot: {snapshotId.Value}");
        if (ftsBuildFailed)
            Console.WriteLine($"  ⚠️ FTS build failed — snapshot is ActiveDegraded. Full-text search may not work correctly.");
        return 0;
    }

    private static async Task<int> RefreshAsync(string[] args)
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
        new Migration0006SchemaV2(),
        new Migration0007ContextPackageFields(),
        new Migration0008ContextPackageFk(),
        new Migration0009PersistentCache(),
        new Migration0010RelationSourceColumn(),
        ]);
        runner.Migrate();

        var repo = new SqliteWorkspaceRepository(factory);
        var workspace = await repo.FindByIdAsync(WorkspaceId.Parse(wsId));
        if (workspace is null)
        {
            Console.Error.WriteLine($"Workspace not found: {wsId}");
            return 1;
        }

        // Get active snapshot
        var activeSnapshot = await GetActiveSnapshotAsync(factory, workspace.Id);
        if (activeSnapshot is null)
        {
            Console.Error.WriteLine("Error: No active snapshot. Run 'index build' first.");
            return 1;
        }

        var snapshotId = activeSnapshot.Value.snapshotId;
        Console.WriteLine($"Refreshing index for: {workspace.Name}");
        Console.WriteLine($"  Snapshot: {snapshotId.Value}");

        // Build ignore rules
        var ignoreEngine = new IgnoreRuleEngine()
            .WithDefaults()
            .WithGitIgnore(Path.Combine(workspace.RootPath, ".gitignore"))
            .WithCacheHubIgnore(Path.Combine(workspace.RootPath, ".cachehubignore"));

        // Get current indexed files
        var indexedFiles = await GetIndexedFileEntriesAsync(factory, workspace.Id);

        // Reconcile against disk — pass ignore engine for consistent filtering
        var result = ConsistencyReconciler.Reconcile(workspace.RootPath, indexedFiles, ignoreEngine: ignoreEngine);

        Console.WriteLine($"  Changes detected:");
        Console.WriteLine($"    Added: {result.AddedFiles}");
        Console.WriteLine($"    Modified: {result.ModifiedFiles}");
        Console.WriteLine($"    Deleted: {result.DeletedFiles}");
        Console.WriteLine($"    Unchanged: {result.UnchangedFiles}");

        if (result.IsConsistent)
        {
            Console.WriteLine("Index is up to date. No changes needed.");
            return 0;
        }

        var fts = new Fts5Index(factory);
        var addedCount = 0;
        var modifiedCount = 0;
        var deletedCount = 0;
        var failedCount = 0;

        // Immutable snapshot pattern: create a Building snapshot, clone data, apply changes, then atomically switch.
        // If anything fails, Active snapshot remains untouched.
        var oldSnapshotId = snapshotId;
        var buildingSnapshotId = IndexSnapshotId.New();
        await InsertSnapshotAsync(factory, buildingSnapshotId, workspace.Id);

        // Clone all data from Active to Building
        await CloneSnapshotDataAsync(factory, oldSnapshotId, buildingSnapshotId);
        Console.Error.WriteLine($"  Cloned snapshot: {oldSnapshotId.Value} → {buildingSnapshotId.Value} (Building)");

        // Use Building snapshot for all subsequent operations
        snapshotId = buildingSnapshotId;

        // Phase 1: Collect all file data (read, hash, parse) into memory
        var filesToAdd = new List<(string path, string fullPath, long size, string language, bool isBinary, string mtime, string hash, string content, string parserId, string parserVersion, Core.Parsing.ParseResult parseResult)>();
        var filesToDelete = new List<string>(result.DeletedPaths);

        foreach (var path in result.AddedPaths)
        {
            // Re-check ignore rules to ensure consistency with Build
            if (ignoreEngine.IsIgnored(path)) continue;

            try
            {
                var fullPath = Path.Combine(workspace.RootPath, path.Replace('/', Path.DirectorySeparatorChar));
                var info = new FileInfo(fullPath);
                var typeInfo = FileTypeDetector.Detect(fullPath, info.Length);
                if (!typeInfo.ShouldIndex) continue;

                var hash = await FileHasher.HashAsync(fullPath, info.Length);
                var content = await File.ReadAllTextAsync(fullPath);
                var parser = SelectParser(path);
                var parseResult = parser.Parse(content, path);

                filesToAdd.Add((path, fullPath, info.Length, typeInfo.Language, typeInfo.IsBinary,
                    info.LastWriteTimeUtc.ToString("O"), hash.Hash, content, parser.Id, parser.Version, parseResult));
                addedCount++;
            }
            catch (Exception ex)
            {
                failedCount++;
                Console.Error.WriteLine($"  Failed to add: {path} - {ex.Message}");
            }
        }

        foreach (var path in result.ModifiedPaths)
        {
            // Re-check ignore rules — a file may have been added to .gitignore since last build
            if (ignoreEngine.IsIgnored(path))
            {
                // Treat as deleted (remove from index) since it's now ignored
                filesToDelete.Add(path);
                continue;
            }

            try
            {
                var fullPath = Path.Combine(workspace.RootPath, path.Replace('/', Path.DirectorySeparatorChar));
                var info = new FileInfo(fullPath);
                var typeInfo = FileTypeDetector.Detect(fullPath, info.Length);
                if (!typeInfo.ShouldIndex) continue;

                var hash = await FileHasher.HashAsync(fullPath, info.Length);
                var content = await File.ReadAllTextAsync(fullPath);
                var parser = SelectParser(path);
                var parseResult = parser.Parse(content, path);

                // Modified = delete old + add new
                filesToDelete.Add(path);
                filesToAdd.Add((path, fullPath, info.Length, typeInfo.Language, typeInfo.IsBinary,
                    info.LastWriteTimeUtc.ToString("O"), hash.Hash, content, parser.Id, parser.Version, parseResult));
                modifiedCount++;
            }
            catch (Exception ex)
            {
                failedCount++;
                Console.Error.WriteLine($"  Failed to update: {path} - {ex.Message}");
            }
        }

        // Phase 2: Batch DB writes — single connection, single transaction
        await using var batchConn = factory.CreateOpenConnection();
        await using var batchTx = await batchConn.BeginTransactionAsync();

        try
        {
            // Delete all removed/modified files
            foreach (var path in filesToDelete)
            {
                using var delCmd = batchConn.CreateCommand();
                delCmd.Transaction = (SqliteTransaction)batchTx;
                delCmd.CommandText = "DELETE FROM files WHERE snapshot_id = $snap AND normalized_path = $path;";
                delCmd.Parameters.AddWithValue("$snap", snapshotId.Value);
                delCmd.Parameters.AddWithValue("$path", path);
                await delCmd.ExecuteNonQueryAsync();
            }
            deletedCount = result.DeletedPaths.Count;

            // Insert all new/modified files
            foreach (var (path, _, size, language, isBinary, mtime, hash, content, parserId, parserVersion, parseResult) in filesToAdd)
            {
                var fileId = Guid.NewGuid().ToString("N");
                using var fileCmd = batchConn.CreateCommand();
                fileCmd.Transaction = (SqliteTransaction)batchTx;
                fileCmd.CommandText = """
                    INSERT INTO files (id, snapshot_id, path, normalized_path, size, content_hash, language, is_binary, status, hash_kind, mtime, parser_id, parser_version)
                    VALUES ($id, $snap, $path, $norm, $size, $hash, $lang, $bin, 'Indexed', $hashKind, $mtime, $parserId, $parserVer);
                    """;
                fileCmd.Parameters.AddWithValue("$id", fileId);
                fileCmd.Parameters.AddWithValue("$snap", snapshotId.Value);
                fileCmd.Parameters.AddWithValue("$path", path);
                fileCmd.Parameters.AddWithValue("$norm", path);
                fileCmd.Parameters.AddWithValue("$size", size);
                fileCmd.Parameters.AddWithValue("$hash", hash);
                fileCmd.Parameters.AddWithValue("$lang", language);
                fileCmd.Parameters.AddWithValue("$bin", isBinary ? 1 : 0);
                fileCmd.Parameters.AddWithValue("$hashKind", hash.StartsWith("fp:", StringComparison.Ordinal) ? "fingerprint" : "full");
                fileCmd.Parameters.AddWithValue("$mtime", mtime);
                fileCmd.Parameters.AddWithValue("$parserId", parserId);
                fileCmd.Parameters.AddWithValue("$parserVer", parserVersion);
                await fileCmd.ExecuteNonQueryAsync();

                foreach (var symbol in parseResult.Symbols)
                {
                    using var symCmd = batchConn.CreateCommand();
                    symCmd.Transaction = (SqliteTransaction)batchTx;
                    symCmd.CommandText = """
                        INSERT INTO file_symbols (id, file_id, snapshot_id, name, kind, start_line, end_line, modifier, confidence)
                        VALUES ($id, $fid, $snap, $name, $kind, $sl, $el, $mod, $conf);
                        """;
                    symCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
                    symCmd.Parameters.AddWithValue("$fid", fileId);
                    symCmd.Parameters.AddWithValue("$snap", snapshotId.Value);
                    symCmd.Parameters.AddWithValue("$name", symbol.Name);
                    symCmd.Parameters.AddWithValue("$kind", symbol.Kind.ToString());
                    symCmd.Parameters.AddWithValue("$sl", symbol.StartLine);
                    symCmd.Parameters.AddWithValue("$el", symbol.EndLine);
                    symCmd.Parameters.AddWithValue("$mod", (object?)symbol.Modifier ?? DBNull.Value);
                    symCmd.Parameters.AddWithValue("$conf", "syntactic");
                    await symCmd.ExecuteNonQueryAsync();
                }

                foreach (var import in parseResult.Imports)
                {
                    using var impCmd = batchConn.CreateCommand();
                    impCmd.Transaction = (SqliteTransaction)batchTx;
                    impCmd.CommandText = """
                        INSERT INTO file_imports (id, file_id, snapshot_id, module, imported_name, line)
                        VALUES ($id, $fid, $snap, $mod, $name, $line);
                        """;
                    impCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
                    impCmd.Parameters.AddWithValue("$fid", fileId);
                    impCmd.Parameters.AddWithValue("$snap", snapshotId.Value);
                    impCmd.Parameters.AddWithValue("$mod", import.Module);
                    impCmd.Parameters.AddWithValue("$name", (object?)import.ImportedName ?? DBNull.Value);
                    impCmd.Parameters.AddWithValue("$line", import.Line);
                    await impCmd.ExecuteNonQueryAsync();
                }

                foreach (var relation in parseResult.Relations)
                {
                    using var relCmd = batchConn.CreateCommand();
                    relCmd.Transaction = (SqliteTransaction)batchTx;
                    relCmd.CommandText = """
                        INSERT INTO file_relations (id, file_id, snapshot_id, source_symbol, target_symbol, relation_type, confidence, line, source)
                        VALUES ($id, $fid, $snap, $src, $tgt, $rt, $conf, $line, $source);
                        """;
                    relCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
                    relCmd.Parameters.AddWithValue("$fid", fileId);
                    relCmd.Parameters.AddWithValue("$snap", snapshotId.Value);
                    relCmd.Parameters.AddWithValue("$src", string.IsNullOrEmpty(relation.SourceSymbol) ? relation.Relation : relation.SourceSymbol);
                    relCmd.Parameters.AddWithValue("$tgt", relation.TargetName);
                    relCmd.Parameters.AddWithValue("$rt", relation.RelationType.ToString());
                    relCmd.Parameters.AddWithValue("$conf", relation.Confidence.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    relCmd.Parameters.AddWithValue("$line", relation.Line > 0 ? relation.Line : DBNull.Value);
                    relCmd.Parameters.AddWithValue("$source", relation.Source);
                    await relCmd.ExecuteNonQueryAsync();
                }
            }

            await batchTx.CommitAsync();
        }
        catch (Exception ex)
        {
            await batchTx.RollbackAsync();
            // Immutable snapshot: clean up the failed Building snapshot, Active remains untouched
            await DeleteSnapshotAsync(factory, buildingSnapshotId);
            Console.Error.WriteLine($"Error: Batch refresh failed, Building snapshot cleaned up: {ex.Message}");
            Console.Error.WriteLine($"  Active snapshot {oldSnapshotId.Value} remains unchanged.");
            return 1;
        }

        // Phase 3: FTS updates (separate — FTS5 virtual tables don't support DDL in DML transactions)
        // Track FTS failures instead of silently swallowing them — FTS consistency is critical for recall accuracy
        var ftsFailedPaths = new List<string>();

        // Delete FTS entries for removed/modified files
        foreach (var path in filesToDelete)
        {
            try { await fts.DeleteFileAsync(snapshotId, path); }
            catch (Exception ftsEx) { ftsFailedPaths.Add(path); Console.Error.WriteLine($"  Warning: FTS delete failed for {path}: {ftsEx.Message}"); }
        }

        // Index new/modified files in FTS
        foreach (var (path, _, _, language, _, _, hash, content, _, _, _) in filesToAdd)
        {
            try { await fts.IndexFileAsync(snapshotId, path, path, content, language, hash); }
            catch (Exception ftsEx) { ftsFailedPaths.Add(path); Console.Error.WriteLine($"  Warning: FTS index failed for {path}: {ftsEx.Message}"); }
        }

        // Update snapshot file count
        var newFileCount = activeSnapshot.Value.fileCount + addedCount - deletedCount;
        await UpdateSnapshotFileCountAsync(factory, snapshotId, newFileCount);

        // Atomically activate Building snapshot and supersede old Active
        // Use Degraded status if FTS had failures
        var refreshStatus = ftsFailedPaths.Count > 0 ? "ActiveDegraded" : "Active";
        await ActivateSnapshotAsync(factory, buildingSnapshotId, workspace.Id, newFileCount, refreshStatus);

        // Clean up old snapshot data (files, symbols, imports, relations, FTS)
        await DeleteSnapshotDataAsync(factory, oldSnapshotId);

        Console.WriteLine($"Refresh complete:");
        Console.WriteLine($"  Added: {addedCount}");
        Console.WriteLine($"  Modified: {modifiedCount}");
        Console.WriteLine($"  Deleted: {deletedCount}");
        Console.WriteLine($"  Failed: {failedCount}");
        Console.WriteLine($"  Total files: {newFileCount}");
        Console.WriteLine($"  Snapshot: {buildingSnapshotId.Value} (was {oldSnapshotId.Value})");
        if (ftsFailedPaths.Count > 0)
        {
            Console.WriteLine($"  ⚠️ FTS failures: {ftsFailedPaths.Count} files — snapshot is ActiveDegraded");
            Console.Error.WriteLine($"  FTS failed paths: {string.Join(", ", ftsFailedPaths)}");
        }
        return 0;
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
            Console.WriteLine($"  Active Snapshot: {activeSnapshot.Value.snapshotId.Value}");
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

        // Run consistency check using VirtualPath + size + mtime
        var indexedFiles = await GetIndexedFileEntriesAsync(factory, workspace.Id);
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

    /// <summary>
    /// Selects the appropriate parser for a file based on its extension.
    /// </summary>
    private static ICodeParser SelectParser(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".cs" => new CSharpRegexParser(),
            ".ts" or ".tsx" or ".js" or ".jsx" => new TypeScriptRegexParser(),
            ".py" => new PythonRegexParser(),
            ".go" => new GoRegexParser(),
            ".rs" => new RustRegexParser(),
            ".java" => new JavaRegexParser(),
            ".c" or ".h" or ".cpp" or ".hpp" or ".cc" or ".cxx" => new CppRegexParser(),
            ".php" => new PhpRegexParser(),
            ".rb" => new RubyRegexParser(),
            ".kt" or ".kts" => new KotlinRegexParser(),
            ".swift" => new SwiftRegexParser(),
            ".md" or ".markdown" => new MarkdownParser(),
            _ => new TextParser(),
        };
    }

    private static string? GetOption(string[] args, string prefix)
        => args.FirstOrDefault(a => a.StartsWith(prefix + "=", StringComparison.OrdinalIgnoreCase))?[(prefix.Length + 1)..];

    private static async Task InsertFileWithParserAsync(
        SqliteConnectionFactory factory, IndexSnapshotId snapshotId,
        string path, string normalizedPath, long size, string contentHash,
        string language, bool isBinary, string mtime,
        string parserId, string parserVersion, Core.Parsing.ParseResult parseResult)
    {
        await using var conn = factory.CreateOpenConnection();
        await using var tx = await conn.BeginTransactionAsync();

        var fileId = Guid.NewGuid().ToString("N");

        using var fileCmd = conn.CreateCommand();
        fileCmd.Transaction = (SqliteTransaction)tx;
        fileCmd.CommandText =
            """
            INSERT INTO files (id, snapshot_id, path, normalized_path, size, content_hash, language, is_binary, status, hash_kind, mtime, parser_id, parser_version)
            VALUES ($id, $snap, $path, $norm, $size, $hash, $lang, $bin, 'Indexed', $hashKind, $mtime, $parserId, $parserVer);
            """;
        fileCmd.Parameters.AddWithValue("$id", fileId);
        fileCmd.Parameters.AddWithValue("$snap", snapshotId.Value);
        fileCmd.Parameters.AddWithValue("$path", path);
        fileCmd.Parameters.AddWithValue("$norm", normalizedPath);
        fileCmd.Parameters.AddWithValue("$size", size);
        fileCmd.Parameters.AddWithValue("$hash", contentHash);
        fileCmd.Parameters.AddWithValue("$lang", language);
        fileCmd.Parameters.AddWithValue("$bin", isBinary ? 1 : 0);
        fileCmd.Parameters.AddWithValue("$hashKind", contentHash.StartsWith("fp:", StringComparison.Ordinal) ? "fingerprint" : "full");
        fileCmd.Parameters.AddWithValue("$mtime", mtime);
        fileCmd.Parameters.AddWithValue("$parserId", parserId);
        fileCmd.Parameters.AddWithValue("$parserVer", parserVersion);
        await fileCmd.ExecuteNonQueryAsync();

        foreach (var symbol in parseResult.Symbols)
        {
            using var symCmd = conn.CreateCommand();
            symCmd.Transaction = (SqliteTransaction)tx;
            symCmd.CommandText =
                """
                INSERT INTO file_symbols (id, file_id, snapshot_id, name, kind, start_line, end_line, modifier, confidence)
                VALUES ($id, $fid, $snap, $name, $kind, $sl, $el, $mod, $conf);
                """;
            symCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            symCmd.Parameters.AddWithValue("$fid", fileId);
            symCmd.Parameters.AddWithValue("$snap", snapshotId.Value);
            symCmd.Parameters.AddWithValue("$name", symbol.Name);
            symCmd.Parameters.AddWithValue("$kind", symbol.Kind.ToString());
            symCmd.Parameters.AddWithValue("$sl", symbol.StartLine);
            symCmd.Parameters.AddWithValue("$el", symbol.EndLine);
            symCmd.Parameters.AddWithValue("$mod", (object?)symbol.Modifier ?? DBNull.Value);
            symCmd.Parameters.AddWithValue("$conf", "syntactic");
            await symCmd.ExecuteNonQueryAsync();
        }

        foreach (var import in parseResult.Imports)
        {
            using var impCmd = conn.CreateCommand();
            impCmd.Transaction = (SqliteTransaction)tx;
            impCmd.CommandText =
                """
                INSERT INTO file_imports (id, file_id, snapshot_id, module, imported_name, line)
                VALUES ($id, $fid, $snap, $mod, $name, $line);
                """;
            impCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            impCmd.Parameters.AddWithValue("$fid", fileId);
            impCmd.Parameters.AddWithValue("$snap", snapshotId.Value);
            impCmd.Parameters.AddWithValue("$mod", import.Module);
            impCmd.Parameters.AddWithValue("$name", (object?)import.ImportedName ?? DBNull.Value);
            impCmd.Parameters.AddWithValue("$line", import.Line);
            await impCmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }

    private static async Task DeleteFileFromSnapshotAsync(SqliteConnectionFactory factory, IndexSnapshotId snapshotId, string normalizedPath)
    {
        await using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM files WHERE snapshot_id = $snap AND normalized_path = $path;";
        cmd.Parameters.AddWithValue("$snap", snapshotId.Value);
        cmd.Parameters.AddWithValue("$path", normalizedPath);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task UpdateSnapshotFileCountAsync(SqliteConnectionFactory factory, IndexSnapshotId snapshotId, int fileCount)
    {
        await using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE index_snapshots SET file_count = $count, completed_at = datetime('now') WHERE id = $id;";
        cmd.Parameters.AddWithValue("$count", fileCount);
        cmd.Parameters.AddWithValue("$id", snapshotId.Value);
        await cmd.ExecuteNonQueryAsync();
    }

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
            INSERT INTO files (id, snapshot_id, path, normalized_path, size, content_hash, language, is_binary, status, hash_kind)
            VALUES ($id, $snap, $path, $norm, $size, $hash, $lang, $bin, 'Indexed', $hashKind);
            """;
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("$snap", snapshotId.Value);
        cmd.Parameters.AddWithValue("$path", path);
        cmd.Parameters.AddWithValue("$norm", normalizedPath);
        cmd.Parameters.AddWithValue("$size", size);
        cmd.Parameters.AddWithValue("$hash", contentHash);
        cmd.Parameters.AddWithValue("$lang", language);
        cmd.Parameters.AddWithValue("$bin", isBinary ? 1 : 0);
        cmd.Parameters.AddWithValue("$hashKind", contentHash.StartsWith("fp:", StringComparison.Ordinal) ? "fingerprint" : "full");
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task DeleteSnapshotAsync(SqliteConnectionFactory factory, IndexSnapshotId snapshotId)
    {
        await using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM files WHERE snapshot_id = $id; DELETE FROM index_snapshots WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", snapshotId.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Clones all data (files, symbols, imports, relations, FTS) from one snapshot to another.
    /// Used by the immutable Refresh pattern to create a Building snapshot before applying changes.
    /// All PKs are regenerated to avoid UNIQUE constraint failures (id columns are global PRIMARY KEYs, not composite).
    /// A mapping (old_file_id → new_file_id) is built so child tables can reference the correct new parent.
    /// </summary>
    private static async Task CloneSnapshotDataAsync(SqliteConnectionFactory factory, IndexSnapshotId fromSnapshot, IndexSnapshotId toSnapshot)
    {
        await using var conn = factory.CreateOpenConnection();
        await using var tx = await conn.BeginTransactionAsync();

        // Phase 1: Read all files from source snapshot and build ID mapping
        var fileIdMap = new Dictionary<string, string>(StringComparer.Ordinal);

        using (var readCmd = conn.CreateCommand())
        {
            readCmd.Transaction = (SqliteTransaction)tx;
            readCmd.CommandText = """
                SELECT id, path, normalized_path, size, content_hash, language, is_binary, status, hash_kind, mtime, parser_id, parser_version
                FROM files WHERE snapshot_id = $from;
                """;
            readCmd.Parameters.AddWithValue("$from", fromSnapshot.Value);
            await using var reader = await readCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var oldFileId = reader.GetString(0);
                var newFileId = Guid.NewGuid().ToString("N");
                fileIdMap[oldFileId] = newFileId;

                using var insCmd = conn.CreateCommand();
                insCmd.Transaction = (SqliteTransaction)tx;
                insCmd.CommandText = """
                    INSERT INTO files (id, snapshot_id, path, normalized_path, size, content_hash, language, is_binary, status, hash_kind, mtime, parser_id, parser_version)
                    VALUES ($id, $snap, $path, $norm, $size, $hash, $lang, $bin, $status, $hashKind, $mtime, $parserId, $parserVer);
                    """;
                insCmd.Parameters.AddWithValue("$id", newFileId);
                insCmd.Parameters.AddWithValue("$snap", toSnapshot.Value);
                insCmd.Parameters.AddWithValue("$path", reader.GetString(1));
                insCmd.Parameters.AddWithValue("$norm", reader.GetString(2));
                insCmd.Parameters.AddWithValue("$size", reader.GetInt64(3));
                insCmd.Parameters.AddWithValue("$hash", reader.GetString(4));
                insCmd.Parameters.AddWithValue("$lang", reader.GetString(5));
                insCmd.Parameters.AddWithValue("$bin", reader.GetInt32(6));
                insCmd.Parameters.AddWithValue("$status", reader.GetString(7));
                insCmd.Parameters.AddWithValue("$hashKind", reader.GetString(8));
                insCmd.Parameters.AddWithValue("$mtime", reader.IsDBNull(9) ? DBNull.Value : reader.GetString(9));
                insCmd.Parameters.AddWithValue("$parserId", reader.IsDBNull(10) ? DBNull.Value : reader.GetString(10));
                insCmd.Parameters.AddWithValue("$parserVer", reader.IsDBNull(11) ? DBNull.Value : reader.GetString(11));
                await insCmd.ExecuteNonQueryAsync();
            }
        }

        // Phase 2: Clone symbols with new IDs and mapped file_id
        using (var readCmd = conn.CreateCommand())
        {
            readCmd.Transaction = (SqliteTransaction)tx;
            readCmd.CommandText = """
                SELECT id, file_id, name, kind, start_line, end_line, modifier, confidence
                FROM file_symbols WHERE snapshot_id = $from;
                """;
            readCmd.Parameters.AddWithValue("$from", fromSnapshot.Value);
            await using var reader = await readCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var oldFileId = reader.GetString(1);
                if (!fileIdMap.TryGetValue(oldFileId, out var newFileId))
                    continue;

                using var insCmd = conn.CreateCommand();
                insCmd.Transaction = (SqliteTransaction)tx;
                insCmd.CommandText = """
                    INSERT INTO file_symbols (id, file_id, snapshot_id, name, kind, start_line, end_line, modifier, confidence)
                    VALUES ($id, $fid, $snap, $name, $kind, $sl, $el, $mod, $conf);
                    """;
                insCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
                insCmd.Parameters.AddWithValue("$fid", newFileId);
                insCmd.Parameters.AddWithValue("$snap", toSnapshot.Value);
                insCmd.Parameters.AddWithValue("$name", reader.GetString(2));
                insCmd.Parameters.AddWithValue("$kind", reader.GetString(3));
                insCmd.Parameters.AddWithValue("$sl", reader.GetInt32(4));
                insCmd.Parameters.AddWithValue("$el", reader.GetInt32(5));
                insCmd.Parameters.AddWithValue("$mod", reader.IsDBNull(6) ? DBNull.Value : reader.GetString(6));
                insCmd.Parameters.AddWithValue("$conf", reader.GetString(7));
                await insCmd.ExecuteNonQueryAsync();
            }
        }

        // Phase 3: Clone imports with new IDs and mapped file_id
        using (var readCmd = conn.CreateCommand())
        {
            readCmd.Transaction = (SqliteTransaction)tx;
            readCmd.CommandText = """
                SELECT id, file_id, module, imported_name, line
                FROM file_imports WHERE snapshot_id = $from;
                """;
            readCmd.Parameters.AddWithValue("$from", fromSnapshot.Value);
            await using var reader = await readCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var oldFileId = reader.GetString(1);
                if (!fileIdMap.TryGetValue(oldFileId, out var newFileId))
                    continue;

                using var insCmd = conn.CreateCommand();
                insCmd.Transaction = (SqliteTransaction)tx;
                insCmd.CommandText = """
                    INSERT INTO file_imports (id, file_id, snapshot_id, module, imported_name, line)
                    VALUES ($id, $fid, $snap, $mod, $name, $line);
                    """;
                insCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
                insCmd.Parameters.AddWithValue("$fid", newFileId);
                insCmd.Parameters.AddWithValue("$snap", toSnapshot.Value);
                insCmd.Parameters.AddWithValue("$mod", reader.GetString(2));
                insCmd.Parameters.AddWithValue("$name", reader.IsDBNull(3) ? DBNull.Value : reader.GetString(3));
                insCmd.Parameters.AddWithValue("$line", reader.GetInt32(4));
                await insCmd.ExecuteNonQueryAsync();
            }
        }

        // Phase 4: Clone relations with new IDs and mapped file_id
        using (var readCmd = conn.CreateCommand())
        {
            readCmd.Transaction = (SqliteTransaction)tx;
            readCmd.CommandText = """
                SELECT id, file_id, source_symbol, target_symbol, relation_type, confidence, line, source
                FROM file_relations WHERE snapshot_id = $from;
                """;
            readCmd.Parameters.AddWithValue("$from", fromSnapshot.Value);
            await using var reader = await readCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var oldFileId = reader.GetString(1);
                if (!fileIdMap.TryGetValue(oldFileId, out var newFileId))
                    continue;

                using var insCmd = conn.CreateCommand();
                insCmd.Transaction = (SqliteTransaction)tx;
                insCmd.CommandText = """
                    INSERT INTO file_relations (id, file_id, snapshot_id, source_symbol, target_symbol, relation_type, confidence, line, source)
                    VALUES ($id, $fid, $snap, $src, $tgt, $rt, $conf, $line, $source);
                    """;
                insCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
                insCmd.Parameters.AddWithValue("$fid", newFileId);
                insCmd.Parameters.AddWithValue("$snap", toSnapshot.Value);
                insCmd.Parameters.AddWithValue("$src", reader.GetString(2));
                insCmd.Parameters.AddWithValue("$tgt", reader.GetString(3));
                insCmd.Parameters.AddWithValue("$rt", reader.GetString(4));
                insCmd.Parameters.AddWithValue("$conf", reader.GetString(5));
                insCmd.Parameters.AddWithValue("$line", reader.IsDBNull(6) ? DBNull.Value : reader.GetInt32(6));
                insCmd.Parameters.AddWithValue("$source", reader.IsDBNull(7) ? DBNull.Value : reader.GetString(7));
                await insCmd.ExecuteNonQueryAsync();
            }
        }

        await tx.CommitAsync();

        // Clone FTS entries (separate — FTS5 virtual table, no transaction)
        // FTS5 virtual tables use rowid internally, no PK conflict risk
        using var ftsConn = factory.CreateOpenConnection();
        using var ftsCmd = ftsConn.CreateCommand();
        ftsCmd.CommandText = """
            INSERT INTO file_contents_fts (path, normalized_path, content, language, content_hash, snapshot_id)
            SELECT path, normalized_path, content, language, content_hash, $to
            FROM file_contents_fts WHERE snapshot_id = $from;
            """;
        ftsCmd.Parameters.AddWithValue("$from", fromSnapshot.Value);
        ftsCmd.Parameters.AddWithValue("$to", toSnapshot.Value);
        await ftsCmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Deletes all data for a snapshot (files, symbols, imports, relations, FTS, snapshot row).
    /// Used to clean up superseded snapshots after atomic switch.
    /// </summary>
    private static async Task DeleteSnapshotDataAsync(SqliteConnectionFactory factory, IndexSnapshotId snapshotId)
    {
        await using var conn = factory.CreateOpenConnection();

        // Delete FTS entries
        using (var ftsCmd = conn.CreateCommand())
        {
            ftsCmd.CommandText = "DELETE FROM file_contents_fts WHERE snapshot_id = $id;";
            ftsCmd.Parameters.AddWithValue("$id", snapshotId.Value);
            await ftsCmd.ExecuteNonQueryAsync();
        }

        // Delete metadata tables and snapshot row
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM file_symbols WHERE snapshot_id = $id;
            DELETE FROM file_imports WHERE snapshot_id = $id;
            DELETE FROM file_relations WHERE snapshot_id = $id;
            DELETE FROM files WHERE snapshot_id = $id;
            DELETE FROM index_snapshots WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", snapshotId.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task ActivateSnapshotAsync(SqliteConnectionFactory factory, IndexSnapshotId snapshotId, WorkspaceId workspaceId, int fileCount, string status = "Active")
    {
        await using var conn = factory.CreateOpenConnection();
        await using var tx = await conn.BeginTransactionAsync();

        // Deactivate only this workspace's active snapshot (not other workspaces')
        using var deactivateCmd = conn.CreateCommand();
        deactivateCmd.Transaction = (SqliteTransaction)tx;
        deactivateCmd.CommandText =
            "UPDATE index_snapshots SET status = 'Superseded' WHERE status = 'Active' AND workspace_id = $ws;";
        deactivateCmd.Parameters.AddWithValue("$ws", workspaceId.Value);
        await deactivateCmd.ExecuteNonQueryAsync();

        using var activateCmd = conn.CreateCommand();
        activateCmd.Transaction = (SqliteTransaction)tx;
        activateCmd.CommandText =
            """
            UPDATE index_snapshots SET status = $status, file_count = $count, completed_at = datetime('now')
            WHERE id = $id;
            """;
        activateCmd.Parameters.AddWithValue("$id", snapshotId.Value);
        activateCmd.Parameters.AddWithValue("$count", fileCount);
        activateCmd.Parameters.AddWithValue("$status", status);
        await activateCmd.ExecuteNonQueryAsync();

        await tx.CommitAsync();
    }

    private static async Task<(IndexSnapshotId snapshotId, int fileCount)?> GetActiveSnapshotAsync(SqliteConnectionFactory factory, WorkspaceId workspaceId)
    {
        await using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, file_count FROM index_snapshots WHERE workspace_id = $ws AND status = 'Active' LIMIT 1;";
        cmd.Parameters.AddWithValue("$ws", workspaceId.Value);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
            return (IndexSnapshotId.Parse(reader.GetString(0)), reader.GetInt32(1));
        return null;
    }

    private static async Task<Dictionary<string, IndexedFileEntry>> GetIndexedFileEntriesAsync(SqliteConnectionFactory factory, WorkspaceId workspaceId)
    {
        var result = new Dictionary<string, IndexedFileEntry>(StringComparer.Ordinal);
        await using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT f.normalized_path, f.size, f.mtime, f.content_hash
            FROM files f
            INNER JOIN index_snapshots s ON f.snapshot_id = s.id
            WHERE s.workspace_id = $ws AND s.status = 'Active';
            """;
        cmd.Parameters.AddWithValue("$ws", workspaceId.Value);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var path = reader.GetString(0);
            result[path] = new IndexedFileEntry
            {
                VirtualPath = path,
                Size = reader.GetInt64(1),
                Mtime = reader.IsDBNull(2) ? null : reader.GetString(2),
                ContentHash = reader.IsDBNull(3) ? null : reader.GetString(3),
            };
        }
        return result;
    }
}
