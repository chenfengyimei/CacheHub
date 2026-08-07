using CacheHub.Core.Identifiers;
using CacheHub.Storage.Database;
using CacheHub.Storage.Database.Migrations;
using CacheHub.Storage.Query;

namespace CacheHub.Tests;

[Collection("SQLite")]
public class IndexQueryServiceTests
{
    private static async Task<(SqliteConnectionFactory factory, IndexSnapshotId snapshotId, string workspaceId)> SetupDatabaseAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cachehub_iqs_{Guid.NewGuid():N}.db");
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

        var workspaceId = "test-ws-" + Guid.NewGuid().ToString("N")[..8];
        var snapshotId = IndexSnapshotId.New();

        await using var conn = factory.CreateOpenConnection();

        using var wsCmd = conn.CreateCommand();
        wsCmd.CommandText = """
            INSERT INTO workspaces (id, name, root_path, root_path_hash, status, created_at)
            VALUES ($id, 'test', '/tmp/test', $hash, 'Ready', datetime('now'));
            """;
        wsCmd.Parameters.AddWithValue("$id", workspaceId);
        wsCmd.Parameters.AddWithValue("$hash", "/tmp/test");
        await wsCmd.ExecuteNonQueryAsync();

        using var snapCmd = conn.CreateCommand();
        snapCmd.CommandText = "INSERT INTO index_snapshots (id, workspace_id, status, file_count) VALUES ($id, $ws, 'Active', 2);";
        snapCmd.Parameters.AddWithValue("$id", snapshotId.Value);
        snapCmd.Parameters.AddWithValue("$ws", workspaceId);
        await snapCmd.ExecuteNonQueryAsync();

        var file1Id = Guid.NewGuid().ToString("N");
        var file2Id = Guid.NewGuid().ToString("N");

        using (var f1Cmd = conn.CreateCommand())
        {
            f1Cmd.CommandText = """
                INSERT INTO files (id, snapshot_id, path, normalized_path, size, content_hash, language, is_binary, status, hash_kind, mtime)
                VALUES ($id, $snap, 'src/auth.ts', 'src/auth.ts', 500, 'sha256:abc123', 'typescript', 0, 'Indexed', 'full', '2026-01-01T00:00:00Z');
                """;
            f1Cmd.Parameters.AddWithValue("$id", file1Id);
            f1Cmd.Parameters.AddWithValue("$snap", snapshotId.Value);
            await f1Cmd.ExecuteNonQueryAsync();
        }

        using (var f2Cmd = conn.CreateCommand())
        {
            f2Cmd.CommandText = """
                INSERT INTO files (id, snapshot_id, path, normalized_path, size, content_hash, language, is_binary, status, hash_kind, mtime)
                VALUES ($id, $snap, 'src/user.ts', 'src/user.ts', 300, 'sha256:def456', 'typescript', 0, 'Indexed', 'full', '2026-01-01T00:00:00Z');
                """;
            f2Cmd.Parameters.AddWithValue("$id", file2Id);
            f2Cmd.Parameters.AddWithValue("$snap", snapshotId.Value);
            await f2Cmd.ExecuteNonQueryAsync();
        }

        using (var symCmd = conn.CreateCommand())
        {
            symCmd.CommandText = """
                INSERT INTO file_symbols (id, file_id, snapshot_id, name, kind, start_line, end_line, modifier)
                VALUES ($id, $fid, $snap, 'AuthService', 'Class', 1, 50, 'public');
                """;
            symCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            symCmd.Parameters.AddWithValue("$fid", file1Id);
            symCmd.Parameters.AddWithValue("$snap", snapshotId.Value);
            await symCmd.ExecuteNonQueryAsync();
        }

        using (var sym2Cmd = conn.CreateCommand())
        {
            sym2Cmd.CommandText = """
                INSERT INTO file_symbols (id, file_id, snapshot_id, name, kind, start_line, end_line, modifier)
                VALUES ($id, $fid, $snap, 'UserService', 'Class', 1, 30, 'public');
                """;
            sym2Cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            sym2Cmd.Parameters.AddWithValue("$fid", file2Id);
            sym2Cmd.Parameters.AddWithValue("$snap", snapshotId.Value);
            await sym2Cmd.ExecuteNonQueryAsync();
        }

        using (var impCmd = conn.CreateCommand())
        {
            impCmd.CommandText = """
                INSERT INTO file_imports (id, file_id, snapshot_id, module, imported_name, line)
                VALUES ($id, $fid, $snap, './auth', 'AuthService', 1);
                """;
            impCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            impCmd.Parameters.AddWithValue("$fid", file2Id);
            impCmd.Parameters.AddWithValue("$snap", snapshotId.Value);
            await impCmd.ExecuteNonQueryAsync();
        }

        using (var relCmd = conn.CreateCommand())
        {
            relCmd.CommandText = """
                INSERT INTO file_relations (id, file_id, snapshot_id, source_symbol, target_symbol, relation_type, confidence, line)
                VALUES ($id, $fid, $snap, 'UserService', './auth', 'imports', 'syntactic', 1);
                """;
            relCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            relCmd.Parameters.AddWithValue("$fid", file2Id);
            relCmd.Parameters.AddWithValue("$snap", snapshotId.Value);
            await relCmd.ExecuteNonQueryAsync();
        }

        var fts = new CacheHub.Storage.Search.Fts5Index(factory);
        await fts.IndexFileAsync(snapshotId, "src/auth.ts", "src/auth.ts",
            "export class AuthService { async login() {} }", "typescript", "sha256:abc123");
        await fts.IndexFileAsync(snapshotId, "src/user.ts", "src/user.ts",
            "import { AuthService } from './auth'; export class UserService {}", "typescript", "sha256:def456");

