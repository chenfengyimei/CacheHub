using CacheHub.Context.Parsing;

namespace CacheHub.Context.Recall.Sources;

/// <summary>
/// Path recall source: matches extracted file paths from the task text against indexed file paths.
/// </summary>
public sealed class PathRecallSource : IRecallSource
{
    public RecallSource SourceType => RecallSource.FilePath;
    public bool IsEnabled { get; init; } = true;

    public IReadOnlyList<RecallHit> Recall(RecallContext context)
    {
        var hits = new List<RecallHit>();

        foreach (var path in context.Task.ExtractedPaths)
        {
            foreach (var file in context.IndexedFiles)
            {
                if (file.NormalizedPath.Contains(path, StringComparison.OrdinalIgnoreCase))
                {
                    hits.Add(new RecallHit
                    {
                        NormalizedPath = file.NormalizedPath,
                        Source = SourceType,
                        MatchedText = path,
                        Confidence = 1.0,
                        ScoreHints =
                        [
                            new ScoreHint { Value = 1.0, Feature = "PathMatch", Confidence = 1.0 },
                        ],
                    });
                }
            }
        }

        return hits;
    }
}

/// <summary>
/// FTS full-text recall source: executes FTS5 queries for task keywords.
/// Requires the ftsSearch callback; falls back to path-based keyword matching if not provided.
/// </summary>
public sealed class FullTextRecallSource : IRecallSource
{
    public RecallSource SourceType => RecallSource.FullText;
    public bool IsEnabled { get; init; } = true;

    public IReadOnlyList<RecallHit> Recall(RecallContext context)
    {
        var hits = new List<RecallHit>();

        if (context.FtsSearch is not null)
        {
            foreach (var keyword in context.Task.ExtractedKeywords)
            {
                var ftsResults = context.FtsSearch(keyword);
                foreach (var match in ftsResults)
                {
                    hits.Add(new RecallHit
                    {
                        NormalizedPath = match.Path,
                        Source = SourceType,
                        MatchedText = keyword,
                        Snippet = match.Snippet,
                        Confidence = 0.9,
                        ScoreHints =
                        [
                            new ScoreHint { Value = 1.0, Feature = "TextMatch", Confidence = 0.9 },
                        ],
                        Anchors = ExtractSnippetAnchors(match.Snippet),
                    });
                }
            }
        }
        else
        {
            // Fallback: keyword matches against path only (no FTS)
            foreach (var keyword in context.Task.ExtractedKeywords)
            {
                foreach (var file in context.IndexedFiles)
                {
                    if (file.NormalizedPath.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        hits.Add(new RecallHit
                        {
                            NormalizedPath = file.NormalizedPath,
                            Source = RecallSource.FileName,
                            MatchedText = keyword,
                            Confidence = 0.3,
                            ScoreHints =
                            [
                                new ScoreHint { Value = 0.5, Feature = "TextMatch", Confidence = 0.3 },
                            ],
                        });
                    }
                }
            }
        }

        return hits;
    }

    private static IReadOnlyList<LineAnchor> ExtractSnippetAnchors(string? snippet)
    {
        if (string.IsNullOrEmpty(snippet)) return [];
        // FTS snippets contain "..." delimiters; we can't reliably extract line numbers
        // without the BM25 offset. Return a full-file anchor as fallback.
        return [new LineAnchor { StartLine = 1, EndLine = 1, AnchorType = AnchorType.FtsHit, MatchedText = snippet, Confidence = 0.7 }];
    }
}

/// <summary>
/// Symbol recall source: queries file_symbols for extracted symbol names.
/// </summary>
public sealed class SymbolRecallSource : IRecallSource
{
    public RecallSource SourceType => RecallSource.Symbol;
    public bool IsEnabled { get; init; } = true;

