using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace CacheHub.Core.Repository;

/// <summary>
/// Captured Git state of a workspace at a point in time.
/// Used for version-aware Context Packages and stale detection.
/// </summary>
public sealed record GitState
{
    /// <summary>Current HEAD commit hash (null if not a git repo).</summary>
    public string? Commit { get; init; }

    /// <summary>Current branch name (null if detached HEAD or not a git repo).</summary>
    public string? Branch { get; init; }

    /// <summary>True if the working tree has uncommitted changes.</summary>
    public bool IsDirty { get; init; }

    /// <summary>Sorted list of dirty file paths (modified + staged + untracked).</summary>
    public IReadOnlyList<string> DirtyFiles { get; init; } = [];

    /// <summary>SHA-256 fingerprint of the full workspace version state.</summary>
    public string? Fingerprint { get; init; }

    /// <summary>Index schema version used when computing the fingerprint.</summary>
    public const string SchemaVersion = "index-schema-v11";

    /// <summary>
    /// Computes a workspace version fingerprint from git state.
    /// SHA256(commit + branch + sorted(dirty_file_path + content_hash) + schema_version).
    /// </summary>
    public static string ComputeFingerprint(
        string? commit,
        string? branch,
        string workspaceRoot,
        IEnumerable<string> dirtyFiles,
        string schemaVersion = SchemaVersion)
    {
        var sb = new StringBuilder();
        sb.Append(commit ?? "no-commit");
        sb.Append('|');
        sb.Append(branch ?? "detached");
        sb.Append('|');

        // Sort dirty files for deterministic ordering
        foreach (var file in dirtyFiles.OrderBy(f => f, StringComparer.Ordinal))
        {
            var fullPath = Path.Combine(workspaceRoot, file.Replace('/', Path.DirectorySeparatorChar));
            var contentHash = ComputeFileHash(fullPath);
            sb.Append(file);
            sb.Append(':');
            sb.Append(contentHash);
            sb.Append(';');
        }

        sb.Append('|');
        sb.Append(schemaVersion);

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Computes SHA-256 of a file's content. Returns "missing" if file doesn't exist.
    /// </summary>
    public static string ComputeFileHash(string fullPath)
    {
        if (!File.Exists(fullPath))
            return "missing";

        try
        {
            using var stream = File.OpenRead(fullPath);
            var hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch
        {
            return "unreadable";
        }
    }

    /// <summary>
    /// Creates a "clean" (non-git) state with a fingerprint based on workspace files.
    /// Used when the workspace is not a git repository.
    /// </summary>
    public static GitState CreateNonGit(string workspaceRoot, IEnumerable<string> indexedFiles)
    {
        var fileList = indexedFiles.OrderBy(f => f, StringComparer.Ordinal).ToList();
        var sb = new StringBuilder("non-git|");
        foreach (var file in fileList)
        {
            var fullPath = Path.Combine(workspaceRoot, file.Replace('/', Path.DirectorySeparatorChar));
            sb.Append(file);
            sb.Append(':');
            sb.Append(ComputeFileHash(fullPath));
            sb.Append(';');
        }
        sb.Append('|');
        sb.Append(SchemaVersion);

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var hash = SHA256.HashData(bytes);
        var fingerprint = Convert.ToHexString(hash).ToLowerInvariant();

        return new GitState
        {
            Commit = null,
            Branch = null,
            IsDirty = false,
            DirtyFiles = [],
            Fingerprint = fingerprint,
        };
    }
}

/// <summary>
/// Captures Git state from a workspace directory using git CLI.
/// </summary>
public sealed class GitStateProvider
{
    private readonly GitProcessWrapper _git;

    public GitStateProvider(GitProcessWrapper? git = null)
    {
        _git = git ?? new GitProcessWrapper();
    }

    /// <summary>
    /// Captures the current Git state of a workspace.
    /// Returns a non-git state if the directory is not a git repository.
    /// V8-P1-01: fileFilter parameter allows callers to exclude non-indexed files from fingerprint scope.
    /// </summary>
    public async Task<GitState> CaptureAsync(string workspaceRoot, Func<string, bool>? fileFilter = null, CancellationToken ct = default)
    {
        if (!Directory.Exists(workspaceRoot))
            return new GitState { Fingerprint = null };

        // Try to get commit hash
        var commitResult = await _git.ExecuteAsync(workspaceRoot, ["rev-parse", "HEAD"], timeout: TimeSpan.FromSeconds(10), ct: ct);
        if (!commitResult.Success)
        {
            // Not a git repo — use file-based fingerprint
            // V8-P1-01: Filter files to only include indexed files (exclude node_modules, bin, obj, etc.)
            // Use "*" rather than "*.*": indexed source files may not have an
            // extension (for example Makefile or Dockerfile), so the non-git
            // fingerprint scope must not be narrower than the index scope.
            var allFiles = Directory.EnumerateFiles(workspaceRoot, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(workspaceRoot, f).Replace('\\', '/'));
            // Always exclude .git/
            allFiles = allFiles.Where(p => !p.StartsWith(".git/", StringComparison.OrdinalIgnoreCase));
            // V8-P1-01: Apply caller's filter (e.g., IgnoreRuleEngine + FileTypeDetector) if provided
            if (fileFilter is not null)
                allFiles = allFiles.Where(fileFilter);
            return GitState.CreateNonGit(workspaceRoot, allFiles);
        }

        var commit = commitResult.Output.Trim();

        // Get branch
        var branchResult = await _git.ExecuteAsync(workspaceRoot, ["rev-parse", "--abbrev-ref", "HEAD"], timeout: TimeSpan.FromSeconds(10), ct: ct);
        var branch = branchResult.Success ? branchResult.Output.Trim() : null;
        if (branch == "HEAD") branch = null; // detached HEAD

        // Get dirty files (modified + staged + untracked)
        var statusResult = await _git.ExecuteAsync(workspaceRoot, ["status", "--porcelain"], timeout: TimeSpan.FromSeconds(15), ct: ct);
        var dirtyFiles = new List<string>();
        if (statusResult.Success)
        {
            foreach (var line in statusResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                // Porcelain format: XY filename (2 status chars + space + filename)
                if (line.Length > 3)
                {
                    var filePath = line[3..].Trim().Trim('"');
                    // Handle rename format "old -> new"
                    if (filePath.Contains(" -> "))
                        filePath = filePath.Split(" -> ")[1].Trim().Trim('"');
                    dirtyFiles.Add(filePath.Replace('\\', '/'));
                }
            }
        }

        // V8-P1-01: Filter dirty files to only include indexed files when filter is provided.
        // This prevents non-indexed files (e.g., binary, generated) from invalidating the fingerprint.
        if (fileFilter is not null)
            dirtyFiles = dirtyFiles.Where(fileFilter).ToList();

        var isDirty = dirtyFiles.Count > 0;
        var fingerprint = GitState.ComputeFingerprint(commit, branch, workspaceRoot, dirtyFiles);

        return new GitState
        {
            Commit = commit,
            Branch = branch,
            IsDirty = isDirty,
            DirtyFiles = dirtyFiles,
            Fingerprint = fingerprint,
        };
    }
}
