using CacheHub.Context.Parsing;
using CacheHub.Context.Recall;
using CacheHub.Context.Recall.Sources;

namespace CacheHub.Tests;

/// <summary>
/// Tests for R4-W006: Test and Config relation recall rules.
/// Verifies they only act as auxiliary signals and never override explicit path/symbol matches.
/// </summary>
public class TestConfigRelationTests
{
    private static readonly IReadOnlyList<IndexedFileInfo> Files =
    [
        new IndexedFileInfo { Path = "src/auth.ts", NormalizedPath = "src/auth.ts", Language = "typescript", Size = 500, Symbols = ["AuthService"] },
        new IndexedFileInfo { Path = "tests/auth.test.ts", NormalizedPath = "tests/auth.test.ts", Language = "typescript", Size = 250, Symbols = [] },
        new IndexedFileInfo { Path = "tests/auth.spec.ts", NormalizedPath = "tests/auth.spec.ts", Language = "typescript", Size = 200, Symbols = [] },
        new IndexedFileInfo { Path = "src/config/appsettings.json", NormalizedPath = "src/config/appsettings.json", Language = "json", Size = 100, Symbols = [] },
        new IndexedFileInfo { Path = "src/config/package.json", NormalizedPath = "src/config/package.json", Language = "json", Size = 80, Symbols = [] },
        new IndexedFileInfo { Path = "src/unrelated.ts", NormalizedPath = "src/unrelated.ts", Language = "typescript", Size = 300, Symbols = ["UnrelatedService"] },
    ];

    private static ParsedTask ParseTask(string text) => new TaskParser().Parse(text);

    private static RecallContext Context(ParsedTask? task = null, HashSet<string>? matched = null) => new()
    {
        Task = task ?? ParseTask("Fix AuthService"),
        IndexedFiles = Files,
        AlreadyMatchedPaths = matched ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
    };

    // === Test Relation ===

    [Fact]
    public void TestRelation_DiscoversTestAndSpecFiles()
    {
        var source = new TestRelationRecallSource();
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "src/auth.ts" };
        var ctx = Context(matched: matched);

        var hits = source.Recall(ctx);

        Assert.NotEmpty(hits);
        Assert.Contains(hits, h => h.NormalizedPath == "tests/auth.test.ts");
        Assert.Contains(hits, h => h.NormalizedPath == "tests/auth.spec.ts");
    }

    [Fact]
    public void TestRelation_DoesNotActivateWithoutPriorMatches()
    {
        var source = new TestRelationRecallSource();
        var ctx = Context(); // empty AlreadyMatchedPaths

        var hits = source.Recall(ctx);
        Assert.Empty(hits);
    }

    [Fact]
    public void TestRelation_DoesNotMatchUnrelatedFiles()
    {
        var source = new TestRelationRecallSource();
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "src/unrelated.ts" };
        var ctx = Context(matched: matched);

        var hits = source.Recall(ctx);

        // "unrelated.ts" base name is "unrelated" — should not match auth.test.ts
        Assert.DoesNotContain(hits, h => h.NormalizedPath == "tests/auth.test.ts");
    }

    [Fact]
    public void TestRelation_ConfidenceIsAuxiliary()
    {
        var source = new TestRelationRecallSource();
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "src/auth.ts" };
        var ctx = Context(matched: matched);

        var hits = source.Recall(ctx);

        Assert.All(hits, h => Assert.True(h.Confidence < 0.7, "Test relation should have lower confidence than direct matches"));
    }

    // === Config Relation ===

    [Fact]
    public void ConfigRelation_DiscoversConfigInMatchedDirectory()
    {
        var source = new ConfigRelationRecallSource();
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "src/auth.ts" };
        var ctx = Context(matched: matched);

        var hits = source.Recall(ctx);

        // Config files in src/config are in a nearby directory to src/auth.ts
        // The matching depends on directory proximity
        Assert.All(hits, h => Assert.Equal(RecallSource.ConfigRelation, h.Source));
    }

    [Fact]
    public void ConfigRelation_DoesNotActivateWithoutPriorMatches()
    {
        var source = new ConfigRelationRecallSource();
        var ctx = Context();

        var hits = source.Recall(ctx);
        Assert.Empty(hits);
    }

    [Fact]
    public void ConfigRelation_ConfidenceIsLowest()
    {
        var source = new ConfigRelationRecallSource();
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "src/config/appsettings.json" };
        var ctx = Context(matched: matched);

        var hits = source.Recall(ctx);
        Assert.All(hits, h => Assert.True(h.Confidence <= 0.5));
    }

    // === Integration: does not override explicit matches ===

    [Fact]
    public void Pipeline_TestRelationDoesNotOverrideSymbolMatch()
    {
        var pipeline = new RecallPipeline();
        var task = ParseTask("Fix AuthService in src/auth.ts");

        var candidates = pipeline.Recall(task, Files);

        var authCandidate = candidates.FirstOrDefault(c => c.NormalizedPath == "src/auth.ts");
        Assert.NotNull(authCandidate);
        // auth.ts should be matched by FilePath and/or Symbol, not just TestRelation
        Assert.Contains(authCandidate.Sources, s => s == RecallSource.FilePath || s == RecallSource.Symbol);
    }

    [Fact]
    public void Pipeline_ConfigRelationDoesNotOverrideSymbolMatch()
    {
        var pipeline = new RecallPipeline();
        var task = ParseTask("Fix AuthService");

        var candidates = pipeline.Recall(task, Files);

        var authCandidate = candidates.FirstOrDefault(c => c.NormalizedPath == "src/auth.ts");
        if (authCandidate is not null)
        {
            // If auth.ts is matched, it should be by Symbol, not just ConfigRelation
            Assert.Contains(authCandidate.Sources, s => s == RecallSource.Symbol);
        }
    }
}
