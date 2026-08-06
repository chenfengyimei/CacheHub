using System.Runtime.InteropServices;

namespace CacheHub.Core.Paths;

/// <summary>
/// Represents a physical file system path (absolute, on disk).
/// Comparison semantics follow the OS: Windows uses OrdinalIgnoreCase,
/// Linux/macOS use Ordinal (case-sensitive).
/// </summary>
public readonly record struct PhysicalPath
{
    /// <summary>
    /// The normalized full path on disk.
    /// </summary>
    public string Value { get; init; }

    public PhysicalPath(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = System.IO.Path.GetFullPath(value);
    }

    public static PhysicalPath FromRooted(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        if (!System.IO.Path.IsPathRooted(fullPath))
            throw new ArgumentException($"PhysicalPath must be absolute: {fullPath}", nameof(fullPath));
        return new PhysicalPath { Value = fullPath };
    }

    public override string ToString() => Value;

    public static implicit operator string(PhysicalPath p) => p.Value;
}

/// <summary>
/// Represents a virtual (workspace-relative) path using forward slashes.
/// VirtualPath is ALWAYS case-sensitive (Ordinal) regardless of OS,
/// because it serves as a stable key for indexing and cache lookups.
/// </summary>
public readonly record struct VirtualPath
{
    /// <summary>
    /// The normalized relative path with forward slashes.
    /// </summary>
    public string Value { get; init; }

    public VirtualPath(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        // Normalize to forward slashes, strip leading separators
        Value = value.Replace('\\', '/').TrimStart('/');
    }

    public override string ToString() => Value;

    public static implicit operator string(VirtualPath v) => v.Value;
}

/// <summary>
/// Provides platform-aware string comparison for physical paths.
/// Windows: OrdinalIgnoreCase (case-insensitive)
/// Linux/macOS: Ordinal (case-sensitive)
/// </summary>
public static class PathComparer
{
    /// <summary>
    /// The StringComparison to use for physical path comparison on the current OS.
    /// </summary>
    public static readonly StringComparison PhysicalPathComparison =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    /// <summary>
    /// Whether the current OS treats physical paths as case-insensitive.
    /// </summary>
    public static readonly bool IsCaseInsensitive =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>
    /// VirtualPath is always case-sensitive.
    /// </summary>
    public static readonly StringComparison VirtualPathComparison = StringComparison.Ordinal;

    /// <summary>
    /// Compares two physical paths using the OS-appropriate comparison.
    /// </summary>
    public static bool PhysicalEquals(string a, string b) =>
        string.Equals(a, b, PhysicalPathComparison);

    /// <summary>
    /// Compares two virtual paths (always case-sensitive).
    /// </summary>
    public static bool VirtualEquals(string a, string b) =>
        string.Equals(a, b, VirtualPathComparison);

    /// <summary>
    /// Checks if a child physical path is within a root physical path.
    /// </summary>
    public static bool IsWithinRoot(string rootPath, string childPath)
    {
        var normalizedRoot = System.IO.Path.GetFullPath(rootPath);
        var normalizedChild = System.IO.Path.GetFullPath(childPath);
        var rootWithSep = normalizedRoot.EndsWith(System.IO.Path.DirectorySeparatorChar)
            ? normalizedRoot
            : normalizedRoot + System.IO.Path.DirectorySeparatorChar;
        return string.Equals(normalizedRoot, normalizedChild, PhysicalPathComparison)
               || normalizedChild.StartsWith(rootWithSep, PhysicalPathComparison);
    }

    /// <summary>
    /// Converts a physical path to a virtual (workspace-relative) path.
    /// Returns null if the physical path is not within the root.
    /// </summary>
    public static VirtualPath? ToVirtualPath(string rootPath, string physicalPath)
    {
        if (!IsWithinRoot(rootPath, physicalPath))
            return null;
        var normalizedRoot = System.IO.Path.GetFullPath(rootPath).Replace('\\', '/');
        var normalizedPhysical = System.IO.Path.GetFullPath(physicalPath).Replace('\\', '/');
        var relative = normalizedPhysical[normalizedRoot.Length..].TrimStart('/');
        return new VirtualPath(relative);
    }

    /// <summary>
    /// Converts a virtual path to a physical path within the given root.
    /// </summary>
    public static PhysicalPath ToPhysicalPath(string rootPath, VirtualPath virtualPath)
    {
        var cleaned = virtualPath.Value.Replace('/', System.IO.Path.DirectorySeparatorChar);
        var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(rootPath, cleaned));
        return PhysicalPath.FromRooted(full);
    }
}
