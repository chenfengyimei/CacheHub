using CacheHub.Context.Engine;
using CacheHub.Context.Recall;
using CacheHub.Core.Context;
using CacheHub.Gateway;
using CacheHub.Gateway.Server;
using CacheHub.Core.Identifiers;
using CacheHub.Core.Workflow;

namespace CacheHub.Tests;

/// <summary>
/// R9 Gate regression tests: unified workflow, independent modes, workspace binding.
/// </summary>
public class R9GateRegressionTests
{
    // R9 Gate: Context-only mode works without Gateway
    [Fact]
    public void Gate_ContextOnlyMode_WorksIndependently()
    {
        var engine = new ContextEngine();
        var manifest = engine.Build(
            new ContextBuildRequest
            {
                WorkspaceId = WorkspaceId.New(),
                IndexSnapshotId = IndexSnapshotId.New(),
                Task = "Fix AuthService",
            },
            () => new List<IndexedFileInfo>
            {
                new() { Path = "auth.ts", NormalizedPath = "auth.ts", Language = "typescript", Size = 100, Symbols = ["AuthService"] },
            },
            _ => "export class AuthService {}",
            _ => "sha256:test");

        Assert.NotNull(manifest);
        Assert.NotEmpty(manifest.SelectedFiles);
    }

    // R9 Gate: Gateway-only mode works without Context Engine
    [Fact]
    public void Gate_GatewayOnlyMode_WorksIndependently()
    {
        using var server = new GatewayServer(new GatewayConfig
        {
            ProviderBaseUrl = "https://api.example.com",
            ProviderApiKey = "test-key",
            Port = 15310,
        });

        // Gateway can be started and has an access token â€?it doesn't need Context Engine
        Assert.NotEmpty(server.AccessToken);
    }

    // R9 Gate: Unified workflow produces Manifest + optional Gateway call
    [Fact]
    public void Gate_UnifiedWorkflow_ManifestOnlyMode()
    {
        // Build context only (CallGateway = false)
        var request = new ContextualCompletionRequest
        {
            WorkspaceId = WorkspaceId.New(),
            Task = "Fix the bug",
            CallGateway = false,
        };

        // Simulate the workflow: build context â†?no gateway call
        var engine = new ContextEngine();
        var manifest = engine.Build(
            new ContextBuildRequest
            {
                WorkspaceId = request.WorkspaceId,
                IndexSnapshotId = IndexSnapshotId.New(),
                Task = request.Task,
            },
            () => [],
            _ => "",
            _ => "sha256:test");

        var response = new ContextualCompletionResponse
        {
            Manifest = manifest,
            GatewayCalled = false,
        };

        Assert.False(response.GatewayCalled);
        Assert.NotNull(response.Manifest);
    }

    // R9 Gate: Workspace binding is explicit (not guessed)
    [Fact]
    public void Gate_WorkspaceBinding_IsExplicit()
    {
        var wsId = WorkspaceId.New();
        var resolution = WorkspaceResolution.Unique(wsId);

        Assert.True(resolution.IsUnique);
        Assert.Equal(wsId, resolution.WorkspaceId);

        // Non-unique path â†?error, not guess
        var nonUnique = WorkspaceResolution.NotUnique("Path matches 3 workspaces");
        Assert.False(nonUnique.IsUnique);
        Assert.Null(nonUnique.WorkspaceId);
    }

    // R9 Gate: Prompt Assembly doesn't depend on specific Agent implementations
    [Fact]
    public void Gate_PromptAssembly_NoAgentSpecificDependencies()
    {
        var manifest = new ContextPackageManifest
        {
            Id = ContextPackageId.New(),
            SchemaVersion = 1,
            WorkspaceId = WorkspaceId.New(),
            IndexSnapshotId = IndexSnapshotId.New(),
            Task = new TaskInfo { OriginalText = "test", QueryParserVersion = "v1" },
            Ranking = new RankingInfo { ProfileId = "test", ProfileVersion = 1 },
            Budget = new BudgetInfo
            {
                ModelContextWindow = 128_000,
                AgentReservedTokens = 1000,
                ResponseReservedTokens = 1000,
                ContextTarget = 50_000,
                ContextHardLimit = 60_000,
                SafetyMargin = 1000,
                ActualEstimate = 100,
            },
            SelectedFiles = [],
            ExcludedCandidates = [],
            Safety = new SafetyInfo { CloudSendAllowed = true, SecretsScanPassed = true },
            ContextEngineVersion = "0.2.0-prealpha",
            ChunkingStrategyVersion = "chunking-v2",
            TokenBudgetPolicyVersion = "budget-v2",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var service = new PromptAssemblyService();
        var (systemPrompt, userContent) = service.Assemble(manifest, "code content");

        // Should not contain Codex, Claude Code, or any specific agent references
        Assert.DoesNotContain("Codex", systemPrompt);
        Assert.DoesNotContain("Claude", systemPrompt);
        Assert.DoesNotContain("copilot", systemPrompt.ToLowerInvariant());
    }

    // R9 Gate: Full lifecycle token tracking
    [Fact]
    public void Gate_LifecycleTokenTracking_IncludesContextAndModel()
    {
        var response = new ContextualCompletionResponse
        {
            Manifest = new ContextPackageManifest
            {
                Id = ContextPackageId.New(),
                SchemaVersion = 1,
                WorkspaceId = WorkspaceId.New(),
                IndexSnapshotId = IndexSnapshotId.New(),
                Task = new TaskInfo { OriginalText = "test", QueryParserVersion = "v1" },
                Ranking = new RankingInfo { ProfileId = "test", ProfileVersion = 1 },
                Budget = new BudgetInfo
                {
                    ModelContextWindow = 128_000,
                    AgentReservedTokens = 1000,
                    ResponseReservedTokens = 1000,
                    ContextTarget = 50_000,
                    ContextHardLimit = 60_000,
                    SafetyMargin = 1000,
                    ActualEstimate = 5000,
                },
                SelectedFiles = [],
                ExcludedCandidates = [],
                Safety = new SafetyInfo { CloudSendAllowed = true, SecretsScanPassed = true },
                ContextEngineVersion = "0.2.0-prealpha",
                ChunkingStrategyVersion = "chunking-v2",
                TokenBudgetPolicyVersion = "budget-v2",
                CreatedAt = DateTimeOffset.UtcNow,
            },
            GatewayCalled = true,
            PromptTokens = 5000,
            CompletionTokens = 2000,
        };

        // Total = 5000 (context) + 5000 (prompt) + 2000 (completion) = 12000
        Assert.Equal(12000, response.TotalLifecycleTokens);
    }
}
