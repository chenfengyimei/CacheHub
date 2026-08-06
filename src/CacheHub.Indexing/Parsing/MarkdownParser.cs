using CacheHub.Core.Parsing;

namespace CacheHub.Indexing.Parsing;

/// <summary>
/// Markdown parser: extracts headings, code blocks, and section structure.
/// </summary>
public sealed class MarkdownParser : ICodeParser
{
    public string Id => "markdown";
    public string Version => "1.0";
    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".markdown",
    };

    public ParseResult Parse(string content, string filePath)
    {
        var lines = content.Split('\n');
        var symbols = new List<CodeSymbol>();
        var diagnostics = new List<ParseDiagnostic>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');

            // Headings
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

            // Code block boundaries
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                var lang = line[3..].Trim();
                symbols.Add(new CodeSymbol
                {
                    Name = string.IsNullOrEmpty(lang) ? "code-block" : $"code:{lang}",
                    Kind = SymbolKind.Other,
                    StartLine = i + 1,
                    EndLine = i + 1,
                });
            }
        }

        return new ParseResult
        {
            ParserId = Id,
            ParserVersion = Version,
            Language = "markdown",
            Symbols = symbols,
        };
    }
}
