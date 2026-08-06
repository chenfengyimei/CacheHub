using CacheHub.Core.Parsing;
using CacheHub.Core.Parsing.Outline;
using CacheHub.Indexing.Parsing;
using CacheHub.Indexing.Parsing.Cache;

namespace CacheHub.Cli.Commands;

public static class OutlineCommands
{
    public static int Handle(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: cachehub outline <file> [--output=json]");
            return 1;
        }

        var filePath = args[0];
        var outputJson = args.Contains("--output=json", StringComparer.OrdinalIgnoreCase) ||
                         args.Contains("--json", StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"Error: File not found: {filePath}");
            return 1;
        }

        var content = File.ReadAllText(filePath);
        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        // Select parser by extension
        ICodeParser? parser = ext switch
        {
            ".cs" => new CSharpRegexParser(),
            ".ts" or ".tsx" or ".js" or ".jsx" => new TypeScriptRegexParser(),
            ".py" => new PythonRegexParser(),
            ".md" or ".markdown" => new MarkdownParser(),
            _ => new TextParser(),
        };

        // Use cache
        var cache = new ParserCache();
        var hash = $"file:{filePath}:{new FileInfo(filePath).LastWriteTimeUtc.Ticks}";
        var result = cache.GetOrParse(content, filePath, hash, parser);

        // Generate outline
        var outline = DeterministicOutlineGenerator.Generate(result, filePath);

        if (outputJson)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                file = outline.FilePath,
                language = outline.Language,
                parser = outline.ParserId,
                parserVersion = outline.ParserVersion,
                symbols = outline.Symbols.Select(s => new
                {
                    name = s.Name,
                    kind = s.Kind.ToString(),
                    startLine = s.StartLine,
                    endLine = s.EndLine,
                    modifier = s.Modifier,
                }),
                imports = outline.Imports.Select(i => new
                {
                    module = i.Module,
                    name = i.ImportedName,
                    line = i.Line,
                }),
            }, _jsonOpts);
            Console.WriteLine(json);
        }
        else
        {
            Console.WriteLine($"File: {outline.FilePath}");
            Console.WriteLine($"Language: {outline.Language}");
            Console.WriteLine($"Parser: {outline.ParserId} v{outline.ParserVersion}");
            Console.WriteLine();

            if (outline.Imports.Count > 0)
            {
                Console.WriteLine("Imports:");
                foreach (var imp in outline.Imports)
                    Console.WriteLine($"  L{imp.Line,4}: {imp.Module}" + (imp.ImportedName is not null ? $" ({imp.ImportedName})" : ""));
                Console.WriteLine();
            }

            if (outline.Symbols.Count > 0)
            {
                Console.WriteLine("Symbols:");
                Console.WriteLine($"  {"Line",-10} {"Kind",-15} {"Modifier",-12} Name");
                Console.WriteLine($"  {new string('-', 10)} {new string('-', 15)} {new string('-', 12)} {new string('-', 30)}");
                foreach (var s in outline.Symbols)
                {
                    var range = s.StartLine == s.EndLine ? $"L{s.StartLine}" : $"L{s.StartLine}-{s.EndLine}";
                    Console.WriteLine($"  {range,-10} {s.Kind,-15} {s.Modifier ?? "-",-12} {s.Name}");
                }
            }
            else
            {
                Console.WriteLine("(No symbols found)");
            }
        }

        return 0;
    }

    private static readonly System.Text.Json.JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };
}
