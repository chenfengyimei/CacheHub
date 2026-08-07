using CacheHub.Core.Benchmarks.Agent;
using Xunit;

namespace CacheHub.Tests;

/// <summary>
/// V6: Tests for GitWorktreePatchTester — real apply-patch/build/test for Agent Benchmark.
/// Uses an isolated temporary git repo (NOT the real CacheHub repo) to avoid side effects.
/// </summary>
public class GitWorktreePatchTesterTests
{
    private static string CreateTempGitRepo()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cachehub-wt-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        RunGit(dir, "init");
        RunGit(dir, "config user.email test@test.com");
        RunGit(dir, "config user.name Test");
        File.WriteAllText(Path.Combine(dir, "file.txt"), "hello\n");
        RunGit(dir, "add .");
        RunGit(dir, "commit -m init");
        return dir;
    }

    /// <summary>
    /// Best-effort cleanup that tolerates Windows file-lock races left by git.
    /// </summary>
    private static void Cleanup(string dir)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
        try { Directory.Delete(dir, recursive: true); }
        catch { /* best effort */ }
    }

    private static void RunGit(string dir, string args)
    {
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var part in parts)
            psi.ArgumentList.Add(part);
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.WaitForExit(15_000);
    }

    [Fact]
    public void CreateWorktree_FromTempRepo_CreatesTree()
    {
        var source = CreateTempGitRepo();
        try
        {
            using var tester = new GitWorktreePatchTester();
            var path = tester.CreateWorktree(source, "HEAD");
            Assert.True(Directory.Exists(path));
            Assert.True(File.Exists(Path.Combine(path, "file.txt")));
        }
        finally { Cleanup(source); }
    }

    [Fact]
    public void RunTests_InvalidCommand_ReturnsFailure()
    {
        var source = CreateTempGitRepo();
        try
        {
            using var tester = new GitWorktreePatchTester();
            tester.CreateWorktree(source, "HEAD");
            var result = tester.RunTests("definitely-not-a-real-command-xyz");
            Assert.False(result.Success);
        }
        finally { Cleanup(source); }
    }

    [Fact]
    public void ApplyPatch_ValidPatch_ReturnsTrue_AndChangesFile()
    {
        var source = CreateTempGitRepo();
        try
        {
            using var tester = new GitWorktreePatchTester();
            var path = tester.CreateWorktree(source, "HEAD");

            // Patch that modifies file.txt content
            var patch = "--- a/file.txt\n+++ b/file.txt\n@@ -1 +1 @@\n-hello\n+hello world\n";
            Assert.True(tester.ApplyPatch(patch));
            // Tolerate CRLF vs LF line endings from git autocrlf
            Assert.Equal("hello world", File.ReadAllText(Path.Combine(path, "file.txt")).Trim('\r', '\n'));
        }
        finally { Cleanup(source); }
    }

    [Fact]
    public void Dispose_CleansUpWorktree()
    {
        var source = CreateTempGitRepo();
        try
        {
            var tester = new GitWorktreePatchTester();
            var path = tester.CreateWorktree(source, "HEAD");
            Assert.True(Directory.Exists(path));
            tester.Dispose();
            Assert.False(Directory.Exists(path));
        }
        finally { Cleanup(source); }
    }
}