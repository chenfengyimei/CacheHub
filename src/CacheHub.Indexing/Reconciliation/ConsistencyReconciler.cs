using CacheHub.Core.Paths;
using CacheHub.Indexing.Hashing;
using CacheHub.Indexing.IgnoreRules;
using CacheHub.Indexing.States;

namespace CacheHub.Indexing.Reconciliation;

/// <summary>
/// Result of a consistency check between the index and disk.
/// </summary>
public sealed record ReconciliationResult
{
    public required int TotalChecked { get; init; }
    public required int AddedFiles { get; init; }
    public required int ModifiedFiles { get; init; }
    public required int DeletedFiles { get; init; }
    public required int UnchangedFiles { get; init; }
    public required bool IsConsistent { get; init; }

    public IReadOnlyList<string> AddedPaths { get; init; } = [];
    public IReadOnlyList<string> ModifiedPaths { get; init; } = [];
    public IReadOnlyList<string> DeletedPaths { get; init; } = [];
}

/// <summary>
/// Represents an indexed file entry for reconciliation.
/// Uses VirtualPath (relative, forward-slash, case-sensitive).
/// </summary>
public sealed record IndexedFileEntry
{
    public required string VirtualPath { get; init; }
    public required long Size { get; init; }
    public string? Mtime { get; init; }
    public string? ContentHash { get; init; }
}

/// <summary>
/// Compares the current index snapshot against disk state.
/// Detects added, modified, and deleted files using relative VirtualPath + size + mtime.
/// </summary>
public sealed class ConsistencyReconciler
{
    /// <summary>
    /// Reconciles the index against disk. Compares relative paths, sizes, mtime,
    /// and content hash for same-size files to detect same-size mutations.
    /// </summary>
    /// <param name="rootPath">Workspace root path (absolute)</param>
    /// <param name="indexedFiles">Indexed files keyed by VirtualPath (relative, forward-slash)</param>
    /// <param name="ignorePatterns">Optional ignore patterns (directory/file names, legacy weak matching)</param>
    /// <param name="ignoreEngine">Optional full IgnoreRuleEngine for glob/negation-aware filtering</param>
    public static ReconciliationResult Reconcile(
        string rootPath,
        IReadOnlyDictionary<string, IndexedFileEntry> indexedFiles,
        IReadOnlySet<string>? ignorePatterns = null,
        IgnoreRuleEngine? ignoreEngine = null)
    {
        var diskFiles = ScanDisk(rootPath, ignorePatterns, ignoreEngine);

        var added = new List<string>();
        var modified = new List<string>();
        var deleted = new List<string>();
        var unchanged = 0;

        // Find added and modified files
        foreach (var (virtualPath, diskEntry) in diskFiles)
        {
            if (!indexedFiles.TryGetValue(virtualPath, out var indexedEntry))
            {
                added.Add(virtualPath);
            }
            else if (IsModified(indexedEntry, diskEntry, rootPath))
            {
                modified.Add(virtualPath);
            }
            else
            {
                unchanged++;
            }
        }

        // Find deleted files (in index but not on disk)
        foreach (var indexedPath in indexedFiles.Keys)
        {
            if (!diskFiles.ContainsKey(indexedPath))
            {
                deleted.Add(indexedPath);
            }
        }

        return new ReconciliationResult
        {
            TotalChecked = diskFiles.Count + deleted.Count,
            AddedFiles = added.Count,
            ModifiedFiles = modified.Count,
            DeletedFiles = deleted.Count,
            UnchangedFiles = unchanged,
            IsConsistent = added.Count == 0 && modified.Count == 0 && deleted.Count == 0,
            AddedPaths = added,
            ModifiedPaths = modified,
            DeletedPaths = deleted,
        };
    }

