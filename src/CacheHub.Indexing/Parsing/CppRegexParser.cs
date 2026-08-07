using System.Text.RegularExpressions;
using CacheHub.Core.Parsing;

namespace CacheHub.Indexing.Parsing;

/// <summary>
/// Regex-based C/C++ parser (regex-baseline):
/// extracts includes, functions, classes, structs, enums, macros, and typedefs.
/// Methods inside class/struct body are marked as Methods.
/// </summary>
public sealed partial class CppRegexParser : ICodeParser
{
    public string Id => "cpp-regex-baseline";
    public string Version => "1.0";
    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".c", ".h", ".cpp", ".hpp", ".cc", ".cxx",
    };

    private static readonly HashSet<string> NonCallKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "if", "else", "for", "while", "switch", "case", "return", "goto",
        "break", "continue", "do", "sizeof", "typeof", "inline", "static",
        "const", "volatile", "register", "auto", "extern", "void", "int",
        "char", "short", "long", "float", "double", "unsigned", "signed",
        "bool", "true", "false", "NULL", "nullptr", "class", "struct",
        "enum", "union", "typedef", "namespace", "using", "template",
        "typename", "public", "private", "protected", "virtual", "override",
        "final", "abstract", "new", "delete", "this", "throw", "try",
        "catch", "operator", "friend", "explicit", "constexpr", "noexcept",
        "decltype", "alignof", "static_cast", "dynamic_cast", "reinterpret_cast",
        "const_cast", "printf", "sprintf", "fprintf", "scanf", "malloc",
        "free", "calloc", "realloc", "memcpy", "memset", "strlen", "strcpy",
        "strcmp", "strcat", "fopen", "fclose", "fread", "fwrite", "exit",
        "abort", "assert", "puts", "getchar", "putchar",
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
        var isHeader = filePath.EndsWith(".h") || filePath.EndsWith(".hpp");

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("//") || string.IsNullOrWhiteSpace(trimmed))
                continue;

            // Skip block comment lines (simple heuristic)
            if (trimmed.StartsWith("/*") || trimmed.StartsWith('*'))
                continue;

            // #include <header> or #include "header"
            var includeMatch = IncludeRegex().Match(line);
            if (includeMatch.Success)
            {
                var module = includeMatch.Groups[1].Value;
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

            // #define MACRO
            var defineMatch = DefineRegex().Match(line);
            if (defineMatch.Success)
            {
                symbols.Add(new CodeSymbol
                {
                    Name = defineMatch.Groups[1].Value,
                    Kind = SymbolKind.Constant,
                    StartLine = i + 1,
                    EndLine = i + 1,
                });
                continue;
            }

            // namespace Name {
            var nsMatch = NamespaceRegex().Match(line);
            if (nsMatch.Success)
            {
                symbols.Add(new CodeSymbol
                {
                    Name = nsMatch.Groups[1].Value,
                    Kind = SymbolKind.Namespace,
                    StartLine = i + 1,
                    EndLine = FindEndOfBlock(lines, i),
                });
                continue;
            }

            // class/struct Name [ : public Base, private IBase ]
            var classMatch = ClassRegex().Match(line);
            if (classMatch.Success)
            {
                var name = classMatch.Groups[2].Value;
                var kindStr = classMatch.Groups[1].Value;
                symbols.Add(new CodeSymbol
                {
                    Name = name,
                    Kind = kindStr == "class" ? SymbolKind.Class : SymbolKind.Struct,
                    StartLine = i + 1,
                    EndLine = FindEndOfBlock(lines, i),
                });

                // Base classes: : public Base, private IBase
                if (classMatch.Groups[3].Success)
                {
                    var bases = classMatch.Groups[3].Value.Split(',', StringSplitOptions.TrimEntries);
                    foreach (var baseClause in bases)
                    {
                        // Remove access specifier: public/protected/private
                        var parts = baseClause.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        var baseType = parts.Length > 0 ? parts[^1].Split('<')[0] : "";
                        if (!string.IsNullOrEmpty(baseType) && baseType != "Base")
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

            // enum Name {
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

            // typedef ... Name;
            var typedefMatch = TypedefRegex().Match(line);
            if (typedefMatch.Success)
            {
                symbols.Add(new CodeSymbol
                {
                    Name = typedefMatch.Groups[1].Value,
                    Kind = SymbolKind.TypeAlias,
                    StartLine = i + 1,
                    EndLine = i + 1,
                });
                continue;
            }

            // function declaration: [modifiers] returnType Name(args) {  or  returnType Name(args);
            // Skip if inside class body and it looks like a method (handled below separately)
            var funcMatch = FunctionRegex().Match(line);
            if (funcMatch.Success)
            {
                symbols.Add(new CodeSymbol
                {
                    Name = funcMatch.Groups[2].Value,
                    Kind = inClassBody ? SymbolKind.Method : SymbolKind.Function,
                    StartLine = i + 1,
                    EndLine = line.Contains('{') ? FindEndOfBlock(lines, i) : i + 1,
                    Modifier = inClassBody ? "method" : null,
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
            Language = isHeader ? "cpp" : "c",
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

    // #include <header> or #include "header"
    [GeneratedRegex(@"^\s*#\s*include\s*[<""]([^>""]+)[>""]")]
    private static partial Regex IncludeRegex();

    // #define NAME
    [GeneratedRegex(@"^\s*#\s*define\s+(\w+)")]
    private static partial Regex DefineRegex();

    // namespace Name {
    [GeneratedRegex(@"^\s*namespace\s+(\w+)")]
    private static partial Regex NamespaceRegex();

    // [class|struct] Name [: access Base, ...]
    [GeneratedRegex(@"^\s*(?:template\s*<[^>]*>\s*)?(class|struct)\s+(\w+)(?:\s*:\s*([^{]+))?")]
    private static partial Regex ClassRegex();

    // enum Name {
    [GeneratedRegex(@"^\s*enum\s+(?:class\s+)?(\w+)")]
    private static partial Regex EnumRegex();

    // typedef ... Name;
    [GeneratedRegex(@"^\s*typedef\s+.*\s+(\w+)\s*;")]
    private static partial Regex TypedefRegex();

    // [modifiers] returnType Name(args) {  or  returnType Name(args);
    [GeneratedRegex(@"^\s*(?:(?:inline|static|virtual|explicit|constexpr|noexcept|public|private|protected)\s+)*[\w:*&<>\[\]]+\s+(\w+::)?(\w+)\s*\(")]
    private static partial Regex FunctionRegex();

    [GeneratedRegex(@"(\w+)\s*\(")]
    private static partial Regex CallRegex();
}
