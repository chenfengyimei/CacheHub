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
            new Migration0006SchemaV2(),
            new Migration0007ContextPackageFields(),
        new Migration0008ContextPackageFk(),
        new Migration0009PersistentCache(),
        new Migration0010RelationSourceColumn(),
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
    public async Task SaveAndFind_ShouldRoundTrip_AllFields()
    {
        var dbPath = GetTempDbPath();
        try
        {
            var factory = SetupFactory(dbPath);
            var wsId = await InsertWorkspaceAsync(factory);
            var repo = new SqliteContextPackageRepository(factory);

            var manifest = new ContextPackageManifest
            {
                Id = ContextPackageId.New(),
                WorkspaceId = wsId,
                IndexSnapshotId = IndexSnapshotId.New(),
                RepositoryCommit = "abc123def",
                Branch = "feature/test",
                DirtyStateHash = "dirty-hash-001",
                Task = new TaskInfo
                {
                    OriginalText = "Fix authentication token refresh in AuthService",
                    QueryParserVersion = "deterministic-query-v2",
                    ExtractedSymbols = ["AuthService", "refreshToken"],
                    ExtractedPaths = ["src/auth/service.ts", "src/auth/token.ts"],
                },
                Ranking = new RankingInfo { ProfileId = "deterministic-v1", ProfileVersion = 3 },
                Budget = new BudgetInfo
                {
                    ModelContextWindow = 128000,
                    AgentReservedTokens = 18000,
                    ResponseReservedTokens = 12000,
                    ContextTarget = 80000,
                    ContextHardLimit = 90000,
                    SafetyMargin = 10000,
                    ActualEstimate = 52345,
                },
                SelectedFiles =
                [
                    new SelectedFile { Path = "src/auth/service.ts", ContentHash = "sha256:abc123", Mode = SelectionMode.Full, Score = 0.95, Reasons = ["symbol match", "path match"] },
                    new SelectedFile { Path = "src/auth/token.ts", ContentHash = "sha256:def456", Mode = SelectionMode.Chunks, Score = 0.82, Reasons = ["keyword match"] },
                ],
                ExcludedCandidates =
                [
                    new ExcludedCandidate { Path = "docs/README.md", Score = 0.15, Reason = "budget exceeded" },
                    new ExcludedCandidate { Path = "test/setup.ts", Score = 0.08, Reason = "low score" },
                ],
                Safety = new SafetyInfo
                {
                    CloudSendAllowed = false,
                    SecretsScanPassed = false,
                    IgnoreRulesHash = "ignore-hash-002",
                    SecurityPolicyVersion = "sec-v2",
                    SecretScannerVersion = "secret-scanner-v1",
                    SensitiveExclusions = [".env", "config/secrets.json"],
                },
                ParserVersions = new Dictionary<string, string> { ["csharp"] = "csharp-regex-v1", ["typescript"] = "ts-regex-v1" },
                RepoMapVersion = "repomap-v2",
                ContextEngineVersion = "0.2.0-prealpha",
                ChunkingStrategyVersion = "chunking-v1",
                TokenBudgetPolicyVersion = "budget-v1",
                CreatedAt = new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero),
                ParentPackageId = ContextPackageId.New(),
            };

            await repo.SaveAsync(manifest);
            var found = await repo.FindByIdAsync(manifest.Id);

            Assert.NotNull(found);
            // Identity
            Assert.Equal(manifest.Id.Value, found!.Id.Value);
            Assert.Equal(manifest.WorkspaceId.Value, found.WorkspaceId.Value);
            Assert.Equal(manifest.IndexSnapshotId.Value, found.IndexSnapshotId.Value);

            // Repository info
            Assert.Equal(manifest.RepositoryCommit, found.RepositoryCommit);
            Assert.Equal(manifest.Branch, found.Branch);
            Assert.Equal(manifest.DirtyStateHash, found.DirtyStateHash);

            // Task
            Assert.Equal(manifest.Task.OriginalText, found.Task.OriginalText);
            Assert.Equal(manifest.Task.QueryParserVersion, found.Task.QueryParserVersion);
            Assert.NotNull(found.Task.ExtractedSymbols);
            Assert.Equal(2, found.Task.ExtractedSymbols!.Count);
            Assert.Contains("AuthService", found.Task.ExtractedSymbols);
            Assert.NotNull(found.Task.ExtractedPaths);
            Assert.Equal(2, found.Task.ExtractedPaths!.Count);

            // Ranking
            Assert.Equal(manifest.Ranking.ProfileId, found.Ranking.ProfileId);
            Assert.Equal(manifest.Ranking.ProfileVersion, found.Ranking.ProfileVersion);

            // Budget
            Assert.Equal(manifest.Budget.ModelContextWindow, found.Budget.ModelContextWindow);
            Assert.Equal(manifest.Budget.AgentReservedTokens, found.Budget.AgentReservedTokens);
            Assert.Equal(manifest.Budget.ResponseReservedTokens, found.Budget.ResponseReservedTokens);
            Assert.Equal(manifest.Budget.ContextTarget, found.Budget.ContextTarget);
            Assert.Equal(manifest.Budget.ContextHardLimit, found.Budget.ContextHardLimit);
            Assert.Equal(manifest.Budget.SafetyMargin, found.Budget.SafetyMargin);
            Assert.Equal(manifest.Budget.ActualEstimate, found.Budget.ActualEstimate);

            // SelectedFiles
            Assert.Equal(2, found.SelectedFiles.Count);
            Assert.Equal("src/auth/service.ts", found.SelectedFiles[0].Path);
            Assert.Equal("sha256:abc123", found.SelectedFiles[0].ContentHash);
            Assert.Equal(SelectionMode.Full, found.SelectedFiles[0].Mode);
            Assert.Equal(0.95, found.SelectedFiles[0].Score, 0.001);
            Assert.Equal(2, found.SelectedFiles[0].Reasons.Count);
            Assert.Equal("src/auth/token.ts", found.SelectedFiles[1].Path);
            Assert.Equal(SelectionMode.Chunks, found.SelectedFiles[1].Mode);

            // ExcludedCandidates
            Assert.Equal(2, found.ExcludedCandidates.Count);
            Assert.Equal("docs/README.md", found.ExcludedCandidates[0].Path);
            Assert.Equal("budget exceeded", found.ExcludedCandidates[0].Reason);
            Assert.Equal(0.15, found.ExcludedCandidates[0].Score, 0.001);

            // Safety
            Assert.False(found.Safety.CloudSendAllowed);
            Assert.False(found.Safety.SecretsScanPassed);
            Assert.Equal("ignore-hash-002", found.Safety.IgnoreRulesHash);
            Assert.Equal("sec-v2", found.Safety.SecurityPolicyVersion);
            Assert.Equal("secret-scanner-v1", found.Safety.SecretScannerVersion);
            Assert.NotNull(found.Safety.SensitiveExclusions);
            Assert.Equal(2, found.Safety.SensitiveExclusions!.Count);

            // Versions
            Assert.Equal("0.2.0-prealpha", found.ContextEngineVersion);
            Assert.Equal("chunking-v1", found.ChunkingStrategyVersion);
            Assert.Equal("budget-v1", found.TokenBudgetPolicyVersion);
            Assert.Equal("repomap-v2", found.RepoMapVersion);

            // Parent
            Assert.NotNull(found.ParentPackageId);
            Assert.Equal(manifest.ParentPackageId!.Value, found.ParentPackageId!.Value);
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
