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
    public double RawScore { get; init; }
}

/// <summary>
/// Options for recall pipeline behaviour.
/// </summary>
public sealed record RecallOptions
{
    /// <summary>Maximum candidates to return. 0 = unlimited.</summary>
    public int MaxCandidates { get; init; } = 200;

    /// <summary>If true, fall back to directory-based recall when no candidates found.</summary>
    public bool EnableDirectoryFallback { get; init; } = true;

    /// <summary>If true, discover test files related to matched source files by convention.</summary>
    public bool EnableTestRelation { get; init; } = true;

    /// <summary>If true, discover config files in directories of matched files.</summary>
    public bool EnableConfigRelation { get; init; } = true;

    /// <summary>If true, expand recall via import graph when an importSearch callback is provided.</summary>
    public bool EnableImportExpansion { get; init; } = true;
}

/// <summary>
/// Recall pipeline: collects candidates from multiple sources.
/// Supports FTS full-text recall, symbol recall, import/test/config relation recall,
/// and safe directory-based fallback when no candidates are found.
/// </summary>
public sealed class RecallPipeline
{
    private static readonly HashSet<string> ConfigFilePatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "appsettings.json", "appsettings.development.json", "appsettings.production.json",
        ".env", "config.json", "config.yaml", "config.yml", "config.toml",
        "package.json", "tsconfig.json", "webpack.config.js", "vite.config.ts",
        ".editorconfig", "dockerfile", "docker-compose.yml", "docker-compose.yaml",
        "makefile", "cmakelists.txt", "cargo.toml", "go.mod", "pom.xml",
        "build.gradle", "directory.build.props", "global.json",
    };

    private static readonly HashSet<string> TestFileSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ".test.ts", ".test.js", ".test.tsx", ".test.jsx",
        ".spec.ts", ".spec.js", ".spec.tsx", ".spec.jsx",
        "test.cs", "tests.cs", "_test.go", "_test.py",
        ".test.py", "_test.rs",
    };

    private static readonly HashSet<string> TestDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "tests", "test", "__tests__", "spec", "specs",
    };

    /// <summary>
    /// Recalls candidate files from indexed data based on a parsed task.
    /// </summary>
    public IReadOnlyList<CandidateFile> Recall(
        Parsing.ParsedTask task,
        IReadOnlyList<IndexedFileInfo> indexedFiles,
        IReadOnlyList<string>? gitDiffFiles = null,
        string? currentFile = null,
        Func<string, IReadOnlyList<FtsMatch>>? ftsSearch = null,
        Func<string, IReadOnlyList<string>>? symbolSearch = null,
        Func<string, IReadOnlyList<string>>? importSearch = null,
        RecallOptions? options = null)
    {
        var opts = options ?? new RecallOptions();
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
                        AddOrUpdate(candidates, file, RecallSource.FullText, keyword, match.Snippet);
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

        // 6. Import relation expansion — expand to files that import matched symbols
        if (opts.EnableImportExpansion && importSearch is not null)
        {
            var matchedSymbols = candidates.Values
                .SelectMany(c => c.GetEvidence().Where(e => e.Source == RecallSource.Symbol).Select(e => e.MatchedText))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var symbol in matchedSymbols)
            {
                var importingPaths = importSearch(symbol);
                foreach (var path in importingPaths)
                {
                    var file = indexedFiles.FirstOrDefault(f =>
                        f.NormalizedPath.Equals(path, StringComparison.OrdinalIgnoreCase));
                    if (file is not null)
                        AddOrUpdate(candidates, file, RecallSource.ImportRelation, symbol,
                            confidence: 0.7);
                }
            }
        }

        // 7. Test relation — find test files related to matched source files
        if (opts.EnableTestRelation && candidates.Count > 0)
        {
            var matchedPaths = candidates.Values.Select(c => c.File.NormalizedPath).ToList();
            foreach (var file in indexedFiles)
            {
                if (IsTestFile(file.NormalizedPath) && !candidates.ContainsKey(file.NormalizedPath))
                {
                    // Check if test file corresponds to any matched source file
                    var baseName = GetTestBaseName(file.NormalizedPath);
                    if (matchedPaths.Any(p =>
                        p.Contains(baseName, StringComparison.OrdinalIgnoreCase) ||
                        baseName.Contains(GetFileBaseName(p), StringComparison.OrdinalIgnoreCase)))
                    {
                        AddOrUpdate(candidates, file, RecallSource.TestRelation, file.NormalizedPath,
                            confidence: 0.6);
                    }
                }
            }
        }

        // 8. Config relation — find config files in directories of matched files
        if (opts.EnableConfigRelation && candidates.Count > 0)
        {
            var matchedDirs = candidates.Values
                .Select(c => Path.GetDirectoryName(c.File.NormalizedPath))
                .Where(d => !string.IsNullOrEmpty(d))
                .Select(d => d!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var file in indexedFiles)
            {
                if (IsConfigFile(file.NormalizedPath) && !candidates.ContainsKey(file.NormalizedPath))
                {
                    var fileDir = Path.GetDirectoryName(file.NormalizedPath) ?? "";
                    if (matchedDirs.Any(d => fileDir.StartsWith(d, StringComparison.OrdinalIgnoreCase) ||
                        d.StartsWith(fileDir, StringComparison.OrdinalIgnoreCase)))
                    {
                        AddOrUpdate(candidates, file, RecallSource.ConfigRelation, file.NormalizedPath,
                            confidence: 0.5);
                    }
                }
            }
        }

        // 9. Directory-based safe fallback when no candidates found
        if (candidates.Count == 0 && opts.EnableDirectoryFallback && indexedFiles.Count > 0)
        {
            // Include top-level entry-point files and recently modified files
            var fallbackFiles = indexedFiles
                .Where(f => IsEntryPointFile(f.NormalizedPath))
                .Take(10)
                .ToList();

            if (fallbackFiles.Count == 0)
            {
                // Last resort: include the smallest files (likely most relevant)
                fallbackFiles = indexedFiles
                    .OrderBy(f => f.Size)
                    .Take(5)
                    .ToList();
            }

            foreach (var file in fallbackFiles)
            {
                AddOrUpdate(candidates, file, RecallSource.DirectoryFallback, file.NormalizedPath,
                    confidence: 0.1);
            }
        }

        var result = candidates.Values.Select(b => b.Build()).ToList();

        if (opts.MaxCandidates > 0 && result.Count > opts.MaxCandidates)
        {
            result = result.Take(opts.MaxCandidates).ToList();
        }

        return result;
    }

    private static void AddOrUpdate(
        Dictionary<string, CandidateFileBuilder> candidates,
        IndexedFileInfo file,
        RecallSource source,
        string matchedText,
        string? snippet = null,
        double confidence = 1.0)
    {
        if (!candidates.TryGetValue(file.NormalizedPath, out var builder))
        {
            builder = new CandidateFileBuilder(file);
            candidates[file.NormalizedPath] = builder;
        }
        builder.AddSource(source);
        if (source == RecallSource.Symbol)
            builder.AddSymbolMatch(matchedText);
        else
            builder.AddMatchedText(matchedText);
        builder.AddEvidence(new SourceEvidence
        {
            Source = source,
            MatchedText = matchedText,
            Snippet = snippet,
            Confidence = confidence,
        });
    }

    private static bool IsTestFile(string normalizedPath)
    {
        var lower = normalizedPath.ToLowerInvariant();
        if (TestFileSuffixes.Any(s => lower.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
            return true;

        var dir = Path.GetDirectoryName(normalizedPath) ?? "";
        var parts = dir.Split('/', '\\');
        return parts.Any(p => TestDirectoryNames.Contains(p));
    }

    private static string GetTestBaseName(string testFilePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(testFilePath);
        // Remove .test, .spec, _test, Test suffix
        foreach (var suffix in new[] { ".test", ".spec", "_test", "test", "Test", "_spec" })
        {
            if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                fileName = fileName[..^suffix.Length];
                break;
            }
        }
        return fileName;
    }

    private static string GetFileBaseName(string filePath)
    {
        return Path.GetFileNameWithoutExtension(filePath);
    }

    private static bool IsConfigFile(string normalizedPath)
    {
        var fileName = Path.GetFileName(normalizedPath);
        return ConfigFilePatterns.Contains(fileName);
    }

    private static bool IsEntryPointFile(string normalizedPath)
    {
        var fileName = Path.GetFileName(normalizedPath);
        return fileName.Equals("Program.cs", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("Main.cs", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("index.ts", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("index.js", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("app.ts", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("app.js", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("main.py", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("main.go", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("main.rs", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("Startup.cs", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class CandidateFileBuilder(IndexedFileInfo file)
    {
        internal IndexedFileInfo File => file;
        private readonly List<RecallSource> _sources = [];
        private readonly List<string> _matched = [];
        private readonly List<string> _symbolMatches = [];
        private readonly List<SourceEvidence> _evidence = [];

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
        public List<SourceEvidence> GetEvidence() => _evidence;

        public CandidateFile Build() => new()
        {
            Path = file.Path,
            NormalizedPath = file.NormalizedPath,
            Language = file.Language,
            Size = file.Size,
            MatchedSymbols = _symbolMatches,
            Sources = _sources,
            Evidence = _evidence,
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
