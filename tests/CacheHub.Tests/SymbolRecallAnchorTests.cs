using CacheHub.Context.Engine;
using CacheHub.Context.Recall;
using CacheHub.Core.Identifiers;
using CacheHub.Storage.Database;
using CacheHub.Storage.Database.Migrations;
using CacheHub.Storage.Query;
using CacheHub.Storage.Search;

namespace CacheHub.Tests;

/// <summary>
/// Tests for R4-W004: Symbol recall with line anchors.
/// Verifies that SymbolSearchDetailed callback produces LineAnchors with definition ranges.
/// </summary>
[Collection("SQLite")]
public class SymbolRecallAnchorTests
{
    private static async Task<(SqliteConnectionFactory factory, IndexSnapshotId snapshotId, string workspacePath)> SetupAsync()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), $"cachehub_sym_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(workspacePath, "src"));

        await File.WriteAllTextAsync(Path.Combine(workspacePath, "src", "auth.ts"),
            "export class AuthService {\n  async login(user: string, pass: string): Promise<string> {\n    return 'token';\n  }\n}");

        var dbPath = Path.Combine(Path.GetTempPath(), $"cachehub_sym_db_{Guid.NewGuid():N}.db");
        var factory = new SqliteConnectionFactory(dbPath);
        var runner = new MigrationRunner(factory, dbPath,
        [
            new Migration0001Initial(), new Migration0002Fts5(), new Migration0003ContextPackages(),
            new Migration0004Feedback(), new Migration0005ContextPackageDetails(),
            new Migration0006SchemaV2(), new Migration0007ContextPackageFields(), new Migration0008ContextPackageFk(),
        new Migration0009PersistentCache(),
        new Migration0010RelationSourceColumn(),
        ]);
        runner.Migrate();

        var workspaceId = "sym-test-ws";
        var snapshotId = IndexSnapshotId.New();

        await using var conn = factory.CreateOpenConnection();

        using var wsCmd = conn.CreateCommand();
        wsCmd.CommandText = "INSERT INTO workspaces (id, name, root_path, root_path_hash, status, created_at) VALUES ($id, 'test', $root, $hash, 'Ready', datetime('now'));";
        wsCmd.Parameters.AddWithValue("$id", workspaceId);
        wsCmd.Parameters.AddWithValue("$root", workspacePath);
        wsCmd.Parameters.AddWithValue("$hash", workspacePath);
        await wsCmd.ExecuteNonQueryAsync();

        using var snapCmd = conn.CreateCommand();
        snapCmd.CommandText = "INSERT INTO index_snapshots (id, workspace_id, status, file_count) VALUES ($id, $ws, 'Active', 1);";
        snapCmd.Parameters.AddWithValue("$id", snapshotId.Value);
        snapCmd.Parameters.AddWithValue("$ws", workspaceId);
        await snapCmd.ExecuteNonQueryAsync();

        var fileId = Guid.NewGuid().ToString("N");
        using var fCmd = conn.CreateCommand();
        fCmd.CommandText = "INSERT INTO files (id, snapshot_id, path, normalized_path, size, content_hash, language, is_binary, status, hash_kind) VALUES ($id, $snap, 'src/auth.ts', 'src/auth.ts', 100, 'sha256:test', 'typescript', 0, 'Indexed', 'full');";
        fCmd.Parameters.AddWithValue("$id", fileId);
        fCmd.Parameters.AddWithValue("$snap", snapshotId.Value);
        await fCmd.ExecuteNonQueryAsync();

        // Insert symbol with line range
        using var symCmd = conn.CreateCommand();
        symCmd.CommandText = "INSERT INTO file_symbols (id, file_id, snapshot_id, name, kind, start_line, end_line, modifier) VALUES ($id, $fid, $snap, 'AuthService', 'Class', 1, 4, 'public');";
        symCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        symCmd.Parameters.AddWithValue("$fid", fileId);
        symCmd.Parameters.AddWithValue("$snap", snapshotId.Value);
        await symCmd.ExecuteNonQueryAsync();

        // FTS
        var fts = new Fts5Index(factory);
        var content = await File.ReadAllTextAsync(Path.Combine(workspacePath, "src", "auth.ts"));
        await fts.IndexFileAsync(snapshotId, "src/auth.ts", "src/auth.ts", content, "typescript", "sha256:test");

        return (factory, snapshotId, workspacePath);
    }

    private static List<IndexedFileInfo> GetIndexedFiles(SqliteConnectionFactory factory, string workspaceId)
    {
        var result = new List<IndexedFileInfo>();
        using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT f.normalized_path, f.size, f.language, f.content_hash
            FROM files f INNER JOIN index_snapshots s ON f.snapshot_id = s.id
            WHERE s.workspace_id = $ws AND s.status = 'Active';
            """;
        cmd.Parameters.AddWithValue("$ws", workspaceId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new IndexedFileInfo
            {
                Path = reader.GetString(0),
                NormalizedPath = reader.GetString(0),
                Size = reader.GetInt64(1),
                Language = reader.GetString(2),
                ContentHash = reader.GetString(3),
                Symbols = [],
            });
        }
        return result;
    }

    [Fact]
    public async Task SymbolSearchDetailed_ReturnsLineRanges()
    {
        var (factory, snapshotId, _) = await SetupAsync();
        var querySvc = new SqliteIndexQueryService(factory);

        var results = await querySvc.SearchSymbolsAsync(snapshotId, "AuthService");

        Assert.NotEmpty(results);
        var sym = results.First(r => r.Name == "AuthService");
        Assert.Equal(1, sym.StartLine);
        Assert.Equal(4, sym.EndLine);
        Assert.True(sym.ExactMatch);
    }

    [Fact]
    public async Task ContextBuild_WithSymbolAnchors_GeneratesLineRanges()
    {
        var (factory, snapshotId, workspacePath) = await SetupAsync();
        try
        {
            var querySvc = new SqliteIndexQueryService(factory);
            var indexedFiles = GetIndexedFiles(factory, "sym-test-ws");
            var engine = new ContextEngine();

            var manifest = engine.Build(
                new ContextBuildRequest
                {
                    WorkspaceId = WorkspaceId.New(),
                    IndexSnapshotId = snapshotId,
                    Task = "Fix AuthService login",
                },
                () => indexedFiles,
                path =>
                {
                    var fullPath = Path.Combine(workspacePath, path.Replace('/', Path.DirectorySeparatorChar));
                    return File.Exists(fullPath) ? File.ReadAllTextAsync(fullPath).GetAwaiter().GetResult() : "";
                },
                path => "sha256:test",
                symbolSearchDetailed: symbol =>
                {
                    var results = querySvc.SearchSymbolsAsync(snapshotId, symbol).GetAwaiter().GetResult();
                    return results.Select(r => new SymbolHit
                    {
                        NormalizedPath = r.NormalizedPath,
                        Name = r.Name,
                        Kind = r.Kind,
                        StartLine = r.StartLine,
                        EndLine = r.EndLine,
                        ExactMatch = r.ExactMatch,
                    }).ToList();
                });

            Assert.NotEmpty(manifest.SelectedFiles);
            var selected = manifest.SelectedFiles.First(f => f.Path.Contains("auth.ts"));
            // The symbol anchor should produce line ranges in the manifest
            Assert.NotNull(selected.Ranges);
        }
        finally
        {
            try { Directory.Delete(workspacePath, true); } catch { }
        }
    }

    [Fact]
    public async Task SymbolSearch_LikeFallback_ReturnsNonExact()
    {
        var (factory, snapshotId, _) = await SetupAsync();
        var querySvc = new SqliteIndexQueryService(factory);

        var results = await querySvc.SearchSymbolsAsync(snapshotId, "Auth");

        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.False(r.ExactMatch));
    }
}
