using CacheHub.Context.Engine;
using CacheHub.Context.Payload;
using CacheHub.Core.Context;
using CacheHub.Core.Identifiers;
using CacheHub.Core.Workspaces;
using CacheHub.Storage.Database;
using CacheHub.Storage.Database.Migrations;
using CacheHub.Storage.Repositories;
using CacheHub.Storage.Search;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CacheHub.Tests;

/// <summary>
/// Real SQLite integration test: verifies the full index→context→payload pipeline
/// using actual database, FTS5, and file system — no in-memory mocks.
/// TEST-P1-001 fix.
/// </summary>
[Collection("SQLite")]
public class RealPipelineIntegrationTests
{
    [Fact]
    public async Task FullPipeline_IndexBuild_ContextBuild_Payload_GeneratesCorrectOutput()
    {
        // 1. Create temp workspace with real files
        var tempRoot = Path.Combine(Path.GetTempPath(), $"cachehub_real_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        Directory.CreateDirectory(Path.Combine(tempRoot, "src"));

        await File.WriteAllTextAsync(Path.Combine(tempRoot, "src", "auth.ts"),
            """
            export class AuthService {
              async login(user: string, pass: string): Promise<string> {
                return 'token';
              }
              async refreshToken(token: string): Promise<string> {
                return 'new_token';
              }
            }
            """);

        await File.WriteAllTextAsync(Path.Combine(tempRoot, "src", "user.ts"),
            """
            export class UserService {
              async getUser(id: string): Promise<User> {
                return { id, name: 'test' };
              }
            }
            """);

        await File.WriteAllTextAsync(Path.Combine(tempRoot, "README.md"),
            "# Test Project\n\nThis is a test repository.");

        var dbPath = Path.Combine(Path.GetTempPath(), $"cachehub_realdb_{Guid.NewGuid():N}.db");
        try
        {
            // 2. Setup database with all migrations
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

            // 3. Insert workspace
            var wsId = WorkspaceId.New();
            var wsRepo = new SqliteWorkspaceRepository(factory);
            var workspace = Workspace.Create("TestProject", tempRoot);
            await wsRepo.InsertAsync(workspace with { Id = wsId });

            // 4. Build index (simplified — write files + FTS directly)
            var snapshotId = IndexSnapshotId.New();
            await using (var conn = factory.CreateOpenConnection())
            {
                using var snapCmd = conn.CreateCommand();
                snapCmd.CommandText = "INSERT INTO index_snapshots (id, workspace_id, status, file_count) VALUES ($id, $ws, 'Building', 0);";
                snapCmd.Parameters.AddWithValue("$id", snapshotId.Value);
                snapCmd.Parameters.AddWithValue("$ws", wsId.Value);
                await snapCmd.ExecuteNonQueryAsync();
            }

            var fts = new Fts5Index(factory);
            var files = new[] { "src/auth.ts", "src/user.ts", "README.md" };
            var fileCount = 0;

            foreach (var relPath in files)
            {
                var fullPath = Path.Combine(tempRoot, relPath.Replace('/', Path.DirectorySeparatorChar));
                var content = await File.ReadAllTextAsync(fullPath);
                var hash = "sha256:" + Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

                await using var conn = factory.CreateOpenConnection();
                using var fileCmd = conn.CreateCommand();
                fileCmd.CommandText = """
                    INSERT INTO files (id, snapshot_id, path, normalized_path, size, content_hash, language, is_binary, status, hash_kind)
                    VALUES ($id, $snap, $path, $norm, $size, $hash, $lang, 0, 'Indexed', 'full');
                    """;
                fileCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
                fileCmd.Parameters.AddWithValue("$snap", snapshotId.Value);
                fileCmd.Parameters.AddWithValue("$path", relPath);
                fileCmd.Parameters.AddWithValue("$norm", relPath);
                fileCmd.Parameters.AddWithValue("$size", (long)content.Length);
                fileCmd.Parameters.AddWithValue("$hash", hash);
                fileCmd.Parameters.AddWithValue("$lang", relPath.EndsWith(".ts") ? "typescript" : "markdown");
                await fileCmd.ExecuteNonQueryAsync();

                await fts.IndexFileAsync(snapshotId, relPath, relPath, content,
                    relPath.EndsWith(".ts") ? "typescript" : "markdown", hash);
                fileCount++;
            }

            // Activate snapshot
            await using (var conn = factory.CreateOpenConnection())
            {
                using var activateCmd = conn.CreateCommand();
                activateCmd.CommandText = "UPDATE index_snapshots SET status = 'Active', file_count = $count, completed_at = datetime('now') WHERE id = $id;";
                activateCmd.Parameters.AddWithValue("$count", fileCount);
                activateCmd.Parameters.AddWithValue("$id", snapshotId.Value);
                await activateCmd.ExecuteNonQueryAsync();
            }

            // 5. Verify: query active snapshot exists
            await using (var conn = factory.CreateOpenConnection())
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT count(*) FROM index_snapshots WHERE workspace_id = $ws AND status = 'Active';";
                cmd.Parameters.AddWithValue("$ws", wsId.Value);
                Assert.Equal(1L, cmd.ExecuteScalar());
            }

            // 6. Verify: files table has correct count
            await using (var conn = factory.CreateOpenConnection())
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT count(*) FROM files WHERE snapshot_id = $snap;";
                cmd.Parameters.AddWithValue("$snap", snapshotId.Value);
                Assert.Equal(3L, cmd.ExecuteScalar());
            }

            // 7. Verify: FTS search works
            var searchResults = await fts.SearchAsync(snapshotId, "AuthService", limit: 10);
            Assert.NotEmpty(searchResults);
            Assert.Contains(searchResults, r => r.Path == "src/auth.ts");

            // 8. Verify: file hash is real (not "pending")
            await using (var conn = factory.CreateOpenConnection())
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT content_hash FROM files WHERE snapshot_id = $snap AND normalized_path = 'src/auth.ts';";
                cmd.Parameters.AddWithValue("$snap", snapshotId.Value);
                var hash = (string)cmd.ExecuteScalar()!;
                Assert.StartsWith("sha256:", hash);
                Assert.NotEqual("sha256:pending", hash);
            }

