using CacheHub.Core.Benchmarks;

namespace CacheHub.Core.Benchmarks.Tasks;

/// <summary>
/// Built-in benchmark task definitions for CacheHub R4 validation.
/// 20 tasks across 5 repository types (C#, TypeScript, Python, Go, Rust),
/// including Chinese task descriptions and Monorepo structure.
/// Each task includes Ground Truth with Required/Helpful/Distractor files.
/// R4-W001: Fixed, reproducible task set.
/// </summary>
public static class BenchmarkTaskSet
{
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
        },

        // === TypeScript Tasks ===

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

        new BenchmarkTask
        {
            Id = "bench-009",
            RepositoryId = "sample-ts-react",
            Language = "typescript",
            TaskDescription = "Add error boundary to the user dashboard component",
            CommitHash = "b2c3d4e",
            RequiredFiles = ["src/components/Dashboard.tsx", "src/components/ErrorBoundary.tsx"],
            HelpfulFiles = ["src/hooks/useAuth.ts", "src/types/user.ts"],
            DistractorFiles = ["src/legacy/OldDashboard.tsx", "public/index.html", "package-lock.json"],
        },

        new BenchmarkTask
        {
            Id = "bench-010",
            RepositoryId = "sample-ts-api",
            Language = "typescript",
            TaskDescription = "实现 Express 路由中间件的请求日志记录功能",
            CommitHash = "c3d4e5f",
            RequiredFiles = ["src/middleware/logger.ts", "src/app.ts"],
            HelpfulFiles = ["src/config/env.ts", "src/utils/logger.ts"],
            DistractorFiles = ["src/legacy/old-logger.ts", "tsconfig.json", ".eslintrc.js"],
        },

        new BenchmarkTask
        {
            Id = "bench-011",
            RepositoryId = "sample-ts-monorepo",
            Language = "typescript",
            TaskDescription = "Fix the shared types package export in the monorepo",
            CommitHash = "d4e5f6g",
            RequiredFiles = ["packages/shared-types/src/index.ts", "packages/shared-types/package.json"],
            HelpfulFiles = ["packages/api/src/types.ts", "packages/web/src/types.ts"],
            DistractorFiles = ["packages/web/src/App.tsx", "packages/api/src/server.ts", "turbo.json"],
        },

        // === Python Tasks ===

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

        new BenchmarkTask
        {
            Id = "bench-012",
            RepositoryId = "sample-py-django",
            Language = "python",
            TaskDescription = "Fix the Django model serializer that drops nullable fields",
            CommitHash = "f5g6h7i",
            RequiredFiles = ["src/serializers/user_serializer.py", "src/models/user.py"],
            HelpfulFiles = ["src/views/user_views.py", "src/tests/test_serializer.py"],
            DistractorFiles = ["src/legacy/old_serializer.py", "manage.py", "setup.py"],
        },

        new BenchmarkTask
        {
            Id = "bench-013",
            RepositoryId = "sample-py-ml",
            Language = "python",
            TaskDescription = "在数据处理管线中添加数据验证步骤，确保输入数据完整性",
            CommitHash = "g6h7i8j",
            RequiredFiles = ["src/pipeline/data_processor.py", "src/validators/schema_validator.py"],
            HelpfulFiles = ["src/config/pipeline_config.py", "src/tests/test_pipeline.py"],
            DistractorFiles = ["src/legacy/old_processor.py", "notebooks/exploration.ipynb", "requirements.txt"],
        },

        // === Go Tasks ===

        new BenchmarkTask
        {
            Id = "bench-014",
            RepositoryId = "sample-go-server",
            Language = "go",
            TaskDescription = "Fix the goroutine leak in the HTTP handler pool",
            CommitHash = "h7i8j9k",
            RequiredFiles = ["internal/handler/pool.go", "internal/server/server.go"],
            HelpfulFiles = ["internal/config/config.go", "internal/handler/middleware.go"],
            DistractorFiles = ["cmd/main.go", "README.md", "go.mod"],
        },

        new BenchmarkTask
        {
            Id = "bench-015",
            RepositoryId = "sample-go-cli",
            Language = "go",
            TaskDescription = "Add context cancellation support to the file walker",
            CommitHash = "i8j9k0l",
            RequiredFiles = ["internal/walker/walker.go", "internal/cli/commands.go"],
            HelpfulFiles = ["internal/config/config.go", "internal/walker/test_walker.go"],
            DistractorFiles = ["internal/legacy/old_walker.go", "Makefile", ".golangci.yml"],
        },

        new BenchmarkTask
        {
            Id = "bench-016",
            RepositoryId = "sample-go-monorepo",
            Language = "go",
            TaskDescription = "修复微服务间 gRPC 通信的超时处理逻辑",
            CommitHash = "j9k0l1m",
            RequiredFiles = ["services/auth/client.go", "services/user/server.go"],
            HelpfulFiles = ["pkg/grpc/interceptor.go", "pkg/config/service.go"],
            DistractorFiles = ["services/gateway/main.go", "go.work", "Makefile"],
        },

        // === Rust Tasks ===

        new BenchmarkTask
        {
            Id = "bench-017",
            RepositoryId = "sample-rust-cli",
            Language = "rust",
            TaskDescription = "Fix the panic in the config parser when encountering unknown keys",
            CommitHash = "k0l1m2n",
            RequiredFiles = ["src/config/parser.rs", "src/config/error.rs"],
            HelpfulFiles = ["src/main.rs", "tests/test_config.rs"],
            DistractorFiles = ["src/legacy/old_parser.rs", "Cargo.lock", "README.md"],
        },

        new BenchmarkTask
        {
            Id = "bench-018",
            RepositoryId = "sample-rust-server",
            Language = "rust",
            TaskDescription = "Add connection pooling to the database layer",
            CommitHash = "l1m2n3o",
            RequiredFiles = ["src/db/pool.rs", "src/db/connection.rs"],
            HelpfulFiles = ["src/config/db_config.rs", "src/db/test_pool.rs"],
            DistractorFiles = ["src/server/main.rs", "Cargo.toml", ".cargo/config.toml"],
        },

        // === Monorepo / Mixed Tasks ===

        new BenchmarkTask
        {
            Id = "bench-019",
            RepositoryId = "sample-monorepo-fullstack",
            Language = "mixed",
            TaskDescription = "Fix the authentication flow between the frontend and backend in the monorepo",
            CommitHash = "m2n3o4p",
            RequiredFiles = ["frontend/src/auth/AuthContext.tsx", "backend/src/auth/middleware.ts"],
            HelpfulFiles = ["shared/types/auth.ts", "frontend/src/api/client.ts"],
            DistractorFiles = ["frontend/src/pages/Home.tsx", "backend/src/routes/health.ts", "docker-compose.yml"],
        },

        new BenchmarkTask
        {
            Id = "bench-020",
            RepositoryId = "sample-monorepo-fullstack",
            Language = "mixed",
            TaskDescription = "在 Monorepo 中添加跨服务的数据一致性检查中间件",
            CommitHash = "m2n3o4p",
            RequiredFiles = ["backend/src/middleware/consistency.ts", "shared/types/events.ts"],
            HelpfulFiles = ["backend/src/services/sync.ts", "frontend/src/hooks/useSync.ts"],
            DistractorFiles = ["frontend/src/components/Footer.tsx", "backend/src/routes/health.ts", "package.json"],
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
}
