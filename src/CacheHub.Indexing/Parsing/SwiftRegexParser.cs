using System.Text.RegularExpressions;
using CacheHub.Core.Parsing;

namespace CacheHub.Indexing.Parsing;

/// <summary>
/// Regex-based Swift parser (regex-baseline):
/// extracts imports, classes, structs, enums, protocols, functions, properties, and constants.
/// Functions inside class/struct body are marked as Methods.
/// </summary>
public sealed partial class SwiftRegexParser : ICodeParser
{
    public string Id => "swift-regex-baseline";
    public string Version => "1.0";
    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".swift",
    };

    private static readonly HashSet<string> NonCallKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "if", "else", "for", "while", "switch", "case", "return", "break",
        "continue", "do", "try", "catch", "throw", "throws", "guard",
        "let", "var", "func", "class", "struct", "enum", "protocol",
        "extension", "import", "init", "deinit", "self", "super", "nil",
        "true", "false", "in", "is", "as", "where", "public", "private",
        "internal", "fileprivate", "open", "final", "static", "const",
        "lazy", "weak", "unowned", "override", "mutating", "nonmutating",
        "convenience", "required", "optional", "indirect", "associatedtype",
        "typealias", "subscript", "operator", "precedencegroup", "inout",
        "repeat", "fallthrough", "default", "any", "some", "async", "await",
        "actor", "distributed", "unchecked", "sending", "consume", "discard",
        "borrow", "print", "fatalError", "assert", "precondition",
    };

    public ParseResult Parse(string content, string filePath)
    {
        var lines = content.Split('\n');
        var symbols = new List<CodeSymbol>();
        var imports = new List<ImportDeclaration>();
        var calls = new List<CallExpression>();
        var relations = new List<CodeRelation>();
        var inTypeBody = false;
        var typeBraceDepth = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("//") || string.IsNullOrWhiteSpace(trimmed))
                continue;

            // import statement
            var importMatch = ImportRegex().Match(line);
            if (importMatch.Success)
            {
                var module = importMatch.Groups[1].Value;
                imports.Add(new ImportDeclaration { Module = module, Line = i + 1 });
                symbols.Add(new CodeSymbol { Name = module, Kind = SymbolKind.Import, StartLine = i + 1, EndLine = i + 1 });
                relations.Add(new CodeRelation
                {
                    RelationType = RelationType.Syntactic,
                    Relation = "imports",
                    TargetName = module,
                    Confidence = 1.0,
                    Source = Id,
                    Line = i + 1,
                });
                continue;
            }

            // class/struct/enum/protocol declaration
            var typeMatch = TypeRegex().Match(line);
            if (typeMatch.Success)
            {
                var kindStr = typeMatch.Groups[1].Value;
                var name = typeMatch.Groups[2].Value;
                var kind = kindStr switch
                {
                    "class" => SymbolKind.Class,
                    "struct" => SymbolKind.Struct,
                    "enum" => SymbolKind.Enum,
                    "protocol" => SymbolKind.Interface,
                    "actor" => SymbolKind.Class,
                    _ => SymbolKind.Other,
                };
                symbols.Add(new CodeSymbol
                {
                    Name = name,
                    Kind = kind,
                    StartLine = i + 1,
                    EndLine = FindEndOfBlock(lines, i),
                });

                // Inheritance: : BaseClass, Protocol1, Protocol2
                if (typeMatch.Groups[3].Success)
                {
                    var bases = typeMatch.Groups[3].Value.Split(',', StringSplitOptions.TrimEntries);
                    foreach (var baseClause in bases)
                    {
                        var baseType = baseClause.Split('<')[0].Trim().TrimEnd('{').Trim();
                        if (!string.IsNullOrEmpty(baseType) && baseType != "Any")
                        {
                            relations.Add(new CodeRelation
                            {
                                RelationType = RelationType.Syntactic,
                                Relation = "inherits",
                                TargetName = baseType,
                                Confidence = 0.9,
                                Source = Id,
                                SourceSymbol = name,
                                Line = i + 1,
                            });
                        }
                    }
                }

                inTypeBody = true;
                typeBraceDepth = 0;
                foreach (var c in line) { if (c == '{') typeBraceDepth++; if (c == '}') typeBraceDepth--; }
                if (typeBraceDepth <= 0 && !line.Contains('{')) inTypeBody = false;
                continue;
            }

            // function: func name(args) or func name<Generic>(args)
            var funcMatch = FuncRegex().Match(line);
            if (funcMatch.Success)
            {
                symbols.Add(new CodeSymbol
                {
                    Name = funcMatch.Groups[1].Value,
                    Kind = inTypeBody ? SymbolKind.Method : SymbolKind.Function,
                    StartLine = i + 1,
                    EndLine = FindEndOfBlock(lines, i),
                    Modifier = inTypeBody ? "method" : null,
                });
                if (inTypeBody)
                {
                    foreach (var c in line) { if (c == '{') typeBraceDepth++; if (c == '}') typeBraceDepth--; }
                }
                continue;
            }

            // property: let/var name : Type = ... or let/var name = ...
            var propMatch = PropRegex().Match(line);
            if (propMatch.Success)
            {
                var keyword = propMatch.Groups[1].Value;
                symbols.Add(new CodeSymbol
                {
                    Name = propMatch.Groups[2].Value,
                    Kind = keyword == "let" ? SymbolKind.Constant : SymbolKind.Property,
                    StartLine = i + 1,
                    EndLine = i + 1,
                });
                continue;
            }

            // Track type body brace depth
            if (inTypeBody)
            {
                foreach (var c in line) { if (c == '{') typeBraceDepth++; if (c == '}') typeBraceDepth--; }
                if (typeBraceDepth <= 0) inTypeBody = false;
            }

            // call expressions (heuristic)
            var callMatches = CallRegex().Matches(line);
            foreach (System.Text.RegularExpressions.Match call in callMatches)
            {
                var funcName = call.Groups[1].Value;
                if (NonCallKeywords.Contains(funcName) || funcName.Length < 2)
                    continue;
                calls.Add(new CallExpression { FunctionName = funcName, Line = i + 1 });
                relations.Add(new CodeRelation
                {
                    RelationType = RelationType.Heuristic,
                    Relation = "possible_call",
                    TargetName = funcName,
                    Confidence = 0.5,
                    Source = Id,
                    SourceSymbol = funcName,
                    Line = i + 1,
                });
            }
        }

        return new ParseResult
        {
            ParserId = Id,
            ParserVersion = Version,
            Language = "swift",
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

    // import ModuleName
    [GeneratedRegex(@"^\s*import\s+(\w+)")]
    private static partial Regex ImportRegex();

    // [modifiers] class|struct|enum|protocol|actor Name [ : Base, Protocol ]
    [GeneratedRegex(@"^\s*(?:(?:public|private|internal|fileprivate|open|final|abstract|sealed)\s+)*(class|struct|enum|protocol|actor)\s+(\w+)(?:<[^>]+>)?(?:\s*:\s*(.+))?")]
    private static partial Regex TypeRegex();

    // func name(
    [GeneratedRegex(@"^\s*(?:(?:public|private|internal|fileprivate|open|final|static|override|mutating|nonmutating|async)\s+)*func\s+(\w+)\s*[<(]")]
    private static partial Regex FuncRegex();

    // let/var name [: Type] [= ...]
    [GeneratedRegex(@"^\s*(?:(?:public|private|internal|fileprivate|open|final|static|lazy|weak|unowned|override)\s+)*(let|var)\s+(\w+)")]
    private static partial Regex PropRegex();

    [GeneratedRegex(@"(\w+)\s*\(")]
    private static partial Regex CallRegex();
}
