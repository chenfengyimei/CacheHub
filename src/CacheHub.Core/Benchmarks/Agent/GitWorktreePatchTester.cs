using System.Diagnostics;
using System.Text;

namespace CacheHub.Core.Benchmarks.Agent;

/// <summary>
/// V6: Real agent benchmark patch evaluation using a temporary git worktree.
/// Creates a worktree, applies the model's patch via `git apply`, runs the
/// configured build+test command, and returns the actual pass/fail result.
/// </summary>
public sealed class GitWorktreePatchTester : IDisposable
{
    private string? _worktreePath;
    private string? _temporarySourceRepoPath;
    private bool _disposed;

    /// <summary>
    /// Creates a new git worktree from the source repository.
    /// Returns the path to the worktree directory.
    /// </summary>
    public string CreateWorktree(string sourceRepoPath, string commitHash = "HEAD")
    {
        if (!Directory.Exists(sourceRepoPath))
            throw new DirectoryNotFoundException($"Source repo not found: {sourceRepoPath}");

        var sourceForWorktree = sourceRepoPath;
        var gitDir = Path.Combine(sourceRepoPath, ".git");
        if (!Directory.Exists(gitDir) && !File.Exists(gitDir))
            sourceForWorktree = CreateTemporaryGitRepository(sourceRepoPath);

        _worktreePath = Path.Combine(Path.GetTempPath(), "cachehub-bench-" + Guid.NewGuid().ToString("N")[..12]);
        // Do NOT pre-create the directory — `git worktree add` requires the target to not exist.
        // If a stale dir remains, clean it up first.
        if (Directory.Exists(_worktreePath))
            Directory.Delete(_worktreePath, recursive: true);

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = sourceForWorktree,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("worktree");
        psi.ArgumentList.Add("add");
        psi.ArgumentList.Add("--detach");
        psi.ArgumentList.Add(_worktreePath);
        psi.ArgumentList.Add(commitHash);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git process");
        var worktreeExited = proc.WaitForExit(30_000);
        if (!worktreeExited)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            throw new InvalidOperationException("git worktree add timed out after 30s");
        }
        if (proc.ExitCode != 0)
        {
            var err = proc.StandardError.ReadToEnd();
            throw new InvalidOperationException($"git worktree add failed: {err}");
        }

