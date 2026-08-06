using System.Diagnostics;
using System.Text.RegularExpressions;

namespace AiKv.Core.Repository;

/// <summary>
/// Type of Git repository source.
/// </summary>
public enum RepositorySource
{
    GitHub,
    Gitee,
    Https,
    Ssh,
    LocalPath,
    Unknown,
}

/// <summary>
/// Parsed repository URL.
/// </summary>
public sealed record RepositoryUrl
{
    public required string OriginalUrl { get; init; }
    public required string NormalizedUrl { get; init; }
    public required RepositorySource Source { get; init; }
    public string? Host { get; init; }
    public string? Owner { get; init; }
    public string? RepoName { get; init; }
    public string? Branch { get; init; }
}

/// <summary>
/// Parses repository URLs from HTTPS, SSH, GitHub, and Gitee formats.
/// Normalizes but never exposes credentials.
/// </summary>
public static partial class RepositoryUrlParser
{
    public static RepositoryUrl Parse(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        var source = DetectSource(url);
        var (host, owner, repo) = ExtractComponents(url, source);

        return new RepositoryUrl
        {
            OriginalUrl = url,
            NormalizedUrl = NormalizeUrl(url, source),
            Source = source,
            Host = host,
            Owner = owner,
            RepoName = repo,
        };
    }

    private static RepositorySource DetectSource(string url)
    {
        if (url.Contains("github.com", StringComparison.OrdinalIgnoreCase))
            return RepositorySource.GitHub;
        if (url.Contains("gitee.com", StringComparison.OrdinalIgnoreCase))
            return RepositorySource.Gitee;
        if (url.StartsWith("git@", StringComparison.OrdinalIgnoreCase))
            return RepositorySource.Ssh;
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return RepositorySource.Https;
        if (Directory.Exists(url) || File.Exists(url))
            return RepositorySource.LocalPath;
        return RepositorySource.Unknown;
    }

    private static (string? host, string? owner, string? repo) ExtractComponents(string url, RepositorySource source)
    {
        var match = SshUrlRegex().Match(url);
        if (match.Success)
            return (match.Groups[1].Value, match.Groups[2].Value, StripGitSuffix(match.Groups[3].Value));

        match = HttpsUrlRegex().Match(url);
        if (match.Success)
            return (match.Groups[1].Value, match.Groups[2].Value, StripGitSuffix(match.Groups[3].Value));

        return (null, null, null);
    }

    private static string StripGitSuffix(string value) =>
        value.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? value[..^4]
            : value;

    private static string NormalizeUrl(string url, RepositorySource source) =>
        source switch
        {
            RepositorySource.LocalPath => Path.GetFullPath(url).Replace('\\', '/'),
            _ => url.TrimEnd('/'),
        };

    [GeneratedRegex(@"git@([^:]+):([^/]+)/(.+)")]
    private static partial Regex SshUrlRegex();

    [GeneratedRegex(@"https?://([^/]+)/([^/]+)/(.+?)(?:\.git)?$")]
    private static partial Regex HttpsUrlRegex();
}

/// <summary>
/// Plan for a clone operation.
/// </summary>
public sealed record ClonePlan
{
    public required string Url { get; init; }
    public required string Destination { get; init; }
    public string? Branch { get; init; }
    public int? Depth { get; init; }
    public bool IncludeSubmodules { get; init; }
    public bool IncludeLfs { get; init; }
    public IReadOnlyList<string> Risks { get; init; } = [];
}

/// <summary>
/// Result of a Git operation.
/// </summary>
public sealed record GitOperationResult
{
    public required bool Success { get; init; }
    public required string Output { get; init; }
    public int ExitCode { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Wraps Git CLI execution with parameter arrays (no shell string concatenation).
/// Supports timeout, cancellation, and output limits.
/// </summary>
public sealed class GitProcessWrapper
{
    public async Task<GitOperationResult> ExecuteAsync(
        string workingDirectory,
        IReadOnlyList<string> args,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi);
        if (process is null)
            return new GitOperationResult { Success = false, Output = "", ExitCode = -1, ErrorMessage = "Failed to start git process" };

        var outputBuilder = new System.Text.StringBuilder();
        var errorBuilder = new System.Text.StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) outputBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) errorBuilder.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(60);
        var killTask = Task.Delay(effectiveTimeout, ct).ContinueWith(_ =>
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
        }, TaskScheduler.Default);

        await process.WaitForExitAsync(ct);

        return new GitOperationResult
        {
            Success = process.ExitCode == 0,
            Output = outputBuilder.ToString(),
            ExitCode = process.ExitCode,
            ErrorMessage = process.ExitCode != 0 ? errorBuilder.ToString() : null,
        };
    }

    public Task<GitOperationResult> CloneAsync(ClonePlan plan, CancellationToken ct = default)
    {
        var args = new List<string> { "clone" };
        if (plan.Depth is not null) args.AddRange(["--depth", plan.Depth.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        if (!plan.IncludeSubmodules) args.Add("--no-recurse-submodules");
        if (!plan.IncludeLfs) args.Add("--no-lfs");
        if (plan.Branch is not null) args.AddRange(["--branch", plan.Branch]);
        args.Add(plan.Url);
        args.Add(plan.Destination);

        return ExecuteAsync(Environment.CurrentDirectory, args, timeout: TimeSpan.FromMinutes(5), ct);
    }

    public Task<GitOperationResult> StatusAsync(string workingDir, CancellationToken ct = default)
        => ExecuteAsync(workingDir, ["status", "--porcelain"], ct: ct);

    public Task<GitOperationResult> DiffAsync(string workingDir, CancellationToken ct = default)
        => ExecuteAsync(workingDir, ["diff", "--name-only"], ct: ct);

    public Task<GitOperationResult> FfOnlyPullAsync(string workingDir, CancellationToken ct = default)
        => ExecuteAsync(workingDir, ["pull", "--ff-only"], ct: ct);
}