    /// <summary>
    /// Checks if a file has been modified by comparing size, mtime, and content hash.
    /// Same-size mutations are detected via content hash comparison.
    /// </summary>
    private static bool IsModified(IndexedFileEntry indexed, DiskFileEntry disk, string rootPath)
    {
        // Size change is definitive
        if (indexed.Size != disk.Size)
            return true;

        // Mtime comparison (if available)
        if (!string.IsNullOrEmpty(indexed.Mtime) && !string.IsNullOrEmpty(disk.Mtime))
        {
            if (!string.Equals(indexed.Mtime, disk.Mtime, StringComparison.Ordinal))
                return true;
        }

        // Same size + same mtime: verify via content hash if we have a stored hash
        if (!string.IsNullOrEmpty(indexed.ContentHash) && indexed.ContentHash != "pending")
        {
            var fullPath = Path.Combine(rootPath, disk.VirtualPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath))
            {
                try
                {
                    var currentHash = FileHasher.ComputeFullHashAsync(fullPath).GetAwaiter().GetResult();
                    return !string.Equals(indexed.ContentHash, currentHash, StringComparison.Ordinal);
                }
                catch
                {
                    // If we can't read the file, treat as modified to be safe
                    return true;
                }
            }
            return true; // File doesn't exist on disk
        }

        // No stored hash: same-size + same-mtime is considered unchanged
        return false;
    }

    /// <summary>
    /// Forces a full re-check (e.g., after branch switch).
    /// Resolves the actual commit hash from .git/HEAD (following ref: symbolic refs).
    /// </summary>
    public static bool NeedsForcedReconcile(string rootPath, string lastKnownCommitHash)
    {
        var gitDir = Path.Combine(rootPath, ".git");
        if (!Directory.Exists(gitDir)) return false;

        var headFile = Path.Combine(gitDir, "HEAD");
        if (!File.Exists(headFile)) return false;

        var currentHead = File.ReadAllText(headFile).Trim();

        // Resolve symbolic ref (e.g., "ref: refs/heads/main" → read the actual commit hash)
        if (currentHead.StartsWith("ref: ", StringComparison.Ordinal))
        {
            var refPath = currentHead["ref: ".Length..];
            var resolvedFile = Path.Combine(gitDir, refPath);
            if (File.Exists(resolvedFile))
            {
                currentHead = File.ReadAllText(resolvedFile).Trim();
            }
            else
            {
                // Packed-refs fallback
                var packedRefs = Path.Combine(gitDir, "packed-refs");
                if (File.Exists(packedRefs))
                {
                    foreach (var line in File.ReadAllLines(packedRefs))
                    {
                        if (line.EndsWith(refPath, StringComparison.Ordinal) && line.Length > 40)
                        {
                            currentHead = line[..40].Trim();
                            break;
                        }
                    }
                }
            }
        }

        return !string.IsNullOrEmpty(currentHead) && currentHead != lastKnownCommitHash;
    }

    /// <summary>
    /// Scans disk and returns files keyed by VirtualPath (relative, forward-slash).
    /// Uses IgnoreRuleEngine for glob-aware filtering when available, falls back to segment matching.
    /// </summary>
    private static Dictionary<string, DiskFileEntry> ScanDisk(string rootPath, IReadOnlySet<string>? ignorePatterns, IgnoreRuleEngine? ignoreEngine)
    {
        var result = new Dictionary<string, DiskFileEntry>(StringComparer.Ordinal);
        var normalizedRoot = rootPath.Replace('\\', '/').TrimEnd('/');

        foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            var normalized = file.Replace('\\', '/');

            // Convert to VirtualPath (relative, forward-slash)
            var relativePath = normalized.StartsWith(normalizedRoot, PathComparer.PhysicalPathComparison)
                ? normalized[normalizedRoot.Length..].TrimStart('/')
                : normalized;

            // Check ignore rules: prefer IgnoreRuleEngine (glob/negation-aware), fall back to segment matching
            if (ignoreEngine is not null && ignoreEngine.IsIgnored(relativePath))
                continue;

            if (ignoreEngine is null && ignorePatterns is not null && IsIgnored(relativePath, ignorePatterns))
                continue;

            try
            {
                var info = new FileInfo(file);
                result[relativePath] = new DiskFileEntry
                {
                    VirtualPath = relativePath,
                    Size = info.Length,
                    Mtime = info.LastWriteTimeUtc.ToString("O"),
                };
            }
            catch (UnauthorizedAccessException) { }
            catch (FileNotFoundException) { }
        }

        return result;
    }

    private static bool IsIgnored(string path, IReadOnlySet<string> patterns)
    {
        var segments = path.Split('/');
        return patterns.Any(p => segments.Any(s => string.Equals(s, p, StringComparison.OrdinalIgnoreCase)));
    }

    private sealed record DiskFileEntry
    {
        public required string VirtualPath { get; init; }
        public required long Size { get; init; }
        public required string Mtime { get; init; }
    }
}
