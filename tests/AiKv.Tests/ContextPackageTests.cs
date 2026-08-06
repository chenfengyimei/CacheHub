using System.Text.Json;
using AiKv.Core.Context;
using AiKv.Core.Identifiers;

namespace AiKv.Tests;

public class ContextPackageTests
{
    [Fact]
    public void Manifest_CanBeCreated_WithRequiredFields()
    {
        var manifest = new ContextPackageManifest
        {
            Id = ContextPackageId.New(),
            WorkspaceId = WorkspaceId.New(),
            IndexSnapshotId = IndexSnapshotId.New(),
            Task = new TaskInfo
            {
                OriginalText = "Fix login token refresh",
                QueryParserVersion = "deterministic-query-v1",
            },
            Ranking = new RankingInfo
            {
                ProfileId = "deterministic-v1",
                ProfileVersion = 3,
            },
            Budget = new BudgetInfo
            {
                ModelContextWindow = 128000,
                AgentReservedTokens = 18000,
                ResponseReservedTokens = 12000,
                ContextTarget = 80000,
                ContextHardLimit = 90000,
                SafetyMargin = 10000,
                ActualEstimate = 75000,
            },
            SelectedFiles =
            [
                new SelectedFile
                {
                    Path = "src/auth/token.ts",
                    ContentHash = "sha256:abc123",
                    Mode = SelectionMode.Chunks,
                    Score = 0.97,
                    Reasons = ["Symbol match", "Git Diff"],
                    Ranges = [new LineRange { StartLine = 20, EndLine = 110 }],
                },
            ],
            ExcludedCandidates =
            [
                new ExcludedCandidate
                {
                    Path = "docs/auth.md",
                    Score = 0.61,
                    Reason = "Token budget insufficient",
                },
            ],
            Safety = new SafetyInfo
            {
                CloudSendAllowed = true,
                SecretsScanPassed = true,
                IgnoreRulesHash = "hash_001",
                SecurityPolicyVersion = "sec-v1",
            },
            ContextEngineVersion = "0.1.0",
            ChunkingStrategyVersion = "chunking-v1",
            TokenBudgetPolicyVersion = "budget-v1",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        Assert.Equal(1, manifest.SchemaVersion);
        Assert.Single(manifest.SelectedFiles);
        Assert.Equal("Fix login token refresh", manifest.Task.OriginalText);
        Assert.Equal(SelectionMode.Chunks, manifest.SelectedFiles[0].Mode);
    }

    [Fact]
    public void Manifest_CanSerializeToJson()
    {
        var manifest = new ContextPackageManifest
        {
            Id = ContextPackageId.Parse("ctx_test001"),
            WorkspaceId = WorkspaceId.Parse("ws_test001"),
            IndexSnapshotId = IndexSnapshotId.Parse("idx_test001"),
            Task = new TaskInfo
            {
                OriginalText = "test task",
                QueryParserVersion = "v1",
            },
            Ranking = new RankingInfo { ProfileId = "v1", ProfileVersion = 1 },
            Budget = new BudgetInfo
            {
                ModelContextWindow = 128000,
                AgentReservedTokens = 10000,
                ResponseReservedTokens = 8000,
                ContextTarget = 80000,
                ContextHardLimit = 90000,
                SafetyMargin = 10000,
                ActualEstimate = 50000,
            },
            SelectedFiles = [],
            ExcludedCandidates = [],
            Safety = new SafetyInfo
            {
                CloudSendAllowed = false,
                SecretsScanPassed = true,
            },
            ContextEngineVersion = "0.1.0",
            ChunkingStrategyVersion = "v1",
            TokenBudgetPolicyVersion = "v1",
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };

        var json = JsonSerializer.Serialize(manifest);
        var deserialized = JsonSerializer.Deserialize<ContextPackageManifest>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("ctx_test001", deserialized.Id.Value);
        Assert.Equal("ws_test001", deserialized.WorkspaceId.Value);
        Assert.Equal("test task", deserialized.Task.OriginalText);
        Assert.False(deserialized.Safety.CloudSendAllowed);
    }

    [Fact]
    public void Payload_CanBeCreated_WithRequiredFields()
    {
        var payload = new ContextPackagePayload
        {
            ContextPackageId = "ctx_test001",
            Format = PayloadFormat.Markdown,
            Items =
            [
                new PayloadItem
                {
                    Path = "src/auth/token.ts",
                    Mode = SelectionMode.Chunks,
                    Content = "export function refreshToken() { ... }",
                    StartLine = 20,
                    EndLine = 110,
                },
            ],
            TotalEstimatedTokens = 500,
        };

        Assert.Equal(PayloadFormat.Markdown, payload.Format);
        Assert.Single(payload.Items);
        Assert.Contains("refreshToken", payload.Items[0].Content);
    }

    [Fact]
    public void SelectionMode_HasExpectedValues()
    {
        Assert.Equal(5, Enum.GetNames<SelectionMode>().Length);
        Assert.True(Enum.IsDefined(SelectionMode.Full));
        Assert.True(Enum.IsDefined(SelectionMode.Chunks));
        Assert.True(Enum.IsDefined(SelectionMode.Outline));
        Assert.True(Enum.IsDefined(SelectionMode.DeterministicSummary));
        Assert.True(Enum.IsDefined(SelectionMode.Metadata));
    }
}
