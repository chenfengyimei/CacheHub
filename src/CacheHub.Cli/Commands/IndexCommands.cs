using CacheHub.Core.Identifiers;
using CacheHub.Core.Indexing;
using CacheHub.Core.Jobs;
using CacheHub.Core.Paths;
using CacheHub.Core.Parsing;
using CacheHub.Core.Repository;
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
        new Migration0011SnapshotGitState(),
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

        // V7-W01: Capture Git state for version-aware snapshots
        var gitStateProvider = new GitStateProvider();
        // The initial and final captures must use the same fingerprint scope;
        // otherwise a non-indexed file alone can make a successful build degraded.
        var fingerprintFilter = ContextCommands.CreateFingerprintFilter(workspace.RootPath);
        var gitState = await gitStateProvider.CaptureAsync(workspace.RootPath, fingerprintFilter);
        Console.WriteLine($"  Git: {(gitState.Commit?[..8] ?? "non-git")} | Branch: {gitState.Branch ?? "detached"} | Dirty: {gitState.IsDirty}");

        // Create snapshot with git state
        var snapshotId = IndexSnapshotId.New();
        await InsertSnapshotAsync(factory, snapshotId, workspace.Id, gitState);

        // Collect through the shared pipeline so CLI and Desktop apply identical
        // ignore, detection, hash, and file-read behavior.
        var collection = await new CacheHub.Indexing.Pipeline.IndexSourceCollector().CollectAsync(workspace.RootPath);
        Console.WriteLine($"  Ignore rules hash: {collection.IgnoreRulesHash}");

        // Batch-write the shared collection in a single transaction.
        var fts = new Fts5Index(factory);
        var fileCount = collection.Documents.Count;
        var failedCount = collection.FailedCount;
        var ignoredCount = collection.IgnoredCount;
        foreach (var failure in collection.Failures)
            Console.Error.WriteLine($"  Failed: {failure}");
        var filesToIndex = collection.Documents
            .Select(file => (file.RelativePath, file.FullPath, file.Size, file.Language,
                file.IsBinary, file.ContentHash, file.Content))
            .ToList();

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
        // V8-audit-35: Two-pass fingerprint — re-capture git state after indexing to detect
        // workspace changes during the (potentially long) indexing process.
        var endGitState = await gitStateProvider.CaptureAsync(workspace.RootPath, fingerprintFilter);
        var workspaceChangedDuringBuild = !string.Equals(
            gitState.Fingerprint, endGitState.Fingerprint, StringComparison.Ordinal);

        var snapshotStatus = (ftsBuildFailed || workspaceChangedDuringBuild) ? "ActiveDegraded" : "Active";
        await ActivateSnapshotAsync(factory, snapshotId, workspace.Id, fileCount, snapshotStatus);
        await repo.UpdateStatusAsync(workspace.Id, (ftsBuildFailed || workspaceChangedDuringBuild) ? WorkspaceStatus.Indexing : WorkspaceStatus.Ready);

        Console.WriteLine($"Index build complete:");
        Console.WriteLine($"  Indexed: {fileCount}");
        Console.WriteLine($"  Ignored: {ignoredCount}");
        Console.WriteLine($"  Failed: {failedCount}");
        Console.WriteLine($"  Snapshot: {snapshotId.Value}");
        if (ftsBuildFailed)
            Console.WriteLine($"  ⚠️ FTS build failed — snapshot is ActiveDegraded. Full-text search may not work correctly.");
        if (workspaceChangedDuringBuild)
            Console.WriteLine($"  ⚠️ Workspace changed during indexing — snapshot is ActiveDegraded. Run 'cachehub index refresh' for a consistent snapshot.");
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
        new Migration0011SnapshotGitState(),
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

        // Get current indexed files before calculating the fingerprint. Deleted
        // paths remain in the prior index scope and therefore contribute their
        // "missing" marker instead of being silently filtered out.
        var indexedFiles = await GetIndexedFileEntriesAsync(factory, workspace.Id);

        // V8-P1-01: Capture fresh git state with fileFilter (fingerprint scope = index scope)
        var fingerprintFilter = ContextCommands.CreateFingerprintFilter(workspace.RootPath, indexedFiles.Keys);
        var refreshGitState = await new GitStateProvider().CaptureAsync(workspace.RootPath, fingerprintFilter);

        // Reconcile against disk — pass ignore engine for consistent filtering
        var result = ConsistencyReconciler.Reconcile(workspace.RootPath, indexedFiles, ignoreEngine: ignoreEngine);

        Console.WriteLine($"  Changes detected:");
        Console.WriteLine($"    Added: {result.AddedFiles}");
        Console.WriteLine($"    Modified: {result.ModifiedFiles}");
        Console.WriteLine($"    Deleted: {result.DeletedFiles}");
        Console.WriteLine($"    Unchanged: {result.UnchangedFiles}");

        if (result.IsConsistent)
        {
            // V8-audit-33: Even when files are consistent, git state (commit/branch/fingerprint) may have changed.
            // If so, create a metadata-only snapshot with updated git provenance.
            var oldGitState = await GetSnapshotGitStateAsync(factory, snapshotId);
            var gitStateChanged = oldGitState is null ||
                !string.Equals(oldGitState.Fingerprint, refreshGitState.Fingerprint, StringComparison.Ordinal);

            if (!gitStateChanged)
            {
                Console.WriteLine("Index is up to date. No changes needed.");
                return 0;
            }

            // Files unchanged but git state changed — create metadata-only snapshot
            Console.WriteLine("  Files unchanged, but workspace version changed (commit/branch/dirty).");
            Console.WriteLine($"  Creating metadata-only snapshot with updated git provenance...");

            var metaSnapshotId = IndexSnapshotId.New();
            await InsertSnapshotAsync(factory, metaSnapshotId, workspace.Id, refreshGitState);

            // Clone all file data from old snapshot to new
            await CloneSnapshotDataAsync(factory, snapshotId, metaSnapshotId);

            // Update file_count on new snapshot
            await using var metaConn = factory.CreateOpenConnection();
            using var metaCmd = metaConn.CreateCommand();
            metaCmd.CommandText = "UPDATE index_snapshots SET file_count = $count WHERE id = $id;";
            metaCmd.Parameters.AddWithValue("$count", activeSnapshot.Value.fileCount);
            metaCmd.Parameters.AddWithValue("$id", metaSnapshotId.Value);
            await metaCmd.ExecuteNonQueryAsync();

            // Atomically switch: new → Active, old → Archived
            using var switchCmd = metaConn.CreateCommand();
            switchCmd.CommandText = """
                UPDATE index_snapshots SET status = 'Archived' WHERE id = $old;
                UPDATE index_snapshots SET status = 'Active' WHERE id = $new;
                """;
            switchCmd.Parameters.AddWithValue("$old", snapshotId.Value);
            switchCmd.Parameters.AddWithValue("$new", metaSnapshotId.Value);
            await switchCmd.ExecuteNonQueryAsync();

            Console.WriteLine($"  Metadata-only snapshot: {snapshotId.Value} → {metaSnapshotId.Value} (Active)");
            Console.WriteLine("  Git provenance updated.");
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
        // V7-W01: reuse git state captured at start of RefreshAsync (with fileFilter)
        await InsertSnapshotAsync(factory, buildingSnapshotId, workspace.Id, refreshGitState);

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

        // The refresh must be version-consistent just like a full build.  Do not
        // activate a snapshot whose files may have been read from different
        // workspace states while the refresh was in progress.
        var endRefreshGitState = await new GitStateProvider().CaptureAsync(workspace.RootPath, fingerprintFilter);
        if (!string.Equals(refreshGitState.Fingerprint, endRefreshGitState.Fingerprint, StringComparison.Ordinal))
        {
            await DeleteSnapshotDataAsync(factory, buildingSnapshotId);
            Console.Error.WriteLine("  ✗ Workspace changed during refresh; discarded the Building snapshot.");
            Console.Error.WriteLine($"  Active snapshot {oldSnapshotId.Value} remains unchanged. Run refresh again.");
            return 3;
        }

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

        // Build ignore rules — same as Build/Refresh for consistency
        var ignoreEngine = new IgnoreRuleEngine()
            .WithDefaults()
            .WithGitIgnore(Path.Combine(workspace.RootPath, ".gitignore"))
            .WithCacheHubIgnore(Path.Combine(workspace.RootPath, ".cachehubignore"));

        // Run consistency check using the same ignore rules as Build/Refresh
        var indexedFiles = await GetIndexedFileEntriesAsync(factory, workspace.Id);
        var result = ConsistencyReconciler.Reconcile(workspace.RootPath, indexedFiles, ignoreEngine: ignoreEngine);

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

    private static async Task InsertSnapshotAsync(SqliteConnectionFactory factory, IndexSnapshotId snapshotId, WorkspaceId workspaceId, GitState? gitState = null)
    {
        await using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO index_snapshots (id, workspace_id, status, file_count, repository_commit, branch, is_dirty, workspace_fingerprint)
            VALUES ($id, $ws, 'Building', 0, $commit, $branch, $dirty, $fp);
            """;
        cmd.Parameters.AddWithValue("$id", snapshotId.Value);
        cmd.Parameters.AddWithValue("$ws", workspaceId.Value);
        cmd.Parameters.AddWithValue("$commit", (object?)gitState?.Commit ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$branch", (object?)gitState?.Branch ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$dirty", gitState?.IsDirty ?? false);
        cmd.Parameters.AddWithValue("$fp", (object?)gitState?.Fingerprint ?? DBNull.Value);
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
        => await SnapshotCloneService.CloneSnapshotDataAsync(factory, fromSnapshot, toSnapshot);

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

        // Deactivate only this workspace's current snapshot(s) — both Active and ActiveDegraded
        using var deactivateCmd = conn.CreateCommand();
        deactivateCmd.Transaction = (SqliteTransaction)tx;
        deactivateCmd.CommandText =
            "UPDATE index_snapshots SET status = 'Superseded' WHERE status IN ('Active', 'ActiveDegraded') AND workspace_id = $ws;";
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
        cmd.CommandText = "SELECT id, file_count FROM index_snapshots WHERE workspace_id = $ws AND status IN ('Active', 'ActiveDegraded') LIMIT 1;";
        cmd.Parameters.AddWithValue("$ws", workspaceId.Value);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
            return (IndexSnapshotId.Parse(reader.GetString(0)), reader.GetInt32(1));
        return null;
    }

    /// <summary>
    /// V8-audit-33: Gets the git state metadata stored on a snapshot.
    /// Used to detect metadata-only changes (commit/branch changed but file content unchanged).
    /// </summary>
    private static async Task<GitState?> GetSnapshotGitStateAsync(SqliteConnectionFactory factory, IndexSnapshotId snapshotId)
    {
        await using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT repository_commit, branch, is_dirty, workspace_fingerprint FROM index_snapshots WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", snapshotId.Value);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new GitState
            {
                Commit = reader.IsDBNull(0) ? null : reader.GetString(0),
                Branch = reader.IsDBNull(1) ? null : reader.GetString(1),
                IsDirty = reader.GetBoolean(2),
                Fingerprint = reader.IsDBNull(3) ? null : reader.GetString(3),
            };
        }
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
            WHERE s.workspace_id = $ws AND s.status IN ('Active', 'ActiveDegraded');
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
