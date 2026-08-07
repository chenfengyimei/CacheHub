using CacheHub.Core.Identifiers;
using CacheHub.Storage.Database;
using CacheHub.Storage.Database.Migrations;
using CacheHub.Storage.Search;

namespace CacheHub.Tests;

/// <summary>
/// Tests for R6-W001: FTS single-file delete and upsert.
/// Verifies that modifying one file doesn't clear the entire snapshot.
/// </summary>
[Collection("SQLite")]
public class FtsSingleFileOperationsTests
{
    private static async Task<(SqliteConnectionFactory factory, IndexSnapshotId snapshotId)> SetupAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cachehub_fts_sf_{Guid.NewGuid():N}.db");
        var factory = new SqliteConnectionFactory(dbPath);
        var runner = new MigrationRunner(factory, dbPath,
        [
            new Migration0001Initial(), new Migration0002Fts5(),
            new Migration0003ContextPackages(), new Migration0004Feedback(),
            new Migration0005ContextPackageDetails(),
            new Migration0006SchemaV2(), new Migration0007ContextPackageFields(),
            new Migration0008ContextPackageFk(),
        new Migration0009PersistentCache(),
        new Migration0010RelationSourceColumn(),
        ]);
        runner.Migrate();

        var snapshotId = IndexSnapshotId.New();
        var fts = new Fts5Index(factory);

        // Insert workspace
        await using var conn = factory.CreateOpenConnection();
        using var wsCmd = conn.CreateCommand();
        wsCmd.CommandText = "INSERT INTO workspaces (id, name, root_path, root_path_hash, status, created_at) VALUES ('ws1', 'test', '/tmp', '/tmp', 'Ready', datetime('now'));";
        await wsCmd.ExecuteNonQueryAsync();

        using var snapCmd = conn.CreateCommand();
        snapCmd.CommandText = "INSERT INTO index_snapshots (id, workspace_id, status, file_count) VALUES ($id, 'ws1', 'Active', 3);";
        snapCmd.Parameters.AddWithValue("$id", snapshotId.Value);
        await snapCmd.ExecuteNonQueryAsync();

        // Index 3 files
        await fts.IndexFileAsync(snapshotId, "src/a.ts", "src/a.ts", "export class AuthService { login() {} }", "typescript", "hash-a");
        await fts.IndexFileAsync(snapshotId, "src/b.ts", "src/b.ts", "export class UserService { getUser() {} }", "typescript", "hash-b");
        await fts.IndexFileAsync(snapshotId, "src/c.ts", "src/c.ts", "export class ConfigService { load() {} }", "typescript", "hash-c");

        return (factory, snapshotId);
    }

    [Fact]
    public async Task DeleteFile_RemovesOnlyOneEntry_OthersRemainSearchable()
    {
        var (factory, snapshotId) = await SetupAsync();
        var fts = new Fts5Index(factory);

        // Delete src/b.ts
        await fts.DeleteFileAsync(snapshotId, "src/b.ts");

        // a.ts and c.ts should still be searchable
        var authResults = await fts.SearchAsync(snapshotId, "AuthService");
        Assert.NotEmpty(authResults);

        var configResults = await fts.SearchAsync(snapshotId, "ConfigService");
        Assert.NotEmpty(configResults);

        // b.ts should NOT be searchable
        var userResults = await fts.SearchAsync(snapshotId, "UserService");
        Assert.DoesNotContain(userResults, r => r.Path == "src/b.ts");
    }

    [Fact]
    public async Task UpsertFile_ReplacesContent_KeepsOthersIntact()
    {
        var (factory, snapshotId) = await SetupAsync();
        var fts = new Fts5Index(factory);

        // Upsert src/a.ts with new content
        await fts.UpsertFileAsync(snapshotId, "src/a.ts", "src/a.ts",
            "export class AuthService { async refreshToken() {} }", "typescript", "hash-a-v2");

        // a.ts should now be searchable for "refreshToken"
        var refreshResults = await fts.SearchAsync(snapshotId, "refreshToken");
        Assert.NotEmpty(refreshResults);

        // b.ts and c.ts should still be searchable
        var userResults = await fts.SearchAsync(snapshotId, "UserService");
        Assert.NotEmpty(userResults);

        var configResults = await fts.SearchAsync(snapshotId, "ConfigService");
        Assert.NotEmpty(configResults);
    }

    [Fact]
    public async Task FileExists_ReturnsTrueForIndexed_ReturnsFalseForMissing()
    {
        var (factory, snapshotId) = await SetupAsync();
        var fts = new Fts5Index(factory);

        Assert.True(await fts.FileExistsAsync(snapshotId, "src/a.ts"));
        Assert.False(await fts.FileExistsAsync(snapshotId, "nonexistent.ts"));
    }

    [Fact]
    public async Task DeleteFile_NonExistentPath_DoesNotThrow()
    {
        var (factory, snapshotId) = await SetupAsync();
        var fts = new Fts5Index(factory);

        // Should not throw
        await fts.DeleteFileAsync(snapshotId, "nonexistent.ts");

        // All original files should still be searchable
        var results = await fts.SearchAsync(snapshotId, "AuthService");
        Assert.NotEmpty(results);
    }

    [Fact]
    public async Task UpsertFile_CalledTwice_DoesNotDuplicate()
    {
        var (factory, snapshotId) = await SetupAsync();
        var fts = new Fts5Index(factory);

        // Upsert same file twice
        await fts.UpsertFileAsync(snapshotId, "src/a.ts", "src/a.ts",
            "export class AuthService { login() {} }", "typescript", "hash-a");
        await fts.UpsertFileAsync(snapshotId, "src/a.ts", "src/a.ts",
            "export class AuthService { login() {} }", "typescript", "hash-a");

        // Should return exactly one result, not duplicates
        var results = await fts.SearchAsync(snapshotId, "AuthService");
        var aResults = results.Where(r => r.Path == "src/a.ts").ToList();
        Assert.Single(aResults);
    }
}
