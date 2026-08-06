using CacheHub.Core.Identifiers;
using CacheHub.Core.Indexing;
using CacheHub.Core.Jobs;
using CacheHub.Core.Logging;
using CacheHub.Core.Workspaces;
using CacheHub.Storage;
using CacheHub.Storage.Database;
using CacheHub.Storage.Database.Migrations;
using CacheHub.Storage.Repositories;
using Microsoft.Data.Sqlite;

namespace CacheHub.Tests;

[Collection("SQLite")]
public class P01RepositoryAndModelTests
{
    [Fact]
    public async Task WorkspaceRepository_InsertAndFind_ShouldWork()
    {
        var dbPath = GetTempDbPath();
        try
        {
            var factory = SetupFactory(dbPath);
            var repo = new SqliteWorkspaceRepository(factory);
            var ws = Workspace.Create("test-project", @"C:\projects\test");

            await repo.InsertAsync(ws);
            var found = await repo.FindByIdAsync(ws.Id);

            Assert.NotNull(found);
            Assert.Equal(ws.Name, found!.Name);
            Assert.Equal(ws.RootPathHash, found.RootPathHash);
            Assert.Equal(WorkspaceStatus.Imported, found.Status);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(dbPath);
        }
    }

    [Fact]
    public async Task WorkspaceRepository_FindByRootPathHash_ShouldWork()
    {
        var dbPath = GetTempDbPath();
        try
        {
            var factory = SetupFactory(dbPath);
            var repo = new SqliteWorkspaceRepository(factory);
            var ws = Workspace.Create("test-project", @"C:\projects\test");
            await repo.InsertAsync(ws);

            var found = await repo.FindByRootPathHashAsync(ws.RootPathHash);

            Assert.NotNull(found);
            Assert.Equal(ws.Id, found!.Id);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(dbPath);
        }
    }

    [Fact]
    public async Task WorkspaceRepository_ListAll_ShouldReturnAllWorkspaces()
    {
        var dbPath = GetTempDbPath();
        try
        {
            var factory = SetupFactory(dbPath);
            var repo = new SqliteWorkspaceRepository(factory);
            await repo.InsertAsync(Workspace.Create("ws1", @"C:\p1"));
            await repo.InsertAsync(Workspace.Create("ws2", @"C:\p2"));

            var list = await repo.ListAllAsync();

            Assert.Equal(2, list.Count);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(dbPath);
        }
    }

    [Fact]
    public async Task WorkspaceRepository_UpdateStatus_ShouldChangeStatus()
    {
        var dbPath = GetTempDbPath();
        try
        {
            var factory = SetupFactory(dbPath);
            var repo = new SqliteWorkspaceRepository(factory);
            var ws = Workspace.Create("test-project", @"C:\projects\test");
            await repo.InsertAsync(ws);

            await repo.UpdateStatusAsync(ws.Id, WorkspaceStatus.Ready);
            var found = await repo.FindByIdAsync(ws.Id);

            Assert.NotNull(found);
            Assert.Equal(WorkspaceStatus.Ready, found!.Status);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(dbPath);
        }
    }

    [Fact]
    public async Task WorkspaceRepository_Remove_ShouldDeleteWorkspace()
    {
        var dbPath = GetTempDbPath();
        try
        {
            var factory = SetupFactory(dbPath);
            var repo = new SqliteWorkspaceRepository(factory);
            var ws = Workspace.Create("test-project", @"C:\projects\test");
            await repo.InsertAsync(ws);

            await repo.RemoveAsync(ws.Id);
            var found = await repo.FindByIdAsync(ws.Id);

            Assert.Null(found);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(dbPath);
        }
    }

    [Fact]
    public void BackgroundJob_Create_ShouldInitializeCorrectly()
    {
        var jobId = JobId.New();
        var wsId = WorkspaceId.New();
        var job = new BackgroundJob
        {
            Id = jobId,
            WorkspaceId = wsId,
            Type = "IndexBuild",
            Total = 100,
        };

        Assert.Equal(JobStatus.Queued, job.Status);
        Assert.Equal(0, job.Progress);
        Assert.Equal(100, job.Total);
        Assert.Null(job.StartedAt);
        Assert.Null(job.CompletedAt);
    }

    [Fact]
    public void BackgroundJob_Create_FactoryMethod_ShouldWork()
    {
        var wsId = WorkspaceId.New();
        var job = BackgroundJob.Create("IndexBuild", wsId, 50);

        Assert.False(string.IsNullOrEmpty(job.Id.Value));
        Assert.Equal("IndexBuild", job.Type);
        Assert.Equal(50, job.Total);
    }

    [Fact]
    public void IndexSnapshot_Create_ShouldInitializeAsBuilding()
    {
        var wsId = WorkspaceId.New();
        var snapshot = IndexSnapshot.Create(wsId);

        Assert.Equal(SnapshotStatus.Building, snapshot.Status);
        Assert.Equal(0, snapshot.FileCount);
        Assert.Null(snapshot.CompletedAt);
    }

    [Fact]
    public void LogRedactor_Redact_ShouldRemoveAuthorizationHeaders()
    {
        var msg = "Request with Authorization: Bearer abc123 sent";
        var redacted = LogRedactor.Redact(msg);

        Assert.DoesNotContain("Bearer abc123", redacted);
        Assert.Contains("[REDACTED]", redacted);
    }

    [Fact]
    public void LogRedactor_Redact_ShouldRemoveApiKeys()
    {
        var msg = "api_key=sk-1234567890";
        var redacted = LogRedactor.Redact(msg);

        Assert.DoesNotContain("sk-1234567890", redacted);
    }

    [Fact]
    public void LogRedactor_Redact_ShouldRemovePasswords()
    {
        var msg = "Server=localhost;password=secret123;";
        var redacted = LogRedactor.Redact(msg);

        Assert.DoesNotContain("secret123", redacted);
    }

    [Fact]
    public void LogRedactor_RedactPath_ShouldKeepOnlyFilename()
    {
        var redacted = LogRedactor.RedactPath(@"C:\Users\admin\secrets\config.json");

        Assert.Contains("config.json", redacted);
        Assert.DoesNotContain("admin", redacted);
    }

    [Fact]
    public void LogRedactor_Redact_ShouldRedactPathsWhenRequested()
    {
        var msg = "Processing C:\\Users\\admin\\project\\file.ts";
        var redacted = LogRedactor.Redact(msg, redactPaths: true);

        Assert.DoesNotContain("C:\\Users", redacted);
    }

    [Fact]
    public void LogRedactor_Redact_ShouldKeepNormalLogMessages()
    {
        var msg = "Index build completed: 1000 files";
        var redacted = LogRedactor.Redact(msg);

        Assert.Equal(msg, redacted);
    }

    private static SqliteConnectionFactory SetupFactory(string dbPath)
    {
        var factory = new SqliteConnectionFactory(dbPath);
        var runner = new MigrationRunner(factory, dbPath, [new Migration0001Initial()]);
        runner.Migrate();
        return factory;
    }

    private static string GetTempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"cachehub_repo_{Guid.NewGuid():N}.db");

    private static void TryDelete(string dbPath)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { if (File.Exists(dbPath + suffix)) File.Delete(dbPath + suffix); }
            catch { }
        }
    }
}