        return _worktreePath;
    }

    /// <summary>
    /// Applies a patch (diff format) to the worktree using `git apply`.
    /// </summary>
    public bool ApplyPatch(string patchContent)
    {
        if (_worktreePath is null)
            throw new InvalidOperationException("CreateWorktree must be called first");

        var patchFile = Path.Combine(_worktreePath, "_model_patch.diff");
        File.WriteAllText(patchFile, patchContent);

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = _worktreePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("apply");
        psi.ArgumentList.Add("--whitespace=nowarn");
        psi.ArgumentList.Add(patchFile);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git process");
        var exited = proc.WaitForExit(15_000);
        if (!exited)
        {
            // V8-P1-03: Kill process on timeout instead of reading ExitCode on a still-running process
            try { proc.Kill(entireProcessTree: true); } catch { }
            try { File.Delete(patchFile); } catch { }
            return false;
        }

        try { File.Delete(patchFile); } catch { }

        return proc.ExitCode == 0;
    }

    /// <summary>
    /// Runs the specified build/test command in the worktree.
    /// Returns the exit code (0 = all tests passed).
    /// V7-W05: Fixed timeout — uses async reads + CancellationTokenSource instead of blocking ReadToEnd.
    /// </summary>
    public AgentTestResult RunTests(string command, string args = "", int timeoutMs = 120_000)
    {
        if (_worktreePath is null)
            throw new InvalidOperationException("CreateWorktree must be called first");

        var psi = new ProcessStartInfo
        {
            FileName = command,
            WorkingDirectory = _worktreePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (!string.IsNullOrEmpty(args))
            foreach (var arg in args.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                psi.ArgumentList.Add(arg);

        Process? proc = null;
        try
        {
            proc = Process.Start(psi);
        }
        catch (Exception ex)
        {
            return new AgentTestResult
            {
                Success = false,
                Passed = 0,
                Total = 1,
                ErrorMessage = ex.Message,
            };
        }

        if (proc is null)
        {
            return new AgentTestResult
            {
                Success = false,
                Passed = 0,
                Total = 1,
                ErrorMessage = "Failed to start process",
            };
        }

        // V7-W05: Use async reads with cancellation token to enforce real timeout
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();
        var stdoutTask = Task.Run(() => { using var reader = proc.StandardOutput; stdoutBuilder.Append(reader.ReadToEnd()); });
        var stderrTask = Task.Run(() => { using var reader = proc.StandardError; stderrBuilder.Append(reader.ReadToEnd()); });

        var exited = proc.WaitForExit(timeoutMs);
        if (!exited)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            return new AgentTestResult
            {
                Success = false,
                Passed = 0,
                Total = 1,
                ErrorMessage = $"Test command timed out after {timeoutMs}ms",
            };
        }

        // Wait for stream reads to complete
        Task.WaitAll([stdoutTask, stderrTask], TimeSpan.FromSeconds(5));

        var stdout = stdoutBuilder.ToString();
        var stderr = stderrBuilder.ToString();
        var success = proc.ExitCode == 0;

        var passed = ParseCount(stdout, "通过:", "Passed:", "passed:", "passed ");
        var failed = ParseCount(stdout, "失败:", "Failed:", "failed:", "failed ");
        var total = passed + failed;

        return new AgentTestResult
        {
            Success = success,
            Passed = passed,
            Total = total > 0 ? total : (success ? 1 : 0),
            ErrorMessage = success ? null : (stderr.Length > 500 ? stderr[..500] : stderr),
        };
    }

    /// <summary>
    /// Resets the worktree by removing it.
    /// </summary>
    public void Reset()
    {
        if (_worktreePath is not null && Directory.Exists(_worktreePath))
        {
            // Remove the worktree
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = _worktreePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("worktree");
            psi.ArgumentList.Add("remove");
            psi.ArgumentList.Add("--force");
            psi.ArgumentList.Add(_worktreePath);
            try
            {
                using var proc = Process.Start(psi);
                proc?.WaitForExit(10_000);
            }
            catch { }

            // If git couldn't remove it, delete manually
            try { Directory.Delete(_worktreePath, recursive: true); } catch { }
        }
        _worktreePath = null;

        if (_temporarySourceRepoPath is not null)
        {
            try { Directory.Delete(_temporarySourceRepoPath, recursive: true); } catch { }
            _temporarySourceRepoPath = null;
        }
    }

    private string CreateTemporaryGitRepository(string sourceDirectory)
    {
        _temporarySourceRepoPath = Path.Combine(Path.GetTempPath(), "cachehub-bench-source-" + Guid.NewGuid().ToString("N")[..12]);
        try
        {
            CopyFixtureSource(sourceDirectory, _temporarySourceRepoPath);
            RunGit(_temporarySourceRepoPath, "init");
            RunGit(_temporarySourceRepoPath, "config", "user.email", "cachehub-benchmark@local.invalid");
            RunGit(_temporarySourceRepoPath, "config", "user.name", "CacheHub Benchmark");
            RunGit(_temporarySourceRepoPath, "add", "--all");
            RunGit(_temporarySourceRepoPath, "commit", "--no-gpg-sign", "-m", "Benchmark fixture seed");
            return _temporarySourceRepoPath;
        }
        catch
        {
            if (Directory.Exists(_temporarySourceRepoPath))
                try { Directory.Delete(_temporarySourceRepoPath, recursive: true); } catch { }
            _temporarySourceRepoPath = null;
            throw;
        }
    }

    private static void CopyFixtureSource(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
            if (relativePath.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith(".git" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith(".git/", StringComparison.OrdinalIgnoreCase))
                continue;

            var targetPath = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(sourceFile, targetPath, overwrite: false);
        }
    }

    private static void RunGit(string workingDirectory, params string[] arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git process");
        if (!process.WaitForExit(30_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidOperationException($"git {arguments[0]} timed out after 30s");
        }
        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"git {arguments[0]} failed: {error}");
        }
    }

    private static int ParseCount(string output, params string[] prefixes)
    {
        foreach (var prefix in prefixes)
        {
            var idx = output.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var start = idx + prefix.Length;
                var end = start;
                while (end < output.Length && char.IsDigit(output[end])) end++;
                if (end > start && int.TryParse(output.AsSpan(start, end - start), out var n))
                    return n;
            }
        }
        return 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Reset();
    }
}
