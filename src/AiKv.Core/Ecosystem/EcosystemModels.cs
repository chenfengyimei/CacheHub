using System.Text.Json.Serialization;

namespace AiKv.Core.Ecosystem;

/// <summary>
/// Plugin manifest with signature and permissions.
/// </summary>
public sealed record PluginManifest
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Author { get; init; }
    public string? Signature { get; init; }
    public bool IsSigned { get; init; }
    public bool IsEnabled { get; init; }
    public required IReadOnlyList<PluginAccess> Permissions { get; init; }
    public string? Description { get; init; }
    public string? Homepage { get; init; }
}

/// <summary>
/// Permissions a plugin can request.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PluginAccess
{
    ReadFiles,
    WriteFiles,
    ExecuteProcess,
    NetworkAccess,
    ReadWorkspace,
    ModifyWorkspace,
    AccessCredentials,
    ExportContext,
}

/// <summary>
/// Team/enterprise sharing configuration.
/// </summary>
public sealed record TeamConfig
{
    public required string TeamId { get; init; }
    public required string Name { get; init; }
    public IReadOnlyList<string> SharedWorkspaceIds { get; init; } = [];
    public IReadOnlyList<string> MemberIds { get; init; } = [];
    public bool EnableSharedIndex { get; init; }
    public bool EnableAuditLog { get; init; }
}

/// <summary>
/// Enterprise policy configuration.
/// </summary>
public sealed record EnterprisePolicy
{
    public required string PolicyId { get; init; }
    public bool ForceCloudSendRestricted { get; init; }
    public bool ForceAuditLog { get; init; }
    public IReadOnlyList<string> AllowedProviders { get; init; } = [];
    public decimal? MaxDailyBudgetUsd { get; init; }
    public bool DisableThirdPartyPlugins { get; init; }
    public bool RequirePluginSignature { get; init; }
    public bool AllowIntranetDeployment { get; init; }
}

/// <summary>
/// Update channel configuration.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UpdateChannel
{
    Stable,
    Beta,
    Alpha,
    Nightly,
}

/// <summary>
/// Auto-update configuration.
/// </summary>
public sealed record UpdateConfig
{
    public required UpdateChannel Channel { get; init; }
    public bool AutoCheck { get; init; } = true;
    public bool AutoDownload { get; init; }
    public bool AutoInstall { get; init; }
    public bool BackupBeforeUpdate { get; init; } = true;
    public TimeSpan CheckInterval { get; init; } = TimeSpan.FromHours(12);
}

/// <summary>
/// Result of an update check.
/// </summary>
public sealed record UpdateCheckResult
{
    public required bool UpdateAvailable { get; init; }
    public string? LatestVersion { get; init; }
    public string? CurrentVersion { get; init; }
    public DateTimeOffset? ReleaseDate { get; init; }
    public string? ReleaseNotes { get; init; }
    public string? DownloadUrl { get; init; }
    public string? Checksum { get; init; }
}