            // 9. Build context package
            var ctxRepo = new SqliteContextPackageRepository(factory);
            var engine = new ContextEngine();

            // Query indexed files from DB
            var indexedFiles = new List<Context.Recall.IndexedFileInfo>();
            await using (var conn = factory.CreateOpenConnection())
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT f.normalized_path, f.size, f.language, f.content_hash
                    FROM files f
                    WHERE f.snapshot_id = $snap;
                    """;
                cmd.Parameters.AddWithValue("$snap", snapshotId.Value);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    indexedFiles.Add(new Context.Recall.IndexedFileInfo
                    {
                        Path = reader.GetString(0),
                        NormalizedPath = reader.GetString(0),
                        Language = reader.IsDBNull(2) ? "unknown" : reader.GetString(2),
                        Size = reader.GetInt64(1),
                        ContentHash = reader.IsDBNull(3) ? null : reader.GetString(3),
                        Symbols = [],
                    });
                }
            }

            var manifest = engine.Build(
                new ContextBuildRequest
                {
                    WorkspaceId = wsId,
                    IndexSnapshotId = snapshotId,
                    Task = "Fix AuthService refreshToken in src/auth.ts",
                },
                () => indexedFiles,
                path =>
                {
                    var fullPath = Path.Combine(tempRoot, path.Replace('/', Path.DirectorySeparatorChar));
                    return File.Exists(fullPath) ? File.ReadAllText(fullPath) : "";
                },
                path => "sha256:test");

            // 10. Verify manifest
            Assert.NotNull(manifest);
            Assert.Equal(wsId.Value, manifest.WorkspaceId.Value);
            Assert.Equal(snapshotId.Value, manifest.IndexSnapshotId.Value);
            Assert.NotEmpty(manifest.SelectedFiles);

            // 11. Persist and reload
            await ctxRepo.SaveAsync(manifest);
            var reloaded = await ctxRepo.FindByIdAsync(manifest.Id);
            Assert.NotNull(reloaded);
            Assert.Equal(manifest.Id.Value, reloaded!.Id.Value);
            Assert.Equal(manifest.Task.OriginalText, reloaded.Task.OriginalText);

            // 12. Generate payload
            var generator = new PayloadGenerator();
            var payload = generator.Generate(manifest,
                path =>
                {
                    var fullPath = Path.Combine(tempRoot, path.Replace('/', Path.DirectorySeparatorChar));
                    return File.Exists(fullPath) ? File.ReadAllText(fullPath) : "";
                });

            Assert.NotEmpty(payload.Items);
            Assert.True(payload.TotalEstimatedTokens > 0);

            // 13. Verify: payload within budget (critical for core value proposition)
            Assert.True(payload.TotalEstimatedTokens <= manifest.Budget.ContextHardLimit,
                $"Payload tokens ({payload.TotalEstimatedTokens}) exceeded hard limit ({manifest.Budget.ContextHardLimit})");

            // 14. Verify: manifest selected files match payload items (no phantom files)
            var manifestPaths = manifest.SelectedFiles.Select(f => f.Path).ToHashSet();
            var payloadPaths = payload.Items.Select(i => i.Path).ToHashSet();
            Assert.Subset(manifestPaths, payloadPaths);

            // 15. Verify: all payload items have non-empty content
            foreach (var item in payload.Items)
            {
                Assert.True(!string.IsNullOrEmpty(item.Content),
                    $"Payload item '{item.Path}' has empty content — potential ghost file");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(dbPath);
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    private static void TryDelete(string dbPath)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { if (File.Exists(dbPath + suffix)) File.Delete(dbPath + suffix); }
            catch { }
        }
    }
}
