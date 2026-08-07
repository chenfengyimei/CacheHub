using CacheHub.Context.Parsing;
using CacheHub.Context.Recall.Sources;

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
    Relation,
    DirectoryFallback,
}

/// <summary>
/// Evidence record: which source matched what text for a candidate.
/// </summary>
public sealed record SourceEvidence
{
    public required RecallSource Source { get; init; }
    public required string MatchedText { get; init; }
    public string? Snippet { get; init; }
    public double Confidence { get; init; } = 1.0;
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
    public IReadOnlyList<SourceEvidence> Evidence { get; init; } = [];
    public IReadOnlyList<LineAnchor> Anchors { get; init; } = [];
    public IReadOnlyList<ScoreHint> ScoreHints { get; init; } = [];
    public double RawScore { get; init; }
}

/// <summary>
/// Options for recall pipeline behaviour.
/// </summary>
public sealed record RecallOptions
{
    public int MaxCandidates { get; init; } = 200;
    public bool EnableDirectoryFallback { get; init; } = true;
    public bool EnableTestRelation { get; init; } = true;
    public bool EnableConfigRelation { get; init; } = true;
    public bool EnableImportExpansion { get; init; } = true;
}

/// <summary>
/// Recall pipeline: collects candidates from multiple composable IRecallSource instances.
/// Sources are executed sequentially; each can see what earlier sources already matched.
/// Backward-compatible: the old Recall() signature still works by constructing default sources.
/// </summary>
public sealed class RecallPipeline
{
    private readonly List<IRecallSource> _sources;

    /// <summary>
    /// Creates a pipeline with default recall sources.
    /// </summary>
    public RecallPipeline() : this(CreateDefaultSources()) { }

    /// <summary>
    /// Creates a pipeline with custom recall sources.
    /// </summary>
    public RecallPipeline(IReadOnlyList<IRecallSource> sources)
    {
        _sources = sources.ToList();
    }

    /// <summary>
    /// Creates the default set of recall sources in execution order.
    /// </summary>
    public static IReadOnlyList<IRecallSource> CreateDefaultSources() =>
    [
        new PathRecallSource(),
        new SymbolRecallSource(),
        new FullTextRecallSource(),
        new GitDiffRecallSource(),
        new CurrentFileRecallSource(),
        new ImportRelationRecallSource(),
        new RelationRecallSource(),
        new TestRelationRecallSource(),
        new ConfigRelationRecallSource(),
        new RepoMapRecallSource(),
        new DirectoryFallbackRecallSource(),
    ];

    /// <summary>
    /// Recalls candidate files using the configured IRecallSource instances.
    /// </summary>
    public IReadOnlyList<CandidateFile> Recall(
        ParsedTask task,
        IReadOnlyList<IndexedFileInfo> indexedFiles,
        IReadOnlyList<string>? gitDiffFiles = null,
        string? currentFile = null,
        Func<string, IReadOnlyList<FtsMatch>>? ftsSearch = null,
        Func<string, IReadOnlyList<string>>? symbolSearch = null,
        Func<string, IReadOnlyList<string>>? importSearch = null,
        RecallOptions? options = null,
        Func<string, IReadOnlyList<SymbolHit>>? symbolSearchDetailed = null,
        Func<string, IReadOnlyList<RelationHit>>? relationSearch = null)
    {
        var opts = options ?? new RecallOptions();
        var builders = new Dictionary<string, CandidateFileBuilder>(StringComparer.OrdinalIgnoreCase);
        var matchedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Build the context for all sources
        var context = new RecallContext
        {
            Task = task,
            IndexedFiles = indexedFiles,
            GitDiffFiles = gitDiffFiles,
            CurrentFile = currentFile,
            FtsSearch = ftsSearch,
            SymbolSearch = symbolSearch,
            SymbolSearchDetailed = symbolSearchDetailed,
            ImportSearch = importSearch,
            RelationSearch = relationSearch,
            AlreadyMatchedPaths = matchedPaths,
        };

        // Execute each enabled source in order
        foreach (var source in _sources)
        {
            if (!source.IsEnabled) continue;

            // Skip disabled sources based on options
            if (!opts.EnableDirectoryFallback && source.SourceType == RecallSource.DirectoryFallback) continue;
            if (!opts.EnableTestRelation && source.SourceType == RecallSource.TestRelation) continue;
            if (!opts.EnableConfigRelation && source.SourceType == RecallSource.ConfigRelation) continue;
            if (!opts.EnableImportExpansion && source.SourceType == RecallSource.ImportRelation) continue;

            // Update context with currently matched paths for expansion sources
            context = context with { AlreadyMatchedPaths = matchedPaths };

            var hits = source.Recall(context);
            foreach (var hit in hits)
            {
                AddOrUpdate(builders, indexedFiles, hit);
                matchedPaths.Add(hit.NormalizedPath);
            }
        }

        var result = builders.Values.Select(b => b.Build()).ToList();

        if (opts.MaxCandidates > 0 && result.Count > opts.MaxCandidates)
        {
            result = result.Take(opts.MaxCandidates).ToList();
        }

        return result;
    }

