using System.Text.RegularExpressions;
using CacheHub.Core.Parsing;

namespace CacheHub.Indexing.Parsing;

/// <summary>
/// Regex-based Ruby parser (regex-baseline):
/// extracts require statements, modules, classes, methods, and constants.
/// Methods inside class/module body are marked as Methods; standalone methods as Functions.
/// </summary>
public sealed partial class RubyRegexParser : ICodeParser
{
    public string Id => "ruby-regex-baseline";
    public string Version => "1.0";
    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".rb",
    };

    private static readonly HashSet<string> NonCallKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "if", "elsif", "else", "unless", "while", "until", "for", "case",
        "when", "return", "break", "next", "redo", "retry", "yield",
        "begin", "rescue", "ensure", "raise", "throw", "catch",
        "def", "class", "module", "require", "require_relative",
        "include", "extend", "attr_accessor", "attr_reader", "attr_writer",
        "private", "public", "protected", "end", "do", "then",
        "true", "false", "nil", "self", "super", "and", "or", "not",
        "lambda", "proc", "puts", "print", "p", "pp", "sleep",
        "require", "load", "require_relative", "defined?", "alias",
        "undef", "BEGIN", "END", "__FILE__", "__LINE__", "__method__",
    };

    public ParseResult Parse(string content, string filePath)
    {
        var lines = content.Split('\n');
        var symbols = new List<CodeSymbol>();
        var imports = new List<ImportDeclaration>();
        var calls = new List<CallExpression>();
        var relations = new List<CodeRelation>();

        // Track class/module nesting via indent stack (like Python)
        var bodyIndentStack = new Stack<int>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith('#') || string.IsNullOrWhiteSpace(trimmed))
                continue;

            // require/require_relative
            var requireMatch = RequireRegex().Match(line);
            if (requireMatch.Success)
            {
                var module = requireMatch.Groups[2].Value;
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

            // module Name
            var moduleMatch = ModuleRegex().Match(line);
            if (moduleMatch.Success)
            {
                var indent = GetIndent(line);
                while (bodyIndentStack.Count > 0 && indent <= bodyIndentStack.Peek())
                    bodyIndentStack.Pop();
                bodyIndentStack.Push(indent);
                symbols.Add(new CodeSymbol
                {
                    Name = moduleMatch.Groups[1].Value,
                    Kind = SymbolKind.Namespace,
                    StartLine = i + 1,
                    EndLine = FindEndKeyword(lines, i),
                });
                continue;
            }

            // class Name [ < BaseClass ]
            var classMatch = ClassRegex().Match(line);
            if (classMatch.Success)
            {
                var indent = GetIndent(line);
                while (bodyIndentStack.Count > 0 && indent <= bodyIndentStack.Peek())
                    bodyIndentStack.Pop();
                bodyIndentStack.Push(indent);
                symbols.Add(new CodeSymbol
                {
                    Name = classMatch.Groups[1].Value,
                    Kind = SymbolKind.Class,
                    StartLine = i + 1,
                    EndLine = FindEndKeyword(lines, i),
                });

                // Inheritance: class Name < BaseClass
                if (classMatch.Groups[2].Success)
                {
                    relations.Add(new CodeRelation
                    {
                        RelationType = RelationType.Syntactic,
                        Relation = "inherits",
                        TargetName = classMatch.Groups[2].Value,
                        Confidence = 0.9,
                        Source = Id,
                        SourceSymbol = classMatch.Groups[1].Value,
                        Line = i + 1,
                    });
                }
                continue;
            }

            // def method_name or def self.method_name or def ClassName.method_name
            var defMatch = DefRegex().Match(line);
            if (defMatch.Success)
            {
                var indent = GetIndent(line);
                while (bodyIndentStack.Count > 0 && indent <= bodyIndentStack.Peek())
                    bodyIndentStack.Pop();
                var isMethod = bodyIndentStack.Count > 0 && indent > bodyIndentStack.Peek();
                var methodName = defMatch.Groups[2].Value;
                symbols.Add(new CodeSymbol
                {
                    Name = methodName,
                    Kind = isMethod ? SymbolKind.Method : SymbolKind.Function,
                    StartLine = i + 1,
                    EndLine = FindEndKeyword(lines, i),
                    Modifier = isMethod ? "method" : null,
                });
                continue;
            }

            // Constants: UPPER_CASE = ...
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
            Language = "ruby",
            Symbols = symbols,
            Imports = imports,
            CallExpressions = calls,
            Relations = relations,
        };
    }

    private static int FindEndKeyword(string[] lines, int startLine)
    {
        var depth = 1;
        for (var i = startLine + 1; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith('#') || string.IsNullOrWhiteSpace(trimmed))
                continue;

            // Count `do`, `def`, `class`, `module`, `begin`, `if`, `unless`, `while`, `until`, `case` as depth+
            // and `end` as depth-
            if (trimmed.StartsWith("end", StringComparison.Ordinal) ||
                trimmed.StartsWith("end ", StringComparison.Ordinal))
            {
                depth--;
                if (depth == 0) return i + 1;
            }
            else if (StartsBlock(trimmed))
            {
                depth++;
            }
        }
        return startLine + 1;
    }

    private static bool StartsBlock(string trimmed)
    {
        return trimmed.StartsWith("def ", StringComparison.Ordinal) ||
               trimmed.StartsWith("class ", StringComparison.Ordinal) ||
               trimmed.StartsWith("module ", StringComparison.Ordinal) ||
               trimmed.StartsWith("begin", StringComparison.Ordinal) ||
               trimmed.StartsWith("do", StringComparison.Ordinal) ||
               trimmed.StartsWith("do ", StringComparison.Ordinal) ||
               (trimmed.StartsWith("if ", StringComparison.Ordinal) && !trimmed.Contains(" then ")) ||
               trimmed.StartsWith("unless ", StringComparison.Ordinal) ||
               trimmed.StartsWith("while ", StringComparison.Ordinal) ||
               trimmed.StartsWith("until ", StringComparison.Ordinal) ||
               trimmed.StartsWith("case ", StringComparison.Ordinal);
    }

    private static int GetIndent(string line) => line.TakeWhile(c => c == ' ').Count();

    // require_relative 'path' or require 'path'
    [GeneratedRegex(@"^\s*(require|require_relative)\s+['""]([^'""]+)['""]")]
    private static partial Regex RequireRegex();

    // module Name
    [GeneratedRegex(@"^\s*module\s+(\w+(?:::\w+)*)")]
    private static partial Regex ModuleRegex();

    // class Name [ < BaseClass ]
    [GeneratedRegex(@"^\s*class\s+([\w:]+)(?:\s*<\s*([\w:]+))?")]
    private static partial Regex ClassRegex();

    // def name or def self.name or def Class.name
    [GeneratedRegex(@"^\s*def\s+(?:self\.|(\w+)\.)?(\w+[!?]?|\w+[=!<>>=~&|*^%]+)")]
    private static partial Regex DefRegex();

    // UPPER_CASE = value (module-level constant)
    [GeneratedRegex(@"^([A-Z][A-Z0-9_]*)\s*=")]
    private static partial Regex ConstantRegex();

    [GeneratedRegex(@"(\w+[!?]?)\s*(?:\(|\s|$)")]
    private static partial Regex CallRegex();
}
