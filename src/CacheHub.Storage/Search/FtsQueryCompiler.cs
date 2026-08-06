namespace CacheHub.Storage.Search;

/// <summary>
/// Compiles a natural language query into a safe FTS5 MATCH expression.
/// Escapes special characters, supports prefix matching, and tokenizes multi-word queries.
/// </summary>
public static class FtsQueryCompiler
{
    /// <summary>
    /// FTS5 special characters that must be escaped in tokens.
    /// </summary>
    private static readonly char[] _specialChars = ['"', '*', '(', ')', '+', '-', ':', '^', '{', '}'];

    /// <summary>
    /// Compiles a query string into a safe FTS5 MATCH expression.
    /// Each token becomes a prefix match (appended with *) for better recall.
    /// Multi-word phrases are quoted.
    /// </summary>
    public static string Compile(string query, bool prefixMatch = true)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "\"\"";

        // Split into tokens
        var tokens = query.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

        var compiledTokens = new List<string>();
        foreach (var token in tokens)
        {
            // Escape double quotes by doubling them, then wrap in quotes
            var escaped = token.Replace("\"", "\"\"");
            var quoted = $"\"{escaped}\"";

            if (prefixMatch && !escaped.EndsWith('*'))
                compiledTokens.Add($"{quoted}*");
            else
                compiledTokens.Add(quoted);
        }

        // Join with implicit AND (FTS5 default)
        return string.Join(" ", compiledTokens);
    }

    /// <summary>
    /// Compiles a list of keywords into an OR query for broader recall.
    /// </summary>
    public static string CompileOr(IReadOnlyList<string> keywords, bool prefixMatch = true)
    {
        if (keywords.Count == 0)
            return "\"\"";

        var compiledTokens = keywords.Select(k => Compile(k, prefixMatch));
        return string.Join(" OR ", compiledTokens);
    }
}
