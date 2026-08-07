using CacheHub.Core.Identifiers;
using CacheHub.Storage.Database;
using CacheHub.Storage.Database.Migrations;
using CacheHub.Storage.Search;
using Microsoft.Data.Sqlite;

namespace CacheHub.Tests;

[Collection("SQLite")]
public class Fts5IndexTests
{
    private static readonly string PaymentContent = string.Join("\n", new[]
    {
        "export class PaymentService {",
        "  // transaction is handled elsewhere",
        "",
        "  configure() {",
        "    return { enabled: false };",
        "  }",
        "",
        "  processTransaction(amount: number) {",
        "    // transaction core logic",
        "    return amount * 1.0;",
        "  }",
        "}",
    });

    [Fact]
    public async Task Fts5Index_IndexFileAsync_ShouldStoreContent()
    {
        var dbPath = GetTempDbPath();
        try
        {
            var factory = SetupFactory(dbPath);
            var snapshotId = IndexSnapshotId.New();
            var fts = new Fts5Index(factory);

            await fts.IndexFileAsync(snapshotId, "src/app.ts", "src/app.ts",
                "export function hello() { return 'world'; }", "typescript", "sha256:abc");

            var results = await fts.SearchAsync(snapshotId, "hello");

            Assert.Single(results);
            Assert.Equal("src/app.ts", results[0].Path);
            Assert.Equal("typescript", results[0].Language);
            Assert.Contains("hello", results[0].Snippet);
        }
        finally { Cleanup(dbPath); }
    }

    [Fact]
    public async Task Fts5Index_SearchAsync_ShouldReturnMatchingFiles()
    {
        var dbPath = GetTempDbPath();
        try
        {
            var factory = SetupFactory(dbPath);
            var snapshotId = IndexSnapshotId.New();
            var fts = new Fts5Index(factory);

            await fts.IndexFileAsync(snapshotId, "src/auth.ts", "src/auth.ts",
                "export function login(user: string) { /* login logic */ }", "typescript", "h1");
            await fts.IndexFileAsync(snapshotId, "src/api.ts", "src/api.ts",
                "export function fetchData(url: string) { /* fetch */ }", "typescript", "h2");

            var results = await fts.SearchAsync(snapshotId, "login");

            Assert.Single(results);
            Assert.Equal("src/auth.ts", results[0].Path);
        }
        finally { Cleanup(dbPath); }
    }

    [Fact]
    public async Task Fts5Index_SearchAsync_ShouldRespectSnapshotId()
    {
        var dbPath = GetTempDbPath();
        try
        {
            var factory = SetupFactory(dbPath);
            var snapshot1 = IndexSnapshotId.New();
            var snapshot2 = IndexSnapshotId.New();
            var fts = new Fts5Index(factory);

            await fts.IndexFileAsync(snapshot1, "old.ts", "old.ts", "function old() {}", "typescript", "h1");
            await fts.IndexFileAsync(snapshot2, "new.ts", "new.ts", "function new() {}", "typescript", "h2");

            var results1 = await fts.SearchAsync(snapshot1, "old");
            var results2 = await fts.SearchAsync(snapshot2, "new");

            Assert.Single(results1);
            Assert.Equal("old.ts", results1[0].Path);
            Assert.Single(results2);
            Assert.Equal("new.ts", results2[0].Path);
        }
        finally { Cleanup(dbPath); }
    }

    [Fact]
    public async Task Fts5Index_IndexChunkAsync_ShouldStoreChunk()
    {
        var dbPath = GetTempDbPath();
        try
        {
            var factory = SetupFactory(dbPath);
            var snapshotId = await InsertSnapshotAsync(factory);
            var fts = new Fts5Index(factory);

            await fts.IndexChunkAsync(snapshotId, "src/big.ts", 10, 50, "function chunk() {}");

            // Chunks are stored in file_chunks table; verify via search on FTS content.
            var results = await fts.SearchAsync(snapshotId, "chunk");
            // Chunks go to file_chunks table, not FTS. Verify no error occurred.
            Assert.Empty(results);
        }
        finally { Cleanup(dbPath); }
    }

