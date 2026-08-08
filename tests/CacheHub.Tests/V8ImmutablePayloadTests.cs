using System.Security.Cryptography;
using System.Text;
using CacheHub.Context.Chunking;
using CacheHub.Context.Payload;
using CacheHub.Context.Selection;
using CacheHub.Core.Context;
using CacheHub.Core.Identifiers;
using Xunit;

namespace CacheHub.Tests;

/// <summary>
/// V8-P0-02: Immutable Context Payload tests.
/// Verifies that PayloadGenerator rejects content that doesn't match the manifest's ContentHash.
/// </summary>
public class V8ImmutablePayloadTests
{
    private static string ComputeSha256(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    [Fact]
    public void Generate_MatchingHash_Succeeds()
    {
        var content = "public class Auth { }";
        var hash = ComputeSha256(content);
        var manifest = CreateManifest("src/auth.cs", hash);
        var generator = new PayloadGenerator();

        var payload = generator.Generate(manifest, _ => content);

        Assert.Single(payload.Items);
        Assert.Equal("src/auth.cs", payload.Items[0].Path);
    }

    [Fact]
    public void Generate_MismatchedHash_ThrowsContextVersionMismatch()
    {
        var originalContent = "public class Auth { }";
        var modifiedContent = "public class Auth { /* modified */ }";
        var hash = ComputeSha256(originalContent);
        var manifest = CreateManifest("src/auth.cs", hash);
        var generator = new PayloadGenerator();

        var ex = Assert.Throws<ContextVersionMismatchException>(() =>
            generator.Generate(manifest, _ => modifiedContent));

        Assert.Equal("src/auth.cs", ex.FilePath);
        Assert.Equal(hash, ex.ExpectedHash);
        Assert.Equal(ComputeSha256(modifiedContent), ex.ActualHash);
    }

    [Fact]
    public void Generate_FingerprintHash_SkipsVerification()
    {
        var content = "some content that doesn't match any hash";
        var manifest = CreateManifest("src/file.cs", "fp:1024:abcdef");
        var generator = new PayloadGenerator();

        var payload = generator.Generate(manifest, _ => content);
        Assert.Single(payload.Items);
    }

    [Fact]
    public void Generate_ExpandedHash_SkipsVerification()
    {
        var content = "expanded content";
        var manifest = CreateManifest("src/expanded.cs", "sha256:expanded");
        var generator = new PayloadGenerator();

        var payload = generator.Generate(manifest, _ => content);
        Assert.Single(payload.Items);
    }

    [Fact]
    public void Generate_PendingHash_SkipsVerification()
    {
        var content = "pending content";
        var manifest = CreateManifest("src/pending.cs", "sha256:pending");
        var generator = new PayloadGenerator();

        var payload = generator.Generate(manifest, _ => content);
        Assert.Single(payload.Items);
    }

    [Fact]
    public void Generate_EmptyHash_SkipsVerification()
    {
        var content = "content without hash";
        var manifest = CreateManifest("src/nohash.cs", "");
        var generator = new PayloadGenerator();

        var payload = generator.Generate(manifest, _ => content);
        Assert.Single(payload.Items);
    }

    [Fact]
    public void GenerateMarkdown_MismatchedHash_ThrowsContextVersionMismatch()
    {
        var originalContent = "public class Service { }";
        var modifiedContent = "public class Service { /* changed */ }";
        var hash = ComputeSha256(originalContent);
        var manifest = CreateManifest("src/service.cs", hash);
        var generator = new PayloadGenerator();

        Assert.Throws<ContextVersionMismatchException>(() =>
            generator.GenerateMarkdown(manifest, _ => modifiedContent));
    }

    [Fact]
    public void GenerateMarkdown_MatchingHash_Succeeds()
    {
        var content = "public class Service { }";
        var hash = ComputeSha256(content);
        var manifest = CreateManifest("src/service.cs", hash);
        var generator = new PayloadGenerator();

        var markdown = generator.GenerateMarkdown(manifest, _ => content);
        Assert.Contains("src/service.cs", markdown);
    }

    [Fact]
    public void ContextVersionMismatchException_ContainsFilePathAndHashes()
    {
        var ex = new ContextVersionMismatchException("path/to/file.cs", "sha256:expected", "sha256:actual");

        Assert.Equal("path/to/file.cs", ex.FilePath);
        Assert.Equal("sha256:expected", ex.ExpectedHash);
        Assert.Equal("sha256:actual", ex.ActualHash);
        Assert.Contains("path/to/file.cs", ex.Message);
        Assert.Contains("sha256:expected", ex.Message);
        Assert.Contains("sha256:actual", ex.Message);
    }

    [Fact]
    public void Generate_MultipleFiles_StopsOnFirstMismatch()
    {
        var content1 = "file1 content";
        var content2 = "file2 content";
        var hash1 = ComputeSha256(content1);
        var hash2 = ComputeSha256(content2);

        var manifest = CreateMultiFileManifest(
            ("src/file1.cs", hash1),
            ("src/file2.cs", hash2));

        var generator = new PayloadGenerator();

        // First file correct, second file modified
        var ex = Assert.Throws<ContextVersionMismatchException>(() =>
            generator.Generate(manifest, path => path == "src/file1.cs" ? content1 : "modified content2"));

        Assert.Equal("src/file2.cs", ex.FilePath);
    }

    // Helpers
    private static ContextPackageManifest CreateManifest(string path, string contentHash)
    {
        return new ContextPackageManifest
        {
            Id = ContextPackageId.New(),
            SchemaVersion = 1,
            WorkspaceId = WorkspaceId.New(),
            IndexSnapshotId = IndexSnapshotId.New(),
            Task = new TaskInfo { OriginalText = "test", QueryParserVersion = "v1" },
            Ranking = new RankingInfo { ProfileId = "default", ProfileVersion = 1 },
            Budget = new BudgetInfo
            {
                ModelContextWindow = 128000,
                AgentReservedTokens = 20000,
                ResponseReservedTokens = 4000,
                ContextTarget = 100000,
                ContextHardLimit = 104000,
                SafetyMargin = 2000,
                ActualEstimate = 500,
                Tokenizer = "bpe",
                TokenizerVersion = "1.0",
            },
            SelectedFiles =
            [
                new SelectedFile
                {
                    Path = path,
                    ContentHash = contentHash,
                    Mode = SelectionMode.Full,
                    Score = 1.0,
                    Reasons = ["test"],
                },
            ],
            ExcludedCandidates = [],
            Safety = new SafetyInfo
            {
                CloudSendAllowed = true,
                SecretsScanPassed = true,
                SecurityPolicyVersion = "sec-v1",
                SecretScannerVersion = "none",
            },
            ContextEngineVersion = "0.2.0-prealpha",
            ChunkingStrategyVersion = ChunkingStrategy.Version,
            TokenBudgetPolicyVersion = "v1",
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static ContextPackageManifest CreateMultiFileManifest(params (string Path, string Hash)[] files)
    {
        return new ContextPackageManifest
        {
            Id = ContextPackageId.New(),
            SchemaVersion = 1,
            WorkspaceId = WorkspaceId.New(),
            IndexSnapshotId = IndexSnapshotId.New(),
            Task = new TaskInfo { OriginalText = "test", QueryParserVersion = "v1" },
            Ranking = new RankingInfo { ProfileId = "default", ProfileVersion = 1 },
            Budget = new BudgetInfo
            {
                ModelContextWindow = 128000,
                AgentReservedTokens = 20000,
                ResponseReservedTokens = 4000,
                ContextTarget = 100000,
                ContextHardLimit = 104000,
                SafetyMargin = 2000,
                ActualEstimate = 500,
                Tokenizer = "bpe",
                TokenizerVersion = "1.0",
            },
            SelectedFiles = files.Select(f => new SelectedFile
            {
                Path = f.Path,
                ContentHash = f.Hash,
                Mode = SelectionMode.Full,
                Score = 1.0,
                Reasons = ["test"],
            }).ToList(),
            ExcludedCandidates = [],
            Safety = new SafetyInfo
            {
                CloudSendAllowed = true,
                SecretsScanPassed = true,
                SecurityPolicyVersion = "sec-v1",
                SecretScannerVersion = "none",
            },
            ContextEngineVersion = "0.2.0-prealpha",
            ChunkingStrategyVersion = ChunkingStrategy.Version,
            TokenBudgetPolicyVersion = "v1",
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