    public IReadOnlyList<RecallHit> Recall(RecallContext context)
    {
        var hits = new List<RecallHit>();

        // R4-W004: Use detailed symbol search (with line ranges) when available
        if (context.SymbolSearchDetailed is not null)
        {
            foreach (var symbol in context.Task.ExtractedSymbols)
            {
                var results = context.SymbolSearchDetailed(symbol);
                foreach (var sym in results)
                {
                    hits.Add(new RecallHit
                    {
                        NormalizedPath = sym.NormalizedPath,
                        Source = SourceType,
                        MatchedText = sym.Name,
                        Confidence = sym.ExactMatch ? 1.0 : 0.7,
                        ScoreHints =
                        [
                            new ScoreHint { Value = sym.ExactMatch ? 1.0 : 0.7, Feature = "SymbolMatch", Confidence = sym.ExactMatch ? 1.0 : 0.7 },
                        ],
                        Anchors =
                        [
                            new LineAnchor
                            {
                                StartLine = sym.StartLine,
                                EndLine = sym.EndLine,
                                AnchorType = AnchorType.SymbolDefinition,
                                MatchedText = sym.Name,
                                Confidence = sym.ExactMatch ? 1.0 : 0.7,
                            },
                        ],
                    });
                }
            }
        }
        else if (context.SymbolSearch is not null)
        {
            // Fallback: path-only symbol search (no line ranges)
            foreach (var symbol in context.Task.ExtractedSymbols)
            {
                var matchingPaths = context.SymbolSearch(symbol);
                foreach (var path in matchingPaths)
                {
                    hits.Add(new RecallHit
                    {
                        NormalizedPath = path,
                        Source = SourceType,
                        MatchedText = symbol,
                        Confidence = 1.0,
                        ScoreHints =
                        [
                            new ScoreHint { Value = 1.0, Feature = "SymbolMatch", Confidence = 1.0 },
                        ],
                    });
                }
            }
        }
        else
        {
            // Fallback: in-memory symbol search against IndexedFileInfo.Symbols
            foreach (var symbol in context.Task.ExtractedSymbols)
            {
                foreach (var file in context.IndexedFiles)
                {
                    if (file.Symbols.Any(s => s.Contains(symbol, StringComparison.OrdinalIgnoreCase)))
                    {
                        hits.Add(new RecallHit
                        {
                            NormalizedPath = file.NormalizedPath,
                            Source = SourceType,
                            MatchedText = symbol,
                            Confidence = 0.8,
                            ScoreHints =
                            [
                                new ScoreHint { Value = 0.8, Feature = "SymbolMatch", Confidence = 0.8 },
                            ],
                        });
                    }
                }
            }
        }

        return hits;
    }
}

/// <summary>
/// Git diff recall source: includes files that appear in the git diff.
/// </summary>
public sealed class GitDiffRecallSource : IRecallSource
{
    public RecallSource SourceType => RecallSource.GitDiff;
    public bool IsEnabled { get; init; } = true;

    public IReadOnlyList<RecallHit> Recall(RecallContext context)
    {
        var hits = new List<RecallHit>();

        if (context.GitDiffFiles is null) return hits;

        foreach (var diffPath in context.GitDiffFiles)
        {
            var file = context.IndexedFiles.FirstOrDefault(f =>
                f.NormalizedPath.EndsWith(diffPath, StringComparison.OrdinalIgnoreCase));
            if (file is not null)
            {
                hits.Add(new RecallHit
                {
                    NormalizedPath = file.NormalizedPath,
                    Source = SourceType,
                    MatchedText = diffPath,
                    Confidence = 1.0,
                    ScoreHints =
                    [
                        new ScoreHint { Value = 1.0, Feature = "GitDiff", Confidence = 1.0 },
                    ],
                });
            }
        }

        return hits;
    }
}

/// <summary>
/// Current file recall source: always includes the file the user is currently editing.
/// </summary>
public sealed class CurrentFileRecallSource : IRecallSource
{
    public RecallSource SourceType => RecallSource.RecentChange;
    public bool IsEnabled { get; init; } = true;

    public IReadOnlyList<RecallHit> Recall(RecallContext context)
    {
        var hits = new List<RecallHit>();

        if (context.CurrentFile is null) return hits;

        var file = context.IndexedFiles.FirstOrDefault(f =>
            f.NormalizedPath.EndsWith(context.CurrentFile, StringComparison.OrdinalIgnoreCase));
        if (file is not null)
        {
            hits.Add(new RecallHit
            {
                NormalizedPath = file.NormalizedPath,
                Source = SourceType,
                MatchedText = context.CurrentFile,
                Confidence = 1.0,
                ScoreHints =
                [
                    new ScoreHint { Value = 1.0, Feature = "CurrentFileRelation", Confidence = 1.0 },
                    new ScoreHint { Value = 1.0, Feature = "RecentChange", Confidence = 1.0 },
                ],
            });
        }

        return hits;
    }
}

