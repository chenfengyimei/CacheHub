using CacheHub.Core.Tokens;

namespace CacheHub.Tests;

public class TokenizerTests
{
    [Fact]
    public void CharEstimateTokenizer_ShouldEstimateByChars()
    {
        var tokenizer = new CharEstimateTokenizer();
        var tokens = tokenizer.CountTokens("hello world"); // 11 chars / 4 ≈ 3

        Assert.True(tokens >= 2 && tokens <= 3);
    }

    [Fact]
    public void CharEstimateTokenizer_ShouldReturnZeroForEmpty()
    {
        var tokenizer = new CharEstimateTokenizer();

        Assert.Equal(0, tokenizer.CountTokens(""));
        Assert.Equal(0, tokenizer.CountTokens(null!));
    }

    [Fact]
    public void WordBoundaryTokenizer_ShouldCountWords()
    {
        var tokenizer = new WordBoundaryTokenizer();
        var tokens = tokenizer.CountTokens("hello world foo bar");

        Assert.Equal(4, tokens);
    }

    [Fact]
    public void WordBoundaryTokenizer_ShouldCountPunctuation()
    {
        var tokenizer = new WordBoundaryTokenizer();
        var tokens = tokenizer.CountTokens("x = 1;");

        // x, =, 1, ; → 4 tokens
        Assert.True(tokens >= 4);
    }

    [Fact]
    public void CodeTokenizer_ShouldHandleCodeBoundaries()
    {
        var tokenizer = new CodeTokenizer();
        var tokens = tokenizer.CountTokens("const x = () => { return 1; };");

        Assert.True(tokens > 5);
    }

    [Fact]
    public void CodeTokenizer_ShouldCountOperators()
    {
        var tokenizer = new CodeTokenizer();
        var tokens1 = tokenizer.CountTokens("a=>b");
        var tokens2 = tokenizer.CountTokens("a->b");
        var tokens3 = tokenizer.CountTokens("a==b");

        Assert.True(tokens1 > 0);
        Assert.True(tokens2 > 0);
        Assert.True(tokens3 > 0);
    }

    [Fact]
    public void TokenizerRegistry_ShouldFallbackToDefault()
    {
        var registry = new TokenizerRegistry();

        var tokenizer = registry.GetForModel("unknown-model");

        Assert.Equal("char-estimate", tokenizer.Id);
    }

    [Fact]
    public void TokenizerRegistry_ShouldMatchExactModelId()
    {
        var registry = new TokenizerRegistry();
        var codeTokenizer = new CodeTokenizer();
        registry.Register("gpt-4", codeTokenizer);

        var tokenizer = registry.GetForModel("gpt-4");

        Assert.Equal("code-tokenizer", tokenizer.Id);
    }

    [Fact]
    public void TokenizerRegistry_ShouldMatchPrefix()
    {
        var registry = new TokenizerRegistry();
        var wordTokenizer = new WordBoundaryTokenizer();
        registry.Register("claude", wordTokenizer);

        var tokenizer = registry.GetForModel("claude-3-opus");

        Assert.Equal("word-boundary", tokenizer.Id);
    }

    [Fact]
    public void TokenizerRegistry_CountTokens_ShouldUseMatchedTokenizer()
    {
        var registry = new TokenizerRegistry();
        registry.Register("gpt-4", new CodeTokenizer());

        var tokens = registry.CountTokens("gpt-4", "const x = 1;");

        Assert.True(tokens > 0);
    }

    [Fact]
    public void TokenizerRegistry_ShouldUseCustomDefault()
    {
        var wordTokenizer = new WordBoundaryTokenizer();
        var registry = new TokenizerRegistry(wordTokenizer);

        var tokenizer = registry.GetForModel("unknown");

        Assert.Equal("word-boundary", tokenizer.Id);
    }

    [Fact]
    public void CharEstimateTokenizer_CountTokensForMessages_ShouldSumAll()
    {
        var tokenizer = new CharEstimateTokenizer();
        var total = tokenizer.CountTokensForMessages(["hello", "world foo"]);

        Assert.True(total > 0);
    }

    [Fact]
    public void CodeTokenizer_ShouldReturnZeroForEmpty()
    {
        var tokenizer = new CodeTokenizer();

        Assert.Equal(0, tokenizer.CountTokens(""));
    }

    [Fact]
    public void TokenizerVersions_ShouldBeSet()
    {
        Assert.Equal("v1", new CharEstimateTokenizer().Version);
        Assert.Equal("v1", new WordBoundaryTokenizer().Version);
        Assert.Equal("v1", new CodeTokenizer().Version);
    }
}
