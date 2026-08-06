using AiKv.Core.Caching;
using AiKv.Context.Cache;
using AiKv.Context.Engine;
using AiKv.Context.Recall;
using AiKv.Core.Context;
using AiKv.Core.Identifiers;
using AiKv.Core.Tokens;

namespace AiKv.Tests;

/// <summary>
/// Integration tests verifying ContextEngine + Cache + Tokenizer + Security
/// work together in realistic scenarios.
/// </summary>
public class ContextEngineRealWorldTests
{
    [Fact]
    public void RealWorld_MultiFileProject_ContextBuild_WithCaching()
    {
        var registry = new TokenizerRegistry();
        registry.Register("gpt-4", new CodeTokenizer());
        var engine = new ContextEngine(registry);
        var cache = new ContextPackageCache();

        var files = new List<IndexedFileInfo>
        {
            new() { Path = "src/auth/AuthService.ts", NormalizedPath = "src/auth/AuthService.ts", Language = "typescript", Size = 800, Symbols = ["AuthService", "login", "refreshToken"] },
            new() { Path = "src/auth/TokenManager.ts", NormalizedPath = "src/auth/TokenManager.ts", Language = "typescript", Size = 500, Symbols = ["TokenManager", "getToken", "refreshToken"] },
            new() { Path = "src/api/UserController.ts", NormalizedPath = "src/api/UserController.ts", Language = "typescript", Size = 600, Symbols = ["UserController", "getUser"] },
            new() { Path = "src/db/Database.ts", NormalizedPath = "src/db/Database.ts", Language = "typescript", Size = 400, Symbols = ["Database", "query"] },
            new() { Path = "src/utils/logger.ts", NormalizedPath = "src/utils/logger.ts", Language = "typescript", Size = 200, Symbols = ["Logger"] },
            new() { Path = "README.md", NormalizedPath = "README.md", Language = "markdown", Size = 2000, Symbols = [] },
            new() { Path = "package.json", NormalizedPath = "package.json", Language = "json", Size = 500, Symbols = [] },
        };

        var contentMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["src/auth/AuthService.ts"] = "export class AuthService { login() {} refreshToken() {} }",
            ["src/auth/TokenManager.ts"] = "export class TokenManager { getToken() {} refreshToken() {} }",
            ["src/api/UserController.ts"] = "export class UserController { getUser() {} }",
            ["src/db/Database.ts"] = "export class Database { query() {} }",
            ["src/utils/logger.ts"] = "export class Logger { log() {} }",
            ["README.md"] = "# Project\n\nThis is a test project.",
            ["package.json"] = """{"name":"test","dependencies":{"express":"4.0"}}""",
        };

        var wsId = WorkspaceId.New();
        var snapId = IndexSnapshotId.New();

        // First build — should populate cache
        var key = CacheKey.Build("Fix refreshToken in AuthService", snapId.Value, "deterministic-v1", 3, 80000, 90000, null, null);
        var wasCached = cache.TryGetOrBuild(key, () => engine.Build(
            new ContextBuildRequest { WorkspaceId = wsId, IndexSnapshotId = snapId, Task = "Fix refreshToken in AuthService", ModelId = "gpt-4" },
            () => files,
            path => contentMap.TryGetValue(path, out var c) ? c : "",
            path => "sha256:abc"), out var manifest1);

        Assert.False(wasCached); // First time — builds
        Assert.NotEmpty(manifest1!.SelectedFiles);
        Assert.Contains(manifest1.SelectedFiles, f => f.Path.Contains("AuthService"));

        // Second build with same key — should hit cache
        var wasCached2 = cache.TryGetOrBuild(key, () => engine.Build(
            new ContextBuildRequest { WorkspaceId = wsId, IndexSnapshotId = snapId, Task = "Fix refreshToken in AuthService", ModelId = "gpt-4" },
            () => files,
            path => contentMap.TryGetValue(path, out var c) ? c : "",
            path => "sha256:abc"), out var manifest2);

