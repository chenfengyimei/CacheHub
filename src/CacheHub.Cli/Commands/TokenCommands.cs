using CacheHub.Core.Tokens;

namespace CacheHub.Cli.Commands;

public static class TokenCommands
{
    public static int Handle(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: cachehub token <text|file> [--file] [--model=<id>] [--output=json]");
            Console.Error.WriteLine("  Default: treat argument as text");
            Console.Error.WriteLine("  --file: treat argument as file path");
            return 1;
        }

        var input = args[0];
        var isFile = args.Contains("--file", StringComparer.OrdinalIgnoreCase);
        var modelId = GetOpt(args, "--model");
        var outputJson = args.Contains("--output=json", StringComparer.OrdinalIgnoreCase) ||
                         args.Contains("--json", StringComparer.OrdinalIgnoreCase);

        var registry = new TokenizerRegistry();
        if (modelId is not null)
            registry.Register(modelId, new CodeTokenizer());

        var tokenizer = modelId is not null ? registry.GetForModel(modelId) : new CodeTokenizer();

        string text;
        string source;

        if (isFile)
        {
            if (!File.Exists(input))
            {
                Console.Error.WriteLine($"Error: File not found: {input}");
                return 1;
            }
            text = File.ReadAllText(input);
            source = input;
        }
        else
        {
            text = input;
            source = "(text input)";
        }

        var tokens = tokenizer.CountTokens(text);
        var charEstimate = new CharEstimateTokenizer().CountTokens(text);
        var wordCount = new WordBoundaryTokenizer().CountTokens(text);

        if (outputJson)
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                source,
                chars = text.Length,
                tokens,
                tokenizer = tokenizer.Id,
                modelId = modelId ?? "(default)",
                charEstimate,
                wordCount,
            }, _jsonOpts));
        }
        else
        {
            Console.WriteLine($"Source:       {source}");
            Console.WriteLine($"Chars:        {text.Length}");
            Console.WriteLine($"Tokens:       {tokens}");
            Console.WriteLine($"Tokenizer:    {tokenizer.Id}");
            Console.WriteLine($"Model:        {modelId ?? "(default)"}");
            Console.WriteLine($"Char est:     {charEstimate}");
            Console.WriteLine($"Word count:   {wordCount}");
        }

        return 0;
    }

    private static readonly System.Text.Json.JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    private static string? GetOpt(string[] args, string prefix) =>
        args.FirstOrDefault(a => a.StartsWith(prefix + "=", StringComparison.OrdinalIgnoreCase))?[(prefix.Length + 1)..];
}
