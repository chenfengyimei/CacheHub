using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CacheHub.Core.Tokens;

/// <summary>
/// Tokenizer contract: converts text to token count.
/// Supports pluggable implementations (BPE, word-level, char-based).
/// </summary>
public interface ITokenizer
{
    string Id { get; }
    string Version { get; }
    int CountTokens(string text);
    int CountTokensForMessages(IReadOnlyList<string> messages);
}

/// <summary>
/// Rough char-based tokenizer (chars / 4).
/// Used as fallback when no model-specific tokenizer is available.
/// </summary>
public sealed class CharEstimateTokenizer : ITokenizer
{
    public string Id => "char-estimate";
    public string Version => "v1";
    private const double CharsPerToken = 4;

    public int CountTokens(string text) =>
        string.IsNullOrEmpty(text) ? 0 : (int)Math.Ceiling(text.Length / CharsPerToken);

    public int CountTokensForMessages(IReadOnlyList<string> messages) =>
        messages.Sum(CountTokens);
}

/// <summary>
/// Word-boundary tokenizer: splits on whitespace and punctuation.
/// More accurate than char-estimate for English text.
/// </summary>
public sealed partial class WordBoundaryTokenizer : ITokenizer
{
    public string Id => "word-boundary";
    public string Version => "v1";

    public int CountTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var matches = TokenRegex().Matches(text);
        return matches.Count;
    }

    public int CountTokensForMessages(IReadOnlyList<string> messages) =>
        messages.Sum(CountTokens);

    [GeneratedRegex(@"\b\w+\b|[^\w\s]")]
    private static partial Regex TokenRegex();
}

/// <summary>
/// BPE-like tokenizer: splits on common code boundaries.
/// Better for source code than word-boundary.
/// </summary>
public sealed partial class CodeTokenizer : ITokenizer
{
    public string Id => "code-tokenizer";
    public string Version => "v1";

    public int CountTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var count = 0;
        var matches = CodeTokenRegex().Matches(text);
        count += matches.Count;

        // Count unmatched characters as individual tokens
        var matchedLength = matches.Sum(m => m.Length);
        var unmatched = text.Length - matchedLength;
        count += (int)Math.Ceiling(unmatched / 4.0);

        return count;
    }

    public int CountTokensForMessages(IReadOnlyList<string> messages) =>
        messages.Sum(CountTokens);

    [GeneratedRegex(@"\b\w+\b|::|=>|->|<=|>=|==|!=|\+\+|--|&&|\|\||[{}()\[\];,.<>+\-*/%&|^!?=:\""'#@]")]
    private static partial Regex CodeTokenRegex();
}

/// <summary>
/// Tokenizer registry: maps model IDs to tokenizers.
/// Falls back to CodeTokenizer when no match is found (better than chars/4 for code).
/// </summary>
public sealed class TokenizerRegistry
{
    private readonly Dictionary<string, ITokenizer> _modelMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly ITokenizer _default;

    public TokenizerRegistry(ITokenizer? defaultTokenizer = null)
    {
        // Default to CodeTokenizer — more accurate than chars/4 for source code
        _default = defaultTokenizer ?? new CodeTokenizer();
    }

    /// <summary>
    /// Creates a registry with common model patterns pre-registered.
    /// Uses CodeTokenizer for code-oriented models and WordBoundaryTokenizer for text models.
    /// </summary>
    public static TokenizerRegistry CreateWithDefaults()
    {
        var registry = new TokenizerRegistry();

        // OpenAI models — GPT-4/3.5 use cl100k_base BPE, ~3.5 chars/token for English code
        registry.Register("gpt-4", new CodeTokenizer());
        registry.Register("gpt-3.5", new CodeTokenizer());
        registry.Register("gpt-4o", new CodeTokenizer());
        registry.Register("o1", new CodeTokenizer());
        registry.Register("o3", new CodeTokenizer());

        // Anthropic Claude — ~3.5 chars/token for code
        registry.Register("claude", new CodeTokenizer());

        // Google Gemini — ~4 chars/token
        registry.Register("gemini", new CodeTokenizer());

        // DeepSeek — similar to GPT
        registry.Register("deepseek", new CodeTokenizer());

        // Qwen — Chinese-heavy, ~2.5 chars/token
        registry.Register("qwen", new CodeTokenizer());

        return registry;
    }

    /// <summary>
    /// Registers a tokenizer for a model ID pattern.
    /// </summary>
    public void Register(string modelPattern, ITokenizer tokenizer)
    {
        _modelMap[modelPattern] = tokenizer;
    }

    /// <summary>
    /// Gets the best matching tokenizer for a model ID.
    /// </summary>
    public ITokenizer GetForModel(string modelId)
    {
        // Exact match
        if (_modelMap.TryGetValue(modelId, out var exact))
            return exact;

        // Prefix match
        foreach (var (pattern, tokenizer) in _modelMap)
        {
            if (modelId.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                return tokenizer;
        }

        return _default;
    }

    /// <summary>
    /// Count tokens for a model.
    /// </summary>
    public int CountTokens(string modelId, string text) =>
        GetForModel(modelId).CountTokens(text);

    /// <summary>
    /// Default tokenizer.
    /// </summary>
    public ITokenizer Default => _default;
}
