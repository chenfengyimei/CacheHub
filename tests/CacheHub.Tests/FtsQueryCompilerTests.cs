using CacheHub.Storage.Search;
using Xunit;

namespace CacheHub.Tests;

public class FtsQueryCompilerTests
{
    [Fact]
    public void Compile_SingleWord_ProducesQuotedPrefix()
    {
        var result = FtsQueryCompiler.Compile("login");
        Assert.Equal("\"login\"*", result);
    }

    [Fact]
    public void Compile_MultipleWords_ProducesAndQuery()
    {
        var result = FtsQueryCompiler.Compile("fix login bug");
        Assert.Equal("\"fix\"* \"login\"* \"bug\"*", result);
    }

    [Fact]
    public void Compile_EscapesDoubleQuotes()
    {
        var result = FtsQueryCompiler.Compile("test\"quote");
        Assert.Contains("\"test\"\"quote\"", result);
    }

    [Fact]
    public void Compile_EmptyQuery_ReturnsEmptyString()
    {
        var result = FtsQueryCompiler.Compile("");
        Assert.Equal("\"\"", result);
    }

    [Fact]
    public void Compile_PrefixMatchFalse_NoAsterisk()
    {
        var result = FtsQueryCompiler.Compile("login", prefixMatch: false);
        Assert.Equal("\"login\"", result);
    }

    [Fact]
    public void CompileOr_MultipleKeywords_ProducesOrQuery()
    {
        var result = FtsQueryCompiler.CompileOr(["auth", "token", "refresh"]);
        Assert.Contains("OR", result);
        Assert.Contains("\"auth\"*", result);
        Assert.Contains("\"token\"*", result);
        Assert.Contains("\"refresh\"*", result);
    }

    [Fact]
    public void Compile_SpecialCharacters_EscapedByQuotes()
    {
        // Special chars are inside quoted strings, so FTS5 treats them literally
        var result = FtsQueryCompiler.Compile("test(parens)");
        Assert.Contains("\"test(parens)\"", result);
    }
}
