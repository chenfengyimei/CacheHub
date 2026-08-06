using System.Reflection;

namespace CacheHub.Cli.Commands;

public static class VersionCommands
{
    private static readonly System.Text.Json.JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    public static int Handle(string[] args)
    {
        var outputJson = args.Contains("--output=json", StringComparer.OrdinalIgnoreCase) ||
                         args.Contains("--json", StringComparer.OrdinalIgnoreCase);

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
        var info = new
        {
            version = "0.2.0",
            protocolVersion = "1.0",
            sdk = $".NET {Environment.Version}",
            os = Environment.OSVersion.ToString(),
            machineName = Environment.MachineName,
            timestamp = DateTimeOffset.UtcNow.ToString("O"),
        };

        if (outputJson)
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(info, _jsonOpts));
        }
        else
        {
            Console.WriteLine($"CacheHub v{info.version} (Protocol {info.protocolVersion})");
            Console.WriteLine($"  SDK: {info.sdk}");
            Console.WriteLine($"  OS: {info.os}");
            Console.WriteLine($"  Machine: {info.machineName}");
            Console.WriteLine($"  Time: {info.timestamp}");
        }

        return 0;
    }
}
