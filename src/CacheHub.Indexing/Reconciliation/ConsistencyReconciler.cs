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
/// Compares the current index snapshot against disk state.
/// Detects added, modified, and deleted files.
/// </summary>
public sealed class ConsistencyReconciler
{
    /// <summary>
    /// Reconciles the index against disk. Compares paths, sizes, and modification times.
    /// </summary>
    public static ReconciliationResult Reconcile(
        string rootPath,
        IReadOnlyDictionary<string, long> indexedFiles,
        IReadOnlySet<string>? ignorePatterns = null)
    {
        var diskFiles = ScanDisk(rootPath, ignorePatterns);

        var added = new List<string>();
        var modified = new List<string>();
        var deleted = new List<string>();
        var unchanged = 0;

        // Find added and modified files
        foreach (var (path, size) in diskFiles)
        {
            if (!indexedFiles.TryGetValue(path, out var indexedSize))
            {
                added.Add(path);
            }
            else if (indexedSize != size)
            {
                modified.Add(path);
            }
            else
            {
                unchanged++;
            }
        }

        // Find deleted files
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
    /// Forces a full re-check (e.g., after branch switch).
    /// </summary>
    public static bool NeedsForcedReconcile(string rootPath, string lastKnownCommitHash)
    {
        // If Git HEAD changed, force reconcile.
        var gitDir = Path.Combine(rootPath, ".git");
        if (!Directory.Exists(gitDir)) return false;

        var headFile = Path.Combine(gitDir, "HEAD");
        if (!File.Exists(headFile)) return false;

        var currentHead = File.ReadAllText(headFile).Trim();
        return currentHead != lastKnownCommitHash;
    }

    private static Dictionary<string, long> ScanDisk(string rootPath, IReadOnlySet<string>? ignorePatterns)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var normalizedRoot = rootPath.Replace('\\', '/').TrimEnd('/');

        foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            var normalized = file.Replace('\\', '/');

            // Check ignore patterns
            if (ignorePatterns is not null)
            {
                var relativePath = normalized.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                    ? normalized[normalizedRoot.Length..].TrimStart('/')
                    : normalized;

                if (IsIgnored(relativePath, ignorePatterns)) continue;
            }

            try
            {
                var info = new FileInfo(file);
                result[normalized] = info.Length;
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
}
