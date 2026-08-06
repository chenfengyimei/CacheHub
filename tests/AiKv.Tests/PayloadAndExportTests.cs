using AiKv.Context.Export;
using AiKv.Context.Payload;
using AiKv.Core.Context;
using AiKv.Core.Identifiers;

namespace AiKv.Tests;

public class PayloadAndExportTests
{
    private static ContextPackageManifest CreateManifest() => new()
    {
        Id = ContextPackageId.Parse("ctx_test001"),
        WorkspaceId = WorkspaceId.Parse("ws_test001"),
        IndexSnapshotId = IndexSnapshotId.Parse("idx_test001"),
        Task = new TaskInfo { OriginalText = "Fix login bug", QueryParserVersion = "v1" },
        Ranking = new RankingInfo { ProfileId = "v1", ProfileVersion = 1 },
        Budget = new BudgetInfo
        {
            ModelContextWindow = 128000,
            AgentReservedTokens = 10000,
            ResponseReservedTokens = 8000,
            ContextTarget = 80000,
            ContextHardLimit = 90000,
            SafetyMargin = 10000,
            ActualEstimate = 500,
        },
        SelectedFiles =
        [
            new SelectedFile
            {
                Path = "src/auth.ts", ContentHash = "h1", Mode = SelectionMode.Full,
                Score = 0.95, Reasons = ["Symbol match"],
            },
            new SelectedFile
            {
                Path = "src/utils.ts", ContentHash = "h2", Mode = SelectionMode.Outline,
                Score = 0.60, Reasons = ["Path match"],
            },
        ],
        ExcludedCandidates =
        [
            new ExcludedCandidate { Path = "README.md", Score = 0.1, Reason = "Low score" },
        ],
        Safety = new SafetyInfo { CloudSendAllowed = true, SecretsScanPassed = true },
        ContextEngineVersion = "0.2.0",
        ChunkingStrategyVersion = "v1",
        TokenBudgetPolicyVersion = "v1",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void PayloadGenerator_Generate_ShouldCreatePayloadItems()
    {
        var generator = new PayloadGenerator();
        var manifest = CreateManifest();
        var contentMap = new Dictionary<string, string>
        {
            ["src/auth.ts"] = "export class Auth { login() {} }",
            ["src/utils.ts"] = "export function log(msg: string) { console.log(msg); }",
        };

        var payload = generator.Generate(manifest, path => contentMap.GetValueOrDefault(path, ""));

        Assert.NotEmpty(payload.Items);
        Assert.True(payload.TotalEstimatedTokens > 0);
        Assert.Equal(PayloadFormat.Markdown, payload.Format);
    }

    [Fact]
    public void PayloadGenerator_GenerateMarkdown_ShouldContainFileContents()
    {
        var generator = new PayloadGenerator();
        var manifest = CreateManifest();
        var contentMap = new Dictionary<string, string>
        {
            ["src/auth.ts"] = "export class Auth { login() {} }",
            ["src/utils.ts"] = "export function log() {}",
        };

        var markdown = generator.GenerateMarkdown(manifest, path => contentMap.GetValueOrDefault(path, ""));

        Assert.Contains("Context Package: ctx_test001", markdown);
        Assert.Contains("src/auth.ts", markdown);
        Assert.Contains("export class Auth", markdown);
        Assert.Contains("Excluded", markdown);
        Assert.Contains("README.md", markdown);
    }

    [Fact]
    public void PayloadGenerator_GenerateMarkdown_ShouldHandleEmptyContent()
    {
        var generator = new PayloadGenerator();
        var manifest = CreateManifest();

        var markdown = generator.GenerateMarkdown(manifest, path => "");

        Assert.Contains("Context Package", markdown);
        // Empty content files should be skipped
        Assert.DoesNotContain("export class", markdown);
    }

    [Fact]
    public async Task FileExporter_ExportAsync_ShouldCreateFiles()
    {
        using var tempDir = new TempDir();
        var appData = new Storage.AppDataDirectory(tempDir.Path);
        var exporter = new FileExporter(appData);
        var manifest = CreateManifest();
        var contentMap = new Dictionary<string, string>
        {
            ["src/auth.ts"] = "export class Auth { }",
            ["src/utils.ts"] = "export function log() { }",
        };

        var exportDir = await exporter.ExportAsync(manifest, path => contentMap.GetValueOrDefault(path, ""));

        Assert.True(Directory.Exists(exportDir));
        Assert.True(File.Exists(Path.Combine(exportDir, "workspace.json")));
        Assert.True(File.Exists(Path.Combine(exportDir, "latest-context.manifest.json")));
        Assert.True(File.Exists(Path.Combine(exportDir, "latest-context.md")));
        Assert.True(File.Exists(Path.Combine(exportDir, "repomap.md")));
    }

    [Fact]
    public async Task FileExporter_ExportAsync_ManifestShouldBeReadable()
    {
        using var tempDir = new TempDir();
        var appData = new Storage.AppDataDirectory(tempDir.Path);
        var exporter = new FileExporter(appData);
        var manifest = CreateManifest();

        await exporter.ExportAsync(manifest, path => "content", manifest.WorkspaceId.Value);

        var readBack = exporter.ReadLatestManifest(manifest.WorkspaceId.Value);

        Assert.NotNull(readBack);
        Assert.Equal(manifest.Id.Value, readBack!.Id.Value);
        Assert.Equal(manifest.Task.OriginalText, readBack.Task.OriginalText);
    }

    [Fact]
    public async Task FileExporter_ExportToRepositoryAsync_ShouldCreateAikvDir()
    {
        using var tempDir = new TempDir();
        using var repoDir = new TempDir();
        var appData = new Storage.AppDataDirectory(tempDir.Path);
        var exporter = new FileExporter(appData);
        var manifest = CreateManifest();

        var aikvDir = await exporter.ExportToRepositoryAsync(repoDir.Path, manifest, path => "content");

        Assert.True(Directory.Exists(aikvDir));
        Assert.True(File.Exists(Path.Combine(aikvDir, "workspace.json")));
        Assert.True(File.Exists(Path.Combine(aikvDir, "latest-context.md")));

        // .gitignore should contain .aikv/
        var gitignore = File.ReadAllText(Path.Combine(repoDir.Path, ".gitignore"));
        Assert.Contains(".aikv/", gitignore);
    }

    [Fact]
    public async Task FileExporter_ExportToRepositoryAsync_ShouldNotDuplicateGitignoreEntry()
    {
        using var tempDir = new TempDir();
        using var repoDir = new TempDir();
        await File.WriteAllTextAsync(Path.Combine(repoDir.Path, ".gitignore"), ".aikv/\n");

        var appData = new Storage.AppDataDirectory(tempDir.Path);
        var exporter = new FileExporter(appData);
        var manifest = CreateManifest();

        await exporter.ExportToRepositoryAsync(repoDir.Path, manifest, path => "content");

        var gitignore = await File.ReadAllTextAsync(Path.Combine(repoDir.Path, ".gitignore"));
        var count = gitignore.Split(".aikv/").Length - 1;
        Assert.Equal(1, count); // Only one occurrence
    }

    [Fact]
    public void FileExporter_ReadLatestManifest_ShouldReturnNullForNonExistent()
    {
        using var tempDir = new TempDir();
        var appData = new Storage.AppDataDirectory(tempDir.Path);
        var exporter = new FileExporter(appData);

        var result = exporter.ReadLatestManifest("nonexistent-ws");

        Assert.Null(result);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; }
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aikv_export_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }
}
