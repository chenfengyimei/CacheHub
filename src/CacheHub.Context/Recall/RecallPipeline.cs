namespace CacheHub.Context.Recall;

/// <summary>
/// Source of a candidate recall.
/// </summary>
public enum RecallSource
{
    FilePath,
    FileName,
    FullText,
    Symbol,
    RepoMap,
    GitDiff,
    RecentChange,
    TestRelation,
    ConfigRelation,
    ImportRelation,
}

/// <summary>
/// A candidate file recalled for context building.
/// </summary>
public sealed record CandidateFile
{
    public required string Path { get; init; }
    public required string NormalizedPath { get; init; }
    public required string Language { get; init; }
    public required long Size { get; init; }
    public IReadOnlyList<string> MatchedSymbols { get; init; } = [];
    public IReadOnlyList<RecallSource> Sources { get; init; } = [];
    public double RawScore { get; init; }
}

/// <summary>
/// Recall pipeline: collects candidates from multiple sources.
/// Supports FTS full-text recall and symbol recall when provided.
/// </summary>
public sealed class RecallPipeline
{
    /// <summary>
    /// Recalls candidate files from indexed data based on a parsed task.
    /// </summary>
    public IReadOnlyList<CandidateFile> Recall(
        Parsing.ParsedTask task,
        IReadOnlyList<IndexedFileInfo> indexedFiles,
        IReadOnlyList<string>? gitDiffFiles = null,
        string? currentFile = null,
        Func<string, IReadOnlyList<FtsMatch>>? ftsSearch = null,
        Func<string, IReadOnlyList<string>>? symbolSearch = null)
    {
        var candidates = new Dictionary<string, CandidateFileBuilder>(StringComparer.OrdinalIgnoreCase);

        // 1. Path matching
        foreach (var path in task.ExtractedPaths)
        {
            foreach (var file in indexedFiles.Where(f => f.NormalizedPath.Contains(path, StringComparison.OrdinalIgnoreCase)))
            {
                AddOrUpdate(candidates, file, RecallSource.FilePath, path);
            }
        }

        // 2. Symbol matching — use symbolSearch if provided, otherwise fall back to in-memory
        if (symbolSearch is not null)
        {
            foreach (var symbol in task.ExtractedSymbols)
            {
                var matchingPaths = symbolSearch(symbol);
                foreach (var path in matchingPaths)
                {
                    var file = indexedFiles.FirstOrDefault(f =>
                        f.NormalizedPath.Equals(path, StringComparison.OrdinalIgnoreCase));
                    if (file is not null)
                        AddOrUpdate(candidates, file, RecallSource.Symbol, symbol);
                }
            }
        }
        else
        {
            foreach (var symbol in task.ExtractedSymbols)
            {
                foreach (var file in indexedFiles.Where(f => f.Symbols.Any(s => s.Contains(symbol, StringComparison.OrdinalIgnoreCase))))
                {
                    AddOrUpdate(candidates, file, RecallSource.Symbol, symbol);
                }
            }
        }

        // 3. FullText search — use ftsSearch if provided
        if (ftsSearch is not null)
        {
            foreach (var keyword in task.ExtractedKeywords)
            {
                var ftsResults = ftsSearch(keyword);
                foreach (var match in ftsResults)
                {
                    var file = indexedFiles.FirstOrDefault(f =>
                        f.NormalizedPath.Equals(match.Path, StringComparison.OrdinalIgnoreCase));
                    if (file is not null)
                        AddOrUpdate(candidates, file, RecallSource.FullText, keyword);
                }
            }
        }
        else
        {
            // Fallback: keyword matches against path only (no FTS)
            foreach (var keyword in task.ExtractedKeywords)
            {
                foreach (var file in indexedFiles.Where(f => f.NormalizedPath.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                {
                    AddOrUpdate(candidates, file, RecallSource.FileName, keyword);
                }
            }
        }

        // 4. Git diff files
        if (gitDiffFiles is not null)
        {
            foreach (var diffPath in gitDiffFiles)
            {
                var file = indexedFiles.FirstOrDefault(f => f.NormalizedPath.EndsWith(diffPath, StringComparison.OrdinalIgnoreCase));
                if (file is not null)
                    AddOrUpdate(candidates, file, RecallSource.GitDiff, diffPath);
            }
        }

        // 5. Current file always included
        if (currentFile is not null)
        {
            var file = indexedFiles.FirstOrDefault(f => f.NormalizedPath.EndsWith(currentFile, StringComparison.OrdinalIgnoreCase));
            if (file is not null)
                AddOrUpdate(candidates, file, RecallSource.RecentChange, currentFile);
        }

        return candidates.Values.Select(b => b.Build()).ToList();
    }

    private static void AddOrUpdate(
        Dictionary<string, CandidateFileBuilder> candidates,
        IndexedFileInfo file,
        RecallSource source,
        string matchedText)
    {
        if (!candidates.TryGetValue(file.NormalizedPath, out var builder))
        {
            builder = new CandidateFileBuilder(file);
            candidates[file.NormalizedPath] = builder;
        }
        builder.AddSource(source);
        builder.AddMatchedText(matchedText);
    }

    private sealed class CandidateFileBuilder(IndexedFileInfo file)
    {
        private readonly List<RecallSource> _sources = [];
        private readonly List<string> _matched = [];

        public void AddSource(RecallSource source) => _sources.Add(source);
        public void AddMatchedText(string text) => _matched.Add(text);

        public CandidateFile Build() => new()
        {
            Path = file.Path,
            NormalizedPath = file.NormalizedPath,
            Language = file.Language,
            Size = file.Size,
            MatchedSymbols = _matched,
            Sources = _sources,
        };
    }
}

/// <summary>
/// Minimal info about an indexed file for recall.
/// </summary>
public sealed record IndexedFileInfo
{
    public required string Path { get; init; }
    public required string NormalizedPath { get; init; }
    public required string Language { get; init; }
    public required long Size { get; init; }
    public string? ContentHash { get; init; }
    public IReadOnlyList<string> Symbols { get; init; } = [];
}

/// <summary>
/// A single FTS match result for recall integration.
/// </summary>
public sealed record FtsMatch(string Path, string Language, string Snippet);
