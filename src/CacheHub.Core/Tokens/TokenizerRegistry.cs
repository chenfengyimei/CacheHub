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
/// Real BPE tokenizer backed by Microsoft.ML.Tokenizers (cl100k_base / o200k_base).
/// Provides actual model token counts instead of the chars/4 or CodeTokenizer estimate.
/// </summary>
public sealed class BpeTokenizer : ITokenizer
{
    private readonly Microsoft.ML.Tokenizers.Tokenizer _tokenizer;
    private readonly string _bpeTag;

    /// <summary>Shared OpenAI cl100k_base tokenizer (GPT-3.5/GPT-4).</summary>
    private static readonly Lazy<Microsoft.ML.Tokenizers.Tokenizer> Cl100K = new(() =>
        Microsoft.ML.Tokenizers.TiktokenTokenizer.CreateForModel("gpt-4"));

    /// <summary>Shared OpenAI o200k_base tokenizer (GPT-4o/o1/o3).</summary>
    private static readonly Lazy<Microsoft.ML.Tokenizers.Tokenizer> O200K = new(() =>
        Microsoft.ML.Tokenizers.TiktokenTokenizer.CreateForModel("gpt-4o"));

    public BpeTokenizer(BpeModel model = BpeModel.Cl100k)
    {
        _bpeTag = model == BpeModel.O200k ? "o200k" : "cl100k";
        _tokenizer = model == BpeModel.O200k ? O200K.Value : Cl100K.Value;
    }

    public string Id => $"bpe-{_bpeTag}";
    public string Version => "1.0";

    public int CountTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return _tokenizer.CountTokens(text);
    }

    public int CountTokensForMessages(IReadOnlyList<string> messages)
    {
        if (messages.Count == 0) return 0;
        // Approximate message overhead: ~4 tokens per message boundary + 3 base
        var baseTokens = 3;
        var overhead = messages.Count * 4;
        return baseTokens + overhead + messages.Sum(CountTokens);
    }
}

/// <summary>BPE model variants supported by BpeTokenizer.</summary>
public enum BpeModel
{
    Cl100k,
    O200k,
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

        // OpenAI GPT-4/3.5 — cl100k_base BPE
        var cl100k = new BpeTokenizer(BpeModel.Cl100k);
        registry.Register("gpt-4", cl100k);
        registry.Register("gpt-3.5", cl100k);

        // OpenAI GPT-4o/o1/o3 — o200k_base BPE
        var o200k = new BpeTokenizer(BpeModel.O200k);
        registry.Register("gpt-4o", o200k);
        registry.Register("o1", o200k);
        registry.Register("o3", o200k);

        // Anthropic Claude — cl100k_base is a reasonable approximation
        registry.Register("claude", cl100k);

        // Google Gemini — cl100k_base approximation
        registry.Register("gemini", cl100k);

        // DeepSeek — cl100k_base approximation
        registry.Register("deepseek", cl100k);

        // Qwen — cl100k_base approximation
        registry.Register("qwen", cl100k);

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