    private static void AddOrUpdate(
        Dictionary<string, CandidateFileBuilder> candidates,
        IReadOnlyList<IndexedFileInfo> indexedFiles,
        RecallHit hit)
    {
        var file = indexedFiles.FirstOrDefault(f =>
            f.NormalizedPath.Equals(hit.NormalizedPath, StringComparison.OrdinalIgnoreCase));

        if (!candidates.TryGetValue(hit.NormalizedPath, out var builder))
        {
            builder = new CandidateFileBuilder(file ?? new IndexedFileInfo
            {
                Path = hit.NormalizedPath,
                NormalizedPath = hit.NormalizedPath,
                Language = "unknown",
                Size = 0,
            });
            candidates[hit.NormalizedPath] = builder;
        }

        builder.AddSource(hit.Source);
        builder.AddEvidence(new SourceEvidence
        {
            Source = hit.Source,
            MatchedText = hit.MatchedText ?? "",
            Snippet = hit.Snippet,
            Confidence = hit.Confidence,
        });

        if (hit.Source == RecallSource.Symbol)
            builder.AddSymbolMatch(hit.MatchedText ?? "");
        else
            builder.AddMatchedText(hit.MatchedText ?? "");

        foreach (var anchor in hit.Anchors)
            builder.AddAnchor(anchor);

        foreach (var hint in hit.ScoreHints)
            builder.AddScoreHint(hint);
    }

    private sealed class CandidateFileBuilder(IndexedFileInfo file)
    {
        internal IndexedFileInfo File => file;
        private readonly List<RecallSource> _sources = [];
        private readonly List<string> _matched = [];
        private readonly List<string> _symbolMatches = [];
        private readonly List<SourceEvidence> _evidence = [];
        private readonly List<LineAnchor> _anchors = [];
        private readonly List<ScoreHint> _scoreHints = [];

        public void AddSource(RecallSource source)
        {
            if (!_sources.Contains(source))
                _sources.Add(source);
        }

        public void AddMatchedText(string text)
        {
            if (!_matched.Contains(text, StringComparer.OrdinalIgnoreCase))
                _matched.Add(text);
        }

        public void AddSymbolMatch(string symbol)
        {
            if (!_symbolMatches.Contains(symbol, StringComparer.OrdinalIgnoreCase))
                _symbolMatches.Add(symbol);
            AddMatchedText(symbol);
        }

        public void AddEvidence(SourceEvidence evidence) => _evidence.Add(evidence);
        public void AddAnchor(LineAnchor anchor) => _anchors.Add(anchor);
        public void AddScoreHint(ScoreHint hint) => _scoreHints.Add(hint);

        public CandidateFile Build() => new()
        {
            Path = file.Path,
            NormalizedPath = file.NormalizedPath,
            Language = file.Language,
            Size = file.Size,
            MatchedSymbols = _symbolMatches,
            Sources = _sources,
            Evidence = _evidence,
            Anchors = _anchors,
            ScoreHints = _scoreHints,
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
public sealed record FtsMatch(string Path, string Language, string Snippet, double RankScore = 0, int? HitLine = null);
