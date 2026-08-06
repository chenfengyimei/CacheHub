using CacheHub.Core.Configuration;
using CacheHub.Core.Security;
using System.Text.Json;

namespace CacheHub.Cli.Commands;

public static class ConfigCommands
{
    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    public static int Handle(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: cachehub config <show|set|init> [options]");
            return 1;
        }

        return args[0] switch
        {
            "show" => Show(),
            "init" => Init(),
            "set" => Set(args.AsSpan(1).ToArray()),
            _ => 1,
        };
    }

    private static int Show()
    {
        var manager = new ConfigManager();
        var config = manager.Load();

        Console.WriteLine($"Config file: {manager.ConfigPath}");
        Console.WriteLine($"Exists: {manager.Exists}");
        Console.WriteLine();
        Console.WriteLine($"Version: {config.Version}");
        Console.WriteLine($"Default model: {config.DefaultModel ?? "(none)"}");

        if (config.DefaultBudget is not null)
        {
            var b = config.DefaultBudget;
            Console.WriteLine($"Budget:");
            Console.WriteLine($"  Model context window: {b.ModelContextWindow}");
            Console.WriteLine($"  Agent reserved: {b.AgentReservedTokens}");
            Console.WriteLine($"  Response reserved: {b.ResponseReservedTokens}");
            Console.WriteLine($"  Target ratio: {b.TargetRatio}");
            Console.WriteLine($"  Hard limit ratio: {b.HardLimitRatio}");
            Console.WriteLine($"  Safety margin: {b.SafetyMargin}");
        }

        if (config.Security is not null)
        {
            var s = config.Security;
            Console.WriteLine($"Security:");
            Console.WriteLine($"  Mode: {s.Mode}");
            Console.WriteLine($"  Secret scan: {s.EnableSecretScan}");
            if (s.BlockedExtensions is not null)
                Console.WriteLine($"  Blocked extensions: {string.Join(", ", s.BlockedExtensions)}");
        }

        if (config.Gateway is not null)
        {
            var g = config.Gateway;
            Console.WriteLine($"Gateway:");
            Console.WriteLine($"  Enabled: {g.Enabled}");
            Console.WriteLine($"  Port: {g.Port}");
            Console.WriteLine($"  Provider URL: {g.ProviderUrl ?? "(none)"}");
            Console.WriteLine($"  Cache: {g.EnableCache}");
        }

        if (config.Indexing is not null)
        {
            var i = config.Indexing;
            Console.WriteLine($"Indexing:");
            Console.WriteLine($"  Max depth: {i.MaxDepth}");
            Console.WriteLine($"  Max file count: {i.MaxFileCount}");
            Console.WriteLine($"  Max file size (MB): {i.MaxFileSizeMb}");
            Console.WriteLine($"  Follow symlinks: {i.FollowSymlinks}");
        }

        return 0;
    }

    private static int Init()
    {
        var manager = new ConfigManager();
        if (manager.Exists)
        {
            Console.Error.WriteLine("Config file already exists. Use 'cachehub config set' to modify.");
            return 1;
        }

        var config = new CacheHubConfig
        {
            DefaultModel = null,
            DefaultBudget = new BudgetConfig(),
            Security = new SecurityConfig(),
            Indexing = new IndexingConfig(),
        };

        manager.Save(config);
        Console.WriteLine($"Config file created: {manager.ConfigPath}");
        return 0;
    }

    private static int Set(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: cachehub config set <key> <value>");
            Console.Error.WriteLine("Keys: defaultModel, security.mode, gateway.enabled, gateway.port, gateway.providerUrl");
            return 1;
        }

        var key = args[0];
        var value = args[1];
        var manager = new ConfigManager();
        var config = manager.Load();

        switch (key.ToLowerInvariant())
        {
            case "defaultmodel":
                config = config with { DefaultModel = value };
                break;
            case "security.mode":
                if (Enum.TryParse<ExfiltrationMode>(value, ignoreCase: true, out var mode))
                    config = config with { Security = (config.Security ?? new SecurityConfig()) with { Mode = mode } };
                else
                {
                    Console.Error.WriteLine($"Invalid mode. Valid: Standard, Restricted, PreviewRequired, Offline");
                    return 1;
                }
                break;
            case "gateway.enabled":
                config = config with { Gateway = (config.Gateway ?? new GatewayConfigFile()) with { Enabled = bool.Parse(value) } };
                break;
            case "gateway.port":
                config = config with { Gateway = (config.Gateway ?? new GatewayConfigFile()) with { Port = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture) } };
                break;
            case "gateway.providerurl":
                config = config with { Gateway = (config.Gateway ?? new GatewayConfigFile()) with { ProviderUrl = value } };
                break;
            default:
                Console.Error.WriteLine($"Unknown key: {key}");
                return 1;
        }

        manager.Save(config);
        Console.WriteLine($"Set {key} = {value}");
        return 0;
    }
}
