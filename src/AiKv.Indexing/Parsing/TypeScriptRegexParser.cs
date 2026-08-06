using System.Text.RegularExpressions;
using AiKv.Core.Parsing;

namespace AiKv.Indexing.Parsing;

/// <summary>
/// Regex-based TypeScript parser: extracts modules, imports, classes, functions, interfaces.
/// </summary>
public sealed partial class TypeScriptRegexParser : ICodeParser
{
    public string Id => "typescript-regex";
    public string Version => "1.0";
    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".ts", ".tsx", ".js", ".jsx",
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

            // import
            var importMatch = ImportRegex().Match(line);
            if (importMatch.Success)
            {
                imports.Add(new ImportDeclaration
                {
                    Module = importMatch.Groups[2].Value,
                    ImportedName = string.IsNullOrWhiteSpace(importMatch.Groups[1].Value)
                        ? null
                        : importMatch.Groups[1].Value.Trim(),
                    Line = i + 1,
                });
                symbols.Add(new CodeSymbol
                {
                    Name = importMatch.Groups[2].Value,
                    Kind = SymbolKind.Import,
                    StartLine = i + 1,
                    EndLine = i + 1,
                });
            }

            // export class/function/interface
            var exportMatch = ExportRegex().Match(line);
            if (exportMatch.Success)
            {
                var kindStr = exportMatch.Groups[1].Value;
                var name = exportMatch.Groups[2].Value;
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
                    EndLine = i + 1,
                });
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
            }

            // call expressions (heuristic)
            var callMatches = CallRegex().Matches(line);
            foreach (System.Text.RegularExpressions.Match call in callMatches)
            {
                var funcName = call.Groups[1].Value;
                calls.Add(new CallExpression { FunctionName = funcName, Line = i + 1 });
                relations.Add(new CodeRelation
                {
                    RelationType = RelationType.Heuristic,
                    Relation = "possible_call",
                    TargetName = funcName,
                    Confidence = 0.55,
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

    [GeneratedRegex(@"import\s+(?:(\{[^}]+\}|[\w*]+)\s+from\s+)?['""]([^'""]+)['""]")]
    private static partial Regex ImportRegex();

    [GeneratedRegex(@"export\s+(class|interface|function|enum|const|type)\s+(\w+)")]
    private static partial Regex ExportRegex();

    [GeneratedRegex(@"function\s+(\w+)\s*[<(]")]
    private static partial Regex FunctionRegex();

    [GeneratedRegex(@"(\w+)\s*\(")]
    private static partial Regex CallRegex();
}
