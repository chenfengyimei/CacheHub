using System.Text.Json.Serialization;

namespace CacheHub.Core.Parsing;

/// <summary>
/// Type of relation/symbol discovered by a parser.
/// Must explicitly mark whether a relation is syntactic, heuristic, or semantic.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RelationType
{
    /// <summary>Directly extracted from syntax tree (e.g., import statement).</summary>
    Syntactic,
    /// <summary>Inferred from patterns (e.g., possible call from identifier occurrence).</summary>
    Heuristic,
    /// <summary>Resolved via LSP or dedicated analyzer (e.g., definition reference).</summary>
    Semantic,
}

/// <summary>
/// Type of code symbol.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SymbolKind
{
    Namespace,
    Class,
    Interface,
    Struct,
    Enum,
    Function,
    Method,
    Property,
    Field,
    Import,
    Constant,
    TypeAlias,
    Variable,
    Other,
}

/// <summary>
/// A code symbol extracted from a file.
/// </summary>
public sealed record CodeSymbol
{
    public required string Name { get; init; }
    public required SymbolKind Kind { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public string? FullyQualifiedName { get; init; }
    public string? Modifier { get; init; } // public, private, static, etc.
}

/// <summary>
/// An import/dependency declaration found in a file.
/// </summary>
public sealed record ImportDeclaration
{
    public required string Module { get; init; }
    public string? ImportedName { get; init; } // specific named import
    public required int Line { get; init; }
}

/// <summary>
/// A call expression found in a file (heuristic, not semantic).
/// </summary>
public sealed record CallExpression
{
    public required string FunctionName { get; init; }
    public required int Line { get; init; }
}

/// <summary>
/// A relation between symbols or files.
/// </summary>
public sealed record CodeRelation
{
    public required RelationType RelationType { get; init; }
    public required string Relation { get; init; } // e.g., "possible_call", "definition_reference"
    public required string TargetName { get; init; }
    public required double Confidence { get; init; } // 0..1
    public required string Source { get; init; } // parser name

    /// <summary>The symbol in this file that has the relation (e.g., the caller). Empty if unknown.</summary>
    public string SourceSymbol { get; init; } = "";

    /// <summary>The line number where the relation occurs (1-based). 0 if unknown.</summary>
    public int Line { get; init; }
}

/// <summary>
/// Diagnostic message from parsing.
/// </summary>
public sealed record ParseDiagnostic
{
    public required int Line { get; init; }
    public required string Message { get; init; }
    public string? Severity { get; init; } // "error", "warning"
}

/// <summary>
/// Result of parsing a file: symbols, imports, calls, relations, and diagnostics.
/// </summary>
public sealed record ParseResult
{
    public required string ParserId { get; init; }
    public required string ParserVersion { get; init; }
    public required string Language { get; init; }
    public IReadOnlyList<CodeSymbol> Symbols { get; init; } = [];
    public IReadOnlyList<ImportDeclaration> Imports { get; init; } = [];
    public IReadOnlyList<CallExpression> CallExpressions { get; init; } = [];
    public IReadOnlyList<CodeRelation> Relations { get; init; } = [];
    public IReadOnlyList<ParseDiagnostic> Diagnostics { get; init; } = [];
    public bool PartialParse { get; init; } // true if file had syntax errors
}

/// <summary>
/// Parser contract: takes file content, returns structured analysis.
/// </summary>
public interface ICodeParser
{
    string Id { get; }
    string Version { get; }
    IReadOnlySet<string> SupportedExtensions { get; }
    ParseResult Parse(string content, string filePath);
}
