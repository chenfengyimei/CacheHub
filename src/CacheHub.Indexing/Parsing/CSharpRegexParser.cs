using System.Text.RegularExpressions;
using CacheHub.Core.Parsing;

namespace CacheHub.Indexing.Parsing;

/// <summary>
/// Regex-based C# parser (regex-baseline): extracts namespaces, types, methods, properties, and imports.
/// All relations are marked as syntactic — this is NOT a semantic analysis.
/// PARSE-P2-001: This is a regex-baseline parser; Tree-sitter integration is deferred to R2-W004.
/// </summary>
public sealed partial class CSharpRegexParser : ICodeParser
{
    public string Id => "csharp-regex-baseline";
    public string Version => "1.0";
    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",
    };

    public ParseResult Parse(string content, string filePath)
    {
        var lines = content.Split('\n');
        var symbols = new List<CodeSymbol>();
        var imports = new List<ImportDeclaration>();
        var calls = new List<CallExpression>();
        var relations = new List<CodeRelation>();
        var diagnostics = new List<ParseDiagnostic>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');

            // using/import
            var usingMatch = UsingRegex().Match(line);
            if (usingMatch.Success)
            {
                var module = usingMatch.Groups[1].Value;
                imports.Add(new ImportDeclaration { Module = module, Line = i + 1 });
                symbols.Add(new CodeSymbol
                {
                    Name = module,
                    Kind = SymbolKind.Import,
                    StartLine = i + 1,
                    EndLine = i + 1,
                });
            }

            // namespace
            var nsMatch = NamespaceRegex().Match(line);
            if (nsMatch.Success)
            {
                symbols.Add(new CodeSymbol
                {
                    Name = nsMatch.Groups[1].Value,
                    Kind = SymbolKind.Namespace,
                    StartLine = i + 1,
                    EndLine = i + 1,
                });
            }

            // class/struct/interface/enum
            var typeMatch = TypeRegex().Match(line);
            if (typeMatch.Success)
            {
                var kindStr = typeMatch.Groups[3].Value.ToLowerInvariant();
                var kind = kindStr switch
                {
                    "class" => SymbolKind.Class,
                    "interface" => SymbolKind.Interface,
                    "struct" => SymbolKind.Struct,
                    "enum" => SymbolKind.Enum,
                    _ => SymbolKind.Other,
                };
                var name = typeMatch.Groups[4].Value;
                symbols.Add(new CodeSymbol
                {
                    Name = name,
                    Kind = kind,
                    StartLine = i + 1,
                    EndLine = FindEndOfBlock(lines, i),
                    Modifier = typeMatch.Groups[0].Value.Contains("public") ? "public" : null,
                });
            }

            // method
            var methodMatch = MethodRegex().Match(line);
            if (methodMatch.Success)
            {
                var name = methodMatch.Groups[3].Value;
                symbols.Add(new CodeSymbol
                {
                    Name = name,
                    Kind = SymbolKind.Method,
                    StartLine = i + 1,
                    EndLine = i + 1,
                    Modifier = methodMatch.Groups[1].Value,
                });
            }

            // property
            var propMatch = PropertyRegex().Match(line);
            if (propMatch.Success)
            {
                symbols.Add(new CodeSymbol
                {
                    Name = propMatch.Groups[3].Value,
                    Kind = SymbolKind.Property,
                    StartLine = i + 1,
                    EndLine = i + 1,
                    Modifier = propMatch.Groups[1].Value,
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
                    Confidence = 0.6,
                    Source = Id,
                });
            }
        }

        return new ParseResult
        {
            ParserId = Id,
            ParserVersion = Version,
            Language = "csharp",
            Symbols = symbols,
            Imports = imports,
            CallExpressions = calls,
            Relations = relations,
            Diagnostics = diagnostics,
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

    [GeneratedRegex(@"^\s*using\s+(?:static\s+)?([\w.]+)", RegexOptions.Multiline)]
    private static partial Regex UsingRegex();

    [GeneratedRegex(@"^\s*namespace\s+([\w.]+)", RegexOptions.Multiline)]
    private static partial Regex NamespaceRegex();

    [GeneratedRegex(@"^\s*(public|private|protected|internal)?\s*(abstract|sealed|static)?\s*(class|interface|struct|enum|record)\s+(\w+)")]
    private static partial Regex TypeRegex();

    [GeneratedRegex(@"^\s*(public|private|protected|internal)\s+(?:static\s+|async\s+)*([\w<>\[\],]+)\s+(\w+)\s*\(")]
    private static partial Regex MethodRegex();

    [GeneratedRegex(@"^\s*(public|private|protected|internal)\s+(\w+)\s+(\w+)\s*\{")]
    private static partial Regex PropertyRegex();

    [GeneratedRegex(@"(\w+)\s*\(")]
    private static partial Regex CallRegex();
}
