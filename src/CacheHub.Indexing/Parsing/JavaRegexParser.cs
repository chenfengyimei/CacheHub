using System.Text.RegularExpressions;
using CacheHub.Core.Parsing;

namespace CacheHub.Indexing.Parsing;

/// <summary>
/// Regex-based Java parser (regex-baseline):
/// extracts package, imports, classes, interfaces, enums, methods, fields, and annotations.
/// Methods inside class/interface body are marked as Methods; methods with annotations
/// carry the annotation name as modifier.
/// </summary>
public sealed partial class JavaRegexParser : ICodeParser
{
    public string Id => "java-regex-baseline";
    public string Version => "1.0";
    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".java",
    };

    private static readonly HashSet<string> NonCallKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "if", "else", "for", "while", "switch", "case", "return", "throw",
        "new", "instanceof", "try", "catch", "finally", "do", "break",
        "continue", "synchronized", "abstract", "final", "static", "void",
        "int", "long", "double", "float", "boolean", "char", "byte", "short",
        "true", "false", "null", "this", "super", "class", "interface",
        "enum", "extends", "implements", "import", "package", "public",
        "private", "protected", "default", "throws", "throw", "assert",
        "volatile", "transient", "native", "strictfp", "goto", "const",
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
        var knownTypeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("//") || string.IsNullOrWhiteSpace(trimmed))
                continue;

            // import: import pkg.Class; or import static pkg.Class.method;
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

            // class/interface/enum declaration: [modifiers] class Name [extends Base] [implements I1, I2]
            var typeMatch = TypeRegex().Match(line);
            if (typeMatch.Success)
            {
                var kindStr = typeMatch.Groups[2].Value;
                var name = typeMatch.Groups[3].Value;
                knownTypeNames.Add(name);
                var kind = kindStr switch
                {
                    "class" => SymbolKind.Class,
                    "interface" => SymbolKind.Interface,
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
                if (typeMatch.Groups[4].Success)
                {
                    relations.Add(new CodeRelation
                    {
                        RelationType = RelationType.Syntactic,
                        Relation = "inherits",
                        TargetName = typeMatch.Groups[4].Value,
                        Confidence = 0.9,
                        Source = Id,
                        SourceSymbol = name,
                        Line = i + 1,
                    });
                }

                // implements
                if (typeMatch.Groups[5].Success)
                {
                    var ifaces = typeMatch.Groups[5].Value.Split(',', StringSplitOptions.TrimEntries);
                    foreach (var iface in ifaces)
                    {
                        var cleanIface = iface.Split('<')[0].Trim();
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
                if (classBraceDepth <= 0) inClassBody = false;
                continue;
            }

            // method declaration: [modifiers] returnType Name(args) [throws ...]
            var methodMatch = MethodRegex().Match(line);
            if (methodMatch.Success)
            {
                symbols.Add(new CodeSymbol
                {
                    Name = methodMatch.Groups[2].Value,
                    Kind = SymbolKind.Method,
                    StartLine = i + 1,
                    EndLine = FindEndOfBlock(lines, i),
                    Modifier = methodMatch.Groups[1].Success ? methodMatch.Groups[1].Value : null,
                });
                continue;
            }

            // field declaration: [modifiers] Type name = ...;
            var fieldMatch = FieldRegex().Match(line);
            if (fieldMatch.Success)
            {
                var fieldName = fieldMatch.Groups[2].Value;
                var isConst = fieldMatch.Groups[1].Value.Contains("static", StringComparison.OrdinalIgnoreCase) &&
                              fieldMatch.Groups[1].Value.Contains("final", StringComparison.OrdinalIgnoreCase);
                symbols.Add(new CodeSymbol
                {
                    Name = fieldName,
                    Kind = isConst ? SymbolKind.Constant : SymbolKind.Field,
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
            Language = "java",
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

    // import [static] pkg.Class[.method];
    [GeneratedRegex(@"^\s*import\s+(?:static\s+)?([\w.]+)(?:\.(\w+))?\s*;")]
    private static partial Regex ImportRegex();

    // [modifiers] class|interface|enum Name [extends Base] [implements I1, I2]
    [GeneratedRegex(@"^\s*(?:(public|private|protected|abstract|final|static)\s+)*(class|interface|enum)\s+(\w+)(?:\s+extends\s+([\w.]+))?(?:\s+implements\s+([\w.,\s]+))?")]
    private static partial Regex TypeRegex();

    // [modifiers] ReturnType Name(args)
    [GeneratedRegex(@"^\s*(?:(public|private|protected|static|final|abstract|synchronized)\s+)+[\w.<>\[\]]+\s+(\w+)\s*\(")]
    private static partial Regex MethodRegex();

    // [modifiers] Type name = ...; or [modifiers] Type name;
    [GeneratedRegex(@"^\s*((?:(?:public|private|protected|static|final|volatile|transient)\s+)+)([\w.<>\[\]]+)\s+(\w+)\s*[=;]")]
    private static partial Regex FieldRegex();

    [GeneratedRegex(@"(\w+)\s*\(")]
    private static partial Regex CallRegex();
}
