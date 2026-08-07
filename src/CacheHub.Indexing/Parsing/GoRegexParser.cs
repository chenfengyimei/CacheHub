using System.Text.RegularExpressions;
using CacheHub.Core.Parsing;

namespace CacheHub.Indexing.Parsing;

/// <summary>
/// Regex-based Go parser (regex-baseline):
/// extracts packages, imports, functions, types (struct/interface/type alias), and constants.
/// Functions with receiver are marked as Methods; standalone functions as Functions.
/// </summary>
public sealed partial class GoRegexParser : ICodeParser
{
    public string Id => "go-regex-baseline";
    public string Version => "1.0";
    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".go",
    };

    private static readonly HashSet<string> NonCallKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "if", "else", "for", "switch", "case", "default", "return", "break",
        "continue", "fallthrough", "go", "defer", "select", "chan", "range",
        "func", "type", "struct", "interface", "map", "make", "new", "len",
        "cap", "append", "copy", "delete", "close", "panic", "recover",
        "print", "println", "nil", "true", "false", "iota", "var", "const",
        "import", "package", "go",
    };

    public ParseResult Parse(string content, string filePath)
    {
        var lines = content.Split('\n');
        var symbols = new List<CodeSymbol>();
        var imports = new List<ImportDeclaration>();
        var calls = new List<CallExpression>();
        var relations = new List<CodeRelation>();
        var inImportBlock = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("//") || string.IsNullOrWhiteSpace(trimmed))
                continue;

            // import block: import ( ... )
            if (trimmed.StartsWith("import ("))
            {
                inImportBlock = true;
                continue;
            }
            if (inImportBlock && trimmed == ")")
            {
                inImportBlock = false;
                continue;
            }
            if (inImportBlock)
            {
                var importMatch = GoImportPathRegex().Match(trimmed);
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
                }
                continue;
            }

            // single import: import "path" or import alias "path"
            var singleImportMatch = GoSingleImportRegex().Match(line);
            if (singleImportMatch.Success)
            {
                var module = singleImportMatch.Groups[2].Value;
                imports.Add(new ImportDeclaration
                {
                    Module = module,
                    ImportedName = string.IsNullOrWhiteSpace(singleImportMatch.Groups[1].Value) ? null : singleImportMatch.Groups[1].Value,
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

            // func with receiver: func (r *Receiver) Method(args) -> Method
            var methodMatch = GoMethodRegex().Match(line);
            if (methodMatch.Success)
            {
                symbols.Add(new CodeSymbol
                {
                    Name = methodMatch.Groups[2].Value,
                    Kind = SymbolKind.Method,
                    StartLine = i + 1,
                    EndLine = FindEndOfBlock(lines, i),
                    Modifier = methodMatch.Groups[1].Value,
                });
                continue;
            }

            // func without receiver: func Function(args)
            var funcMatch = GoFuncRegex().Match(line);
            if (funcMatch.Success)
            {
                symbols.Add(new CodeSymbol
                {
                    Name = funcMatch.Groups[1].Value,
                    Kind = SymbolKind.Function,
                    StartLine = i + 1,
                    EndLine = FindEndOfBlock(lines, i),
                });
                continue;
            }

            // type declarations: type Name struct/interface/type
            var typeMatch = GoTypeRegex().Match(line);
            if (typeMatch.Success)
            {
                var kindStr = typeMatch.Groups[2].Value;
                var kind = kindStr switch
                {
                    "struct" => SymbolKind.Struct,
                    "interface" => SymbolKind.Interface,
                    _ => SymbolKind.TypeAlias,
                };
                symbols.Add(new CodeSymbol
                {
                    Name = typeMatch.Groups[1].Value,
                    Kind = kind,
                    StartLine = i + 1,
                    EndLine = kind is SymbolKind.Struct or SymbolKind.Interface ? FindEndOfBlock(lines, i) : i + 1,
                });

                // interface embedding: type X interface { Y; Z }
                if (kind == SymbolKind.Interface && typeMatch.Groups[3].Success)
                {
                    var embedded = typeMatch.Groups[3].Value.Split(',', StringSplitOptions.TrimEntries);
                    foreach (var emb in embedded)
                    {
                        if (!string.IsNullOrEmpty(emb))
                        {
                            relations.Add(new CodeRelation
                            {
                                RelationType = RelationType.Syntactic,
                                Relation = "embeds",
                                TargetName = emb,
                                Confidence = 0.9,
                                Source = Id,
                                SourceSymbol = typeMatch.Groups[1].Value,
                                Line = i + 1,
                            });
                        }
                    }
                }
                continue;
            }

            // const/var declarations: const Name = ... or var Name = ...
            var constVarMatch = GoConstVarRegex().Match(line);
            if (constVarMatch.Success)
            {
                var keyword = constVarMatch.Groups[1].Value;
                var names = constVarMatch.Groups[2].Value.Split(',', StringSplitOptions.TrimEntries);
                foreach (var name in names)
                {
                    if (!string.IsNullOrEmpty(name))
                    {
                        symbols.Add(new CodeSymbol
                        {
                            Name = name,
                            Kind = keyword == "const" ? SymbolKind.Constant : SymbolKind.Field,
                            StartLine = i + 1,
                            EndLine = i + 1,
                        });
                    }
                }
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
            Language = "go",
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
            if (depth <= 0 && i > startLine) return i + 1;
        }
        return startLine + 1;
    }

    // import "path" or import alias "path"
    [GeneratedRegex(@"^\s*import\s+(?:(\w+)\s+)?""([^""]+)""")]
    private static partial Regex GoSingleImportRegex();

    // inside import block: "path" or alias "path"
    [GeneratedRegex(@"""([^""]+)""")]
    private static partial Regex GoImportPathRegex();

    // func (r *Receiver) Method(args)
    [GeneratedRegex(@"^\s*func\s+\(\s*(\w+)\s+\*?\w+\s*\)\s+(\w+)\s*\(")]
    private static partial Regex GoMethodRegex();

    // func Function(args)
    [GeneratedRegex(@"^\s*func\s+(\w+)\s*\(")]
    private static partial Regex GoFuncRegex();

    // type Name struct/interface/type [embedded]
    [GeneratedRegex(@"^\s*type\s+(\w+)\s+(struct|interface|[\w.]+)(?:\s*\{)?")]
    private static partial Regex GoTypeRegex();

    // const/var Name = ...
    [GeneratedRegex(@"^\s*(const|var)\s+([^=\s{]+)")]
    private static partial Regex GoConstVarRegex();

    [GeneratedRegex(@"(\w+)\s*\(")]
    private static partial Regex CallRegex();
}
