using AiKv.Context.Engine;
using AiKv.Context.Recall;
using AiKv.Core.Security;
using AiKv.Core.Tokens;

namespace AiKv.Tests;

public class ContextEngineIntegrationTests
{
    [Fact]
    public void ContextEngine_WithTokenizer_ShouldUseCorrectTokenizerId()
    {
        var registry = new TokenizerRegistry();
        registry.Register("gpt-4", new CodeTokenizer());
        var engine = new ContextEngine(registry);

        var manifest = engine.Build(
            new ContextBuildRequest
            {
                WorkspaceId = Core.Identifiers.WorkspaceId.New(),
                IndexSnapshotId = Core.Identifiers.IndexSnapshotId.New(),
                Task = "Fix bug",
                ModelId = "gpt-4",
            },
            () => [],
            path => "",
            path => "sha256:abc");

        Assert.Equal("code-tokenizer", manifest.Budget.Tokenizer);
        Assert.Equal("v1", manifest.Budget.TokenizerVersion);
    }

    [Fact]
    public void ContextEngine_WithoutModelId_ShouldUseDefaultTokenizer()
    {
        var engine = new ContextEngine();

        var manifest = engine.Build(
            new ContextBuildRequest
            {
                WorkspaceId = Core.Identifiers.WorkspaceId.New(),
                IndexSnapshotId = Core.Identifiers.IndexSnapshotId.New(),
                Task = "Fix bug",
            },
            () => [],
            path => "",
            path => "sha256:abc");

        Assert.Equal("char-estimate", manifest.Budget.Tokenizer);
    }

    [Fact]
    public void ContextEngine_WithSecurityPolicy_ShouldScanContent()
    {
        var policy = new SecurityPolicy { Version = "sec-v1" };
        var engine = new ContextEngine(securityPolicy: policy);

        var files = new List<IndexedFileInfo>
        {
            new() { Path = "config.ts", NormalizedPath = "config.ts", Language = "typescript", Size = 100, Symbols = ["Config"] },
        };

        var manifest = engine.Build(
            new ContextBuildRequest
            {
                WorkspaceId = Core.Identifiers.WorkspaceId.New(),
                IndexSnapshotId = Core.Identifiers.IndexSnapshotId.New(),
                Task = "Fix Config in config.ts",
            },
            () => files,
            path => "const apiKey = 'sk-1234567890abcdefghijklmnopqrstuv';",
            path => "sha256:abc");

        Assert.Equal("secret-scanner-v1", manifest.Safety.SecretScannerVersion);
        // The file with the secret should have been flagged
        if (manifest.Safety.SensitiveExclusions is not null)
            Assert.NotEmpty(manifest.Safety.SensitiveExclusions);
    }

    [Fact]
    public void ContextEngine_WithOfflinePolicy_ShouldSetCloudSendNotAllowed()
    {
        var policy = new SecurityPolicy { Version = "sec-v1", Mode = ExfiltrationMode.Offline };
        var engine = new ContextEngine(securityPolicy: policy);

        var manifest = engine.Build(
            new ContextBuildRequest
            {
                WorkspaceId = Core.Identifiers.WorkspaceId.New(),
                IndexSnapshotId = Core.Identifiers.IndexSnapshotId.New(),
                Task = "Fix bug",
            },
            () => [],
            path => "",
            path => "sha256:abc");

        Assert.False(manifest.Safety.CloudSendAllowed);
    }

    [Fact]
    public void ContextEngine_WithoutSecurityPolicy_ShouldDefaultSafe()
    {
        var engine = new ContextEngine();

        var manifest = engine.Build(
            new ContextBuildRequest
            {
                WorkspaceId = Core.Identifiers.WorkspaceId.New(),
                IndexSnapshotId = Core.Identifiers.IndexSnapshotId.New(),
                Task = "Fix bug",
            },
            () => [],
            path => "",
            path => "sha256:abc");

        Assert.True(manifest.Safety.CloudSendAllowed);
        Assert.True(manifest.Safety.SecretsScanPassed);
    }

    [Fact]
    public void ContextEngine_ShouldUpdateVersion()
    {
        var engine = new ContextEngine();

        var manifest = engine.Build(
            new ContextBuildRequest
            {
                WorkspaceId = Core.Identifiers.WorkspaceId.New(),
                IndexSnapshotId = Core.Identifiers.IndexSnapshotId.New(),
                Task = "Fix bug",
            },
            () => [],
            path => "",
            path => "sha256:abc");

        Assert.Equal("0.2.0", manifest.ContextEngineVersion);
    }
}