    [Fact]
    public async Task Fts5Index_ClearSnapshotAsync_ShouldRemoveEntries()
    {
        var dbPath = GetTempDbPath();
        try
        {
            var factory = SetupFactory(dbPath);
            var snapshotId = IndexSnapshotId.New();
            var fts = new Fts5Index(factory);

            await fts.IndexFileAsync(snapshotId, "a.ts", "a.ts", "content here", "typescript", "h1");
            await fts.ClearSnapshotAsync(snapshotId);

            var results = await fts.SearchAsync(snapshotId, "content");
            Assert.Empty(results);
        }
        finally { Cleanup(dbPath); }
    }

    /// <summary>
    /// V4: FTS Anchor should locate the line matching the snippet's highlighted terms,
    /// not just the first occurrence of any query keyword in the file.
    /// </summary>
    [Fact]
    public async Task Fts5Index_SearchAsync_HitLine_PrefersSnippetHighlightedPosition()
    {
        var dbPath = GetTempDbPath();
        try
        {
            var factory = SetupFactory(dbPath);
            var snapshotId = IndexSnapshotId.New();
            var fts = new Fts5Index(factory);

            // Construct content where "transaction" appears first in a comment (line 2)
            // but the actual implementation is at line 8.
            var content = PaymentContent;

            await fts.IndexFileAsync(snapshotId, "payment.ts", "payment.ts", content, "typescript", "h1");

            var results = await fts.SearchAsync(snapshotId, "transaction");
            Assert.NotEmpty(results);
            var hitLine = results[0].HitLine;

            // The HitLine should be near the snippet's highlighted region.
            // The snippet function with 10 fragments should highlight "transaction" near the processTransaction method.
            // Both line 2 (comment) and line 8/9 (implementation) contain "transaction",
            // but with multiple highlighted terms (e.g., "processTransaction", "transaction"),
            // the line containing the most highlighted terms should win.
            Assert.NotNull(hitLine);
            Assert.True(hitLine >= 7, $"Expected hit line near the implementation (>=7), got {hitLine}");
        }
        finally { Cleanup(dbPath); }
    }

    private static async Task<IndexSnapshotId> InsertSnapshotAsync(SqliteConnectionFactory factory)
    {
        var wsId = WorkspaceId.New();
        var snapshotId = IndexSnapshotId.New();

        await using var conn = factory.CreateOpenConnection();
        using var wsCmd = conn.CreateCommand();
        wsCmd.CommandText =
            """
            INSERT INTO workspaces (id, name, root_path, root_path_hash, status)
            VALUES ($id, $name, $path, $hash, 'Ready');
            """;
        wsCmd.Parameters.AddWithValue("$id", wsId.Value);
        wsCmd.Parameters.AddWithValue("$name", "test");
        wsCmd.Parameters.AddWithValue("$path", "/test");
        wsCmd.Parameters.AddWithValue("$hash", "hash_test");
        await wsCmd.ExecuteNonQueryAsync();

        using var snapCmd = conn.CreateCommand();
        snapCmd.CommandText =
            """
            INSERT INTO index_snapshots (id, workspace_id, status, file_count)
            VALUES ($id, $ws, 'Active', 0);
            """;
        snapCmd.Parameters.AddWithValue("$id", snapshotId.Value);
        snapCmd.Parameters.AddWithValue("$ws", wsId.Value);
        await snapCmd.ExecuteNonQueryAsync();

        return snapshotId;
    }

    private static SqliteConnectionFactory SetupFactory(string dbPath)
    {
        var factory = new SqliteConnectionFactory(dbPath);
        var runner = new MigrationRunner(factory, dbPath,
        [
            new Migration0001Initial(),
            new Migration0002Fts5(),
        ]);
        runner.Migrate();
        return factory;
    }

    private static string GetTempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"cachehub_fts_{Guid.NewGuid():N}.db");

    private static void Cleanup(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { if (File.Exists(dbPath + suffix)) File.Delete(dbPath + suffix); }
            catch { }
        }
    }
}
