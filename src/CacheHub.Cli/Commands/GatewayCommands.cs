using CacheHub.Core.Caching;
using CacheHub.Core.Security;
using CacheHub.Gateway;
using CacheHub.Gateway.Server;
using CacheHub.Storage;
using CacheHub.Storage.Database;
using CacheHub.Storage.Database.Migrations;

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

        // V6: Fall back to GUI-saved provider config (review #33 round-trip closure).
        // If --provider-url absent, read what the Desktop Settings provider UI saved.
        var cm = new CacheHub.Core.Configuration.ConfigManager();
        var storedConfig = cm.Load();
        var storedGw = storedConfig.Gateway;
        if (string.IsNullOrEmpty(baseUrl) && storedGw?.ProviderUrl is not null && storedGw.ProviderUrl.Length > 0)
        {
            baseUrl = storedGw.ProviderUrl.TrimEnd('/');
            Console.Error.WriteLine($"  Using GUI-saved provider URL: {baseUrl}");
        }
        if (port == "5218" && storedGw?.Port > 0)
        {
            port = storedGw.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (string.IsNullOrEmpty(baseUrl))
        {
            Console.Error.WriteLine("Error: --provider-url=<url> is required");
            Console.Error.WriteLine("  (Or configure the provider once via the Desktop GUI Settings → Gateway/Provider.)");
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

        // Create persistent cache store if a workspace DB path exists
        ICacheStore? cacheStore = null;
        var appData = new AppDataDirectory();
        var gatewayDbPath = Path.Combine(appData.Root, "gateway", "cache.db");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(gatewayDbPath)!);
            var cacheFactory = new SqliteConnectionFactory(gatewayDbPath);
            var cacheRunner = new MigrationRunner(cacheFactory, gatewayDbPath,
            [
                new Migration0001Initial(),
                new Migration0002Fts5(),
                new Migration0003ContextPackages(),
                new Migration0004Feedback(),
                new Migration0005ContextPackageDetails(),
                new Migration0006SchemaV2(),
                new Migration0007ContextPackageFields(),
                new Migration0008ContextPackageFk(),
                new Migration0009PersistentCache(),
                new Migration0010RelationSourceColumn(),
            ]);
            cacheRunner.Migrate();
            cacheStore = new Storage.Caching.SqliteCacheStore(cacheFactory, Path.Combine(appData.Root, "gateway", "blobs"));
            Console.Error.WriteLine($"  Persistent cache: {gatewayDbPath}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Warning: Persistent cache unavailable ({ex.Message}), using in-memory cache");
        }

        // V6: Check security policy — block Gateway provider forwarding in Offline mode
        var (secPolicy, secEnforcer) = SecurityPolicyResolver.CreateEnforcer();
        var isOffline = secPolicy.Mode == CacheHub.Core.Security.ExfiltrationMode.Offline;

        var config = new GatewayConfig
        {
            ProviderBaseUrl = baseUrl,
            ProviderApiKey = apiKey ?? "",
            Port = portNum,
            CacheStore = cacheStore,
            FallbackProviders = LoadFallbackProviders(appData),
            IsOfflineMode = isOffline,
        };

        Console.Error.WriteLine($"Starting CacheHub Gateway on http://127.0.0.1:{config.Port}");
        Console.Error.WriteLine($"  Provider: {config.ProviderBaseUrl}");
        Console.Error.WriteLine($"  Cache: {config.EnableCache}");
        Console.Error.WriteLine($"  SingleFlight: {config.EnableSingleFlight}");
        if (isOffline)
            Console.Error.WriteLine("  ⚠ Security mode: Offline — provider forwarding blocked");
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

    /// <summary>
    /// Loads fallback providers from the gateway provider config file.
    /// Path: {appData.Root}/gateway/providers.json
    /// Format: { "fallbackProviders": [{ "baseUrl": "...", "apiKey": "env:VAR" }] }
    /// </summary>
    private static List<FallbackProvider> LoadFallbackProviders(AppDataDirectory appData)
    {
        var configPath = Path.Combine(appData.Root, "gateway", "providers.json");
        var providerConfig = GatewayProviderConfigLoader.Load(configPath);
        if (providerConfig is null) return [];

        var fallbacks = GatewayProviderConfigLoader.ToFallbackProviders(providerConfig);
        if (fallbacks.Count > 0)
            Console.Error.WriteLine($"  Fallback providers: {fallbacks.Count} (from {configPath})");
        return fallbacks;
    }
}
