using System.Runtime.InteropServices;

namespace CacheHub.Core.Paths;

/// <summary>
/// Resolves relative paths within a workspace root, enforcing security boundaries.
/// Prevents path traversal, symlink escape, and arbitrary absolute path access.
/// V8-P0-03: Enhanced with parent-directory symlink/junction traversal detection.
/// </summary>
public sealed class SafePathResolver
{
    private readonly string _rootPath;
    private readonly StringComparison _pathComparison;

    /// <summary>
    /// Creates a resolver scoped to the given workspace root.
    /// </summary>
    public SafePathResolver(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        _rootPath = Path.GetFullPath(rootPath);
        _pathComparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    /// <summary>
    /// The normalized root path.
    /// </summary>
    public string RootPath => _rootPath;

    /// <summary>
    /// Resolves a relative/virtual path to a full physical path within the workspace root.
    /// Returns null if the path escapes the root, contains traversal, or points to a symlink target outside root.
    /// V8-P0-03: Also checks every parent directory component for symlinks/junctions.
    /// </summary>
    public string? Resolve(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        // Reject absolute paths — only workspace-relative paths are allowed
        if (Path.IsPathRooted(relativePath))
            return null;

        // Reject path traversal patterns (including URL-encoded)
        if (PathNormalizer.ContainsTraversal(relativePath))
            return null;

        // Normalize the relative path
        var cleaned = relativePath.Replace('/', Path.DirectorySeparatorChar)
                                  .Replace('\\', Path.DirectorySeparatorChar);

        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, cleaned));

        // Verify the resolved path is within the root (using separator-aware comparison)
        if (!IsWithinRoot(fullPath))
            return null;

        // V8-P0-03: Check every parent directory component for symlinks/junctions.
        // This catches cases where an intermediate directory is a symlink pointing outside root,
        // even if the final file itself is not a symlink.
        if (!IsPathChainSafe(fullPath))
            return null;

        // Check the final path itself for symlinks
        if (IsSymlink(fullPath))
        {
            var resolvedTarget = ResolveSymlinkTarget(fullPath);
            if (resolvedTarget is null || !IsWithinRoot(resolvedTarget))
                return null;
        }

        return fullPath;
    }

    /// <summary>
    /// Resolves and verifies the file exists.
    /// </summary>
    public string? ResolveFile(string? relativePath)
    {
        var resolved = Resolve(relativePath);
        return resolved is not null && File.Exists(resolved) ? resolved : null;
    }

    private bool IsWithinRoot(string fullPath)
    {
        var rootWithSep = _rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? _rootPath
            : _rootPath + Path.DirectorySeparatorChar;

        return fullPath.StartsWith(_rootPath + Path.DirectorySeparatorChar, _pathComparison)
               || string.Equals(fullPath, _rootPath, _pathComparison);
    }

    /// <summary>
    /// V8-P0-03: Walks each directory component from root to the target path,
    /// checking that no intermediate directory is a symlink/junction escaping root.
    /// </summary>
    private bool IsPathChainSafe(string fullPath)
    {
        var relativeToRoot = Path.GetRelativePath(_rootPath, fullPath);
        if (relativeToRoot == "." || relativeToRoot == ".")
            return true;

        var currentPath = _rootPath;
        var segments = relativeToRoot.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            // Skip ".." segments — already rejected by ContainsTraversal, but defensive
            if (segment == "..")
                return false;

            currentPath = Path.Combine(currentPath, segment);

            // Check if this intermediate component is a symlink/junction
            if (IsSymlink(currentPath))
            {
                var resolvedTarget = ResolveSymlinkTarget(currentPath);
                if (resolvedTarget is null || !IsWithinRoot(resolvedTarget))
                    return false;
            }
        }

        return true;
    }

    private static bool IsSymlink(string path)
    {
        try
        {
            // On Windows, File.GetAttributes returns ReparsePoint for symlinks.
            // On Unix, File.GetAttributes returns the TARGET's attributes, not the link's,
            // so ReparsePoint is never set. Use FileInfo.LinkTarget for cross-platform detection.
            var info = new FileInfo(path);
            return info.LinkTarget is not null;
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolveSymlinkTarget(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.LinkTarget is not null)
            {
                var target = info.LinkTarget;
                if (!Path.IsPathRooted(target))
                    target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, target));
                return Path.GetFullPath(target);
            }
            return Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }
}
