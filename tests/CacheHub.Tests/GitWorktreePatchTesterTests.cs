using CacheHub.Core.Benchmarks.Agent;
using Xunit;

namespace CacheHub.Tests;

/// <summary>
/// V6: Tests for GitWorktreePatchTester — real apply-patch/build/test for Agent Benchmark.
/// Uses the CacheHub repo itself as the test source repo.
/// </summary>
public class GitWorktreePatchTesterTests
{
    private static string GetRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var proj = Path.Combine(dir, "CacheHub.sln");
            if (File.Exists(proj)) return dir;
            dir = Path.GetDirectoryName(dir) ?? "";
            if (string.IsNullOrEmpty(dir)) break;
        }
        throw new InvalidOperationException("Could not locate repo root");
    }

    [Fact]
    public async Task CreateWorktree_FromRepo_OntainsSolution()
    {
        using var tester = new GitWorktreePatchTester();
        var path = tester.CreateWorktree(GetRepoRoot(), "HEAD");

        Assert.True(Directory.Exists(path));
        Assert.True(File.Exists(Path.Combine(path, "CacheHub.sln")));
    }

    [Fact]
    public void RunTests_InvalidCommand_ReturnsFailure()
    {
        using var tester = new GitWorktreePatchTester();
        var path = tester.CreateWorktree(GetRepoRoot(), "HEAD");

        // Run a command that doesn't exist → must report failure (non-zero exit)
        var result = tester.RunTests("definitely-not-a-real-command-xyz");

        Assert.False(result.Success);
        Assert.True(result.Total >= 1);
    }

    [Fact]
    public void RunTests_ValidCommand_OntainsSuccessOrFailure()
    {
        using var tester = new GitWorktreePatchTester();
        var path = tester.CreateWorktree(GetRepoRoot(), "HEAD");

        // Running git status should always succeed (exit 0)
        var result = tester.RunTests("git", "status");

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Reset_RemovesWorktree()
    {
        using var tester = new GitWorktreePatchTester();
        var path = tester.CreateWorktree(GetRepoRoot(), "HEAD");
        Assert.True(Directory.Exists(path));

        tester.Reset();

        // After dispose + reset, the directory should be gone
        await Task.Delay(500); // git worktree remove may take a moment
        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public void Dispose_CleansUpWorktree()
    {
        var tester = new GitWorktreePatchTester();
        var path = tester.CreateWorktree(GetRepoRoot(), "HEAD");
        Assert.True(Directory.Exists(path));

        tester.Dispose();

        Assert.False(Directory.Exists(path));
    }
}