/// <summary>
/// Import relation recall source: expands from matched symbols to files that import them.
/// </summary>
public sealed class ImportRelationRecallSource : IRecallSource
{
    public RecallSource SourceType => RecallSource.ImportRelation;
    public bool IsEnabled { get; init; } = true;

    public IReadOnlyList<RecallHit> Recall(RecallContext context)
    {
        var hits = new List<RecallHit>();

        if (!IsEnabled || context.ImportSearch is null) return hits;

        // R4-W005: Expand from both matched file symbols AND task-extracted symbols
        var matchedSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Symbols from already matched files
        foreach (var path in context.AlreadyMatchedPaths)
        {
            var file = context.IndexedFiles.FirstOrDefault(f =>
                f.NormalizedPath.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (file is not null)
            {
                foreach (var sym in file.Symbols)
                    matchedSymbols.Add(sym);
            }
        }

        // 2. Symbols directly extracted from the task text
        foreach (var sym in context.Task.ExtractedSymbols)
            matchedSymbols.Add(sym);

        foreach (var symbol in matchedSymbols)
        {
            var importingPaths = context.ImportSearch(symbol);
            foreach (var path in importingPaths)
            {
                if (context.AlreadyMatchedPaths.Contains(path)) continue;

                hits.Add(new RecallHit
                {
                    NormalizedPath = path,
                    Source = SourceType,
                    MatchedText = symbol,
                    Confidence = 0.7,
                    ScoreHints =
                    [
                        new ScoreHint { Value = 1.0, Feature = "ImportRelation", Confidence = 0.7 },
                    ],
                    Anchors =
                    [
                        new LineAnchor
                        {
                            StartLine = 1,
                            EndLine = 1,
                            AnchorType = AnchorType.ImportRelation,
                            MatchedText = symbol,
                            Confidence = 0.7,
                        },
                    ],
                });
            }
        }

        return hits;
    }
}

/// <summary>
/// Test relation recall source: discovers test files related to matched source files by naming convention.
/// </summary>
public sealed class TestRelationRecallSource : IRecallSource
{
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

    public RecallSource SourceType => RecallSource.TestRelation;
    public bool IsEnabled { get; init; } = true;

    public IReadOnlyList<RecallHit> Recall(RecallContext context)
    {
        var hits = new List<RecallHit>();
        if (!IsEnabled || context.AlreadyMatchedPaths.Count == 0) return hits;

        var matchedPaths = context.AlreadyMatchedPaths.ToList();

        foreach (var file in context.IndexedFiles)
        {
            if (context.AlreadyMatchedPaths.Contains(file.NormalizedPath)) continue;
            if (!IsTestFile(file.NormalizedPath)) continue;

            var baseName = GetTestBaseName(file.NormalizedPath);
            if (matchedPaths.Any(p =>
                p.Contains(baseName, StringComparison.OrdinalIgnoreCase) ||
                baseName.Contains(GetFileBaseName(p), StringComparison.OrdinalIgnoreCase)))
            {
                hits.Add(new RecallHit
                {
                    NormalizedPath = file.NormalizedPath,
                    Source = SourceType,
                    MatchedText = file.NormalizedPath,
                    Confidence = 0.6,
                    ScoreHints =
                    [
                        new ScoreHint { Value = 1.0, Feature = "TestRelation", Confidence = 0.6 },
                    ],
                });
            }
        }

        return hits;
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

    private static string GetFileBaseName(string filePath) => Path.GetFileNameWithoutExtension(filePath);
}

/// <summary>
/// Config relation recall source: discovers config files in directories of matched files.
/// </summary>
public sealed class ConfigRelationRecallSource : IRecallSource
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

    public RecallSource SourceType => RecallSource.ConfigRelation;
    public bool IsEnabled { get; init; } = true;

