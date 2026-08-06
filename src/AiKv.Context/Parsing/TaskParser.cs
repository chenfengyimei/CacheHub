using System.Text.RegularExpressions;

namespace AiKv.Context.Parsing;

/// <summary>
/// Type of operation the task implies.
/// </summary>
public enum TaskOperationType
{
    Unknown,
    Fix,
    Add,
    Refactor,
    Test,
    Review,
    Delete,
    Document,
}

/// <summary>
/// Parsed task: extracted paths, symbols, keywords, operation type.
/// First version is deterministic — no LLM calls.
/// </summary>
public sealed record ParsedTask
{
    public required string OriginalText { get; init; }
    public required string QueryParserVersion { get; init; }
    public IReadOnlyList<string> ExtractedPaths { get; init; } = [];
    public IReadOnlyList<string> ExtractedSymbols { get; init; } = [];
    public IReadOnlyList<string> ExtractedKeywords { get; init; } = [];
    public IReadOnlyList<string> ErrorStackReferences { get; init; } = [];
    public TaskOperationType OperationType { get; init; } = TaskOperationType.Unknown;
}

/// <summary>
/// Deterministic task parser: extracts paths, symbols, keywords, and operation type from task text.
/// Version 1: rule-based, no LLM.
/// </summary>
public sealed partial class TaskParser
{
    public const string Version = "deterministic-query-v1";

    public ParsedTask Parse(string taskText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskText);

        var paths = ExtractPaths(taskText);
        var symbols = ExtractSymbols(taskText);
        var keywords = ExtractKeywords(taskText);
        var errorStack = ExtractErrorStackReferences(taskText);
        var opType = DetectOperationType(taskText);

        return new ParsedTask
        {
            OriginalText = taskText,
            QueryParserVersion = Version,
            ExtractedPaths = paths,
            ExtractedSymbols = symbols,
            ExtractedKeywords = keywords,
            ErrorStackReferences = errorStack,
            OperationType = opType,
        };
    }

    private static List<string> ExtractPaths(string text)
    {
        var paths = new List<string>();
        var matches = FilePathRegex().Matches(text);
        foreach (Match m in matches)
        {
            var path = m.Value;
            if (!paths.Contains(path, StringComparer.OrdinalIgnoreCase))
                paths.Add(path);
        }
        return paths;
    }

    private static List<string> ExtractSymbols(string text)
    {
        var symbols = new List<string>();
        // PascalCase identifiers (likely class/method names)
        var matches = PascalCaseRegex().Matches(text);
        foreach (Match m in matches)
        {
            var sym = m.Value;
            // Filter out common words
            if (sym.Length >= 3 && !CommonWords.Contains(sym.ToLowerInvariant()))
                symbols.Add(sym);
        }
        // snake_case identifiers
        var snakeMatches = SnakeCaseRegex().Matches(text);
        foreach (Match m in snakeMatches)
        {
            if (!symbols.Contains(m.Value, StringComparer.OrdinalIgnoreCase))
                symbols.Add(m.Value);
        }
        return symbols.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> ExtractKeywords(string text)
    {
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "is", "are", "was", "were", "be", "been",
            "to", "in", "on", "at", "for", "of", "with", "by", "from",
            "and", "or", "not", "but", "if", "else", "when", "then",
            "this", "that", "these", "those", "it", "its",
            "fix", "add", "update", "remove", "delete", "change",
            "function", "method", "class", "file", "code",
            "i", "we", "you", "they", "he", "she",
        };

        var words = WordRegex().Matches(text)
            .Select(m => m.Value.ToLowerInvariant())
            .Where(w => w.Length >= 3 && !stopWords.Contains(w))
            .Distinct()
            .ToList();

        return words;
    }

    private static List<string> ExtractErrorStackReferences(string text)
    {
        var refs = new List<string>();
        var matches = StackTraceRegex().Matches(text);
        foreach (Match m in matches)
        {
            refs.Add(m.Value);
        }
        return refs;
    }

    private static TaskOperationType DetectOperationType(string text)
    {
        var lower = text.ToLowerInvariant();

        if (lower.Contains("fix") || lower.Contains("bug") || lower.Contains("error") || lower.Contains("broken"))
            return TaskOperationType.Fix;
        if (lower.Contains("add") || lower.Contains("create") || lower.Contains("implement") || lower.Contains("new"))
            return TaskOperationType.Add;
        if (lower.Contains("refactor") || lower.Contains("cleanup") || lower.Contains("reorganize"))
            return TaskOperationType.Refactor;
        if (lower.Contains("test") || lower.Contains("spec"))
            return TaskOperationType.Test;
        if (lower.Contains("review") || lower.Contains("audit") || lower.Contains("check"))
            return TaskOperationType.Review;
        if (lower.Contains("delete") || lower.Contains("remove"))
            return TaskOperationType.Delete;
        if (lower.Contains("document") || lower.Contains("doc") || lower.Contains("readme"))
            return TaskOperationType.Document;

        return TaskOperationType.Unknown;
    }

    private static readonly HashSet<string> CommonWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "are", "but", "not", "you", "all", "can", "her",
        "was", "one", "our", "out", "day", "had", "has", "his", "how", "its",
        "may", "new", "now", "old", "see", "way", "who", "did", "get", "let",
        "say", "she", "too", "use",
    };

    [GeneratedRegex(@"(?:src/|lib/|test/|tests/|app/|docs?/|config/|scripts?/|api/)?[\w/]+\.\w{1,10}")]
    private static partial Regex FilePathRegex();

    [GeneratedRegex(@"\b[A-Z][a-z]+(?:[A-Z][a-z]+)+\b")]
    private static partial Regex PascalCaseRegex();

    [GeneratedRegex(@"\b[a-z]+(?:_[a-z]+)+\b")]
    private static partial Regex SnakeCaseRegex();

    [GeneratedRegex(@"\b[a-z]{3,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"at\s+[\w.<>]+\([^)]*\)")]
    private static partial Regex StackTraceRegex();
}
