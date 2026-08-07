using CacheHub.Core.Context;
using CacheHub.Core.Identifiers;
using CacheHub.Core.Workflow;

namespace CacheHub.Tests;

/// <summary>
/// Tests for R9-W001 to W004: Contextual Completion protocol, Gateway metadata,
/// Prompt Assembly, and workspace resolution.
/// </summary>
public class R9WorkflowTests
{
    private static ContextPackageManifest BuildTestManifest()
    {
        return new ContextPackageManifest
        {
            Id = ContextPackageId.New(),
            SchemaVersion = 1,
            WorkspaceId = WorkspaceId.New(),
            IndexSnapshotId = IndexSnapshotId.New(),
            Task = new TaskInfo { OriginalText = "Fix AuthService login bug", QueryParserVersion = "v1" },
            Ranking = new RankingInfo { ProfileId = "test", ProfileVersion = 1 },
            Budget = new BudgetInfo
            {
                ModelContextWindow = 128_000,
                AgentReservedTokens = 1000,
                ResponseReservedTokens = 1000,
                ContextTarget = 50_000,
                ContextHardLimit = 60_000,
                SafetyMargin = 1000,
                ActualEstimate = 500,
            },
            SelectedFiles =
            [
                new SelectedFile { Path = "src/auth.ts", ContentHash = "h1", Mode = SelectionMode.Full, Score = 0.8, Reasons = ["match"] },
            ],
            ExcludedCandidates = [],
            Safety = new SafetyInfo { CloudSendAllowed = true, SecretsScanPassed = true },
            ContextEngineVersion = "0.2.0-prealpha",
            ChunkingStrategyVersion = "chunking-v2",
            TokenBudgetPolicyVersion = "budget-v2",
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    // === R9-W001: Contextual Completion Protocol ===

    [Fact]
    public void ContextualCompletionRequest_RequiresWorkspaceId()
    {
        var req = new ContextualCompletionRequest
        {
            WorkspaceId = WorkspaceId.New(),
            Task = "Fix the bug",
        };

        Assert.NotNull(req.WorkspaceId);
        Assert.Equal("Fix the bug", req.Task);
        Assert.False(req.CallGateway); // Default: manifest only
    }

    [Fact]
    public void ContextualCompletionResponse_CalculatesTotalLifecycleTokens()
    {
        var resp = new ContextualCompletionResponse
        {
            Manifest = BuildTestManifest(),
            GatewayCalled = true,
            PromptTokens = 1000,
            CompletionTokens = 500,
        };

        // Total = manifest.ActualEstimate (500) + prompt (1000) + completion (500) = 2000
        Assert.Equal(2000, resp.TotalLifecycleTokens);
    }

    // === R9-W002: Gateway Metadata ===

    [Fact]
    public void GatewayMetadata_WithIds_HasMetadata()
    {
        var meta = new GatewayMetadata
        {
            ContextPackageId = ContextPackageId.New(),
            SnapshotId = IndexSnapshotId.New(),
            ClientId = "test-client",
        };

        Assert.True(meta.HasMetadata);
    }

    [Fact]
    public void GatewayMetadata_WithoutIds_HasNoMetadata()
    {
        var meta = new GatewayMetadata { ClientId = "test" };

        Assert.False(meta.HasMetadata);
    }

    // === R9-W003: Prompt Assembly ===

    [Fact]
    public void PromptAssembly_ProducesSystemAndUserContent()
    {
        var manifest = BuildTestManifest();
        var service = new PromptAssemblyService();

        var (systemPrompt, userContent) = service.Assemble(manifest, "export class AuthService {}");

        Assert.Contains("CacheHub Context", systemPrompt);
        Assert.Contains("Fix AuthService login bug", systemPrompt);
        Assert.Contains("src/auth.ts", systemPrompt);
        Assert.Contains("Code Context", userContent);
        Assert.Contains("export class AuthService", userContent);
    }

    [Fact]
    public void PromptAssembly_ManifestOnly_ReturnsSummary()
    {
        var manifest = BuildTestManifest();
        var service = new PromptAssemblyService();

        var result = service.ManifestOnly(manifest);

        Assert.Contains(manifest.Id.Value, result);
        Assert.Contains("Fix AuthService login bug", result);
    }

    [Fact]
    public void PromptAssembly_WithoutPayload_ReturnsEmptyUserContent()
    {
        var manifest = BuildTestManifest();
        var service = new PromptAssemblyService();

        var (systemPrompt, userContent) = service.Assemble(manifest, "payload",
            new PromptAssemblyOptions { IncludePayload = false });

        Assert.NotEmpty(systemPrompt);
        Assert.Empty(userContent);
    }

    // === R9-W004: Workspace Resolution ===

    [Fact]
    public void WorkspaceResolution_Unique_ReturnsWorkspaceId()
    {
        var wsId = WorkspaceId.New();
        var resolution = WorkspaceResolution.Unique(wsId);

        Assert.True(resolution.IsUnique);
        Assert.Equal(wsId, resolution.WorkspaceId);
    }

    [Fact]
    public void WorkspaceResolution_NotUnique_ReturnsError()
    {
        var resolution = WorkspaceResolution.NotUnique("Path matches multiple workspaces");

        Assert.False(resolution.IsUnique);
        Assert.Contains("multiple", resolution.Error);
    }

    [Fact]
    public void WorkspaceResolution_NotFound_ReturnsError()
    {
        var resolution = WorkspaceResolution.NotFound("No workspace found for path");

        Assert.False(resolution.IsUnique);
        Assert.Contains("No workspace", resolution.Error);
    }
}
