using AiKv.Context.Integration;
using AiKv.Core.Repository;

namespace AiKv.Tests;

public class GitDiffProviderTests
{
    [Fact]
    public async Task GitDiffProvider_GetChangedFilesAsync_ShouldReturnEmpty_WhenNotGitRepo()
    {
        using var temp = new TempDir();
        var provider = new GitDiffProvider();

        var files = await provider.GetChangedFilesAsync(temp.Path);

        Assert.Empty(files);
    }

    [Fact]
    public async Task GitDiffProvider_GetRecentDiffFilesAsync_ShouldReturnEmpty_WhenNotGitRepo()
    {
        using var temp = new TempDir();
        var provider = new GitDiffProvider();

        var files = await provider.GetRecentDiffFilesAsync(temp.Path);

        Assert.Empty(files);
    }

    [Fact]
    public async Task GitDiffProvider_GetHeadCommitAsync_ShouldReturnNull_WhenNotGitRepo()
    {
        using var temp = new TempDir();
        var provider = new GitDiffProvider();

        var commit = await provider.GetHeadCommitAsync(temp.Path);

        Assert.Null(commit);
    }

    [Fact(Skip = "Requires git to be available and configured on the test machine")]
    public async Task GitDiffProvider_GetChangedFilesAsync_ShouldWork_WithRealGitRepo()
    {
        // Create a real git repo in temp
        using var temp = new TempDir();
        await InitGitRepoAsync(temp.Path);

        // Create and commit a file
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "app.ts"), "export const x = 1;");
        await RunGitAsync(temp.Path, ["add", "."]);
        await RunGitAsync(temp.Path, ["commit", "-m", "initial"]);

        // Modify the file (creates unstaged change)
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "app.ts"), "export const x = 2;");

        var provider = new GitDiffProvider();
        var files = await provider.GetChangedFilesAsync(temp.Path);

        Assert.NotEmpty(files);
        Assert.Contains(files, f => f.Contains("app.ts"));
    }

    [Fact(Skip = "Requires git to be available and configured on the test machine")]
    public async Task GitDiffProvider_GetHeadCommitAsync_ShouldReturnHash_WithRealGitRepo()
    {
        using var temp = new TempDir();
        await InitGitRepoAsync(temp.Path);
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "test.txt"), "test");
        await RunGitAsync(temp.Path, ["add", "."]);
        await RunGitAsync(temp.Path, ["commit", "-m", "test"]);

        var provider = new GitDiffProvider();
        var commit = await provider.GetHeadCommitAsync(temp.Path);

        Assert.NotNull(commit);
        Assert.True(commit!.Length >= 7); // Git short hash
    }

    private static async Task InitGitRepoAsync(string path)
    {
        await RunGitAsync(path, ["init"]);
        await RunGitAsync(path, ["config", "user.email", "test@test.com"]);
        await RunGitAsync(path, ["config", "user.name", "Test"]);
    }

    private static async Task RunGitAsync(string workingDir, string[] args)
    {
        var git = new GitProcessWrapper();
        await git.ExecuteAsync(workingDir, args, timeout: TimeSpan.FromSeconds(10));
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; }
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aikv_git_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }
}
