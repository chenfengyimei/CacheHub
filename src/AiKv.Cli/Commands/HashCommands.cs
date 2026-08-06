using AiKv.Indexing.Hashing;

namespace AiKv.Cli.Commands;

public static class HashCommands
{
    public static async Task<int> HandleAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: aikv hash <file> [--output=json]");
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

        var info = new FileInfo(filePath);
        var hash = await FileHasher.HashAsync(filePath, info.Length);

        if (outputJson)
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                file = filePath,
                size = info.Length,
                hash = hash.Hash,
                isFullHash = hash.IsFullHash,
            }, _jsonOpts));
        }
        else
        {
            Console.WriteLine($"File:       {filePath}");
            Console.WriteLine($"Size:       {info.Length:N0} bytes");
            Console.WriteLine($"Hash:       {hash.Hash}");
            Console.WriteLine($"Full hash:  {hash.IsFullHash}");
        }

        return 0;
    }

    private static readonly System.Text.Json.JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };
}
