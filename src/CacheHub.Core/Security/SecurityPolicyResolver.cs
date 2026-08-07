using CacheHub.Core.Configuration;

namespace CacheHub.Core.Security;

/// <summary>
/// Single source of truth for security policy resolution.
/// All entry points (CLI Context, Workflow, Desktop, Payload, Gateway) must use this resolver
/// to obtain the security policy, ensuring user configuration (e.g. Offline mode) is respected everywhere.
/// </summary>
public sealed class SecurityPolicyResolver
{
    public const string Version = "config-v2";

    /// <summary>
    /// Resolves the security policy from user configuration.
    /// If no config file exists, returns a permissive default (Standard mode).
    /// </summary>
    public static SecurityPolicy Resolve(ConfigManager? configManager = null)
    {
        configManager ??= new ConfigManager();
        var config = configManager.Load();

        if (config.Security is null)
            return new SecurityPolicy { Version = Version };

        return new SecurityPolicy
        {
            Version = Version,
            Mode = config.Security.Mode,
            EnableSecretScan = config.Security.EnableSecretScan,
            BlockedExtensions = config.Security.BlockedExtensions is not null
                ? new HashSet<string>(config.Security.BlockedExtensions, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        };
    }

    /// <summary>
    /// Creates a SecurityPolicyEnforcer from the resolved policy.
    /// Convenience method for callers that need both the policy and the enforcer.
    /// </summary>
    public static (SecurityPolicy policy, SecurityPolicyEnforcer enforcer) CreateEnforcer(ConfigManager? configManager = null)
    {
        var policy = Resolve(configManager);
        return (policy, new SecurityPolicyEnforcer(policy));
    }

    /// <summary>
    /// Hard-block check before any network send (Gateway call, Provider forward).
    /// If the resolved policy is Offline, this returns false and the caller must NOT send.
    /// </summary>
    public static bool IsCloudSendAllowed(ConfigManager? configManager = null)
    {
        var policy = Resolve(configManager);
        return policy.Mode != ExfiltrationMode.Offline;
    }
}
