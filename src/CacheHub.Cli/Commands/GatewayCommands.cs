using CacheHub.Core.Gateway;
using CacheHub.Core.Gateway.Server;

namespace CacheHub.Cli.Commands;

public static class GatewayCommands
{
    public static async Task<int> HandleAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: cachehub gateway <start|status|stop> [options]");
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
        var port = GetOpt(args, "--port") ?? "5218";

        // CONFIG-P2-001 fix: API key only from environment variable, never from CLI args
        var apiKey = Environment.GetEnvironmentVariable("CACHEHUB_PROVIDER_KEY")
                     ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        if (string.IsNullOrEmpty(baseUrl))
        {
            Console.Error.WriteLine("Error: --provider-url=<url> is required");
            Console.Error.WriteLine("Example: cachehub gateway start --provider-url=https://api.openai.com");
            Console.Error.WriteLine("  Set API key via: CACHEHUB_PROVIDER_KEY or OPENAI_API_KEY environment variable");
            return 1;
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            Console.Error.WriteLine("Warning: No API key found. Set CACHEHUB_PROVIDER_KEY or OPENAI_API_KEY environment variable.");
            Console.Error.WriteLine("  Gateway will forward without auth header (requests may fail).");
        }

        if (!int.TryParse(port, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var portNum))
        {
            Console.Error.WriteLine($"Error: Invalid port number: {port}");
            return 1;
        }

        var config = new GatewayConfig
        {
            ProviderBaseUrl = baseUrl,
            ProviderApiKey = apiKey ?? "",
            Port = portNum,
        };

        Console.Error.WriteLine($"Starting CacheHub Gateway on http://127.0.0.1:{config.Port}");
        Console.Error.WriteLine($"  Provider: {config.ProviderBaseUrl}");
        Console.Error.WriteLine($"  Cache: {config.EnableCache}");
        Console.Error.WriteLine($"  SingleFlight: {config.EnableSingleFlight}");
        Console.Error.WriteLine("  Press Ctrl+C to stop.");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            using var server = new GatewayServer(config);
            Console.Error.WriteLine($"  Access Token: {server.AccessToken}");
            Console.Error.WriteLine("  All API requests require: Authorization: Bearer <token>");
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
        Console.WriteLine("""{"status": "not_running", "message": "Gateway is not started. Use 'cachehub gateway start' to begin."}""");
        return 0;
    }

    private static int Stop()
    {
        Console.Error.WriteLine("Gateway stop: Send Ctrl+C to the running gateway process.");
        return 0;
    }

    private static string? GetOpt(string[] args, string prefix) =>
        args.FirstOrDefault(a => a.StartsWith(prefix + "=", StringComparison.OrdinalIgnoreCase))?[(prefix.Length + 1)..];
}
