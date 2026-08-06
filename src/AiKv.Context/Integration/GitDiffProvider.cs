using AiKv.Core.Repository;
using AiKv.Core.Workspaces;

namespace AiKv.Context.Integration;

/// <summary>
/// Integrates Git diff into Context Build requests.
/// Uses GitProcessWrapper to get changed files, then passes them to ContextEngine.
/// </summary>
public sealed class GitDiffProvider
{
    private readonly GitProcessWrapper _git;

    public GitDiffProvider(GitProcessWrapper? git = null)
    {
        _git = git ?? new GitProcessWrapper();
    }

    /// <summary>
    /// Gets the list of files changed since the last commit (unstaged + staged).
    /// </summary>
    public async Task<IReadOnlyList<string>> GetChangedFilesAsync(string workspaceRoot, CancellationToken ct = default)
    {
        if (!Directory.Exists(Path.Combine(workspaceRoot, ".git")))
            return [];

        var statusResult = await _git.StatusAsync(workspaceRoot, ct);
        if (!statusResult.Success) return [];

        var files = new List<string>();
        foreach (var line in statusResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length < 3) continue;

            // git status --porcelain format: XY filename
            var filePath = trimmed[2..].Trim().Trim('"');
            files.Add(filePath.Replace('\\', '/'));
        }
        return files;
    }

    /// <summary>
    /// Gets files changed in the last N commits.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetRecentDiffFilesAsync(string workspaceRoot, int commitCount = 1, CancellationToken ct = default)
    {
        if (!Directory.Exists(Path.Combine(workspaceRoot, ".git")))
            return [];

        var diffResult = await _git.ExecuteAsync(workspaceRoot,
            ["diff", $"HEAD~{commitCount}..HEAD", "--name-only"],
            timeout: TimeSpan.FromSeconds(30), ct: ct);

        if (!diffResult.Success) return [];

        return diffResult.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Trim().Replace('\\', '/'))
            .ToList();
    }

    /// <summary>
    /// Gets the current HEAD commit hash.
    /// </summary>
    public async Task<string?> GetHeadCommitAsync(string workspaceRoot, CancellationToken ct = default)
    {
        if (!Directory.Exists(Path.Combine(workspaceRoot, ".git")))
            return null;

        var result = await _git.ExecuteAsync(workspaceRoot, ["rev-parse", "HEAD"], ct: ct);
        return result.Success ? result.Output.Trim() : null;
    }
}
