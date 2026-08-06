using System.Text.Json.Serialization;

namespace CacheHub.Core.Capabilities;

/// <summary>
/// Capability discovery response returned by CLI and Local API.
/// Allows clients to detect available modules and protocol version.
/// </summary>
public sealed record CapabilityDiscovery
{
    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("protocolVersion")]
    public required string ProtocolVersion { get; init; }

    [JsonPropertyName("capabilities")]
    public required CapabilityFlags Capabilities { get; init; }

    [JsonPropertyName("schemaVersions")]
    public IReadOnlyDictionary<string, int>? SchemaVersions { get; init; }

    [JsonPropertyName("limitations")]
    public IReadOnlyList<string>? Limitations { get; init; }
}

/// <summary>
/// Flags indicating which CacheHub modules are available.
/// All flags default to false; specific phases enable them as implemented.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Capability
{
    WorkspaceImport,
    ContextBuild,
    ContextExpand,
    ContextFeedback,
    ContextExplain,
    RepositoryClone,
    RepositoryPull,
    ProjectDetection,
    Gateway,
    Semantic,
    Lsp,
    FileExport,
    Cache,
}

/// <summary>
/// Capability flags for JSON serialization. Serializes as a list of enabled capabilities.
/// </summary>
public sealed record CapabilityFlags
{
    [JsonPropertyName("enabled")]
    public IReadOnlyList<Capability> Enabled { get; init; } = [];

    [JsonPropertyName("workspaceImport")]
    public bool WorkspaceImport => Enabled.Contains(Capability.WorkspaceImport);

    [JsonPropertyName("contextBuild")]
    public bool ContextBuild => Enabled.Contains(Capability.ContextBuild);

    [JsonPropertyName("contextExpand")]
    public bool ContextExpand => Enabled.Contains(Capability.ContextExpand);

    [JsonPropertyName("contextFeedback")]
    public bool ContextFeedback => Enabled.Contains(Capability.ContextFeedback);

    [JsonPropertyName("contextExplain")]
    public bool ContextExplain => Enabled.Contains(Capability.ContextExplain);

    [JsonPropertyName("repositoryClone")]
    public bool RepositoryClone => Enabled.Contains(Capability.RepositoryClone);

    [JsonPropertyName("repositoryPull")]
    public bool RepositoryPull => Enabled.Contains(Capability.RepositoryPull);

    [JsonPropertyName("projectDetection")]
    public bool ProjectDetection => Enabled.Contains(Capability.ProjectDetection);

    [JsonPropertyName("gateway")]
    public bool Gateway => Enabled.Contains(Capability.Gateway);

    [JsonPropertyName("semantic")]
    public bool Semantic => Enabled.Contains(Capability.Semantic);

    [JsonPropertyName("lsp")]
    public bool Lsp => Enabled.Contains(Capability.Lsp);

    [JsonPropertyName("fileExport")]
    public bool FileExport => Enabled.Contains(Capability.FileExport);

    [JsonPropertyName("cache")]
    public bool Cache => Enabled.Contains(Capability.Cache);

    public bool IsEnabled(Capability capability) => Enabled.Contains(capability);

    public static CapabilityFlags With(params Capability[] flags) => new() { Enabled = [.. flags] };
}