    public IReadOnlyList<RecallHit> Recall(RecallContext context)
    {
        var hits = new List<RecallHit>();
        if (!IsEnabled || context.AlreadyMatchedPaths.Count == 0) return hits;

        var matchedDirs = context.AlreadyMatchedPaths
            .Select(p => Path.GetDirectoryName(p))
            .Where(d => !string.IsNullOrEmpty(d))
            .Select(d => d!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var file in context.IndexedFiles)
        {
            if (context.AlreadyMatchedPaths.Contains(file.NormalizedPath)) continue;
            if (!IsConfigFile(file.NormalizedPath)) continue;

            var fileDir = Path.GetDirectoryName(file.NormalizedPath) ?? "";
            if (matchedDirs.Any(d => fileDir.StartsWith(d, StringComparison.OrdinalIgnoreCase) ||
                d.StartsWith(fileDir, StringComparison.OrdinalIgnoreCase)))
            {
                hits.Add(new RecallHit
                {
                    NormalizedPath = file.NormalizedPath,
                    Source = SourceType,
                    MatchedText = file.NormalizedPath,
                    Confidence = 0.5,
                    ScoreHints =
                    [
                        new ScoreHint { Value = 1.0, Feature = "ConfigRelation", Confidence = 0.5 },
                    ],
                });
            }
        }

        return hits;
    }

    private static bool IsConfigFile(string normalizedPath)
    {
        var fileName = Path.GetFileName(normalizedPath);
        return ConfigFilePatterns.Contains(fileName);
    }
}

/// <summary>
/// Repo Map recall source: provides structural context as a low-cost adjacency source.
/// When no direct candidates found, suggests files from important directories.
/// </summary>
public sealed class RepoMapRecallSource : IRecallSource
{
    public RecallSource SourceType => RecallSource.RepoMap;
    public bool IsEnabled { get; init; } = true;

    public IReadOnlyList<RecallHit> Recall(RecallContext context)
    {
        var hits = new List<RecallHit>();
        if (!IsEnabled) return hits;

        // RepoMap as adjacency: if some files are already matched,
        // suggest files in the same directories (structural proximity)
        if (context.AlreadyMatchedPaths.Count == 0) return hits;

        var matchedDirs = context.AlreadyMatchedPaths
            .Select(p => Path.GetDirectoryName(p))
            .Where(d => !string.IsNullOrEmpty(d))
            .Select(d => d!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var file in context.IndexedFiles)
        {
            if (context.AlreadyMatchedPaths.Contains(file.NormalizedPath)) continue;

            var fileDir = Path.GetDirectoryName(file.NormalizedPath) ?? "";
            if (matchedDirs.Contains(fileDir))
            {
                hits.Add(new RecallHit
                {
                    NormalizedPath = file.NormalizedPath,
                    Source = SourceType,
                    MatchedText = fileDir,
                    Confidence = 0.3,
                    ScoreHints =
                    [
                        new ScoreHint { Value = 0.3, Feature = "RepoMap", Confidence = 0.3 },
                    ],
                });
            }
        }

        return hits;
    }
}

/// <summary>
/// Directory fallback recall source: when no candidates found, includes entry-point files.
/// </summary>
public sealed class DirectoryFallbackRecallSource : IRecallSource
{
    public RecallSource SourceType => RecallSource.DirectoryFallback;
    public bool IsEnabled { get; init; } = true;

    public IReadOnlyList<RecallHit> Recall(RecallContext context)
    {
        var hits = new List<RecallHit>();
        if (!IsEnabled || context.AlreadyMatchedPaths.Count > 0) return hits;
        if (context.IndexedFiles.Count == 0) return hits;

        var fallbackFiles = context.IndexedFiles
            .Where(f => IsEntryPointFile(f.NormalizedPath))
            .Take(10)
            .ToList();

        if (fallbackFiles.Count == 0)
        {
            fallbackFiles = context.IndexedFiles
                .OrderBy(f => f.Size)
                .Take(5)
                .ToList();
        }

        foreach (var file in fallbackFiles)
        {
            hits.Add(new RecallHit
            {
                NormalizedPath = file.NormalizedPath,
                Source = SourceType,
                MatchedText = file.NormalizedPath,
                Confidence = 0.1,
                ScoreHints =
                [
                    new ScoreHint { Value = 0.1, Feature = "DirectoryFallback", Confidence = 0.1 },
                ],
            });
        }

        return hits;
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
}
