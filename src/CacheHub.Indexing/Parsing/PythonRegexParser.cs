using System.Text.RegularExpressions;
using CacheHub.Core.Parsing;

namespace CacheHub.Indexing.Parsing;

/// <summary>
/// Regex-based Python parser: extracts classes, functions, imports, and decorators.
/// </summary>
public sealed partial class PythonRegexParser : ICodeParser
{
    public string Id => "python-regex";
    public string Version => "1.0";
    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".py",
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

            // import/from import
            var fromMatch = FromImportRegex().Match(line);
            if (fromMatch.Success)
            {
                imports.Add(new ImportDeclaration
                {
                    Module = fromMatch.Groups[1].Value,
                    ImportedName = fromMatch.Groups[2].Value,
                    Line = i + 1,
                });
                symbols.Add(new CodeSymbol
                {
                    Name = fromMatch.Groups[1].Value,
                    Kind = SymbolKind.Import,
                    StartLine = i + 1,
                    EndLine = i + 1,
                });
                continue;
            }

            var importMatch = ImportRegex().Match(line);
            if (importMatch.Success)
            {
                imports.Add(new ImportDeclaration
                {
                    Module = importMatch.Groups[1].Value,
                    Line = i + 1,
                });
                symbols.Add(new CodeSymbol
                {
                    Name = importMatch.Groups[1].Value,
                    Kind = SymbolKind.Import,
                    StartLine = i + 1,
                    EndLine = i + 1,
                });
                continue;
            }

            // class
            var classMatch = ClassRegex().Match(line);
            if (classMatch.Success)
            {
                symbols.Add(new CodeSymbol
                {
                    Name = classMatch.Groups[1].Value,
                    Kind = SymbolKind.Class,
                    StartLine = i + 1,
                    EndLine = FindBlockEnd(lines, i),
                });
            }

            // function/def
            var funcMatch = FunctionRegex().Match(line);
            if (funcMatch.Success)
            {
                symbols.Add(new CodeSymbol
                {
                    Name = funcMatch.Groups[1].Value,
                    Kind = SymbolKind.Function,
                    StartLine = i + 1,
                    EndLine = FindBlockEnd(lines, i),
                });
            }

            // decorator
            var decoMatch = DecoratorRegex().Match(line);
            if (decoMatch.Success)
            {
                symbols.Add(new CodeSymbol
                {
                    Name = decoMatch.Groups[1].Value,
                    Kind = SymbolKind.Other,
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
                    Confidence = 0.5,
                    Source = Id,
                });
            }
        }

        return new ParseResult
        {
            ParserId = Id,
            ParserVersion = Version,
            Language = "python",
            Symbols = symbols,
            Imports = imports,
            CallExpressions = calls,
            Relations = relations,
        };
    }

    private static int FindBlockEnd(string[] lines, int startLine)
    {
        if (startLine + 1 >= lines.Length) return startLine + 1;
        var indent = GetIndent(lines[startLine]);
        for (var i = startLine + 1; i < lines.Length; i++)
        {
            if (lines[i].Trim().Length == 0) continue;
            if (GetIndent(lines[i]) <= indent && lines[i].Trim().Length > 0) return i;
        }
        return lines.Length;
    }

    private static int GetIndent(string line) => line.TakeWhile(c => c == ' ').Count();

    [GeneratedRegex(@"^\s*from\s+([\w.]+)\s+import\s+(.+)")]
    private static partial Regex FromImportRegex();

    [GeneratedRegex(@"^\s*import\s+([\w.]+)")]
    private static partial Regex ImportRegex();

    [GeneratedRegex(@"^\s*class\s+(\w+)")]
    private static partial Regex ClassRegex();

    [GeneratedRegex(@"^\s*(?:async\s+)?def\s+(\w+)")]
    private static partial Regex FunctionRegex();

    [GeneratedRegex(@"^\s*@(\w+)")]
    private static partial Regex DecoratorRegex();

    [GeneratedRegex(@"(\w+)\s*\(")]
    private static partial Regex CallRegex();
}
