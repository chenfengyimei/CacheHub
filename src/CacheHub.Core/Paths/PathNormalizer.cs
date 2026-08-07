using System.Security.Cryptography;

namespace CacheHub.Core.Paths;

/// <summary>
/// Normalizes and validates file system paths.
/// Handles separators, case, UNC, symlinks, and path traversal detection.
/// </summary>
public sealed class PathNormalizer
{
    private static readonly char[] _separators = ['\\', '/'];

    /// <summary>
    /// Normalizes a path to its canonical form.
    /// </summary>
    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        return fullPath.Replace('\\', '/');
    }

    /// <summary>
    /// Checks if a child path is inside the parent directory.
    /// Prevents path traversal attacks.
    /// </summary>
    public static bool IsWithinRoot(string rootPath, string childPath)
    {
        var normalizedRoot = Normalize(rootPath).TrimEnd('/');
        var normalizedChild = Normalize(childPath);

        return normalizedChild.StartsWith(normalizedRoot + "/", PathComparer.PhysicalPathComparison)
               || string.Equals(normalizedChild, normalizedRoot, PathComparer.PhysicalPathComparison);
    }

    /// <summary>
    /// Detects path traversal patterns (.., %2e%2e, etc).
    /// </summary>
    public static bool ContainsTraversal(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        var decoded = path.Replace("%2e", ".", StringComparison.OrdinalIgnoreCase)
                          .Replace("%2E", ".", StringComparison.OrdinalIgnoreCase)
                          .Replace("%2f", "/", StringComparison.OrdinalIgnoreCase)
                          .Replace("%2F", "/", StringComparison.OrdinalIgnoreCase);

        var segments = decoded.Split(_separators, StringSplitOptions.None);
        return segments.Any(s => s == "..");
    }

    /// <summary>
    /// Computes a SHA-256 hash of a path for fingerprinting.
    /// </summary>
    public static string ComputePathHash(string normalizedPath)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(normalizedPath.ToLowerInvariant());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Gets the relative path from root to target, using forward slashes.
    /// </summary>
    public static string GetRelativePath(string rootPath, string targetPath)
    {
        var normalizedRoot = Normalize(rootPath).TrimEnd('/');
        var normalizedTarget = Normalize(targetPath);

        if (!IsWithinRoot(rootPath, targetPath))
        {
            throw new ArgumentException("Target path is not within root path.");
        }

        var relative = normalizedTarget[normalizedRoot.Length..].TrimStart('/');
        return relative;
    }
}
