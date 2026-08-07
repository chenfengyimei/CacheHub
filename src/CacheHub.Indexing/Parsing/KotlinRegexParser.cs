using System.Text.RegularExpressions;
using CacheHub.Core.Parsing;

namespace CacheHub.Indexing.Parsing;

/// <summary>
/// Regex-based Kotlin parser (regex-baseline):
/// extracts imports, classes, interfaces, objects, functions, properties, and enums.
/// Functions inside class/object body are marked as Methods.
/// </summary>
public sealed partial class KotlinRegexParser : ICodeParser
{
    public string Id => "kotlin-regex-baseline";
    public string Version => "1.0";
    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".kt", ".kts",
    };

    private static readonly HashSet<string> NonCallKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "if", "else", "for", "while", "when", "return", "break", "continue",
        "do", "try", "catch", "finally", "throw", "fun", "val", "var", "const",
        "class", "interface", "object", "enum", "sealed", "data", "abstract",
        "open", "final", "override", "private", "protected", "public", "internal",
        "companion", "init", "constructor", "by", "is", "in", "as", "out", "in",
        "typealias", "import", "package", "this", "super", "null", "true", "false",
        "suspend", "inline", "noinline", "crossinline", "reified", "operator",
        "infix", "tailrec", "external", "lateinit", "delegate", "field", "it",
        "println", "print", " arrayOf", "listOf", "setOf", "mapOf", "mutableListOf",
    };

    public ParseResult Parse(string content, string filePath)
    {
        var lines = content.Split('\n');
        var symbols = new List<CodeSymbol>();
        var imports = new List<ImportDeclaration>();
        var calls = new List<CallExpression>();
        var relations = new List<CodeRelation>();
        var inClassBody = false;
        var classBraceDepth = 0;

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
                imports.Add(new ImportDeclaration
                {
                    Module = module,
                    ImportedName = importMatch.Groups[2].Success ? importMatch.Groups[2].Value : null,
                    Line = i + 1,
                });
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

            // class/interface/object/enum declaration
            var typeMatch = TypeRegex().Match(line);
            if (typeMatch.Success)
            {
                var kindStr = typeMatch.Groups[1].Value;
                var name = typeMatch.Groups[2].Value;
                var kind = kindStr switch
                {
                    "class" => SymbolKind.Class,
                    "interface" => SymbolKind.Interface,
                    "object" => SymbolKind.Class,
                    "enum" => SymbolKind.Enum,
                    _ => SymbolKind.Other,
                };
                symbols.Add(new CodeSymbol
                {
                    Name = name,
                    Kind = kind,
                    StartLine = i + 1,
                    EndLine = FindEndOfBlock(lines, i),
                });

                // Inheritance: : BaseClass, Interface1, Interface2
                if (typeMatch.Groups[3].Success)
                {
                    var bases = typeMatch.Groups[3].Value.Split(',', StringSplitOptions.TrimEntries);
                    foreach (var baseClause in bases)
                    {
                        var baseType = baseClause.Split('<')[0].Trim().TrimEnd('{', ' ').Trim();
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

                inClassBody = true;
                classBraceDepth = 0;
                foreach (var c in line) { if (c == '{') classBraceDepth++; if (c == '}') classBraceDepth--; }
                if (classBraceDepth <= 0 && !line.Contains('{')) inClassBody = false;
                continue;
            }

            // function: fun name(args)
            var funcMatch = FunRegex().Match(line);
            if (funcMatch.Success)
            {
                symbols.Add(new CodeSymbol
                {
                    Name = funcMatch.Groups[1].Value,
                    Kind = inClassBody ? SymbolKind.Method : SymbolKind.Function,
                    StartLine = i + 1,
                    EndLine = FindEndOfBlock(lines, i),
                    Modifier = inClassBody ? "method" : null,
                });
                if (inClassBody)
                {
                    foreach (var c in line) { if (c == '{') classBraceDepth++; if (c == '}') classBraceDepth--; }
                }
                continue;
            }

            // property: val/var name = ... or val/var name: Type = ...
            var propMatch = PropRegex().Match(line);
            if (propMatch.Success)
            {
                var keyword = propMatch.Groups[1].Value;
                symbols.Add(new CodeSymbol
                {
                    Name = propMatch.Groups[2].Value,
                    Kind = keyword == "val" && propMatch.Groups[3].Success &&
                           propMatch.Groups[3].Value.Contains("const", StringComparison.OrdinalIgnoreCase)
                        ? SymbolKind.Constant : SymbolKind.Property,
                    StartLine = i + 1,
                    EndLine = i + 1,
                });
                continue;
            }

            // Track class body brace depth
            if (inClassBody)
            {
                foreach (var c in line) { if (c == '{') classBraceDepth++; if (c == '}') classBraceDepth--; }
                if (classBraceDepth <= 0) inClassBody = false;
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
            Language = "kotlin",
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

    // import pkg.Name [as alias]
    [GeneratedRegex(@"^\s*import\s+([\w.]+)(?:\s+as\s+(\w+))?")]
    private static partial Regex ImportRegex();

    // [modifiers] class|interface|object|enum Name [ : Base, iface ]
    [GeneratedRegex(@"^\s*(?:(?:data|sealed|abstract|open|final|inner|companion)\s+)*(class|interface|object|enum)\s+(?:class\s+)?(\w+)(?:<[^>]+>)?(?:\s*:\s*(.+))?")]
    private static partial Regex TypeRegex();

    // fun name(
    [GeneratedRegex(@"^\s*(?:(?:private|public|protected|internal|open|override|abstract|final|suspend|inline|operator|infix)\s+)*fun\s+(\w+)\s*[<(]")]
    private static partial Regex FunRegex();

    // val/var name [: Type] [= ...]
    [GeneratedRegex(@"^\s*(?:(?:private|public|protected|internal|const|lateinit|override|open)\s+)*(val|var)\s+(\w+)(?::\s*([\w.<>\[\]]+))?")]
    private static partial Regex PropRegex();

    [GeneratedRegex(@"(\w+)\s*\(")]
    private static partial Regex CallRegex();
}
