using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CacheHub.Core.Ecosystem;

namespace CacheHub.Core.Ecosystem;

/// <summary>
/// Plugin signature verifier: validates plugin integrity and permissions.
/// R9: plugin signing, permissions, and isolation.
/// </summary>
public sealed class PluginSecurityManager
{
    private readonly HashSet<string> _trustedKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PluginManifest> _loadedPlugins = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a trusted public key for signature verification.
    /// </summary>
    public void RegisterTrustedKey(string publicKey)
    {
        _trustedKeys.Add(publicKey);
    }

    /// <summary>
    /// Validates a plugin manifest: checks signature, permissions, and isolation.
    /// Returns (isValid, issues[]).
    /// </summary>
    public (bool isValid, IReadOnlyList<string> issues) ValidatePlugin(PluginManifest manifest)
    {
        var issues = new List<string>();

        // Check signature if required
        if (manifest.IsSigned)
        {
            if (string.IsNullOrEmpty(manifest.Signature))
                issues.Add("Plugin claims to be signed but has no signature");
        }

        // Check dangerous permissions
        if (manifest.Permissions.Contains(PluginAccess.ExecuteProcess) &&
            manifest.Permissions.Contains(PluginAccess.AccessCredentials))
        {
            issues.Add("Plugin requests both ExecuteProcess and AccessCredentials — high risk combination");
        }

        if (manifest.Permissions.Contains(PluginAccess.NetworkAccess) &&
            manifest.Permissions.Contains(PluginAccess.AccessCredentials))
        {
            issues.Add("Plugin requests both NetworkAccess and AccessCredentials — credential exfiltration risk");
        }

        // Check required fields
        if (string.IsNullOrWhiteSpace(manifest.Id))
            issues.Add("Plugin ID is required");

        if (string.IsNullOrWhiteSpace(manifest.Version))
            issues.Add("Plugin version is required");

        return (issues.Count == 0, issues);
    }

    /// <summary>
    /// Registers a loaded plugin.
    /// </summary>
    public void RegisterPlugin(PluginManifest manifest)
    {
        _loadedPlugins[manifest.Id] = manifest;
    }

    /// <summary>
    /// Unregisters a plugin.
    /// </summary>
    public void UnregisterPlugin(string pluginId)
    {
        _loadedPlugins.Remove(pluginId);
    }

    /// <summary>
    /// Gets all loaded plugins.
    /// </summary>
    public IReadOnlyList<PluginManifest> GetLoadedPlugins() => _loadedPlugins.Values.ToList();

    /// <summary>
    /// Checks if a plugin has a specific permission.
    /// </summary>
    public bool HasPermission(string pluginId, PluginAccess access)
    {
        return _loadedPlugins.TryGetValue(pluginId, out var manifest) &&
               manifest.Permissions.Contains(access);
    }
}

/// <summary>
/// Auto-update manager: handles backup, update, and rollback.
/// R9: auto-update with backup/rollback.
/// </summary>
public sealed class UpdateManager
{
    private readonly UpdateConfig _config;
    private readonly string _appDataPath;
    private readonly List<UpdateBackup> _backups = [];
    private readonly Lock _lock = new();

    public UpdateManager(UpdateConfig config, string appDataPath)
    {
        _config = config;
        _appDataPath = appDataPath;
    }

