using CacheHub.Core.Parsing;
using CacheHub.Core.Parsing.Outline;
using CacheHub.Core.Parsing.RepoMap;
using CacheHub.Indexing.Parsing;
using CacheHub.Indexing.Parsing.Cache;

namespace CacheHub.Cli.Commands;

public static class RepoMapCommands
{
    public static async Task<int> Handle(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: cachehub repomap <directory> [--max-tokens=N] [--output=json]");
            return 1;
        }

        var dir = args[0];
        var maxTokensStr = args.SkipWhile(a => a != "--max-tokens").Skip(1).FirstOrDefault();
        var maxTokens = maxTokensStr is not null
            ? int.Parse(maxTokensStr, System.Globalization.CultureInfo.InvariantCulture)
            : 4000;
        var outputJson = args.Contains("--output=json", StringComparer.OrdinalIgnoreCase) ||
                         args.Contains("--json", StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(dir))
        {
            Console.Error.WriteLine($"Error: Directory not found: {dir}");
            return 1;
        }

        var files = new List<(string relativePath, FileOutline outline)>();
        var cache = new ParserCache();
        var enumerator = new CacheHub.Indexing.Scanning.DirectoryEnumerator();

        await foreach (var file in enumerator.EnumerateAsync(dir))
        {
            if (file.IsDirectory) continue;
            var ext = file.Extension ?? "";
            if (ext is not (".cs" or ".ts" or ".tsx" or ".js" or ".py" or ".md")) continue;

            var fullPath = file.Path;
            if (!File.Exists(fullPath)) continue;

            var content = File.ReadAllText(fullPath);
            var relativePath = CacheHub.Core.Paths.PathNormalizer.GetRelativePath(dir, fullPath);

            ICodeParser? parser = ext switch
            {
                ".cs" => new CSharpRegexParser(),
                ".ts" or ".tsx" or ".js" => new TypeScriptRegexParser(),
                ".py" => new PythonRegexParser(),
                ".md" => new MarkdownParser(),
                _ => new TextParser(),
            };

            var hash = $"file:{fullPath}:{file.LastModified.Ticks}";
            var result = cache.GetOrParse(content, fullPath, hash, parser);
            var outline = DeterministicOutlineGenerator.Generate(result, relativePath);
            files.Add((relativePath, outline));
        }

        var repomap = RepoMapGenerator.Generate(dir, files, maxTokens);

        if (outputJson)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                rootPath = repomap.RootPath,
                totalFiles = repomap.TotalFiles,
                totalSymbols = repomap.TotalSymbols,
                estimatedTokens = repomap.EstimatedTokens,
                root = new
                {
                    name = repomap.Root.Name,
                    type = repomap.Root.Type.ToString(),
                    children = repomap.Root.Children.Select(c => new
                    {
                        name = c.Name,
                        path = c.Path,
                        type = c.Type.ToString(),
                        symbolCount = c.SymbolCount,
                        keySymbols = c.KeySymbols.Select(s => new { name = s.Name, kind = s.Kind.ToString(), line = s.StartLine }),
                    }),
                },
            }, _jsonOpts);
            Console.WriteLine(json);
        }
        else
        {
            Console.WriteLine($"Repository Map: {repomap.RootPath}");
            Console.WriteLine($"  Files: {repomap.TotalFiles}");
            Console.WriteLine($"  Symbols: {repomap.TotalSymbols}");
            Console.WriteLine($"  Est. Tokens: {repomap.EstimatedTokens}");
            Console.WriteLine();
            Console.WriteLine("Structure:");
            foreach (var child in repomap.Root.Children)
            {
                Console.WriteLine($"  [{child.Type}] {child.Name} ({child.SymbolCount} symbols)");
                foreach (var sym in child.KeySymbols)
                    Console.WriteLine($"    {sym.Kind,-15} L{sym.StartLine,4}  {sym.Name}");
            }
        }

        return 0;
    }

    private static readonly System.Text.Json.JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };
}
