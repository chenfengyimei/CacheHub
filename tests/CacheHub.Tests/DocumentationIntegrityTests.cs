using Xunit;

namespace CacheHub.Tests;

/// <summary>
/// V6: Documentation integrity regression tests.
/// Locks down the encoding + drift issues the reviewer flagged repeatedly:
///  - No U+FFFD replacement chars in docs (encoding corruption from sync-test-count.ps1)
///  - README/AGENTS .NET version references stay in sync with global.json
///  - sync-test-count.ps1 must be UTF-8 with BOM so Windows PowerShell 5.x can parse Chinese
/// </summary>
public class DocumentationIntegrityTests
{
    private static string GetRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            if (File.Exists(Path.Combine(dir, "CacheHub.sln")))
                return dir;
            dir = Path.GetDirectoryName(dir) ?? "";
            if (string.IsNullOrEmpty(dir)) break;
        }
        throw new InvalidOperationException("Could not locate repo root");
    }

    private static readonly string[] DocFiles =
    [
        "README.md",
        "AGENTS.md",
        "CONTRIBUTING.md",
        "Docs/ARCHITECTURE.md",
        "CHANGELOG.md",
        "SECURITY.md",
        "CODE_OF_CONDUCT.md",
        "THIRD_PARTY_NOTICES.md",
        "CODELY.md",
    ];

    public static TheoryData<string> DocFileTheory()
    {
        var data = new TheoryData<string>();
        foreach (var f in DocFiles)
            data.Add(f);
        return data;
    }

    // Review #5/#27: no U+FFFD replacement characters in docs
    [Theory]
    [MemberData(nameof(DocFileTheory))]
    public void MarkdownDocs_ContainNoReplacementChars(string relative)
    {
        var full = Path.Combine(GetRepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full)) return; // file optional

        var content = File.ReadAllText(full);
        Assert.DoesNotContain('\uFFFD', content);
    }

    // Review #27: sync-test-count.ps1 must be UTF-8 with BOM so Windows PowerShell 5 can parse Chinese
    [Fact]
    public void SyncTestCount_IsUtf8WithBom()
    {
        var path = Path.Combine(GetRepoRoot(), "scripts", "sync-test-count.ps1");
        Assert.True(File.Exists(path), "sync-test-count.ps1 should exist");

        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length >= 3, "script too short");
        // UTF-8 BOM: EF BB BF
        Assert.True(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            "sync-test-count.ps1 must be saved as UTF-8 with BOM (Windows PowerShell 5 needs it for Chinese)");
    }

    // Review #28: README + AGENTS must reference the same .NET version as global.json
    [Fact]
    public void Docs_NetVersion_MatchesGlobalJson()
    {
        var root = GetRepoRoot();
        var globalJson = File.ReadAllText(Path.Combine(root, "global.json"));
        var majorVersion = ""; // e.g. "10"
        var marker = "\"version\": \"";
        var idx = globalJson.IndexOf(marker, StringComparison.Ordinal);
        if (idx >= 0)
        {
            var start = idx + marker.Length;
            var end = globalJson.IndexOf('"', start);
            var ver = globalJson[start..end];
            var dot = ver.IndexOf('.');
            if (dot > 0) majorVersion = ver[..dot];
        }
        Assert.NotEqual("", majorVersion);

        string readme = File.ReadAllText(Path.Combine(root, "README.md"));

        // README must not reference stale .NET 9
        Assert.DoesNotContain(".NET 9", readme);
        Assert.DoesNotContain("SDK 9.", readme);
        // README must reference the current major
        Assert.Contains($".NET {majorVersion}", readme);

        // Install scripts must also reference the current major (were drifting at ".NET 9")
        foreach (var script in new[] { "install.ps1", "install.sh" })
        {
            var sp = Path.Combine(root, script);
            if (File.Exists(sp))
            {
                var content = File.ReadAllText(sp);
                Assert.DoesNotContain(".NET 9", content);
                Assert.Contains($".NET {majorVersion}", content);
            }
        }
    }

    // Review #28: README "SDK | <version>" must match global.json's exact SDK version,
    // so sync-test-count.ps1's auto-sync is locked (no manual doc drift).
    [Fact]
    public void Readme_SdkVersion_MatchesGlobalJson()
    {
        var root = GetRepoRoot();

        var globalJson = File.ReadAllText(Path.Combine(root, "global.json"));
        var marker = "\"version\": \"";
        var idx = globalJson.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(idx >= 0, "global.json should contain sdk.version");
        var start = idx + marker.Length;
        var end = globalJson.IndexOf('"', start);
        var sdkVersion = globalJson[start..end];
        Assert.Matches(@"^\d+\.\d+\.\d+$", sdkVersion);

        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        // The tech-stack table row: "| SDK | 10.0.302 (global.json 锁定) |"
        Assert.Contains($"| SDK | {sdkVersion}", readme);
    }

    // Review #28: README "N 个迁移" must match the actual Migration*.cs file count,
    // so sync-test-count.ps1's migration auto-sync is locked (no manual doc drift).
    [Fact]
    public void Readme_MigrationCount_MatchesActualFiles()
    {
        var root = GetRepoRoot();

        var migrationsDir = Path.Combine(root, "src", "CacheHub.Storage", "Database", "Migrations");
        Assert.True(Directory.Exists(migrationsDir), "Migrations directory should exist");

        var actualCount = Directory.GetFiles(migrationsDir, "Migration*.cs").Length;
        Assert.True(actualCount > 0, "Should have at least one migration file");

        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        Assert.Contains($"{actualCount} 个迁移", readme);
    }
}
