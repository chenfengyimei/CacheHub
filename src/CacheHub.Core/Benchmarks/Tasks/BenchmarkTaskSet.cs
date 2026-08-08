using CacheHub.Core.Benchmarks;

namespace CacheHub.Core.Benchmarks.Tasks;

/// <summary>
/// Built-in benchmark task definitions for CacheHub Benchmark Matrix.
/// 25 tasks across 13 repository fixtures (C#, TypeScript, Python, Go, Rust, Monorepo),
/// including Chinese task descriptions and Monorepo structure.
/// V7-W17: All tasks now point at real fixture repos under tests/fixtures/repos/.
/// </summary>
public static class BenchmarkTaskSet
{
    // Fixture paths relative to the solution root
    private const string FixturesRoot = "tests/fixtures/repos";

    public static IReadOnlyList<BenchmarkTask> Tasks { get; } =
    [
        // === C# Tasks (cachehub-self) ===

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
            RepositoryPath = ".", // Self-referential
            TestCommand = "dotnet",
            TestCommandArgs = "test -c Release --nologo --verbosity quiet",
        },

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
            RepositoryPath = ".",
            TestCommand = "dotnet",
            TestCommandArgs = "test -c Release --nologo --verbosity quiet",
        },

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
            RepositoryPath = ".",
            TestCommand = "dotnet",
            TestCommandArgs = "test -c Release --nologo --verbosity quiet",
        },

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
            RepositoryPath = ".",
            TestCommand = "dotnet",
            TestCommandArgs = "test -c Release --nologo --verbosity quiet",
        },

        new BenchmarkTask
        {
            Id = "bench-007",
            RepositoryId = "cachehub-self",
            Language = "csharp",
            TaskDescription = "修复 RecallPipeline 中符号匹配的 Unicode 支持问题",
            CommitHash = "HEAD",
            RequiredFiles = ["src/CacheHub.Context/Recall/RecallPipeline.cs", "src/CacheHub.Context/Parsing/TaskParser.cs"],
            HelpfulFiles = ["tests/CacheHub.Tests/RecallAndRankingTests.cs", "src/CacheHub.Context/Ranking/RankingEngine.cs"],
            DistractorFiles = ["src/CacheHub.Core/Benchmarks/BenchmarkModels.cs", "LICENSE"],
            RepositoryPath = ".",
            TestCommand = "dotnet",
            TestCommandArgs = "test -c Release --nologo --verbosity quiet",
        },

        new BenchmarkTask
        {
            Id = "bench-008",
            RepositoryId = "cachehub-self",
            Language = "csharp",
            TaskDescription = "在 Desktop API 中添加索引构建端点，支持后台任务和状态轮询",
            CommitHash = "HEAD",
            RequiredFiles = ["src/CacheHub.Desktop/Program.cs", "src/CacheHub.Storage/Repositories/SqliteWorkspaceRepository.cs"],
            HelpfulFiles = ["src/CacheHub.Cli/Commands/IndexCommands.cs", "src/CacheHub.Core/Workspaces/Workspace.cs"],
            DistractorFiles = ["src/CacheHub.Core/Semantic/SemanticModels.cs", "CHANGELOG.md"],
            RepositoryPath = ".",
            TestCommand = "dotnet",
            TestCommandArgs = "test -c Release --nologo --verbosity quiet",
        },

        // === TypeScript Tasks ===

        new BenchmarkTask
        {
            Id = "bench-003",
            RepositoryId = "sample-ts-auth",
            Language = "typescript",
            TaskDescription = "Fix the token refresh logic that doesn't handle 401 retries in AuthService",
            CommitHash = "HEAD",
            RequiredFiles = ["src/auth/AuthService.ts", "src/auth/TokenManager.ts"],
            HelpfulFiles = ["src/auth/types.ts", "src/config/http.ts"],
            DistractorFiles = ["README.md", "src/legacy/OldAuth.ts", "package.json"],
            RepositoryPath = $"{FixturesRoot}/sample-ts-auth",
            TestCommand = "npx",
            TestCommandArgs = "jest --passWithNoTests",
        },

        new BenchmarkTask
        {
            Id = "bench-009",
            RepositoryId = "sample-ts-react",
            Language = "typescript",
            TaskDescription = "Add error boundary to the user dashboard component",
            CommitHash = "HEAD",
            RequiredFiles = ["src/components/Dashboard.tsx", "src/components/ErrorBoundary.tsx"],
            HelpfulFiles = ["src/hooks/useAuth.ts", "src/types/user.ts"],
            DistractorFiles = ["README.md"],
            RepositoryPath = $"{FixturesRoot}/sample-ts-react",
            TestCommand = "npx",
            TestCommandArgs = "tsc --noEmit",
        },

        new BenchmarkTask
        {
            Id = "bench-010",
            RepositoryId = "sample-ts-api",
            Language = "typescript",
            TaskDescription = "实现 Express 路由中间件的请求日志记录功能",
            CommitHash = "HEAD",
            RequiredFiles = ["src/middleware/logger.ts", "src/app.ts"],
            HelpfulFiles = ["src/config/env.ts", "src/utils/logger.ts"],
            DistractorFiles = ["README.md", "tsconfig.json", ".eslintrc.js"],
            RepositoryPath = $"{FixturesRoot}/sample-ts-api",
            TestCommand = "npx",
            TestCommandArgs = "tsc --noEmit",
        },

        new BenchmarkTask
        {
            Id = "bench-011",
            RepositoryId = "sample-ts-monorepo",
            Language = "typescript",
            TaskDescription = "Fix the shared types package export in the monorepo",
            CommitHash = "HEAD",
            RequiredFiles = ["packages/shared-types/src/index.ts", "packages/shared-types/package.json"],
            HelpfulFiles = ["packages/api/src/types.ts", "packages/web/src/types.ts"],
            DistractorFiles = ["README.md", "turbo.json"],
            RepositoryPath = $"{FixturesRoot}/sample-ts-monorepo",
            TestCommand = "npx",
            TestCommandArgs = "tsc --noEmit",
        },

        // === Python Tasks ===

        new BenchmarkTask
        {
            Id = "bench-004",
            RepositoryId = "sample-py-api",
            Language = "python",
            TaskDescription = "Add a retry decorator to the user_repository fetch function",
            CommitHash = "HEAD",
            RequiredFiles = ["src/repositories/user_repository.py", "src/utils/decorators.py"],
            HelpfulFiles = ["src/config/settings.py", "src/tests/test_user_repo.py"],
            DistractorFiles = ["README.md", "src/legacy/old_repo.py", "requirements.txt"],
            RepositoryPath = $"{FixturesRoot}/sample-py-api",
            TestCommand = "python",
            TestCommandArgs = "-m pytest src/tests/ -v",
        },

        new BenchmarkTask
        {
            Id = "bench-012",
            RepositoryId = "sample-py-django",
            Language = "python",
            TaskDescription = "Fix the Django model serializer that drops nullable fields",
            CommitHash = "HEAD",
            RequiredFiles = ["src/serializers/user_serializer.py", "src/models/user.py"],
            HelpfulFiles = ["src/views/user_views.py", "src/tests/test_serializer.py"],
            DistractorFiles = ["README.md", "manage.py", "setup.py"],
            RepositoryPath = $"{FixturesRoot}/sample-py-django",
            TestCommand = "python",
            TestCommandArgs = "-m pytest src/tests/ -v",
        },

        new BenchmarkTask
        {
            Id = "bench-013",
            RepositoryId = "sample-py-ml",
            Language = "python",
            TaskDescription = "在数据处理管线中添加数据验证步骤，确保输入数据完整性",
            CommitHash = "HEAD",
            RequiredFiles = ["src/pipeline/data_processor.py", "src/validators/schema_validator.py"],
            HelpfulFiles = ["src/config/pipeline_config.py", "src/tests/test_pipeline.py"],
            DistractorFiles = ["README.md", "notebooks/exploration.ipynb", "requirements.txt"],
            RepositoryPath = $"{FixturesRoot}/sample-py-ml",
            TestCommand = "python",
            TestCommandArgs = "-m pytest src/tests/ -v",
        },

        // === Go Tasks ===

        new BenchmarkTask
        {
            Id = "bench-014",
            RepositoryId = "sample-go-server",
            Language = "go",
            TaskDescription = "Fix the goroutine leak in the HTTP handler pool",
            CommitHash = "HEAD",
            RequiredFiles = ["internal/handler/pool.go", "internal/server/server.go"],
            HelpfulFiles = ["internal/config/config.go", "internal/handler/pool_test.go"],
            DistractorFiles = ["README.md", "go.mod"],
            RepositoryPath = $"{FixturesRoot}/sample-go-server",
            TestCommand = "go",
            TestCommandArgs = "test ./...",
        },

        new BenchmarkTask
        {
            Id = "bench-015",
            RepositoryId = "sample-go-cli",
            Language = "go",
            TaskDescription = "Add context cancellation support to the file walker",
            CommitHash = "HEAD",
            RequiredFiles = ["internal/walker/walker.go", "internal/cli/commands.go"],
            HelpfulFiles = ["internal/config/config.go", "internal/walker/test_walker.go"],
            DistractorFiles = ["README.md", "Makefile", ".golangci.yml"],
            RepositoryPath = $"{FixturesRoot}/sample-go-cli",
            TestCommand = "go",
            TestCommandArgs = "test ./...",
        },

        new BenchmarkTask
        {
            Id = "bench-016",
            RepositoryId = "sample-go-monorepo",
            Language = "go",
            TaskDescription = "修复微服务间 gRPC 通信的超时处理逻辑",
            CommitHash = "HEAD",
            RequiredFiles = ["services/auth/client.go", "services/user/server.go"],
            HelpfulFiles = ["pkg/grpc/interceptor.go", "pkg/config/service.go"],
            DistractorFiles = ["README.md", "go.work", "Makefile"],
            RepositoryPath = $"{FixturesRoot}/sample-go-monorepo",
            TestCommand = "go",
            TestCommandArgs = "test ./...",
        },

        // === Rust Tasks ===

        new BenchmarkTask
        {
            Id = "bench-017",
            RepositoryId = "sample-rust-cli",
            Language = "rust",
            TaskDescription = "Fix the panic in the config parser when encountering unknown keys",
            CommitHash = "HEAD",
            RequiredFiles = ["src/config/parser.rs", "src/config/error.rs"],
            HelpfulFiles = ["src/main.rs", "tests/test_config.rs"],
            DistractorFiles = ["README.md", "Cargo.lock"],
            RepositoryPath = $"{FixturesRoot}/sample-rust-cli",
            TestCommand = "cargo",
            TestCommandArgs = "test",
        },

        new BenchmarkTask
        {
            Id = "bench-018",
            RepositoryId = "sample-rust-server",
            Language = "rust",
            TaskDescription = "Add connection pooling to the database layer",
            CommitHash = "HEAD",
            RequiredFiles = ["src/db/pool.rs", "src/db/connection.rs"],
            HelpfulFiles = ["src/config/db_config.rs", "tests/test_pool.rs"],
            DistractorFiles = ["README.md", "Cargo.toml", ".cargo/config.toml"],
            RepositoryPath = $"{FixturesRoot}/sample-rust-server",
            TestCommand = "cargo",
            TestCommandArgs = "test",
        },

        // === Monorepo / Mixed Tasks ===

        new BenchmarkTask
        {
            Id = "bench-019",
            RepositoryId = "sample-monorepo-fullstack",
            Language = "mixed",
            TaskDescription = "Fix the authentication flow between the frontend and backend in the monorepo",
            CommitHash = "HEAD",
            RequiredFiles = ["frontend/src/auth/AuthContext.tsx", "backend/src/auth/middleware.ts"],
            HelpfulFiles = ["shared/types/auth.ts", "frontend/src/api/client.ts"],
            DistractorFiles = ["README.md", "docker-compose.yml"],
            RepositoryPath = $"{FixturesRoot}/sample-monorepo-fullstack",
            TestCommand = "npx",
            TestCommandArgs = "tsc --noEmit",
        },

        new BenchmarkTask
        {
            Id = "bench-020",
            RepositoryId = "sample-monorepo-fullstack",
            Language = "mixed",
            TaskDescription = "在 Monorepo 中添加跨服务的数据一致性检查中间件",
            CommitHash = "HEAD",
            RequiredFiles = ["backend/src/services/sync.ts", "shared/types/events.ts"],
            HelpfulFiles = ["frontend/src/hooks/useSync.ts", "shared/types/auth.ts"],
            DistractorFiles = ["README.md", "package.json"],
            RepositoryPath = $"{FixturesRoot}/sample-monorepo-fullstack",
            TestCommand = "npx",
            TestCommandArgs = "tsc --noEmit",
        },

        // === V7-W17: Additional tasks (bench-021 to bench-025) ===

        new BenchmarkTask
        {
            Id = "bench-021",
            RepositoryId = "sample-ts-auth",
            Language = "typescript",
            TaskDescription = "Add token expiry check before making authenticated API requests",
            CommitHash = "HEAD",
            RequiredFiles = ["src/auth/TokenManager.ts", "src/auth/AuthService.ts"],
            HelpfulFiles = ["src/auth/types.ts", "src/config/http.ts"],
            DistractorFiles = ["README.md", "src/legacy/OldAuth.ts"],
            RepositoryPath = $"{FixturesRoot}/sample-ts-auth",
            TestCommand = "npx",
            TestCommandArgs = "jest --passWithNoTests",
        },

        new BenchmarkTask
        {
            Id = "bench-022",
            RepositoryId = "sample-py-api",
            Language = "python",
            TaskDescription = "Add bulk delete operation to the user repository with proper error handling",
            CommitHash = "HEAD",
            RequiredFiles = ["src/repositories/user_repository.py", "src/utils/decorators.py"],
            HelpfulFiles = ["src/config/settings.py", "src/tests/test_user_repo.py"],
            DistractorFiles = ["README.md", "src/legacy/old_repo.py"],
            RepositoryPath = $"{FixturesRoot}/sample-py-api",
            TestCommand = "python",
            TestCommandArgs = "-m pytest src/tests/ -v",
        },

        new BenchmarkTask
        {
            Id = "bench-023",
            RepositoryId = "sample-go-server",
            Language = "go",
            TaskDescription = "Add graceful shutdown with context to the HTTP server",
            CommitHash = "HEAD",
            RequiredFiles = ["internal/server/server.go", "internal/handler/pool.go"],
            HelpfulFiles = ["internal/config/config.go", "internal/handler/pool_test.go"],
            DistractorFiles = ["README.md", "go.mod"],
            RepositoryPath = $"{FixturesRoot}/sample-go-server",
            TestCommand = "go",
            TestCommandArgs = "test ./...",
        },

        new BenchmarkTask
        {
            Id = "bench-024",
            RepositoryId = "sample-rust-cli",
            Language = "rust",
            TaskDescription = "Add default value support to the config parser",
            CommitHash = "HEAD",
            RequiredFiles = ["src/config/parser.rs", "src/config/error.rs"],
            HelpfulFiles = ["src/main.rs", "tests/test_config.rs"],
            DistractorFiles = ["README.md", "Cargo.lock"],
            RepositoryPath = $"{FixturesRoot}/sample-rust-cli",
            TestCommand = "cargo",
            TestCommandArgs = "test",
        },

        new BenchmarkTask
        {
            Id = "bench-025",
            RepositoryId = "sample-monorepo-fullstack",
            Language = "mixed",
            TaskDescription = "Add API client retry interceptor with exponential backoff",
            CommitHash = "HEAD",
            RequiredFiles = ["frontend/src/api/client.ts", "backend/src/auth/middleware.ts"],
            HelpfulFiles = ["shared/types/auth.ts", "shared/types/events.ts"],
            DistractorFiles = ["README.md"],
            RepositoryPath = $"{FixturesRoot}/sample-monorepo-fullstack",
            TestCommand = "npx",
            TestCommandArgs = "tsc --noEmit",
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

    /// <summary>
    /// Gets all unique repository IDs in the task set.
    /// </summary>
    public static IReadOnlyList<string> GetRepositoryIds() =>
        Tasks.Select(t => t.RepositoryId).Distinct().ToList();

    /// <summary>
    /// Gets all unique languages in the task set.
    /// </summary>
    public static IReadOnlyList<string> GetLanguages() =>
        Tasks.Select(t => t.Language).Distinct().ToList();

    /// <summary>
    /// V7-W17: Gets all tasks for a specific repository.
    /// </summary>
    public static IReadOnlyList<BenchmarkTask> GetTasksForRepository(string repositoryId) =>
        Tasks.Where(t => t.RepositoryId == repositoryId).ToList();
}
