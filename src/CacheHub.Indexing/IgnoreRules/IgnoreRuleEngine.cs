using System.Security.Cryptography;
using System.Text;

namespace CacheHub.Indexing.IgnoreRules;

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
    CacheHubIgnore,
    User,
}

/// <summary>
/// Merges ignore rules from default, .gitignore, .cachehubignore, and user sources.
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
    /// Loads patterns from a .cachehubignore file.
    /// </summary>
    public IgnoreRuleEngine WithCacheHubIgnore(string? cachehubignorePath)
    {
        LoadFile(cachehubignorePath, IgnoreRuleSource.CacheHubIgnore);
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
    /// Rules are evaluated in order; last matching rule wins.
    /// Negation rules (starting with !) un-ignore a previously ignored path.
    /// </summary>
    public bool IsIgnored(string path)
    {
        var normalizedPath = path.Replace('\\', '/').TrimStart('/');

        var ignored = false;
        foreach (var rule in _rules)
        {
            var pattern = rule.Pattern;

            // Handle negation: !pattern un-ignores
            var negate = pattern.StartsWith('!');
            if (negate)
                pattern = pattern[1..];

            if (Matches(normalizedPath, pattern))
            {
                ignored = !negate;
            }
        }
        return ignored;
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
        pattern = pattern.Trim();

        // Root-anchored pattern (leading /): matches only from repo root
        var rootAnchored = pattern.StartsWith('/');
        if (rootAnchored)
            pattern = pattern[1..];

        // Directory-only pattern (trailing /)
        if (pattern.EndsWith('/'))
        {
            var dirPattern = pattern.TrimEnd('/');
            // **/dir matches "dir" at any depth
            if (dirPattern.StartsWith("**/"))
            {
                var target = dirPattern[3..];
                return path.Split('/').Any(seg => string.Equals(seg, target, StringComparison.OrdinalIgnoreCase));
            }
            // If the directory pattern contains wildcards, use glob matching
            if (dirPattern.Contains('*') || dirPattern.Contains('?'))
            {
                return GlobMatchRecursive(path, dirPattern, rootAnchored);
            }
            return MatchSegments(path, dirPattern, rootAnchored);
        }

        // Pattern contains ** (any number of directories)
        if (pattern.Contains("**"))
        {
            return GlobMatchRecursive(path, pattern, rootAnchored);
        }

        // Pattern contains wildcard
        if (pattern.Contains('*') || pattern.Contains('?'))
        {
            if (rootAnchored)
                return GlobMatch(path, pattern);
            return GlobMatch(path, pattern) || GlobMatch(GetFileName(path), pattern);
        }

        // Exact pattern with no wildcard: match against each path segment
        return MatchSegments(path, pattern, rootAnchored);
    }

    private static bool MatchSegments(string path, string pattern, bool rootAnchored)
    {
        if (rootAnchored)
            return string.Equals(path, pattern, StringComparison.OrdinalIgnoreCase)
                   || path.StartsWith(pattern + "/", StringComparison.OrdinalIgnoreCase);

        // Direct match or if pattern matches any file in path
        if (string.Equals(GetFileName(path), pattern, StringComparison.OrdinalIgnoreCase))
            return true;

        return path.Split('/').Any(seg => string.Equals(seg, pattern, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Recursive glob matching supporting ** (any directory depth).
    /// </summary>
    private static bool GlobMatchRecursive(string path, string pattern, bool rootAnchored)
    {
        // Split pattern into segments; convert ** to a regex that matches any directory depth
        var regexPattern = "^" + BuildRegexFromPattern(pattern, recursive: true) + "$";

        return System.Text.RegularExpressions.Regex.IsMatch(path, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase)
               || (!rootAnchored && System.Text.RegularExpressions.Regex.IsMatch(GetFileName(path), "^" + BuildRegexFromPattern(pattern, recursive: true) + "$", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    }

    private static string BuildRegexFromPattern(string pattern, bool recursive)
    {
        var sb = new StringBuilder();
        var i = 0;
        while (i < pattern.Length)
        {
            var c = pattern[i];

            if (c == '*')
            {
                // Check for ** (recursive)
                if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                {
                    // **/ matches any number of directories (including zero)
                    if (i + 2 < pattern.Length && pattern[i + 2] == '/')
                    {
                        sb.Append("(?:.*/)?");
                        i += 3;
                    }
                    else
                    {
                        sb.Append(".*");
                        i += 2;
                    }
                    continue;
                }
                sb.Append("[^/]*");
            }
            else if (c == '?')
            {
                sb.Append("[^/]");
            }
            else
            {
                sb.Append(System.Text.RegularExpressions.Regex.Escape(c.ToString()));
            }
            i++;
        }
        return sb.ToString();
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
