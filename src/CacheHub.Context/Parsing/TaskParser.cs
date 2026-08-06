using System.Text.RegularExpressions;

namespace CacheHub.Context.Parsing;

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
/// Includes Unicode/Chinese support and independent channels for
/// code identifiers, error stacks, and paths.
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
/// Version 2: Unicode/Chinese support, code identifier independent channel, Windows path support.
/// </summary>
public sealed partial class TaskParser
{
    public const string Version = "deterministic-query-v2";

    // Chinese operation keywords
    private static readonly Dictionary<string, TaskOperationType> ChineseOps = new()
    {
        ["修复"] = TaskOperationType.Fix,
        ["修"] = TaskOperationType.Fix,
        ["解决"] = TaskOperationType.Fix,
        ["bug"] = TaskOperationType.Fix,
        ["错误"] = TaskOperationType.Fix,
        ["异常"] = TaskOperationType.Fix,
        ["添加"] = TaskOperationType.Add,
        ["新增"] = TaskOperationType.Add,
        ["实现"] = TaskOperationType.Add,
        ["创建"] = TaskOperationType.Add,
        ["新建"] = TaskOperationType.Add,
        ["重构"] = TaskOperationType.Refactor,
        ["优化"] = TaskOperationType.Refactor,
        ["整理"] = TaskOperationType.Refactor,
        ["测试"] = TaskOperationType.Test,
        ["删除"] = TaskOperationType.Delete,
        ["移除"] = TaskOperationType.Delete,
        ["文档"] = TaskOperationType.Document,
        ["说明"] = TaskOperationType.Document,
    };

    // English operation keywords
    private static readonly Dictionary<string, TaskOperationType> EnglishOps = new()
    {
        ["fix"] = TaskOperationType.Fix,
        ["bug"] = TaskOperationType.Fix,
        ["error"] = TaskOperationType.Fix,
        ["broken"] = TaskOperationType.Fix,
        ["add"] = TaskOperationType.Add,
        ["create"] = TaskOperationType.Add,
        ["implement"] = TaskOperationType.Add,
        ["new"] = TaskOperationType.Add,
        ["refactor"] = TaskOperationType.Refactor,
        ["cleanup"] = TaskOperationType.Refactor,
        ["reorganize"] = TaskOperationType.Refactor,
        ["test"] = TaskOperationType.Test,
        ["spec"] = TaskOperationType.Test,
        ["review"] = TaskOperationType.Review,
        ["audit"] = TaskOperationType.Review,
        ["check"] = TaskOperationType.Review,
        ["delete"] = TaskOperationType.Delete,
        ["remove"] = TaskOperationType.Delete,
        ["document"] = TaskOperationType.Document,
        ["doc"] = TaskOperationType.Document,
        ["readme"] = TaskOperationType.Document,
    };

    // Chinese stop words (common characters that carry little meaning alone)
    private static readonly HashSet<string> ChineseStopChars =
    [
        "的", "了", "在", "是", "和", "与", "或", "也", "都", "不", "要",
        "把", "被", "让", "给", "为", "对", "从", "到", "中", "上", "下",
    ];

