using CacheHub.Context.Cache;
using CacheHub.Context.Engine;
using CacheHub.Context.Recall;
using CacheHub.Core.Context;
using CacheHub.Core.Identifiers;
using CacheHub.Core.Workspaces;
using CacheHub.Storage.Database;
using CacheHub.Storage.Database.Migrations;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CacheHub.Tests;

/// <summary>
/// Verifies that ContextPackageCache is correctly wired into ContextEngine.Build.
/// CTX-P1-010 fix verification.
/// </summary>
[Collection("SQLite")]
public class ContextCacheIntegrationTests
{
    [Fact]
    public void ContextEngine_WithCache_ReturnsCachedResultOnSecondCall()
    {
        var cache = new ContextPackageCache();
        var engine = new ContextEngine(cache: cache);

        var request = new ContextBuildRequest
        {
            WorkspaceId = WorkspaceId.New(),
            IndexSnapshotId = IndexSnapshotId.New(),
            Task = "Fix login bug in AuthService",
        };

        var indexedFiles = new List<IndexedFileInfo>
        {
            new() { Path = "src/auth.ts", NormalizedPath = "src/auth.ts", Size = 200, Language = "TypeScript", ContentHash = "sha256:abc" },
            new() { Path = "src/config.ts", NormalizedPath = "src/config.ts", Size = 100, Language = "TypeScript", ContentHash = "sha256:def" },
        };

        var contentProvider = new Func<string, string>(path => path switch
        {
            "src/auth.ts" => "export class AuthService { login() {} }",
            "src/config.ts" => "export const API = 'test';",
            _ => "",
        });
        var hashProvider = new Func<string, string>(_ => "sha256:test");

        // First call: should build and cache
        var manifest1 = engine.Build(request, () => indexedFiles, contentProvider, hashProvider);
        Assert.True(cache.Count >= 1);

        // Second call with same parameters: should return cached result
        var manifest2 = engine.Build(request, () => indexedFiles, contentProvider, hashProvider);

        // Same manifest returned (cached)
        Assert.Equal(manifest1.Id.Value, manifest2.Id.Value);
    }

    [Fact]
    public void ContextEngine_WithCache_DifferentTaskReturnsDifferentResult()
    {
        var cache = new ContextPackageCache();
        var engine = new ContextEngine(cache: cache);

        var snapshotId = IndexSnapshotId.New();
        var wsId = WorkspaceId.New();

        var indexedFiles = new List<IndexedFileInfo>
        {
            new() { Path = "src/auth.ts", NormalizedPath = "src/auth.ts", Size = 200, Language = "TypeScript", ContentHash = "sha256:abc" },
        };

        var contentProvider = new Func<string, string>(path => "export class AuthService {}");
        var hashProvider = new Func<string, string>(_ => "sha256:test");

        var request1 = new ContextBuildRequest
        {
            WorkspaceId = wsId,
            IndexSnapshotId = snapshotId,
            Task = "Fix login bug",
        };
        var request2 = new ContextBuildRequest
        {
            WorkspaceId = wsId,
            IndexSnapshotId = snapshotId,
            Task = "Add new feature", // Different task → different cache key
        };

        var manifest1 = engine.Build(request1, () => indexedFiles, contentProvider, hashProvider);
        var manifest2 = engine.Build(request2, () => indexedFiles, contentProvider, hashProvider);

        // Different manifests (different cache keys)
        Assert.NotEqual(manifest1.Id.Value, manifest2.Id.Value);
    }

    [Fact]
    public void ContextEngine_WithoutCache_WorksNormally()
    {
        // Default: no cache, should not throw
        var engine = new ContextEngine();

        var request = new ContextBuildRequest
        {
            WorkspaceId = WorkspaceId.New(),
            IndexSnapshotId = IndexSnapshotId.New(),
            Task = "Fix bug",
        };

        var indexedFiles = new List<IndexedFileInfo>
        {
            new() { Path = "test.ts", NormalizedPath = "test.ts", Size = 100, Language = "TypeScript", ContentHash = "sha256:abc" },
        };

        var manifest = engine.Build(request, () => indexedFiles, _ => "export const x = 1;", _ => "sha256:test");

        Assert.NotNull(manifest);
        Assert.NotEmpty(manifest.SelectedFiles);
    }

    /// <summary>
    /// V4: Persistent cache backend — a new ContextPackageCache instance (simulating a fresh
    /// CLI process) should hit the persistent SQLite cache without re-running the pipeline.
    /// </summary>
    [Fact]
    public async Task ContextEngine_PersistentCache_NewCacheInstanceHits()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cachehub_ctxcache_{Guid.NewGuid():N}.db");
        var blobDir = Path.Combine(Path.GetTempPath(), $"cachehub_ctxcache_blobs_{Guid.NewGuid():N}");
        try
        {
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

            var store = new CacheHub.Storage.Caching.SqliteCacheStore(factory, blobDir);

            // First "process": build context and persist
            var cache1 = new ContextPackageCache(store);
            var engine1 = new ContextEngine(cache: cache1);
            var snapshotId = IndexSnapshotId.New();
            var wsId = WorkspaceId.New();

            var indexedFiles = new List<IndexedFileInfo>
            {
                new() { Path = "src/auth.ts", NormalizedPath = "src/auth.ts", Size = 200, Language = "TypeScript", ContentHash = "sha256:abc" },
            };

            var request = new ContextBuildRequest
            {
                WorkspaceId = wsId,
                IndexSnapshotId = snapshotId,
                Task = "Fix login bug in AuthService",
            };

            var manifest1 = engine1.Build(request, () => indexedFiles, _ => "export class AuthService {}", _ => "sha256:test");
            Assert.True(cache1.Count >= 1);

            // Second "process": fresh cache instance backed by the same SQLite store.
            // Should restore from persistent store without re-executing the pipeline.
            var cache2 = new ContextPackageCache(store);
            var engine2 = new ContextEngine(cache: cache2);
            var manifest2 = engine2.Build(request, () => indexedFiles, _ => "export class AuthService {}", _ => "sha256:test");

            Assert.Equal(manifest1.Id.Value, manifest2.Id.Value);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try { if (File.Exists(dbPath + suffix)) File.Delete(dbPath + suffix); } catch { }
            }
            try { if (Directory.Exists(blobDir)) Directory.Delete(blobDir, true); } catch { }
        }
    }
}
