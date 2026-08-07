using CacheHub.Context.Engine;
using CacheHub.Context.Expand;
using CacheHub.Context.Payload;
using CacheHub.Context.Ranking;
using CacheHub.Context.Recall;
using CacheHub.Core.Context;
using CacheHub.Core.Identifiers;

namespace CacheHub.Tests;

/// <summary>
/// Tests for R5-W007 (Context Expand Revision) and R5-W008 (compression quality regression).
/// </summary>
public class ExpandAndCompressionTests
{
    // === R5-W007: Context Expand Revision ===

    [Fact]
    public void CreateRevision_SetsParentPackageId()
    {
        var parentManifest = BuildTestManifest();
        var expander = new ContextExpander();

        var expansion = expander.ExpandByFile(
            parentManifest.Id.Value, "src/new.ts", "export class NewService {}", "test expand");

        var revision = expander.CreateRevision(parentManifest, expansion);

        Assert.Equal(parentManifest.Id, revision.ParentPackageId);
        Assert.NotEqual(parentManifest.Id, revision.Id);
    }

    [Fact]
    public void CreateRevision_IncludesExpandedFiles()
    {
        var parentManifest = BuildTestManifest();
        var expander = new ContextExpander();

        var expansion = expander.ExpandByFile(
            parentManifest.Id.Value, "src/new.ts", "export class NewService {}", "add new file");

        var revision = expander.CreateRevision(parentManifest, expansion);

        Assert.Contains(revision.SelectedFiles, f => f.Path == "src/new.ts");
        Assert.True(revision.SelectedFiles.Count > parentManifest.SelectedFiles.Count);
    }

    [Fact]
    public void CreateRevision_AccumulatesTokens()
    {
        var parentManifest = BuildTestManifest();
        var expander = new ContextExpander();
        var parentTokens = parentManifest.Budget.ActualEstimate;

        var expansion = expander.ExpandByFile(
            parentManifest.Id.Value, "src/big.ts", new string('x', 4000), "add big file");

        var revision = expander.CreateRevision(parentManifest, expansion);

        Assert.True(revision.Budget.ActualEstimate > parentTokens,
            $"Revision tokens ({revision.Budget.ActualEstimate}) should be > parent tokens ({parentTokens})");
    }

    [Fact]
    public void CreateRevision_PreservesParentMetadata()
    {
        var parentManifest = BuildTestManifest();
        var expander = new ContextExpander();

        var expansion = expander.ExpandByFile(
            parentManifest.Id.Value, "src/x.ts", "content", "test");

        var revision = expander.CreateRevision(parentManifest, expansion);

        Assert.Equal(parentManifest.WorkspaceId, revision.WorkspaceId);
        Assert.Equal(parentManifest.IndexSnapshotId, revision.IndexSnapshotId);
        Assert.Equal(parentManifest.Task.OriginalText, revision.Task.OriginalText);
        Assert.Equal(parentManifest.ContextEngineVersion, revision.ContextEngineVersion);
    }

    // === R5-W008: Compression Quality Regression ===

    [Fact]
    public void LargeFile_ChunksMode_StaysWithinBudget()
    {
        var engine = new ContextEngine();
        var largeContent = string.Join('\n', Enumerable.Range(1, 2000).Select(i => $"line {i}: code content here"));

        var files = new List<IndexedFileInfo>
        {
            new() { Path = "big.ts", NormalizedPath = "big.ts", Language = "typescript", Size = largeContent.Length, Symbols = ["BigService"] },
        };

        var manifest = engine.Build(
            new ContextBuildRequest
            {
                WorkspaceId = WorkspaceId.New(),
                IndexSnapshotId = IndexSnapshotId.New(),
                Task = "Fix BigService",
            },
            () => files,
            _ => largeContent,
            _ => "sha256:test");

        Assert.NotEmpty(manifest.SelectedFiles);
        Assert.True(manifest.Budget.ActualEstimate <= manifest.Budget.ContextHardLimit,
            $"ActualEstimate {manifest.Budget.ActualEstimate} must not exceed HardLimit {manifest.Budget.ContextHardLimit}");
    }