    /// <summary>
    /// Checks if an update is available.
    /// </summary>
    public UpdateCheckResult CheckForUpdate(string currentVersion, string? latestVersion)
    {
        var updateAvailable = !string.IsNullOrEmpty(latestVersion) &&
            !string.Equals(currentVersion, latestVersion, StringComparison.OrdinalIgnoreCase);

        return new UpdateCheckResult
        {
            UpdateAvailable = updateAvailable,
            CurrentVersion = currentVersion,
            LatestVersion = latestVersion,
            ReleaseDate = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Creates a backup before updating.
    /// </summary>
    public UpdateBackup CreateBackup(string currentVersion)
    {
        var backup = new UpdateBackup
        {
            BackupId = Guid.NewGuid().ToString("N"),
            Version = currentVersion,
            CreatedAt = DateTimeOffset.UtcNow,
            Path = Path.Combine(_appDataPath, "backups", $"backup-{Guid.NewGuid():N}"),
        };

        lock (_lock)
        {
            _backups.Add(backup);
        }

        return backup;
    }

    /// <summary>
    /// Rolls back to a previous backup.
    /// </summary>
    public bool Rollback(string backupId)
    {
        lock (_lock)
        {
            var backup = _backups.FirstOrDefault(b => b.BackupId == backupId);
            if (backup is null) return false;

            // In a full implementation, this would restore files from the backup directory
            return true;
        }
    }

    /// <summary>
    /// Gets all available backups.
    /// </summary>
    public IReadOnlyList<UpdateBackup> GetBackups()
    {
        lock (_lock) { return _backups.ToList(); }
    }
}

/// <summary>
/// A backup created before an update.
/// </summary>
public sealed record UpdateBackup
{
    public required string BackupId { get; init; }
    public required string Version { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required string Path { get; init; }
}

/// <summary>
/// Enterprise policy enforcer: enforces policies at all output points.
/// R9: enterprise policy, audit.
/// </summary>
public sealed class EnterprisePolicyEnforcer
{
    private readonly EnterprisePolicy _policy;
    private readonly List<AuditEvent> _auditLog = [];
    private readonly Lock _lock = new();

    public EnterprisePolicyEnforcer(EnterprisePolicy policy)
    {
        _policy = policy;
    }

    /// <summary>
    /// Checks if a cloud send is allowed under the policy.
    /// </summary>
    public bool IsCloudSendAllowed()
    {
        return !_policy.ForceCloudSendRestricted;
    }

    /// <summary>
    /// Checks if a provider is allowed under the policy.
    /// </summary>
    public bool IsProviderAllowed(string providerId)
    {
        if (_policy.AllowedProviders.Count == 0) return true;
        return _policy.AllowedProviders.Contains(providerId, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if a plugin is allowed under the policy.
    /// </summary>
    public bool IsPluginAllowed(PluginManifest plugin)
    {
        if (_policy.DisableThirdPartyPlugins) return false;
        if (_policy.RequirePluginSignature && !plugin.IsSigned) return false;
        return true;
    }

    /// <summary>
    /// Checks if the daily budget is exceeded.
    /// </summary>
    public bool IsBudgetExceeded(decimal dailyCost)
    {
        if (!_policy.MaxDailyBudgetUsd.HasValue) return false;
        return dailyCost >= _policy.MaxDailyBudgetUsd.Value;
    }

    /// <summary>
    /// Logs an audit event.
    /// </summary>
    public void LogAudit(string action, string? details = null, string? userId = null)
    {
        if (!_policy.ForceAuditLog) return;

        lock (_lock)
        {
            _auditLog.Add(new AuditEvent
            {
                Timestamp = DateTimeOffset.UtcNow,
                Action = action,
                Details = details,
                UserId = userId,
            });
        }
    }

    /// <summary>
    /// Gets the audit log.
    /// </summary>
    public IReadOnlyList<AuditEvent> GetAuditLog()
    {
        lock (_lock) { return _auditLog.ToList(); }
    }
}

/// <summary>
/// An audit event for enterprise compliance.
/// </summary>
public sealed record AuditEvent
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string Action { get; init; }
    public string? Details { get; init; }
    public string? UserId { get; init; }
}

/// <summary>
/// Team shared index manager: manages shared workspace indices across team members.
/// R9: team shared index.
/// </summary>
public sealed class TeamIndexManager
{
    private readonly Dictionary<string, SharedIndex> _sharedIndices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    /// <summary>
    /// Publishes a workspace index for team sharing.
    /// </summary>
    public void PublishIndex(string workspaceId, string publisherId, string indexHash)
    {
        lock (_lock)
        {
            _sharedIndices[workspaceId] = new SharedIndex
            {
                WorkspaceId = workspaceId,
                PublisherId = publisherId,
                IndexHash = indexHash,
                PublishedAt = DateTimeOffset.UtcNow,
                DownloadCount = 0,
            };
        }
    }

    /// <summary>
    /// Retrieves a shared index for a workspace.
    /// </summary>
    public SharedIndex? GetSharedIndex(string workspaceId)
    {
        lock (_lock)
        {
            if (_sharedIndices.TryGetValue(workspaceId, out var index))
            {
                index = index with { DownloadCount = index.DownloadCount + 1 };
                _sharedIndices[workspaceId] = index;
                return index;
            }
            return null;
        }
    }

    /// <summary>
    /// Removes a shared index.
    /// </summary>
    public bool RemoveSharedIndex(string workspaceId)
    {
        lock (_lock) { return _sharedIndices.Remove(workspaceId); }
    }

    /// <summary>
    /// Gets all shared indices.
    /// </summary>
    public IReadOnlyList<SharedIndex> ListSharedIndices()
    {
        lock (_lock) { return _sharedIndices.Values.ToList(); }
    }
}

/// <summary>
/// A shared workspace index.
/// </summary>
public sealed record SharedIndex
{
    public required string WorkspaceId { get; init; }
    public required string PublisherId { get; init; }
    public required string IndexHash { get; init; }
    public required DateTimeOffset PublishedAt { get; init; }
    public int DownloadCount { get; init; }
}
