using AiKv.Core.Gateway;
using AiKv.Core.Gateway.Server;

namespace AiKv.Cli.Commands;

public static class GatewayCommands
{
    public static async Task<int> HandleAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: aikv gateway <start|status|stop> [options]");
            return 1;
        }

        return args[0] switch
        {
            "start" => Start(args.AsSpan(1).ToArray()),
            "status" => Status(),
            "stop" => Stop(),
            _ => 1,
        };
    }

    private static int Start(string[] args)
    {
        var baseUrl = GetOpt(args, "--provider-url");
        var apiKey = GetOpt(args, "--provider-key");
        var port = GetOpt(args, "--port") ?? "5218";

        if (string.IsNullOrEmpty(baseUrl))
        {
            Console.Error.WriteLine("Error: --provider-url=<url> is required");
            Console.Error.WriteLine("Example: aikv gateway start --provider-url=https://api.openai.com --provider-key=sk-xxx");
            return 1;
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            Console.Error.WriteLine("Warning: --provider-key not set; Gateway will forward without auth header");
        }

        var config = new GatewayConfig
        {
            ProviderBaseUrl = baseUrl,
            ProviderApiKey = apiKey ?? "",
            Port = int.Parse(port, System.Globalization.CultureInfo.InvariantCulture),
        };

        Console.Error.WriteLine($"Starting AI_KV Gateway on http://{config.Host}:{config.Port}");
        Console.Error.WriteLine($"  Provider: {config.ProviderBaseUrl}");
        Console.Error.WriteLine($"  Cache: {config.EnableCache}");
        Console.Error.WriteLine($"  SingleFlight: {config.EnableSingleFlight}");
        Console.Error.WriteLine("  Press Ctrl+C to stop.");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            using var server = new GatewayServer(config);
            var statsTask = Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), cts.Token);
                    var stats = server.GetStats();
                    Console.Error.WriteLine($"[Stats] Requests: {stats.TotalRequests}, Cache hit: {stats.CacheHitRate:P1}, Avg latency: {stats.AvgLatencyMs:F0}ms");
                }
            }, cts.Token);

            server.StartAsync(cts.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Gateway stopped.");
        }

        return 0;
    }

    private static int Status()
    {
        Console.WriteLine("""{"status": "not_running", "message": "Gateway is not started. Use 'aikv gateway start' to begin."}""");
        return 0;
    }

    private static int Stop()
    {
        Console.Error.WriteLine("Gateway stop: Send Ctrl+C to the running gateway process.");
        return 0;
    }

    private static string? GetOpt(string[] args, string prefix) =>
        args.FirstOrDefault(a => a.StartsWith(prefix + "=", StringComparison.OrdinalIgnoreCase))?[prefix.Length..];
}
