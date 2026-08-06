using AiKv.Core.Parsing;

namespace AiKv.Indexing.Parsing;
public sealed class TextParser : ICodeParser
{
    public string Id => "text";
    public string Version => "1.0";
    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".cfg", ".conf", ".ini", ".env.example", ".log",
    };

    public ParseResult Parse(string content, string filePath)
    {
        var lines = content.Split('\n');
        var symbols = new List<CodeSymbol>();
        var imports = new List<ImportDeclaration>();
        var diagnostics = new List<ParseDiagnostic>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');

            // Markdown headings
            if (line.StartsWith('#'))
            {
                var level = 0;
                while (level < line.Length && line[level] == '#') level++;
                if (level <= 6 && line.Length > level)
                {
                    symbols.Add(new CodeSymbol
                    {
                        Name = line[level..].Trim(),
                        Kind = SymbolKind.Other,
                        StartLine = i + 1,
                        EndLine = i + 1,
                    });
                }
            }

            // INI/Config key-value pairs
            if (line.Contains('=') && !line.StartsWith('#') && !line.StartsWith("//"))
            {
                var eqIdx = line.IndexOf('=');
                var key = line[..eqIdx].Trim();
                if (key.Length > 0 && IsValidIdentifier(key))
                {
                    symbols.Add(new CodeSymbol
                    {
                        Name = key,
                        Kind = SymbolKind.Other,
                        StartLine = i + 1,
                        EndLine = i + 1,
                    });
                }
            }

            // Comments as context
            if (line.StartsWith("//") || line.StartsWith("/*") || line.StartsWith('*'))
            {
                // Comments are not symbols but could be useful for context
            }
        }

        return new ParseResult
        {
            ParserId = Id,
            ParserVersion = Version,
            Language = "text",
            Symbols = symbols,
        };
    }

    private static bool IsValidIdentifier(string s)
    {
        foreach (var c in s)
        {
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '-' && c != '.')
                return false;
        }
        return true;
    }
}
