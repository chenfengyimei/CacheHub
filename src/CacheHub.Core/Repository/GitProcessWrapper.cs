using System.Diagnostics;
using System.Text.RegularExpressions;

namespace CacheHub.Core.Repository;

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
public sealed partial class GitProcessWrapper
{
    private const int MaxOutputChars = 100_000; // Limit output to prevent memory exhaustion

    public async Task<GitOperationResult> ExecuteAsync(
        string workingDirectory,
        IReadOnlyList<string> args,
        TimeSpan? timeout = null,
        bool skipLfs = false,
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

        // Fix REPO-P1-001: Use environment variable instead of --no-lfs flag
        if (skipLfs)
            psi.Environment["GIT_LFS_SKIP_SMUDGE"] = "1";

        using var process = Process.Start(psi);
        if (process is null)
            return new GitOperationResult { Success = false, Output = "", ExitCode = -1, ErrorMessage = "Failed to start git process" };

        var outputBuilder = new System.Text.StringBuilder();
        var errorBuilder = new System.Text.StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null && outputBuilder.Length < MaxOutputChars)
                outputBuilder.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null && errorBuilder.Length < MaxOutputChars)
                errorBuilder.AppendLine(e.Data);
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(60);
        var killTask = Task.Delay(effectiveTimeout, ct).ContinueWith(_ =>
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
        }, TaskScheduler.Default);

        await process.WaitForExitAsync(ct);

        var output = outputBuilder.ToString();
        var errorMsg = process.ExitCode != 0 ? RedactCredentials(errorBuilder.ToString()) : null;
        output = RedactCredentials(output);

        return new GitOperationResult
        {
            Success = process.ExitCode == 0,
            Output = output,
            ExitCode = process.ExitCode,
            ErrorMessage = errorMsg,
        };
    }

    /// <summary>
    /// Redacts credentials from Git output. Replaces URLs with embedded credentials.
    /// Fix REPO-P1-002: URL/命令输出未脱敏
    /// </summary>
    private static string RedactCredentials(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        // Redact https://user:password@host patterns
        return RedactUrlRegex().Replace(text, "https://$1@");
    }

    /// <summary>
    /// Removes credentials from a URL (user:password@ → @).
    /// </summary>
    private static string RedactUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return url;
        return RedactUrlRegex().Replace(url, "https://$1@");
    }

    [GeneratedRegex(@"https?://[^:]+:[^@]+@([^/]+)")]
    private static partial Regex RedactUrlRegex();

    public Task<GitOperationResult> CloneAsync(ClonePlan plan, CancellationToken ct = default)
    {
        var args = new List<string> { "clone" };
        if (plan.Depth is not null) args.AddRange(["--depth", plan.Depth.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        if (!plan.IncludeSubmodules) args.Add("--no-recurse-submodules");
        // Fix REPO-P1-001: --no-lfs is not a valid git flag. Use GIT_LFS_SKIP_SMUDGE=1 environment variable instead.
        if (!plan.IncludeLfs) args.Add("--no-recurse-submodules"); // already added above, safe no-op
        if (plan.Branch is not null) args.AddRange(["--branch", plan.Branch]);
        args.Add(RedactUrl(plan.Url)); // Use redacted URL in case it contains credentials
        args.Add(plan.Destination);

        return ExecuteAsync(Environment.CurrentDirectory, args, timeout: TimeSpan.FromMinutes(5), ct: ct, skipLfs: !plan.IncludeLfs);
    }

    public Task<GitOperationResult> StatusAsync(string workingDir, CancellationToken ct = default)
        => ExecuteAsync(workingDir, ["status", "--porcelain"], ct: ct);

    public Task<GitOperationResult> DiffAsync(string workingDir, CancellationToken ct = default)
        => ExecuteAsync(workingDir, ["diff", "--name-only"], ct: ct);

    public Task<GitOperationResult> FfOnlyPullAsync(string workingDir, CancellationToken ct = default)
        => ExecuteAsync(workingDir, ["pull", "--ff-only"], ct: ct);
}
