using System.Text.Json;
using System.Text.Json.Serialization;

namespace CacheHub.Core.Configuration;

/// <summary>
/// CacheHub configuration file model.
/// Stored as .cachehub-config.json in the CacheHub data directory.
/// </summary>
public sealed record CacheHubConfig
{
    [JsonPropertyName("version")]
    public string Version { get; init; } = "1";

    [JsonPropertyName("defaultModel")]
    public string? DefaultModel { get; init; }

    [JsonPropertyName("defaultBudget")]
    public BudgetConfig? DefaultBudget { get; init; }

    [JsonPropertyName("security")]
    public SecurityConfig? Security { get; init; }

    [JsonPropertyName("gateway")]
    public GatewayConfigFile? Gateway { get; init; }

    [JsonPropertyName("indexing")]
    public IndexingConfig? Indexing { get; init; }
}

public sealed record BudgetConfig
{
    [JsonPropertyName("modelContextWindow")]
    public int ModelContextWindow { get; init; } = 128000;

    [JsonPropertyName("agentReservedTokens")]
    public int AgentReservedTokens { get; init; } = 18000;

    [JsonPropertyName("responseReservedTokens")]
    public int ResponseReservedTokens { get; init; } = 12000;

    [JsonPropertyName("targetRatio")]
    public double TargetRatio { get; init; } = 0.625;

    [JsonPropertyName("hardLimitRatio")]
    public double HardLimitRatio { get; init; } = 0.703;

    [JsonPropertyName("safetyMargin")]
    public int SafetyMargin { get; init; } = 10000;
}

public sealed record SecurityConfig
{
    [JsonPropertyName("mode")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Security.ExfiltrationMode Mode { get; init; } = Security.ExfiltrationMode.Standard;

    [JsonPropertyName("enableSecretScan")]
    public bool EnableSecretScan { get; init; } = true;

    [JsonPropertyName("blockedExtensions")]
    public IReadOnlyList<string>? BlockedExtensions { get; init; }
}

public sealed record GatewayConfigFile
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("port")]
    public int Port { get; init; } = 5218;

    [JsonPropertyName("providerUrl")]
    public string? ProviderUrl { get; init; }

    [JsonPropertyName("enableCache")]
    public bool EnableCache { get; init; } = true;

    [JsonPropertyName("enableSingleFlight")]
    public bool EnableSingleFlight { get; init; } = true;
}

public sealed record IndexingConfig
{
    [JsonPropertyName("maxDepth")]
    public int MaxDepth { get; init; } = 50;

    [JsonPropertyName("maxFileCount")]
    public int MaxFileCount { get; init; } = 500000;

    [JsonPropertyName("maxFileSizeMb")]
    public int MaxFileSizeMb { get; init; } = 100;

    [JsonPropertyName("followSymlinks")]
    public bool FollowSymlinks { get; init; }
}

/// <summary>
/// Loads and saves CacheHub configuration.
/// </summary>
public sealed class ConfigManager
{
    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };
    private readonly string _configPath;

    public ConfigManager(string? configDir = null)
    {
        var dir = configDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
            "CacheHub");
        _configPath = Path.Combine(dir, "config", ".cachehub-config.json");
    }

    public CacheHubConfig Load()
    {
        if (!File.Exists(_configPath)) return new CacheHubConfig();
        var json = File.ReadAllText(_configPath);
        return JsonSerializer.Deserialize<CacheHubConfig>(json, _jsonOpts) ?? new CacheHubConfig();
    }

    public void Save(CacheHubConfig config)
    {
        var dir = Path.GetDirectoryName(_configPath);
        if (dir is not null) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(config, _jsonOpts);

        // Atomic write: write to temp file then replace, to avoid config corruption on crash.
        var tmpPath = _configPath + ".tmp";
        File.WriteAllText(tmpPath, json);
        File.Move(tmpPath, _configPath, overwrite: true);
    }

    public bool Exists => File.Exists(_configPath);
    public string ConfigPath => _configPath;
}
