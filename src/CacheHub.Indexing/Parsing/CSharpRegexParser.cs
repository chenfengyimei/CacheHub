using System.Text.RegularExpressions;
using CacheHub.Core.Parsing;

namespace CacheHub.Indexing.Parsing;

/// <summary>
/// Regex-based C# parser (regex-baseline): extracts namespaces, types, methods, properties,
/// constructors, fields, constants, and imports.
/// All call relations are marked as heuristic — this is NOT a semantic analysis.
/// Import relations are marked syntactic with confidence 1.0.
/// PARSE-P2-001: This is a regex-baseline parser; Tree-sitter integration is deferred.
/// </summary>
public sealed partial class CSharpRegexParser : ICodeParser
{
    public string Id => "csharp-regex-baseline";
    public string Version => "2.0";
    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",
    };

    // Keywords that look like function calls but are control flow or language constructs
    private static readonly HashSet<string> NonCallKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "if", "else", "for", "foreach", "while", "switch", "lock", "using",
        "catch", "finally", "return", "throw", "new", "base", "this",
        "yield", "break", "continue", "goto", "checked", "unchecked",
        "fixed", "unsafe", "try", "do", "in", "out", "ref", "params",
        "typeof", "sizeof", "nameof", "default", "is", "as", "stackalloc",
        "await", "async", "override", "virtual", "abstract", "sealed",
        "static", "readonly", "const", "get", "set", "init", "value",
        "var", "dynamic", "object", "string", "int", "long", "double",
        "float", "bool", "char", "byte", "short", "decimal", "uint",
        "ulong", "ushort", "sbyte", "void", "true", "false", "null",
        "where", "select", "group", "by", "into", "orderby", "join",
        "on", "equals", "let", "from", "ascending", "descending",
    };

    public ParseResult Parse(string content, string filePath)
    {
        var lines = content.Split('\n');
        var symbols = new List<CodeSymbol>();
        var imports = new List<ImportDeclaration>();
        var calls = new List<CallExpression>();
        var relations = new List<CodeRelation>();
        var diagnostics = new List<ParseDiagnostic>();

        // Track type names for constructor detection
        var knownTypeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.TrimStart();

            // Skip comments and empty lines
            if (trimmed.StartsWith('/') || trimmed.StartsWith('*') || string.IsNullOrWhiteSpace(trimmed))
                continue;

            // using/import — including alias using
            var usingMatch = UsingRegex().Match(line);
            if (usingMatch.Success)
            {
                var module = usingMatch.Groups[2].Value;
                var aliasName = usingMatch.Groups[1].Success ? usingMatch.Groups[1].Value : null;
                imports.Add(new ImportDeclaration
                {
                    Module = module,
                    ImportedName = aliasName,
                    Line = i + 1,
                });
                symbols.Add(new CodeSymbol
                {
                    Name = module,
                    Kind = SymbolKind.Import,
                    StartLine = i + 1,
                    EndLine = i + 1,
                });
                relations.Add(new CodeRelation
                {
                    RelationType = RelationType.Syntactic,
                    Relation = "imports",
                    TargetName = module,
                    Confidence = 1.0,
                    Source = Id,
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

            // class/struct/interface/enum/record (including generics and base types)
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
                    "record" => SymbolKind.Class, // record is a class
                    _ => SymbolKind.Other,
                };
                var name = typeMatch.Groups[4].Value;
                knownTypeNames.Add(name);
                symbols.Add(new CodeSymbol
                {
                    Name = name,
                    Kind = kind,
                    StartLine = i + 1,
                    EndLine = FindEndOfBlock(lines, i),
                    Modifier = typeMatch.Groups[1].Success ? typeMatch.Groups[1].Value : null,
                });

                // If inherits from base types, add syntactic relation
                if (typeMatch.Groups[5].Success)
                {
                    var bases = typeMatch.Groups[5].Value.Split(',', StringSplitOptions.TrimEntries);
                    foreach (var baseType in bases)
                    {
                        var cleanBase = baseType.Split('<')[0].Trim();
                        if (!string.IsNullOrEmpty(cleanBase))
                        {
                            relations.Add(new CodeRelation
                            {
                                RelationType = RelationType.Syntactic,
                                Relation = "inherits",
                                TargetName = cleanBase,
                                Confidence = 0.9,
                                Source = Id,
                            });
                        }
                    }
                }
            }

            // constructor (matches: public ClassName( or ClassName()
            var ctorMatch = ConstructorRegex().Match(line);
            if (ctorMatch.Success)
            {
                var name = ctorMatch.Groups[2].Value;
                if (knownTypeNames.Contains(name))
                {
                    symbols.Add(new CodeSymbol
                    {
                        Name = $".ctor",
                        Kind = SymbolKind.Method,
                        StartLine = i + 1,
                        EndLine = i + 1,
                        Modifier = ctorMatch.Groups[1].Success ? ctorMatch.Groups[1].Value : null,
                    });
                }
            }

            // method (including async, generics, expression-bodied)
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

            // expression-bodied method/property: ReturnType Name(...) => 
            var exprBodiedMatch = ExpressionBodiedRegex().Match(line);
            if (exprBodiedMatch.Success)
            {
                var name = exprBodiedMatch.Groups[2].Value;
                // Avoid duplicate with MethodRegex
                if (!symbols.Any(s => s.Name == name && s.StartLine == i + 1))
                {
                    symbols.Add(new CodeSymbol
                    {
                        Name = name,
                        Kind = SymbolKind.Method,
                        StartLine = i + 1,
                        EndLine = i + 1,
                        Modifier = exprBodiedMatch.Groups[1].Success ? exprBodiedMatch.Groups[1].Value : null,
                    });
                }
            }

            // property (with auto-getter/setter)
            var propMatch = PropertyRegex().Match(line);
            if (propMatch.Success)
            {
                var propName = propMatch.Groups[3].Value;
                if (!symbols.Any(s => s.Name == propName && s.StartLine == i + 1))
                {
                    symbols.Add(new CodeSymbol
                    {
                        Name = propName,
                        Kind = SymbolKind.Property,
                        StartLine = i + 1,
                        EndLine = i + 1,
                        Modifier = propMatch.Groups[1].Value,
                    });
                }
            }

            // field/constant: private readonly Type _field; or public const int MaxSize = 100;
            var fieldMatch = FieldRegex().Match(line);
            if (fieldMatch.Success)
            {
                var fieldName = fieldMatch.Groups[4].Value;
                var isConst = fieldMatch.Groups[2].Value.Contains("const");
                symbols.Add(new CodeSymbol
                {
                    Name = fieldName,
                    Kind = isConst ? SymbolKind.Constant : SymbolKind.Field,
                    StartLine = i + 1,
                    EndLine = i + 1,
                    Modifier = fieldMatch.Groups[1].Value,
                });
            }

            // call expressions (heuristic) — filtered to exclude keywords
            var callMatches = CallRegex().Matches(line);
            foreach (System.Text.RegularExpressions.Match call in callMatches)
            {
                var funcName = call.Groups[1].Value;
                if (NonCallKeywords.Contains(funcName))
                    continue;
                if (funcName.Length < 2)
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

    // using System; or using static System.Math; or using Alias = System.Text;
    [GeneratedRegex(@"^\s*using\s+(?:static\s+)?(?:([\w.]+)\s+=\s+)?([\w.]+)")]
    private static partial Regex UsingRegex();

    [GeneratedRegex(@"^\s*namespace\s+([\w.]+)")]
    private static partial Regex NamespaceRegex();

    // Matches: [access] [modifiers] (class|interface|struct|enum|record) Name<Generics> : Base, Interfaces
    [GeneratedRegex(@"^\s*(public|private|protected|internal)?\s*(abstract|sealed|static)?\s*(class|interface|struct|enum|record)\s+(\w+)(?:<[^>]+>)?(?:\s*:\s*([^\{]+))?")]
    private static partial Regex TypeRegex();

    // Matches: public ClassName( or internal ClassName(
    [GeneratedRegex(@"^\s*(public|private|protected|internal)?\s*(\w+)\s*\(")]
    private static partial Regex ConstructorRegex();

    // Matches: [access] [async|static] ReturnType MethodName(
    [GeneratedRegex(@"^\s*(public|private|protected|internal)?\s+(?:static\s+|async\s+|override\s+|virtual\s+|abstract\s+)*([\w<>\[\],?]+)\s+(\w+)\s*\(")]
    private static partial Regex MethodRegex();

    // Matches: ReturnType MethodName(...) => expression;
    [GeneratedRegex(@"^\s*(public|private|protected|internal)?\s+(?:static\s+|async\s+)*([\w<>\[\],?]+)\s+(\w+)\s*\([^)]*\)\s*=>")]
    private static partial Regex ExpressionBodiedRegex();

    // Matches: public Type Name { get; set; }
    [GeneratedRegex(@"^\s*(public|private|protected|internal)\s+(\w+(?:<[^>]+>)?)\s+(\w+)\s*\{")]
    private static partial Regex PropertyRegex();

    // Matches: access [modifiers] Type fieldName;  or  access [modifiers] Type fieldName = value;
    [GeneratedRegex(@"^\s*(public|private|protected|internal)\s+((?:(?:readonly|const|static)\s+)*)\s*(\w+(?:<[^>]+>)?)\s+(\w+)\s*[;=]")]
    private static partial Regex FieldRegex();

    // Matches: identifier followed by (
    [GeneratedRegex(@"(\w+)\s*\(")]
    private static partial Regex CallRegex();
}