        Assert.True(wasCached2); // Cache hit
        Assert.Same(manifest1, manifest2);
    }

    [Fact]
    public void RealWorld_SecurityBlocksApiKeyInSelectedFile()
    {
        var policy = new Core.Security.SecurityPolicy { Version = "sec-v1" };
        var engine = new ContextEngine(securityPolicy: policy);

        var files = new List<IndexedFileInfo>
        {
            new() { Path = "config/settings.ts", NormalizedPath = "config/settings.ts", Language = "typescript", Size = 100, Symbols = ["Settings"] },
        };

        var manifest = engine.Build(
            new ContextBuildRequest
            {
                WorkspaceId = WorkspaceId.New(),
                IndexSnapshotId = IndexSnapshotId.New(),
                Task = "Fix Settings in config/settings.ts",
            },
            () => files,
            path => "export const API_KEY = 'sk-1234567890abcdefghijklmnopqrstuv';",
            path => "sha256:abc");

        // Security scan should have found the API key
        Assert.False(manifest.Safety.SecretsScanPassed);
        Assert.NotNull(manifest.Safety.SensitiveExclusions);
        Assert.NotEmpty(manifest.Safety.SensitiveExclusions!);
    }

    [Fact]
    public void RealWorld_TokenizerAffectsBudgetReporting()
    {
        var registry = new TokenizerRegistry();
        registry.Register("gpt-4", new CodeTokenizer());
        registry.Register("claude", new WordBoundaryTokenizer());
        var engine = new ContextEngine(registry);

        var wsId = WorkspaceId.New();
        var snapId = IndexSnapshotId.New();
        var files = new List<IndexedFileInfo>
        {
            new() { Path = "app.ts", NormalizedPath = "app.ts", Language = "typescript", Size = 100, Symbols = ["App"] },
        };
        var content = "export class App { run() { console.log('hello world'); } }";

        var manifestGpt4 = engine.Build(
            new ContextBuildRequest { WorkspaceId = wsId, IndexSnapshotId = snapId, Task = "Fix App", ModelId = "gpt-4" },
            () => files, path => content, path => "h");

        var manifestClaude = engine.Build(
            new ContextBuildRequest { WorkspaceId = wsId, IndexSnapshotId = snapId, Task = "Fix App", ModelId = "claude-3-opus" },
            () => files, path => content, path => "h");

        Assert.Equal("code-tokenizer", manifestGpt4.Budget.Tokenizer);
        Assert.Equal("word-boundary", manifestClaude.Budget.Tokenizer);
    }

    [Fact]
    public void RealWorld_OfflineMode_BlocksCloudSend()
    {
        var policy = new Core.Security.SecurityPolicy { Version = "sec-v1", Mode = Core.Security.ExfiltrationMode.Offline };
        var engine = new ContextEngine(securityPolicy: policy);

        var manifest = engine.Build(
            new ContextBuildRequest
            {
                WorkspaceId = WorkspaceId.New(),
                IndexSnapshotId = IndexSnapshotId.New(),
                Task = "Any task",
            },
            () => [],
            path => "",
            path => "h");

        Assert.False(manifest.Safety.CloudSendAllowed);
    }

    [Fact]
    public void RealWorld_Explain_ProducesMeaningfulOutput()
    {
        var registry = new TokenizerRegistry();
        var engine = new ContextEngine(registry);

        var files = new List<IndexedFileInfo>
        {
            new() { Path = "src/auth.ts", NormalizedPath = "src/auth.ts", Language = "typescript", Size = 500, Symbols = ["AuthService"] },
            new() { Path = "src/unused.ts", NormalizedPath = "src/unused.ts", Language = "typescript", Size = 1000, Symbols = ["Unused"] },
        };

        var manifest = engine.Build(
            new ContextBuildRequest
            {
                WorkspaceId = WorkspaceId.New(),
                IndexSnapshotId = IndexSnapshotId.New(),
                Task = "Fix AuthService in src/auth.ts",
            },
            () => files,
            path => $"export class {path} {{ }}",
            path => "sha256:abc");

        var explanations = Context.Explain.ContextExplainer.Explain(manifest);
        var misses = Context.Explain.ContextExplainer.DetectPotentialMisses(manifest);
        var budget = Context.Explain.ContextExplainer.BudgetSummary(manifest);

        Assert.NotEmpty(explanations);
        Assert.True(budget.Contains("已用", StringComparison.Ordinal) || budget.Contains("hard", StringComparison.Ordinal));
    }
}