    // English stop words
    private static readonly HashSet<string> EnglishStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "is", "are", "was", "were", "be", "been",
        "to", "in", "on", "at", "for", "of", "with", "by", "from",
        "and", "or", "not", "but", "if", "else", "when", "then",
        "this", "that", "these", "those", "it", "its",
        "function", "method", "class", "file", "code",
        "i", "we", "you", "they", "he", "she",
    };

    private static readonly HashSet<string> CommonWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "are", "but", "not", "you", "all", "can", "her",
        "was", "one", "our", "out", "day", "had", "has", "his", "how", "its",
        "may", "new", "now", "old", "see", "way", "who", "did", "get", "let",
        "say", "she", "too", "use", "fix", "add", "update", "remove", "delete",
        "change", "the", "should", "would", "could", "will", "shall",
    };

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

        // Match file paths: forward slash, backslash, with extensions
        // Supports spaces and hyphens in directory names
        var matches = FilePathRegex().Matches(text);
        foreach (Match m in matches)
        {
            var path = m.Value.Replace('\\', '/').TrimStart('/');
            if (!paths.Contains(path, StringComparer.OrdinalIgnoreCase))
                paths.Add(path);
        }
        return paths;
    }

    private static List<string> ExtractSymbols(string text)
    {
        var symbols = new List<string>();

        // PascalCase identifiers (e.g., AuthService, TokenManager)
        foreach (Match m in PascalCaseRegex().Matches(text))
        {
            var sym = m.Value;
            if (sym.Length >= 3 && !CommonWords.Contains(sym.ToLowerInvariant()))
                symbols.Add(sym);
        }

        // camelCase identifiers (e.g., refreshToken, getUserById)
        foreach (Match m in CamelCaseRegex().Matches(text))
        {
            if (!symbols.Contains(m.Value, StringComparer.OrdinalIgnoreCase))
                symbols.Add(m.Value);
        }

        // snake_case identifiers (e.g., refresh_token, user_id)
        foreach (Match m in SnakeCaseRegex().Matches(text))
        {
            if (!symbols.Contains(m.Value, StringComparer.OrdinalIgnoreCase))
                symbols.Add(m.Value);
        }

        // kebab-case identifiers (e.g., auth-service, user-controller)
        foreach (Match m in KebabCaseRegex().Matches(text))
        {
            if (!symbols.Contains(m.Value, StringComparer.OrdinalIgnoreCase))
                symbols.Add(m.Value);
        }

        // Chinese code identifiers (e.g., 用户服务, 认证模块)
        foreach (Match m in ChineseIdentifierRegex().Matches(text))
        {
            var sym = m.Value;
            if (sym.Length >= 2 && !ChineseStopChars.Contains(sym))
                symbols.Add(sym);
        }

        return symbols.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> ExtractKeywords(string text)
    {
        var keywords = new List<string>();

        // English words: Unicode-aware, minimum 3 chars
        foreach (Match m in EnglishWordRegex().Matches(text))
        {
            var word = m.Value.ToLowerInvariant();
            if (word.Length >= 3 && !EnglishStopWords.Contains(word) && !CommonWords.Contains(word))
                keywords.Add(word);
        }

        // Chinese bigrams (2-character sequences) for keyword extraction
        // This is a simple n-gram approach — not as good as jieba but works without dependencies
        var chineseChars = ChineseCharRegex().Matches(text);
        if (chineseChars.Count >= 2)
        {
            var charSequence = new List<char>();
            foreach (Match m in chineseChars)
                charSequence.Add(m.Value[0]);

            // Extract 2-grams
            var seen = new HashSet<string>();
            for (var i = 0; i < charSequence.Count - 1; i++)
            {
                var bigram = new string([charSequence[i], charSequence[i + 1]]);
                if (!ChineseStopChars.Contains(bigram) && !seen.Contains(bigram))
                {
                    seen.Add(bigram);
                    keywords.Add(bigram);
                }
            }
        }

        return keywords.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> ExtractErrorStackReferences(string text)
    {
        var refs = new List<string>();
        // Java/C# style: at ClassName.method(File.java:123)
        foreach (Match m in StackTraceRegex().Matches(text))
            refs.Add(m.Value);

        // Python style: File "path/to/file.py", line 123, in function
        foreach (Match m in PythonTraceRegex().Matches(text))
            refs.Add(m.Value);

        return refs;
    }

    private static TaskOperationType DetectOperationType(string text)
    {
        var lower = text.ToLowerInvariant();

        // Check English keywords
        foreach (var (keyword, opType) in EnglishOps)
        {
            if (lower.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return opType;
        }

        // Check Chinese keywords
        foreach (var (keyword, opType) in ChineseOps)
        {
            if (text.Contains(keyword, StringComparison.Ordinal))
                return opType;
        }

        return TaskOperationType.Unknown;
    }

    // File path: supports forward slash and backslash, spaces in dir names, hyphens
    // Matches: src/auth/service.ts, src\auth\service.ts, my app/auth.ts, auth-service/index.js
    [GeneratedRegex(@"(?:[\w\-./\\]+\.)+\w{1,10}(?:\s+\w+)*", RegexOptions.None, 5000)]
    private static partial Regex FilePathRegex();

    // PascalCase: AuthService, TokenManager, GetUserById
    [GeneratedRegex(@"\b[A-Z][a-z]+(?:[A-Z][a-z]+)+\b")]
    private static partial Regex PascalCaseRegex();

    // camelCase: refreshToken, getUserById (starts lowercase, has uppercase)
    [GeneratedRegex(@"\b[a-z]+[A-Z][a-zA-Z]*\b")]
    private static partial Regex CamelCaseRegex();

    // snake_case: refresh_token, user_id
    [GeneratedRegex(@"\b[a-z]+(?:_[a-z]+)+\b")]
    private static partial Regex SnakeCaseRegex();

    // kebab-case: auth-service, user-controller
    [GeneratedRegex(@"\b[a-z]+(?:-[a-z]+)+\b")]
    private static partial Regex KebabCaseRegex();

    // Chinese identifiers: 2+ consecutive CJK characters
    [GeneratedRegex(@"[\u4e00-\u9fff]{2,}")]
    private static partial Regex ChineseIdentifierRegex();

    // English words: Unicode-aware, 3+ characters
    [GeneratedRegex(@"[\p{L}][\p{L}\p{N}]{2,}", RegexOptions.None, 5000)]
    private static partial Regex EnglishWordRegex();

    // Individual Chinese characters
    [GeneratedRegex(@"[\u4e00-\u9fff]")]
    private static partial Regex ChineseCharRegex();

    // Stack trace: at ClassName.method(File:line)
    [GeneratedRegex(@"at\s+[\w.<>]+\([^)]*\)")]
    private static partial Regex StackTraceRegex();

    // Python trace: File "path", line N, in function
    [GeneratedRegex(@"File\s+""[^""]+"",\s*line\s+\d+,\s*in\s+\w+")]
    private static partial Regex PythonTraceRegex();
}