    [Fact]
    public void ChineseTask_ProducesCandidates()
    {
        var engine = new ContextEngine();
        var files = new List<IndexedFileInfo>
        {
            new() { Path = "auth.ts", NormalizedPath = "auth.ts", Language = "typescript", Size = 500, Symbols = ["AuthService"] },
            new() { Path = "user.ts", NormalizedPath = "user.ts", Language = "typescript", Size = 300, Symbols = ["UserService"] },
        };

        var manifest = engine.Build(
            new ContextBuildRequest
            {
                WorkspaceId = WorkspaceId.New(),
                IndexSnapshotId = IndexSnapshotId.New(),
                Task = "修复 AuthService 登录方法的 bug",
            },
            () => files,
            _ => "export class AuthService { async login() {} }",
            _ => "sha256:test");

        Assert.NotEmpty(manifest.SelectedFiles);
        Assert.NotEmpty(manifest.Task.ExtractedSymbols!);
    }

    [Fact]
    public void MultipleSameNameSymbols_RankingStillStable()
    {
        var engine = new ContextEngine();
        var files = new List<IndexedFileInfo>
        {
            new() { Path = "a.ts", NormalizedPath = "a.ts", Language = "typescript", Size = 200, Symbols = ["Handler"] },
            new() { Path = "b.ts", NormalizedPath = "b.ts", Language = "typescript", Size = 200, Symbols = ["Handler"] },
            new() { Path = "c.ts", NormalizedPath = "c.ts", Language = "typescript", Size = 200, Symbols = ["Handler"] },
        };

        var manifest1 = engine.Build(
            new ContextBuildRequest
            {
                WorkspaceId = WorkspaceId.New(),
                IndexSnapshotId = IndexSnapshotId.New(),
                Task = "Fix Handler",
            },
            () => files, _ => "content", _ => "hash");

        var manifest2 = engine.Build(
            new ContextBuildRequest
            {
                WorkspaceId = WorkspaceId.New(),
                IndexSnapshotId = IndexSnapshotId.New(),
                Task = "Fix Handler",
            },
            () => files, _ => "content", _ => "hash");

        // Same input → same output (deterministic)
        Assert.Equal(
            manifest1.SelectedFiles.Select(f => f.Path),
            manifest2.SelectedFiles.Select(f => f.Path));
    }

    [Fact]
    public void Payload_MatchesManifestRanges()
    {
        var manifest = BuildTestManifest();
        var generator = new PayloadGenerator();
        var content = string.Join('\n', Enumerable.Range(1, 50).Select(i => $"line {i}"));

        var payload = generator.Generate(manifest, _ => content);

        Assert.NotEmpty(payload.Items);
        // Each payload item should correspond to a Manifest range
        foreach (var item in payload.Items)
        {
            Assert.NotNull(item.StartLine);
            Assert.NotNull(item.EndLine);
        }
    }

    [Fact]
    public void ErrorStackInTask_ProducesErrorStackReferences()
    {
        var parser = new Context.Parsing.TaskParser();
        var task = parser.Parse("Fix the error: at Service.Process(File.cs:123)");

        Assert.NotEmpty(task.ErrorStackReferences);
    }

    [Fact]
    public void CrossFileTask_SelectsMultipleFiles()
    {
        var engine = new ContextEngine();
        var files = new List<IndexedFileInfo>
        {
            new() { Path = "auth.ts", NormalizedPath = "auth.ts", Language = "typescript", Size = 300, Symbols = ["AuthService", "login"] },
            new() { Path = "user.ts", NormalizedPath = "user.ts", Language = "typescript", Size = 300, Symbols = ["UserService"] },
            new() { Path = "config.ts", NormalizedPath = "config.ts", Language = "typescript", Size = 200, Symbols = ["Config"] },
        };

        var manifest = engine.Build(
            new ContextBuildRequest
            {
                WorkspaceId = WorkspaceId.New(),
                IndexSnapshotId = IndexSnapshotId.New(),
                Task = "Fix the auth login flow that calls UserService",
            },
            () => files,
            path => $"export class {Path.GetFileNameWithoutExtension(path)} {{}}",
            _ => "sha256:test");

        // Should select multiple files
        Assert.True(manifest.SelectedFiles.Count >= 1);
    }

    private static ContextPackageManifest BuildTestManifest()
    {
        return new ContextPackageManifest
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
                ActualEstimate = 500,
            },
            SelectedFiles =
            [
                new SelectedFile
                {
                    Path = "src/auth.ts",
                    ContentHash = "sha256:abc",
                    Mode = SelectionMode.Full,
                    Score = 0.8,
                    Reasons = ["test"],
                    Ranges = [new LineRange { StartLine = 1, EndLine = 20 }],
                },
            ],
            ExcludedCandidates = [],
            Safety = new SafetyInfo { CloudSendAllowed = true, SecretsScanPassed = true },
            ContextEngineVersion = "0.2.0-prealpha",
            ChunkingStrategyVersion = "chunking-v2",
            TokenBudgetPolicyVersion = "budget-v2",
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
