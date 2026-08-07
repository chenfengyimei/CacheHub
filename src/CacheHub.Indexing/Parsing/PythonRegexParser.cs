using System.Text.RegularExpressions;
using CacheHub.Core.Parsing;

namespace CacheHub.Indexing.Parsing;

/// <summary>
/// Regex-based Python parser (regex-baseline):
/// extracts classes, functions, imports, decorators, and module-level variables.
/// Import relations are marked syntactic; call relations are heuristic.
/// </summary>
public sealed partial class PythonRegexParser : ICodeParser
{
    public string Id => "python-regex-baseline";
    public string Version => "2.0";
    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".py",
    };

    private static readonly HashSet<string> NonCallKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "if", "else", "elif", "for", "while", "return", "yield",
        "import", "from", "as", "in", "not", "and", "or", "is",
        "def", "class", "try", "except", "finally", "raise",
        "with", "lambda", "pass", "break", "continue", "global",
        "nonlocal", "assert", "del", "print", "len", "range",
        "True", "False", "None", "self", "cls", "super",
        "type", "isinstance", "issubclass", "getattr", "setattr",
        "hasattr", "staticmethod", "classmethod", "property",
        "async", "await", "asyncio",
    };

    public ParseResult Parse(string content, string filePath)
    {
        var lines = content.Split('\n');
        var symbols = new List<CodeSymbol>();
        var imports = new List<ImportDeclaration>();
        var calls = new List<CallExpression>();
        var relations = new List<CodeRelation>();

        // Stack of enclosing class indents to correctly handle nested classes.
        // A def is a method if it's indented deeper than its nearest enclosing class.
        var classIndentStack = new Stack<int>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.TrimStart();

            // Skip comments and empty lines
            if (trimmed.StartsWith('#') || string.IsNullOrWhiteSpace(trimmed))
                continue;

            // from ... import ...
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
                relations.Add(new CodeRelation
                {
                    RelationType = RelationType.Syntactic,
                    Relation = "imports",
                    TargetName = fromMatch.Groups[1].Value,
                    Confidence = 1.0,
                    Source = Id,
                });
                continue;
            }

            // import ...
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
                relations.Add(new CodeRelation
                {
                    RelationType = RelationType.Syntactic,
                    Relation = "imports",
                    TargetName = importMatch.Groups[1].Value,
                    Confidence = 1.0,
                    Source = Id,
                });
                continue;
            }

            // class with optional base classes: class Name(Base1, Base2):
            var classMatch = ClassRegex().Match(line);
            if (classMatch.Success)
            {
                var className = classMatch.Groups[1].Value;
                var classIndent = GetIndent(line);
                // Pop inner classes that are dedented relative to this new class.
                while (classIndentStack.Count > 0 && classIndent <= classIndentStack.Peek())
                    classIndentStack.Pop();
                classIndentStack.Push(classIndent);
                symbols.Add(new CodeSymbol
                {
                    Name = className,
                    Kind = SymbolKind.Class,
                    StartLine = i + 1,
                    EndLine = FindBlockEnd(lines, i),
                });

                // Base classes
                if (classMatch.Groups[2].Success)
                {
                    var bases = classMatch.Groups[2].Value.Split(',', StringSplitOptions.TrimEntries);
                    foreach (var baseType in bases)
                    {
                        var cleanBase = baseType.Split('[')[0].Trim();
                        if (!string.IsNullOrEmpty(cleanBase) && cleanBase != "object")
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
                continue;
            }

            // function/def (including async)
            var funcMatch = FunctionRegex().Match(line);
            if (funcMatch.Success)
            {
                var indent = GetIndent(line);
                // Pop class scopes this def is dedented from (a def at/below a class indent
                // belongs to an outer scope, not that class).
                while (classIndentStack.Count > 0 && indent <= classIndentStack.Peek())
                    classIndentStack.Pop();
                // A def indented deeper than the nearest enclosing class is a method.
                var isMethod = classIndentStack.Count > 0 && indent > classIndentStack.Peek();
                symbols.Add(new CodeSymbol
                {
                    Name = funcMatch.Groups[1].Value,
                    Kind = isMethod ? SymbolKind.Method : SymbolKind.Function,
                    StartLine = i + 1,
                    EndLine = FindBlockEnd(lines, i),
                    Modifier = isMethod ? "method" : null,
                });
                continue;
            }

            // Pop class scopes that this line is dedented from.
            while (classIndentStack.Count > 0 && GetIndent(line) <= classIndentStack.Peek())
                classIndentStack.Pop();

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
                relations.Add(new CodeRelation
                {
                    RelationType = RelationType.Syntactic,
                    Relation = "decorated_by",
                    TargetName = decoMatch.Groups[1].Value,
                    Confidence = 0.8,
                    Source = Id,
                });
                continue;
            }

            // module-level constant: UPPER_CASE = ...
            var constMatch = ConstantRegex().Match(line);
            if (constMatch.Success && GetIndent(line) == 0)
            {
                symbols.Add(new CodeSymbol
                {
                    Name = constMatch.Groups[1].Value,
                    Kind = SymbolKind.Constant,
                    StartLine = i + 1,
                    EndLine = i + 1,
                });
            }

            // call expressions (heuristic) — filtered
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
                    Confidence = 0.45,
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

    // class Name(Base1, Base2):
    [GeneratedRegex(@"^\s*class\s+(\w+)\s*(?:\(([^)]*)\))?\s*:")]
    private static partial Regex ClassRegex();

    [GeneratedRegex(@"^\s*(?:async\s+)?def\s+(\w+)")]
    private static partial Regex FunctionRegex();

    [GeneratedRegex(@"^\s*@(\w+)")]
    private static partial Regex DecoratorRegex();

    // UPPER_CASE = value (module-level constant)
    [GeneratedRegex(@"^([A-Z][A-Z0-9_]*)\s*=")]
    private static partial Regex ConstantRegex();

    [GeneratedRegex(@"(\w+)\s*\(")]
    private static partial Regex CallRegex();
}
