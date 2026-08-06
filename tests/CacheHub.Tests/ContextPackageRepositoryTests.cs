using CacheHub.Core.Context;
using CacheHub.Core.Identifiers;
using CacheHub.Storage.Database;
using CacheHub.Storage.Database.Migrations;
using CacheHub.Storage.Repositories;
using Microsoft.Data.Sqlite;

namespace CacheHub.Tests;

[Collection("SQLite")]
public class ContextPackageRepositoryTests
{
    private static string GetTempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"cachehub_ctx_{Guid.NewGuid():N}.db");

    private static SqliteConnectionFactory SetupFactory(string dbPath)
    {
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
        return factory;
    }

    private static async Task<WorkspaceId> InsertWorkspaceAsync(SqliteConnectionFactory factory)
    {
        var wsId = WorkspaceId.New();
        await using var conn = factory.CreateOpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO workspaces (id, name, root_path, root_path_hash, status) VALUES ($id, 'test', '/test', 'hash', 'Ready');";
        cmd.Parameters.AddWithValue("$id", wsId.Value);
        await cmd.ExecuteNonQueryAsync();
        return wsId;
    }

    private static ContextPackageManifest CreateManifest(WorkspaceId wsId) => new()
    {
        Id = ContextPackageId.New(),
        WorkspaceId = wsId,
        IndexSnapshotId = IndexSnapshotId.New(),
        Task = new TaskInfo { OriginalText = "Fix login bug", QueryParserVersion = "deterministic-query-v1" },
        Ranking = new RankingInfo { ProfileId = "deterministic-v1", ProfileVersion = 3 },
        Budget = new BudgetInfo
        {
            ModelContextWindow = 128000,
            AgentReservedTokens = 18000,
            ResponseReservedTokens = 12000,
            ContextTarget = 80000,
            ContextHardLimit = 90000,
            SafetyMargin = 10000,
            ActualEstimate = 50000,
        },
        SelectedFiles = [new SelectedFile { Path = "src/app.ts", ContentHash = "h1", Mode = SelectionMode.Full, Score = 0.95, Reasons = ["match"] }],
        ExcludedCandidates = [new ExcludedCandidate { Path = "docs.md", Score = 0.2, Reason = "budget" }],
        Safety = new SafetyInfo { CloudSendAllowed = true, SecretsScanPassed = true },
        ContextEngineVersion = "0.2.0",
        ChunkingStrategyVersion = "chunking-v1",
        TokenBudgetPolicyVersion = "budget-v1",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task SaveAndFind_ShouldRoundTrip()
    {
        var dbPath = GetTempDbPath();
        try
        {
            var factory = SetupFactory(dbPath);
            var wsId = await InsertWorkspaceAsync(factory);
            var repo = new SqliteContextPackageRepository(factory);
            var manifest = CreateManifest(wsId);

            await repo.SaveAsync(manifest);
            var found = await repo.FindByIdAsync(manifest.Id);

            Assert.NotNull(found);
            Assert.Equal(manifest.Id.Value, found!.Id.Value);
            Assert.Equal(manifest.Task.OriginalText, found.Task.OriginalText);
            Assert.Equal(manifest.Budget.ActualEstimate, found.Budget.ActualEstimate);
            Assert.Equal(manifest.Ranking.ProfileId, found.Ranking.ProfileId);
        }
        finally { SqliteConnection.ClearAllPools(); TryDelete(dbPath); }
    }

    [Fact]
    public async Task ListByWorkspace_ShouldReturnAll()
    {
        var dbPath = GetTempDbPath();
        try
        {
            var factory = SetupFactory(dbPath);
            var wsId = await InsertWorkspaceAsync(factory);
            var repo = new SqliteContextPackageRepository(factory);

            await repo.SaveAsync(CreateManifest(wsId));
            await repo.SaveAsync(CreateManifest(wsId));

            var list = await repo.ListByWorkspaceAsync(wsId);

            Assert.Equal(2, list.Count);
        }
        finally { SqliteConnection.ClearAllPools(); TryDelete(dbPath); }
    }

    [Fact]
    public async Task Remove_ShouldDelete()
    {
        var dbPath = GetTempDbPath();
        try
        {
            var factory = SetupFactory(dbPath);
            var wsId = await InsertWorkspaceAsync(factory);
            var repo = new SqliteContextPackageRepository(factory);
            var manifest = CreateManifest(wsId);
            await repo.SaveAsync(manifest);

            await repo.RemoveAsync(manifest.Id);
            var found = await repo.FindByIdAsync(manifest.Id);

            Assert.Null(found);
        }
        finally { SqliteConnection.ClearAllPools(); TryDelete(dbPath); }
    }

    [Fact]
    public async Task FindById_ShouldReturnNullForNonExistent()
    {
        var dbPath = GetTempDbPath();
        try
        {
            var factory = SetupFactory(dbPath);
            var repo = new SqliteContextPackageRepository(factory);

            var found = await repo.FindByIdAsync(ContextPackageId.New());

            Assert.Null(found);
        }
        finally { SqliteConnection.ClearAllPools(); TryDelete(dbPath); }
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
