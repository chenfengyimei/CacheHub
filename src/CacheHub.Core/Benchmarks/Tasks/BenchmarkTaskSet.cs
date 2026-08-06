using CacheHub.Core.Benchmarks;

namespace CacheHub.Core.Benchmarks.Tasks;

/// <summary>
/// Built-in benchmark task definitions for CacheHub 0.1-alpha validation.
/// Each task includes Ground Truth with Required/Helpful/Distractor files.
/// </summary>
public static class BenchmarkTaskSet
{
    public static IReadOnlyList<BenchmarkTask> Tasks { get; } =
    [
        // Task 1: C# — Fix async method exception handling
        new BenchmarkTask
        {
            Id = "bench-001",
            RepositoryId = "cachehub-self",
            Language = "csharp",
            TaskDescription = "Fix the async method that swallows exceptions in WorkspaceCommands",
            CommitHash = "HEAD",
            RequiredFiles = ["src/CacheHub.Cli/Commands/WorkspaceCommands.cs", "src/CacheHub.Core/Errors/ErrorCode.cs"],
            HelpfulFiles = ["src/CacheHub.Core/Errors/CacheHubException.cs", "src/CacheHub.Core/Results/Result.cs"],
            DistractorFiles = ["README.md", "docs/ai/AI_DEV_STATE.json", "src/CacheHub.Desktop/Program.cs"],
        },

        // Task 2: C# — Add SQLite migration for new table
        new BenchmarkTask
        {
            Id = "bench-002",
            RepositoryId = "cachehub-self",
            Language = "csharp",
            TaskDescription = "Add a new SQLite migration for storing context package feedback",
            CommitHash = "HEAD",
            RequiredFiles = ["src/CacheHub.Storage/Database/Migrations/Migration0001Initial.cs", "src/CacheHub.Storage/Database/IMigration.cs"],
            HelpfulFiles = ["src/CacheHub.Storage/Database/MigrationRunner.cs", "src/CacheHub.Core/Feedback/ContextFeedback.cs"],
            DistractorFiles = ["src/CacheHub.Core/Capabilities/CapabilityDiscovery.cs", ".gitignore"],
        },

        // Task 3: TypeScript — Fix token refresh logic
        new BenchmarkTask
        {
            Id = "bench-003",
            RepositoryId = "sample-ts-auth",
            Language = "typescript",
            TaskDescription = "Fix the token refresh logic that doesn't handle 401 retries in AuthService",
            CommitHash = "a1b2c3d",
            RequiredFiles = ["src/auth/AuthService.ts", "src/auth/TokenManager.ts"],
            HelpfulFiles = ["src/auth/types.ts", "src/config/http.ts"],
            DistractorFiles = ["README.md", "src/legacy/OldAuth.ts", "package.json"],
        },

        // Task 4: Python — Add retry decorator
        new BenchmarkTask
        {
            Id = "bench-004",
            RepositoryId = "sample-py-api",
            Language = "python",
            TaskDescription = "Add a retry decorator to the user_repository fetch function",
            CommitHash = "e4f5g6h",
            RequiredFiles = ["src/repositories/user_repository.py", "src/utils/decorators.py"],
            HelpfulFiles = ["src/config/settings.py", "src/tests/test_user_repo.py"],
            DistractorFiles = ["docs/api.md", "src/legacy/old_repo.py", "requirements.txt"],
        },

        // Task 5: C# — Implement path traversal check
        new BenchmarkTask
        {
            Id = "bench-005",
            RepositoryId = "cachehub-self",
            Language = "csharp",
            TaskDescription = "Implement path traversal detection in PathNormalizer",
            CommitHash = "HEAD",
            RequiredFiles = ["src/CacheHub.Core/Paths/PathNormalizer.cs"],
            HelpfulFiles = ["tests/CacheHub.Tests/PathAndWorkspaceTests.cs", "src/CacheHub.Core/Errors/ErrorCode.cs"],
            DistractorFiles = ["src/CacheHub.Core/Parsing/ICodeParser.cs", "NOTICE"],
        },

        // Task 6: Mixed — Context Engine ranking optimization
        new BenchmarkTask
        {
            Id = "bench-006",
            RepositoryId = "cachehub-self",
            Language = "csharp",
            TaskDescription = "Optimize the RankingEngine to weight symbol matches higher than path matches",
            CommitHash = "HEAD",
            RequiredFiles = ["src/CacheHub.Context/Ranking/RankingEngine.cs", "src/CacheHub.Context/Recall/RecallPipeline.cs"],
            HelpfulFiles = ["src/CacheHub.Context/Parsing/TaskParser.cs", "tests/CacheHub.Tests/RecallAndRankingTests.cs"],
            DistractorFiles = ["src/CacheHub.Core/Benchmarks/BenchmarkModels.cs", "CODE_OF_CONDUCT.md"],
        },
    ];

    /// <summary>
    /// Gets ground truth for a benchmark task.
    /// </summary>
    public static GroundTruth GetGroundTruth(string taskId)
    {
        var task = Tasks.FirstOrDefault(t => t.Id == taskId)
            ?? throw new ArgumentException($"Benchmark task not found: {taskId}");

        return new GroundTruth
        {
            TaskId = taskId,
            RequiredFiles = task.RequiredFiles,
            HelpfulFiles = task.HelpfulFiles,
            DistractorFiles = task.DistractorFiles,
        };
    }

    /// <summary>
    /// Gets all ground truths.
    /// </summary>
    public static IReadOnlyList<GroundTruth> GetAllGroundTruths() =>
        Tasks.Select(t => GetGroundTruth(t.Id)).ToList();
}
