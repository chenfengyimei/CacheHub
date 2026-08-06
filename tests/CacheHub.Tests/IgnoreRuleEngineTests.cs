using CacheHub.Indexing.IgnoreRules;

namespace CacheHub.Tests;

public class IgnoreRuleEngineTests
{
    [Fact]
    public void WithDefaults_ShouldAddDefaultPatterns()
    {
        var engine = new IgnoreRuleEngine().WithDefaults();

        Assert.NotEmpty(engine.Rules);
        Assert.Contains(engine.Rules, r => r.Pattern == ".git");
        Assert.Contains(engine.Rules, r => r.Pattern == "node_modules");
        Assert.Contains(engine.Rules, r => r.Source == IgnoreRuleSource.Default);
    }

    [Fact]
    public void IsIgnored_ShouldMatchDefaultPatterns()
    {
        var engine = new IgnoreRuleEngine().WithDefaults();

        Assert.True(engine.IsIgnored("src/.git/config"));
        Assert.True(engine.IsIgnored("project/node_modules/express"));
        Assert.True(engine.IsIgnored("app/bin/debug"));
        Assert.True(engine.IsIgnored("app/obj/release"));
    }

    [Fact]
    public void IsIgnored_ShouldNotMatchNormalPaths()
    {
        var engine = new IgnoreRuleEngine().WithDefaults();

        Assert.False(engine.IsIgnored("src/main.ts"));
        Assert.False(engine.IsIgnored("app/Services/UserService.cs"));
    }

    [Fact]
    public void WithUserRules_ShouldAddCustomPatterns()
    {
        var engine = new IgnoreRuleEngine()
            .WithDefaults()
            .WithUserRules(["*.tmp", "secrets/"]);

        Assert.True(engine.IsIgnored("cache.tmp"));
        Assert.True(engine.IsIgnored("config/secrets/key.pem"));
        Assert.False(engine.IsIgnored("src/app.ts"));
    }

    [Fact]
    public void WithUserRules_ShouldSkipCommentsAndEmpty()
    {
        var engine = new IgnoreRuleEngine()
            .WithUserRules(["# comment", "", "  ", "*.log"]);

        Assert.Single(engine.Rules);
        Assert.True(engine.IsIgnored("app.log"));
    }

    [Fact]
    public void WithGitIgnore_ShouldLoadFromFile()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(tmpFile, ["dist/", "# comment", "*.env", ""]);

            var engine = new IgnoreRuleEngine().WithGitIgnore(tmpFile);

            Assert.Equal(2, engine.Rules.Count);
            Assert.True(engine.IsIgnored("build/dist/app.js"));
            Assert.True(engine.IsIgnored("config.env"));
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [Fact]
    public void WithGitIgnore_ShouldHandleMissingFile()
    {
        var engine = new IgnoreRuleEngine().WithGitIgnore(null);

        Assert.Empty(engine.Rules);

        engine = new IgnoreRuleEngine().WithGitIgnore(@"C:\nonexistent\.gitignore");
        Assert.Empty(engine.Rules);
    }

    [Fact]
    public void GetRulesHash_ShouldBeDeterministic()
    {
        var engine1 = new IgnoreRuleEngine().WithDefaults().WithUserRules(["*.tmp"]);
        var engine2 = new IgnoreRuleEngine().WithDefaults().WithUserRules(["*.tmp"]);

        Assert.Equal(engine1.GetRulesHash(), engine2.GetRulesHash());
    }

    [Fact]
    public void GetRulesHash_ShouldDifferForDifferentRules()
    {
        var engine1 = new IgnoreRuleEngine().WithDefaults().WithUserRules(["*.tmp"]);
        var engine2 = new IgnoreRuleEngine().WithDefaults().WithUserRules(["*.log"]);

        Assert.NotEqual(engine1.GetRulesHash(), engine2.GetRulesHash());
    }

    [Fact]
    public void IsIgnored_ShouldMatchGlobPatterns()
    {
        var engine = new IgnoreRuleEngine().WithUserRules(["*.test.ts"]);

        Assert.True(engine.IsIgnored("src/app.test.ts"));
        Assert.False(engine.IsIgnored("src/app.ts"));
    }

    [Fact]
    public void IsIgnored_ShouldMatchDirectoryPatterns()
    {
        var engine = new IgnoreRuleEngine().WithUserRules(["coverage/"]);

        Assert.True(engine.IsIgnored("coverage/lcov.info"));
        Assert.False(engine.IsIgnored("src/app.ts"));
    }

    [Fact]
    public void IsIgnored_NegationRule_UnIgnores()
    {
        var engine = new IgnoreRuleEngine().WithUserRules(["*.log", "!important.log"]);

        Assert.True(engine.IsIgnored("debug.log"));
        Assert.False(engine.IsIgnored("important.log"));
    }

    [Fact]
    public void IsIgnored_DoubleStar_MatchesAnyDepth()
    {
        var engine = new IgnoreRuleEngine().WithUserRules(["**/temp/"]);

        Assert.True(engine.IsIgnored("temp/file.txt"));
        Assert.True(engine.IsIgnored("src/temp/file.txt"));
        Assert.True(engine.IsIgnored("a/b/c/temp/file.txt"));
        Assert.False(engine.IsIgnored("src/app.ts"));
    }

    [Fact]
    public void IsIgnored_RootAnchored_OnlyMatchesFromRoot()
    {
        var engine = new IgnoreRuleEngine().WithUserRules(["/build"]);

        Assert.True(engine.IsIgnored("build/output.txt"));
        Assert.False(engine.IsIgnored("src/build/output.txt"));
    }

    [Fact]
    public void IsIgnored_LastRuleWins()
    {
        // If *.ts is ignored but app.ts is negated, app.ts should not be ignored
        var engine = new IgnoreRuleEngine().WithUserRules(["*.ts", "!app.ts"]);

        Assert.False(engine.IsIgnored("app.ts"));
        Assert.True(engine.IsIgnored("other.ts"));
    }

    [Fact]
    public void IsIgnored_DoubleStarInName()
    {
        var engine = new IgnoreRuleEngine().WithUserRules(["src/**/test/*.cs"]);

        Assert.True(engine.IsIgnored("src/test/UnitTest.cs"));
        Assert.True(engine.IsIgnored("src/a/test/UnitTest.cs"));
        Assert.True(engine.IsIgnored("src/a/b/c/test/UnitTest.cs"));
        Assert.False(engine.IsIgnored("src/a/b/UnitTest.cs"));
    }
}
