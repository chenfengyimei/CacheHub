using System.Text.RegularExpressions;

namespace CacheHub.Core.Logging;

/// <summary>
/// Redacts sensitive information from log messages.
/// Replaces credentials, tokens, secrets, and optionally path components.
/// </summary>
public static partial class LogRedactor
{
    public static string Redact(string message, bool redactPaths = false)
    {
        if (string.IsNullOrEmpty(message)) return message;

        var result = message;

        // Redact Authorization headers
        result = AuthorizationRegex().Replace(result, "Authorization: [REDACTED]");

        // Redact API keys and tokens
        result = ApiKeyRegex().Replace(result, "$1[REDACTED]");
        result = BearerTokenRegex().Replace(result, "Bearer [REDACTED]");

        // Redact passwords in connection strings
        result = PasswordRegex().Replace(result, "password=[REDACTED]");

        // Redact file paths if requested
        if (redactPaths)
        {
            result = WindowsPathRegex().Replace(result, "[PATH]");
            result = UnixPathRegex().Replace(result, "[PATH]");
        }

        return result;
    }

    public static string RedactPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        return Path.GetFileName(path) is { } name and not ""
            ? $".../{name}"
            : "[PATH]";
    }

    [GeneratedRegex(@"Authorization:\s*\S+", RegexOptions.IgnoreCase)]
    private static partial Regex AuthorizationRegex();

    [GeneratedRegex(@"(api[_-]?key\s*[:=]\s*)\S+", RegexOptions.IgnoreCase)]
    private static partial Regex ApiKeyRegex();

    [GeneratedRegex(@"Bearer\s+\S+", RegexOptions.IgnoreCase)]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex(@"password\s*=\s*\S+", RegexOptions.IgnoreCase)]
    private static partial Regex PasswordRegex();

    [GeneratedRegex(@"[A-Za-z]:[\\/][^\s]+")]
    private static partial Regex WindowsPathRegex();

    [GeneratedRegex(@"/[^\s]+")]
    private static partial Regex UnixPathRegex();
}
