using System.Text.RegularExpressions;
using CacheHub.Core.Parsing;

namespace CacheHub.Indexing.Parsing;

/// <summary>
/// Regex-based Rust parser (regex-baseline):
/// extracts use declarations, functions, structs, enums, traits, impls, and constants.
/// Functions inside impl blocks are marked as Methods.
/// </summary>
public sealed partial class RustRegexParser : ICodeParser
{
    public string Id => "rust-regex-baseline";
    public string Version => "1.0";
    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".rs",
    };

    private static readonly HashSet<string> NonCallKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "if", "else", "for", "while", "loop", "match", "return", "break",
        "continue", "fn", "let", "mut", "const", "static", "struct", "enum",
        "trait", "impl", "mod", "use", "pub", "crate", "self", "super",
        "as", "in", "ref", "move", "where", "unsafe", "async", "await",
        "dyn", "union", "type", "extern", "true", "false", "println",
        "print", "vec", "format", "panic", "todo", "unimplemented",
    };

    public ParseResult Parse(string content, string filePath)
    {
        var lines = content.Split('\n');
        var symbols = new List<CodeSymbol>();
        var imports = new List<ImportDeclaration>();
        var calls = new List<CallExpression>();
        var relations = new List<CodeRelation>();
        var inImplBlock = false;
        var implDepth = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("//") || string.IsNullOrWhiteSpace(trimmed))
                continue;

            // use declaration: use std::collections::HashMap; or use std::io::{Read, Write};
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

            // impl block: impl Trait for Type { or impl Type {
            var implMatch = ImplRegex().Match(line);
            if (implMatch.Success)
            {
                inImplBlock = true;
                implDepth = 0;
                var implType = implMatch.Groups[2].Success ? implMatch.Groups[2].Value : implMatch.Groups[1].Value;
                symbols.Add(new CodeSymbol
                {
                    Name = implType,
                    Kind = SymbolKind.Class,
                    StartLine = i + 1,
                    EndLine = FindEndOfBlock(lines, i),
                });
                if (implMatch.Groups[1].Success && implMatch.Groups[2].Success)
                {
                    relations.Add(new CodeRelation
                    {
                        RelationType = RelationType.Syntactic,
                        Relation = "implements",
                        TargetName = implMatch.Groups[1].Value,
                        Confidence = 0.9,
                        Source = Id,
                        SourceSymbol = implMatch.Groups[2].Value,
                        Line = i + 1,
                    });
                }
                // Count braces on this line to detect inline impl
                foreach (var c in line) { if (c == '{') implDepth++; if (c == '}') implDepth--; }
                if (implDepth <= 0) inImplBlock = false;
                continue;
            }

            // fn declaration
            var fnMatch = FnRegex().Match(line);
            if (fnMatch.Success)
            {
                symbols.Add(new CodeSymbol
                {
                    Name = fnMatch.Groups[1].Value,
                    Kind = inImplBlock ? SymbolKind.Method : SymbolKind.Function,
                    StartLine = i + 1,
                    EndLine = FindEndOfBlock(lines, i),
                    Modifier = inImplBlock ? "method" : null,
                });
                continue;
            }

            // struct declaration
            var structMatch = StructRegex().Match(line);
            if (structMatch.Success)
            {
                symbols.Add(new CodeSymbol
                {
                    Name = structMatch.Groups[1].Value,
                    Kind = SymbolKind.Struct,
                    StartLine = i + 1,
                    EndLine = FindEndOfBlock(lines, i),
                });
                continue;
            }

            // enum declaration
            var enumMatch = EnumRegex().Match(line);
            if (enumMatch.Success)
            {
                symbols.Add(new CodeSymbol
                {
                    Name = enumMatch.Groups[1].Value,
                    Kind = SymbolKind.Enum,
                    StartLine = i + 1,
                    EndLine = FindEndOfBlock(lines, i),
                });
                continue;
            }

            // trait declaration
            var traitMatch = TraitRegex().Match(line);
            if (traitMatch.Success)
            {
                symbols.Add(new CodeSymbol
                {
                    Name = traitMatch.Groups[1].Value,
                    Kind = SymbolKind.Interface,
                    StartLine = i + 1,
                    EndLine = FindEndOfBlock(lines, i),
                });
                continue;
            }

            // const/static declaration
            var constMatch = ConstRegex().Match(line);
            if (constMatch.Success)
            {
                symbols.Add(new CodeSymbol
                {
                    Name = constMatch.Groups[2].Value,
                    Kind = constMatch.Groups[1].Value == "const" ? SymbolKind.Constant : SymbolKind.Field,
                    StartLine = i + 1,
                    EndLine = i + 1,
                });
            }

            // Track brace depth for impl block exit
            if (inImplBlock)
            {
                foreach (var c in line) { if (c == '{') implDepth++; if (c == '}') implDepth--; }
                if (implDepth <= 0) inImplBlock = false;
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
            Language = "rust",
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

    // use path::to::module; or use path::{A, B}
    [GeneratedRegex(@"^\s*use\s+([\w]+(?:::[\w]+)*)(?:::\s*\{([^}]+)\})?\s*;")]
    private static partial Regex UseRegex();

    // impl [Trait] for [Type] { or impl [Type] {
    [GeneratedRegex(@"^\s*(?:pub\s+)?impl\s+(?:(\w+)\s+for\s+)?(\w+)")]
    private static partial Regex ImplRegex();

    // fn name(
    [GeneratedRegex(@"^\s*(?:pub\s+)?(?:async\s+)?fn\s+(\w+)\s*[<(]")]
    private static partial Regex FnRegex();

    // struct Name
    [GeneratedRegex(@"^\s*(?:pub\s+)?struct\s+(\w+)")]
    private static partial Regex StructRegex();

    // enum Name
    [GeneratedRegex(@"^\s*(?:pub\s+)?enum\s+(\w+)")]
    private static partial Regex EnumRegex();

    // trait Name
    [GeneratedRegex(@"^\s*(?:pub\s+)?trait\s+(\w+)")]
    private static partial Regex TraitRegex();

    // const/static NAME = ...
    [GeneratedRegex(@"^\s*(?:pub\s+)?(const|static)\s+(\w+)")]
    private static partial Regex ConstRegex();

    [GeneratedRegex(@"(\w+)\s*\(")]
    private static partial Regex CallRegex();
}