        return (factory, snapshotId, workspaceId);
    }

    [Fact]
    public async Task GetActiveSnapshotId_ReturnsCorrectId()
    {
        var (factory, snapshotId, workspaceId) = await SetupDatabaseAsync();
        var svc = new SqliteIndexQueryService(factory);
        var result = await svc.GetActiveSnapshotIdAsync(workspaceId);
        Assert.NotNull(result);
        Assert.Equal(snapshotId.Value, result.Value);
    }

    [Fact]
    public async Task GetActiveSnapshotId_ReturnsNullForUnknownWorkspace()
    {
        var (factory, _, _) = await SetupDatabaseAsync();
        var svc = new SqliteIndexQueryService(factory);
        var result = await svc.GetActiveSnapshotIdAsync("nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetIndexedFiles_ReturnsAllFiles()
    {
        var (factory, _, workspaceId) = await SetupDatabaseAsync();
        var svc = new SqliteIndexQueryService(factory);
        var files = await svc.GetIndexedFilesAsync(workspaceId);
        Assert.Equal(2, files.Count);
        Assert.Contains(files, f => f.NormalizedPath == "src/auth.ts");
        Assert.Contains(files, f => f.NormalizedPath == "src/user.ts");
    }

    [Fact]
    public async Task GetIndexedFilesBySnapshot_ReturnsAllFiles()
    {
        var (factory, snapshotId, _) = await SetupDatabaseAsync();
        var svc = new SqliteIndexQueryService(factory);
        var files = await svc.GetIndexedFilesBySnapshotAsync(snapshotId);
        Assert.Equal(2, files.Count);
        Assert.Contains(files, f => f.ContentHash == "sha256:abc123");
    }

    [Fact]
    public async Task GetFileHash_ReturnsCorrectHash()
    {
        var (factory, snapshotId, _) = await SetupDatabaseAsync();
        var svc = new SqliteIndexQueryService(factory);
        var hash = await svc.GetFileHashAsync(snapshotId, "src/auth.ts");
        Assert.Equal("sha256:abc123", hash);
    }

    [Fact]
    public async Task GetFileHash_ReturnsNullForMissingFile()
    {
        var (factory, snapshotId, _) = await SetupDatabaseAsync();
        var svc = new SqliteIndexQueryService(factory);
        var hash = await svc.GetFileHashAsync(snapshotId, "nonexistent.ts");
        Assert.Null(hash);
    }

    [Fact]
    public async Task SearchSymbols_ExactMatch_ReturnsResults()
    {
        var (factory, snapshotId, _) = await SetupDatabaseAsync();
        var svc = new SqliteIndexQueryService(factory);
        var results = await svc.SearchSymbolsAsync(snapshotId, "AuthService");
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Name == "AuthService");
        Assert.All(results, r => Assert.True(r.ExactMatch));
    }

    [Fact]
    public async Task SearchSymbols_LikeFallback_ReturnsResults()
    {
        var (factory, snapshotId, _) = await SetupDatabaseAsync();
        var svc = new SqliteIndexQueryService(factory);
        var results = await svc.SearchSymbolsAsync(snapshotId, "Auth");
        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.False(r.ExactMatch));
    }

    [Fact]
    public async Task GetFileSymbols_ReturnsSymbolsForFile()
    {
        var (factory, snapshotId, _) = await SetupDatabaseAsync();
        var svc = new SqliteIndexQueryService(factory);
        var symbols = await svc.GetFileSymbolsAsync(snapshotId, "src/auth.ts");
        Assert.NotEmpty(symbols);
        Assert.Contains(symbols, s => s.Name == "AuthService");
    }

    [Fact]
    public async Task GetFileImports_ReturnsImportsForFile()
    {
        var (factory, snapshotId, _) = await SetupDatabaseAsync();
        var svc = new SqliteIndexQueryService(factory);
        var imports = await svc.GetFileImportsAsync(snapshotId, "src/user.ts");
        Assert.NotEmpty(imports);
        Assert.Contains(imports, i => i.Module == "./auth");
    }

    [Fact]
    public async Task GetFileRelations_ReturnsRelationsForFile()
    {
        var (factory, snapshotId, _) = await SetupDatabaseAsync();
        var svc = new SqliteIndexQueryService(factory);
        var relations = await svc.GetFileRelationsAsync(snapshotId, "src/user.ts");
        Assert.NotEmpty(relations);
        Assert.Contains(relations, r => r.RelationType == "imports");
    }

    [Fact]
    public async Task GetFilesByImportedSymbol_ReturnsImportingFiles()
    {
        var (factory, snapshotId, _) = await SetupDatabaseAsync();
        var svc = new SqliteIndexQueryService(factory);
        var files = await svc.GetFilesByImportedSymbolAsync(snapshotId, "AuthService");
        Assert.NotEmpty(files);
        Assert.Contains(files, f => f == "src/user.ts");
    }

    [Fact]
    public async Task GetSnapshotStatus_ReturnsCorrectStatus()
    {
        var (factory, snapshotId, _) = await SetupDatabaseAsync();
        var svc = new SqliteIndexQueryService(factory);
        var status = await svc.GetSnapshotStatusAsync(snapshotId);
        Assert.NotNull(status);
        Assert.Equal("Active", status.Status);
        Assert.Equal(2, status.FileCount);
    }

    [Fact]
    public async Task SearchFts_ReturnsResults()
    {
        var (factory, snapshotId, _) = await SetupDatabaseAsync();
        var svc = new SqliteIndexQueryService(factory);
        var results = await svc.SearchFtsAsync(snapshotId, "login");
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Path == "src/auth.ts");
    }
}
