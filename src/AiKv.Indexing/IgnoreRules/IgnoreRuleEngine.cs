using System.Security.Cryptography;
using System.Text;

namespace AiKv.Indexing.IgnoreRules;

/// <summary>
/// Default ignore patterns always applied.
/// </summary>
public static class DefaultIgnorePatterns
{
    public static readonly IReadOnlyList<string> Patterns =
    [
        ".git",
        "node_modules",
        "Library",
        "Temp",
        "Logs",
        "obj",
        "bin",
        "dist",
        "build",
        "target",
        ".venv",
        "DerivedData",
        "Pods",
        "vendor",
        "coverage",
    ];
}

/// <summary>
/// Represents a single ignore rule.
/// </summary>
public sealed record IgnoreRule(string Pattern, IgnoreRuleSource Source);

/// <summary>
/// Source of an ignore rule.
/// </summary>
public enum IgnoreRuleSource
{
    Default,
    GitIgnore,
    AiKvIgnore,
    User,
}

/// <summary>
/// Merges ignore rules from default, .gitignore, .aikvignore, and user sources.
/// Provides matching and hash computation for reproducibility.
/// </summary>
public sealed class IgnoreRuleEngine
{
    private readonly List<IgnoreRule> _rules = [];
    private string? _cachedHash;

    public IReadOnlyList<IgnoreRule> Rules => _rules;

    /// <summary>
    /// Loads default ignore patterns.
    /// </summary>
    public IgnoreRuleEngine WithDefaults()
    {
        foreach (var pattern in DefaultIgnorePatterns.Patterns)
            _rules.Add(new IgnoreRule(pattern, IgnoreRuleSource.Default));
        _cachedHash = null;
        return this;
    }

    /// <summary>
    /// Loads patterns from a .gitignore file.
    /// </summary>
    public IgnoreRuleEngine WithGitIgnore(string? gitignorePath)
    {
        LoadFile(gitignorePath, IgnoreRuleSource.GitIgnore);
        return this;
    }

    /// <summary>
    /// Loads patterns from a .aikvignore file.
    /// </summary>
    public IgnoreRuleEngine WithAiKvIgnore(string? aikvignorePath)
    {
        LoadFile(aikvignorePath, IgnoreRuleSource.AiKvIgnore);
        return this;
    }

    /// <summary>
    /// Adds user-specified patterns.
    /// </summary>
    public IgnoreRuleEngine WithUserRules(IEnumerable<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            var trimmed = pattern.Trim();
            if (trimmed.Length > 0 && !trimmed.StartsWith('#'))
                _rules.Add(new IgnoreRule(trimmed, IgnoreRuleSource.User));
        }
        _cachedHash = null;
        return this;
    }

    /// <summary>
    /// Checks if a path should be ignored.
    /// </summary>
    public bool IsIgnored(string path)
    {
        var normalizedPath = path.Replace('\\', '/');

        foreach (var rule in _rules)
        {
            if (Matches(normalizedPath, rule.Pattern))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Computes a SHA-256 hash of all rules for reproducibility.
    /// </summary>
    public string GetRulesHash()
    {
        if (_cachedHash is not null) return _cachedHash;

        var sb = new StringBuilder();
        foreach (var rule in _rules.OrderBy(r => r.Pattern, StringComparer.Ordinal))
        {
            sb.Append(rule.Pattern).Append('|').Append(rule.Source).Append('\n');
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var hash = SHA256.HashData(bytes);
        _cachedHash = Convert.ToHexString(hash).ToLowerInvariant();
        return _cachedHash;
    }

    private void LoadFile(string? path, IgnoreRuleSource source)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0 && !trimmed.StartsWith('#'))
                _rules.Add(new IgnoreRule(trimmed, source));
        }
        _cachedHash = null;
    }

    private static bool Matches(string path, string pattern)
    {
        // Simple glob matching: *, directory names, and exact patterns.
        if (pattern.EndsWith('/'))
        {
            var dirName = pattern.TrimEnd('/');
            return path.Split('/').Any(seg => string.Equals(seg, dirName, StringComparison.OrdinalIgnoreCase));
        }

        if (pattern.Contains('*'))
        {
            // Check against full path and against filename only.
            return GlobMatch(path, pattern) || GlobMatch(GetFileName(path), pattern);
        }

        return path.Split('/').Any(seg => string.Equals(seg, pattern, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetFileName(string path)
    {
        var idx = path.LastIndexOf('/');
        return idx >= 0 ? path[(idx + 1)..] : path;
    }

    private static bool GlobMatch(string path, string pattern)
    {
        // Convert glob to regex: * → [^/]*, ? → [^/]
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", "[^/]*")
            .Replace("\\?", "[^/]") + "$";

        return System.Text.RegularExpressions.Regex.IsMatch(path, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
