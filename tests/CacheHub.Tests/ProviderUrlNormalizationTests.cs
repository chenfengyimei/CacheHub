using CacheHub.Cli.Commands;
using Xunit;

namespace CacheHub.Tests;

/// <summary>
/// V7-W11: Provider URL normalization tests.
/// Verifies that /v1 suffix is stripped to prevent /v1/v1/chat/completions.
/// </summary>
public class ProviderUrlNormalizationTests
{
    [Theory]
    [InlineData("https://api.openai.com/v1", "https://api.openai.com")]
    [InlineData("https://api.openai.com/v1/", "https://api.openai.com")]
    [InlineData("https://api.openai.com", "https://api.openai.com")]
    [InlineData("https://api.openai.com/", "https://api.openai.com")]
    [InlineData("https://api.deepseek.com/v1", "https://api.deepseek.com")]
    [InlineData("https://api.deepseek.com/V1", "https://api.deepseek.com")]
    [InlineData("http://localhost:8080/v1", "http://localhost:8080")]
    [InlineData("https://api.openai.com/v1/v1", "https://api.openai.com/v1")]
    public void NormalizeProviderUrl_StripsTrailingV1(string input, string expected)
    {
        var result = InvokeNormalizeProviderUrl(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void NormalizeProviderUrl_PreservesNonV1Paths()
    {
        var result = InvokeNormalizeProviderUrl("https://api.openai.com/custom");
        Assert.Equal("https://api.openai.com/custom", result);
    }

    /// <summary>
    /// Uses reflection to call the private NormalizeProviderUrl method.
    /// </summary>
    private static string InvokeNormalizeProviderUrl(string url)
    {
        var method = typeof(GatewayCommands)
            .GetMethod("NormalizeProviderUrl", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        return (string)method!.Invoke(null, [url])!;
    }
}
