using System.Diagnostics;

namespace CacheHub.Indexing.Search;

/// <summary>
/// Result source for search operations.
/// </summary>
public enum SearchSource
{
    Fts5,
    Ripgrep,
    Fallback,
}

/// <summary>
/// A single ripgrep search result.
/// </summary>
public sealed record RipgrepResult
{
    public required string Path { get; init; }
    public required int LineNumber { get; init; }
    public required string Line { get; init; }
    public SearchSource Source { get; init; } = SearchSource.Ripgrep;
}

/// <summary>
/// Wraps ripgrep for real-time disk search, regex, and fallback scenarios.
/// Results are always marked with SearchSource.Ripgrep.
/// </summary>
public sealed class RipgrepSearcher
{
    private readonly string? _ripgrepPath;

    public RipgrepSearcher(string? ripgrepPath = null, bool autoDetect = true)
    {
        _ripgrepPath = autoDetect ? ripgrepPath ?? FindRipgrep() : ripgrepPath;
    }

    public bool IsAvailable => _ripgrepPath is not null;

    /// <summary>
    /// Searches files using ripgrep with a regex pattern.
    /// </summary>
    public async Task<IReadOnlyList<RipgrepResult>> SearchAsync(
        string directory,
        string pattern,
        bool ignoreCase = false,
        string[]? globs = null,
        CancellationToken ct = default)
    {
        if (_ripgrepPath is null)
            return SearchFallback(directory, pattern, ignoreCase, ct);

        var args = new List<string>
        {
            "--line-number",
            "--no-heading",
            "--color=never",
        };

        if (ignoreCase) args.Add("-i");
        if (globs is not null)
        {
            foreach (var g in globs)
            {
                args.Add("--glob");
                args.Add(g);
            }
        }

        args.Add(pattern);
        args.Add(directory);

        var psi = new ProcessStartInfo
        {
            FileName = _ripgrepPath,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi);
        if (process is null) return [];

        var results = new List<RipgrepResult>();
        // V5-W11: .NET 10 CA2024 — avoid EndOfStream in async methods; use ReadLine loop
        string? line;
        while ((line = process.StandardOutput.ReadLine()) is not null)
        {
            ct.ThrowIfCancellationRequested();

            var parts = line.Split(':', 3);
            if (parts.Length >= 3 && int.TryParse(parts[1], out var lineNum))
            {
                results.Add(new RipgrepResult
                {
                    Path = parts[0],
                    LineNumber = lineNum,
                    Line = parts[2],
                });
            }
        }

        await process.WaitForExitAsync(ct);
        return results;
    }

    /// <summary>
    /// Fallback search using simple in-process text matching.
    /// Results marked with SearchSource.Fallback.
    /// </summary>
    private static List<RipgrepResult> SearchFallback(
        string directory, string pattern, bool ignoreCase, CancellationToken ct)
    {
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var results = new List<RipgrepResult>();

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(pattern, comparison))
                {
                    results.Add(new RipgrepResult
                    {
                        Path = file,
                        LineNumber = i + 1,
                        Line = lines[i],
                        Source = SearchSource.Fallback,
                    });
                }
            }
        }

        return results;
    }

    private static string? FindRipgrep()
    {
        var names = new[] { "rg", "rg.exe" };
        foreach (var name in names)
        {
            var path = Environment.GetEnvironmentVariable("PATH")
                ?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => Path.Combine(p, name))
                .FirstOrDefault(File.Exists);
            if (path is not null) return path;
        }
        return null;
    }
}
