using CacheHub.Context.Parsing;

namespace CacheHub.Context.Recall;

/// <summary>
/// A line anchor: a range of lines in a file, with the reason it was selected
/// and a confidence score. Used to drive chunk-based selection.
/// </summary>
public sealed record LineAnchor
{
    /// <summary>The start line (1-based).</summary>
    public required int StartLine { get; init; }

    /// <summary>The end line (1-based, inclusive).</summary>
    public required int EndLine { get; init; }

    /// <summary>What produced this anchor (symbol definition, FTS hit, git diff, etc.).</summary>
    public required AnchorType AnchorType { get; init; }

    /// <summary>The matched text that caused this anchor.</summary>
    public string? MatchedText { get; init; }

    /// <summary>Confidence 0..1 from the source.</summary>
    public double Confidence { get; init; } = 1.0;
}

/// <summary>
/// Type of anchor that produced a line range.
/// </summary>
public enum AnchorType
{
    SymbolDefinition,
    FtsHit,
    GitDiff,
    ErrorStack,
    UserRange,
    ImportRelation,
    FullFile,
}

/// <summary>
/// A hint from a recall source about how strongly this hit should influence ranking.
/// </summary>
public sealed record ScoreHint
{
    /// <summary>Suggested raw score contribution 0..1.</summary>
    public required double Value { get; init; }

    /// <summary>Which feature dimension this hint maps to.</summary>
    public required string Feature { get; init; }

    /// <summary>Confidence of this hint.</summary>
    public double Confidence { get; init; } = 1.0;
}

/// <summary>
/// A single recall hit: one file matched by one source.
/// Multiple hits for the same file are merged by the pipeline.
/// </summary>
public sealed record RecallHit
{
    /// <summary>The normalized path of the matched file.</summary>
    public required string NormalizedPath { get; init; }

    /// <summary>Which recall source produced this hit.</summary>
    public required RecallSource Source { get; init; }

    /// <summary>The text that was matched (keyword, symbol, path fragment).</summary>
    public string? MatchedText { get; init; }

    /// <summary>FTS snippet if available.</summary>
    public string? Snippet { get; init; }

    /// <summary>Score hints for the ranking engine.</summary>
    public IReadOnlyList<ScoreHint> ScoreHints { get; init; } = [];

    /// <summary>Line anchors for chunk selection.</summary>
    public IReadOnlyList<LineAnchor> Anchors { get; init; } = [];

    /// <summary>Confidence of this hit 0..1.</summary>
    public double Confidence { get; init; } = 1.0;
}

/// <summary>
/// Context passed to recall sources containing all shared state.
/// </summary>
public sealed record RecallContext
{
    /// <summary>The parsed task description.</summary>
    public required ParsedTask Task { get; init; }

    /// <summary>All indexed files for the active snapshot.</summary>
    public required IReadOnlyList<IndexedFileInfo> IndexedFiles { get; init; }

    /// <summary>Files changed in git diff (if available).</summary>
    public IReadOnlyList<string>? GitDiffFiles { get; init; }

    /// <summary>The file the user is currently editing.</summary>
    public string? CurrentFile { get; init; }

    /// <summary>FTS search callback (snapshot-bound).</summary>
    public Func<string, IReadOnlyList<FtsMatch>>? FtsSearch { get; init; }

    /// <summary>Symbol search callback (queries file_symbols table, returns paths only).</summary>
    public Func<string, IReadOnlyList<string>>? SymbolSearch { get; init; }

    /// <summary>Detailed symbol search callback (returns full symbol info with line ranges).</summary>
    public Func<string, IReadOnlyList<SymbolHit>>? SymbolSearchDetailed { get; init; }

    /// <summary>Import search callback (queries file_imports table).</summary>
    public Func<string, IReadOnlyList<string>>? ImportSearch { get; init; }

    /// <summary>Relation search callback (queries file_relations table for a given file path).</summary>
    public Func<string, IReadOnlyList<RelationHit>>? RelationSearch { get; init; }

    /// <summary>Semantic search callback (queries historical references for similar tasks/errors).</summary>
    public Func<string, IReadOnlyList<SemanticHit>>? SemanticSearch { get; init; }

    /// <summary>Paths already matched by earlier sources (for expansion).</summary>
    public IReadOnlySet<string> AlreadyMatchedPaths { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Composable recall source interface. Each implementation handles one source type
/// (path, FTS, symbol, git diff, import relation, etc.).
/// Sources are executed sequentially; each can see what earlier sources already matched.
/// </summary>
public interface IRecallSource
{
    /// <summary>Which RecallSource enum value this implementation produces.</summary>
    RecallSource SourceType { get; }

    /// <summary>Whether this source is enabled (can be disabled for degradation testing).</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Executes recall for this source. Returns hits for files matched by this source only.
    /// </summary>
    IReadOnlyList<RecallHit> Recall(RecallContext context);
}

/// <summary>
/// A symbol search result with line range information.
/// Used by SymbolRecallSource to generate LineAnchors.
/// </summary>
public sealed record SymbolHit
{
    public required string NormalizedPath { get; init; }
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required bool ExactMatch { get; init; }
}

/// <summary>
/// A relation hit: a relation found in a file pointing to a target symbol.
/// Used by RelationRecallSource to expand call/reference chains.
/// </summary>
public sealed record RelationHit
{
    public required string TargetName { get; init; }
    public required string RelationType { get; init; }
    public required string Relation { get; init; }
    public required double Confidence { get; init; }
}

/// <summary>
/// A semantic hit: a historical reference similar to the current task.
/// Used by SemanticRecallSource to provide reference-only context.
/// </summary>
public sealed record SemanticHit
{
    public required string Content { get; init; }
    public required double Similarity { get; init; }
    public required string ReferenceType { get; init; }
    public string? TaskDescription { get; init; }
}
