using System.Text.RegularExpressions;
using CacheHub.Core.Parsing;

namespace CacheHub.Indexing.Parsing;

/// <summary>
/// Regex-based TypeScript/JavaScript parser (regex-baseline):
/// extracts modules, imports, classes, functions, interfaces, type aliases, and arrow functions.
/// Import relations are marked syntactic; call relations are heuristic.
/// </summary>
public sealed partial class TypeScriptRegexParser : ICodeParser
{
    public string Id => "typescript-regex-baseline";
    public string Version => "2.0";
    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".ts", ".tsx", ".js", ".jsx",
    };

    private static readonly HashSet<string> NonCallKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "if", "else", "for", "while", "switch", "return", "throw",
        "new", "typeof", "instanceof", "in", "of", "await", "async",
        "function", "class", "interface", "enum", "type", "const",
        "let", "var", "import", "export", "from", "default", "try",
        "catch", "finally", "do", "break", "continue", "yield",
        "delete", "void", "this", "super", "true", "false", "null",
        "undefined", "as", "is", "extends", "implements", "static",
        "get", "set", "public", "private", "protected", "readonly",
        "abstract", "declare", "module", "namespace", "require",
    };

    public ParseResult Parse(string content, string filePath)
    {
        var lines = content.Split('\n');
        var symbols = new List<CodeSymbol>();
        var imports = new List<ImportDeclaration>();
        var calls = new List<CallExpression>();
        var relations = new List<CodeRelation>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.TrimStart();

            // Skip comments and empty lines
            if (trimmed.StartsWith('/') || trimmed.StartsWith('*') || string.IsNullOrWhiteSpace(trimmed))
                continue;

            // import
            var importMatch = ImportRegex().Match(line);
            if (importMatch.Success)
            {
                var module = importMatch.Groups[2].Value;
                imports.Add(new ImportDeclaration
                {
                    Module = module,
                    ImportedName = string.IsNullOrWhiteSpace(importMatch.Groups[1].Value)
                        ? null
                        : importMatch.Groups[1].Value.Trim(),
                    Line = i + 1,
                });
                symbols.Add(new CodeSymbol
                {
                    Name = module,
                    Kind = SymbolKind.Import,
                    StartLine = i + 1,
                    EndLine = i + 1,
                });
                relations.Add(new CodeRelation
                {
                    RelationType = RelationType.Syntactic,
                    Relation = "imports",
                    TargetName = module,
                    Confidence = 1.0,
                    Source = Id,
                });
                continue;
            }

            // export class/function/interface/enum/const/type
            var exportMatch = ExportRegex().Match(line);
            if (exportMatch.Success)
            {
                var kindStr = exportMatch.Groups[3].Value;
                var name = exportMatch.Groups[4].Value;
                var kind = kindStr switch
                {
                    "class" => SymbolKind.Class,
                    "interface" => SymbolKind.Interface,
                    "function" => SymbolKind.Function,
                    "enum" => SymbolKind.Enum,
                    "const" => SymbolKind.Variable,
                    "type" => SymbolKind.TypeAlias,
                    _ => SymbolKind.Other,
                };
                symbols.Add(new CodeSymbol
                {
                    Name = name,
                    Kind = kind,
                    StartLine = i + 1,
                    EndLine = kind is SymbolKind.Class or SymbolKind.Interface or SymbolKind.Enum
                        ? FindEndOfBlock(lines, i)
                        : i + 1,
                });

                // Check for extends/implements
                if (exportMatch.Groups[5].Success)
                {
                    var baseClause = exportMatch.Groups[5].Value;
                    // Split by "implements" keyword and commas to get individual base types
                    var parts = baseClause.Split(BaseClauseSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var baseType in parts)
                    {
                        var cleanBase = baseType.Split('<')[0].Trim();
                        if (!string.IsNullOrEmpty(cleanBase))
                        {
                            relations.Add(new CodeRelation
                            {
                                RelationType = RelationType.Syntactic,
                                Relation = "extends",
                                TargetName = cleanBase,
                                Confidence = 0.9,
                                Source = Id,
                            });
                        }
                    }
                }
                continue;
            }

            // function declaration (non-export)
            var funcMatch = FunctionRegex().Match(line);
            if (funcMatch.Success)
            {
                symbols.Add(new CodeSymbol
                {
                    Name = funcMatch.Groups[1].Value,
                    Kind = SymbolKind.Function,
                    StartLine = i + 1,
                    EndLine = i + 1,
                });
                continue;
            }

            // arrow function: const name = (...) => or const name = async (...) =>
            var arrowMatch = ArrowFunctionRegex().Match(line);
            if (arrowMatch.Success)
            {
                symbols.Add(new CodeSymbol
                {
                    Name = arrowMatch.Groups[1].Value,
                    Kind = SymbolKind.Function,
                    StartLine = i + 1,
                    EndLine = i + 1,
                });
                continue;
            }

            // method inside class: methodName(...) { or methodName(...):
            var methodMatch = MethodRegex().Match(line);
            if (methodMatch.Success)
            {
                var name = methodMatch.Groups[1].Value;
                if (!NonCallKeywords.Contains(name))
                {
                    symbols.Add(new CodeSymbol
                    {
                        Name = name,
                        Kind = SymbolKind.Method,
                        StartLine = i + 1,
                        EndLine = i + 1,
                    });
                }
            }

            // call expressions (heuristic) — filtered
            var callMatches = CallRegex().Matches(line);
            foreach (System.Text.RegularExpressions.Match call in callMatches)
            {
                var funcName = call.Groups[1].Value;
                if (NonCallKeywords.Contains(funcName))
                    continue;
                if (funcName.Length < 2)
                    continue;

                calls.Add(new CallExpression { FunctionName = funcName, Line = i + 1 });
                relations.Add(new CodeRelation
                {
                    RelationType = RelationType.Heuristic,
                    Relation = "possible_call",
                    TargetName = funcName,
                    Confidence = 0.5,
                    Source = Id,
                });
            }
        }

        return new ParseResult
        {
            ParserId = Id,
            ParserVersion = Version,
            Language = "typescript",
            Symbols = symbols,
            Imports = imports,
            CallExpressions = calls,
            Relations = relations,
        };
    }

    private static int FindEndOfBlock(string[] lines, int startLine)
    {
        var depth = 0;
        for (var i = startLine; i < lines.Length; i++)
        {
            foreach (var c in lines[i])
            {
                if (c == '{') depth++;
                if (c == '}') depth--;
            }
            if (depth == 0 && i > startLine) return i + 1;
        }
        return startLine + 1;
    }

    private static readonly string[] BaseClauseSeparators = ["implements", "extends", ","];

    // Matches: import with optional named imports and module path
    [GeneratedRegex(@"import\s+(?:(\{[^}]+\}|[\w*]+(?:\s+as\s+\w+)?(?:\s*,\s*\{[^}]+\})?)\s+from\s+)?['""]([^'""]+)['""]")]
    private static partial Regex ImportRegex();

    // export [default] class|function|interface|enum|const|type Name ...
    [GeneratedRegex(@"export\s+(?:(default)\s+)?(abstract\s+)?(class|interface|function|enum|const|type)\s+(\w+)(?:<[^>]+>)?(?:\s+(?:extends|implements)\s+([^\{]+))?")]
    private static partial Regex ExportRegex();

    [GeneratedRegex(@"function\s+(\w+)\s*[<(]")]
    private static partial Regex FunctionRegex();

    // const name = (args) => or const name = async (args) =>
    [GeneratedRegex(@"(?:const|let|var)\s+(\w+)\s*=\s*(?:async\s+)?\(?[^)]*\)?\s*=>")]
    private static partial Regex ArrowFunctionRegex();

    // Method inside class: name(args) { or name(args): ReturnType {
    [GeneratedRegex(@"^\s+(\w+)\s*\([^)]*\)\s*(?::\s*[\w<>\[\]\|]+)?\s*\{")]
    private static partial Regex MethodRegex();

    [GeneratedRegex(@"(\w+)\s*\(")]
    private static partial Regex CallRegex();
}
