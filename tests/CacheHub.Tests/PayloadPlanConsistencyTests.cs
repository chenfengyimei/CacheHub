using CacheHub.Context.Engine;
using CacheHub.Context.Payload;
using CacheHub.Core.Context;
using CacheHub.Core.Identifiers;
using CacheHub.Core.Security;

namespace CacheHub.Tests;

/// <summary>
/// Tests for R5-W006: Manifest/Payload 共用 PayloadPlan.
/// Verifies that Payload content matches Manifest ranges exactly.
/// </summary>
public class PayloadPlanConsistencyTests
{
    [Fact]
    public void Payload_UsesManifestRanges_NotRechunked()
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
            SelectedFiles =
            [
                new SelectedFile
                {
                    Path = "test.ts",
                    ContentHash = "sha256:abc",
                    Mode = SelectionMode.Chunks,
                    Score = 0.8,
                    Reasons = ["test"],
                    Ranges = [new LineRange { StartLine = 10, EndLine = 20 }],
                },
            ],
            ExcludedCandidates = [],
            Safety = new SafetyInfo { CloudSendAllowed = true, SecretsScanPassed = true },
            ContextEngineVersion = "0.2.0-prealpha",
            ChunkingStrategyVersion = "chunking-v2",
            TokenBudgetPolicyVersion = "budget-v2",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        // Content has 30 lines
        var content = string.Join('\n', Enumerable.Range(1, 30).Select(i => $"line {i}"));
        var generator = new PayloadGenerator();
        var payload = generator.Generate(manifest, _ => content);

        // Payload should contain ONLY lines 10-20 from the Manifest range
        Assert.Single(payload.Items);
        var item = payload.Items[0];
        Assert.Equal(10, item.StartLine);
        Assert.Equal(20, item.EndLine);

        // Content should be lines 10-20 (11 lines)
        var lines = item.Content.Split('\n');
        Assert.Equal(11, lines.Length);
        Assert.Equal("line 10", lines[0]);
        Assert.Equal("line 20", lines[^1]);
    }

    [Fact]
    public void Payload_FallbackRechunks_WhenNoRanges()
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
            SelectedFiles =
            [
                new SelectedFile
                {
                    Path = "test.ts",
                    ContentHash = "sha256:abc",
                    Mode = SelectionMode.Full,
                    Score = 0.8,
                    Reasons = ["test"],
                    Ranges = null, // No ranges → fallback to re-chunking
                },
            ],
            ExcludedCandidates = [],
            Safety = new SafetyInfo { CloudSendAllowed = true, SecretsScanPassed = true },
            ContextEngineVersion = "0.2.0-prealpha",
            ChunkingStrategyVersion = "chunking-v2",
            TokenBudgetPolicyVersion = "budget-v2",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var content = string.Join('\n', Enumerable.Range(1, 10).Select(i => $"line {i}"));
        var generator = new PayloadGenerator();
        var payload = generator.Generate(manifest, _ => content);

        // Should still produce items (via fallback re-chunking)
        Assert.NotEmpty(payload.Items);
    }

}
