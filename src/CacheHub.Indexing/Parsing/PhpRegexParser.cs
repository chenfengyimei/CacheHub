using System.Text.RegularExpressions;
using CacheHub.Core.Parsing;

namespace CacheHub.Indexing.Parsing;

/// <summary>
/// Regex-based PHP parser (regex-baseline):
/// extracts use statements, classes, interfaces, traits, functions, methods, and constants.
/// Methods inside class/trait body are marked as Methods.
/// </summary>
public sealed partial class PhpRegexParser : ICodeParser
{
    public string Id => "php-regex-baseline";
    public string Version => "1.0";
    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".php",
    };

    private static readonly HashSet<string> NonCallKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "if", "else", "elseif", "for", "foreach", "while", "switch",
        "case", "return", "break", "continue", "echo", "print", "new",
        "array", "list", "isset", "unset", "empty", "compact", "extract",
        "function", "class", "interface", "trait", "extends", "implements",
        "use", "namespace", "public", "private", "protected", "static",
        "final", "abstract", "const", "var", "global", "try", "catch",
        "finally", "throw", "instanceof", "clone", "require", "require_once",
        "include", "include_once", "true", "false", "null", "parent",
        "self", "this", "yield", "fn", "match", "enum", "readonly",
        "printf", "sprintf", "count", "strlen", "strpos", "str_replace",
        "array_map", "array_filter", "array_push", "array_merge", "sort",
        "in_array", "header", "exit", "die", "json_encode", "json_decode",
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

            if (trimmed.StartsWith("//") || trimmed.StartsWith('#') || trimmed.StartsWith('*') || string.IsNullOrWhiteSpace(trimmed))
                continue;

            // use statement: use Namespace\Class; or use Namespace\Class as Alias;
            var useMatch = UseRegex().Match(line);
            if (useMatch.Success)
            {
                var module = useMatch.Groups[1].Value;
                imports.Add(new ImportDeclaration
                {
                    Module = module,
                    ImportedName = useMatch.Groups[2].Success ? useMatch.Groups[2].Value : null,
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

            // class/interface/trait/enum declaration
            var typeMatch = TypeRegex().Match(line);
            if (typeMatch.Success)
            {
                var kindStr = typeMatch.Groups[1].Value;
                var name = typeMatch.Groups[2].Value;
                var kind = kindStr switch
                {
                    "class" => SymbolKind.Class,
                    "interface" => SymbolKind.Interface,
                    "trait" => SymbolKind.Struct, // traits are closest to struct
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

                // extends
                if (typeMatch.Groups[3].Success)
                {
                    relations.Add(new CodeRelation
                    {
                        RelationType = RelationType.Syntactic,
                        Relation = "inherits",
                        TargetName = typeMatch.Groups[3].Value,
                        Confidence = 0.9,
                        Source = Id,
                        SourceSymbol = name,
                        Line = i + 1,
                    });
                }

                // implements (comma-separated)
                if (typeMatch.Groups[4].Success)
                {
                    var ifaces = typeMatch.Groups[4].Value.Split(',', StringSplitOptions.TrimEntries);
                    foreach (var iface in ifaces)
                    {
                        var cleanIface = iface.Split('\\')[^1].Trim();
                        if (!string.IsNullOrEmpty(cleanIface))
                        {
                            relations.Add(new CodeRelation
                            {
                                RelationType = RelationType.Syntactic,
                                Relation = "implements",
                                TargetName = cleanIface,
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

            // function declaration: function name(args)
            var funcMatch = FunctionRegex().Match(line);
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
                // Update class body brace depth so subsequent methods are still detected as Methods
                if (inClassBody)
                {
                    foreach (var c in line) { if (c == '{') classBraceDepth++; if (c == '}') classBraceDepth--; }
                }
                continue;
            }

            // const declaration: const NAME = ...
            var constMatch = ConstRegex().Match(line);
            if (constMatch.Success)
            {
                symbols.Add(new CodeSymbol
                {
                    Name = constMatch.Groups[1].Value,
                    Kind = SymbolKind.Constant,
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
            Language = "php",
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

    // use Namespace\Class [as Alias];
    [GeneratedRegex(@"^\s*use\s+([\w\\]+)(?:\s+as\s+(\w+))?\s*;")]
    private static partial Regex UseRegex();

    // [final|abstract] class|interface|trait|enum Name [extends Base] [implements I1, I2]
    [GeneratedRegex(@"^\s*(?:(?:final|abstract)\s+)?(class|interface|trait|enum)\s+(\w+)(?:\s+extends\s+([\w\\]+))?(?:\s+implements\s+([\w\\,\s]+))?")]
    private static partial Regex TypeRegex();

    // function name(
    [GeneratedRegex(@"^\s*(?:(?:public|private|protected|static|final|abstract)\s+)*function\s+(\w+)\s*\(")]
    private static partial Regex FunctionRegex();

    // const NAME = ...
    [GeneratedRegex(@"^\s*const\s+(\w+)\s*=")]
    private static partial Regex ConstRegex();

    [GeneratedRegex(@"(\w+)\s*\(")]
    private static partial Regex CallRegex();
}
