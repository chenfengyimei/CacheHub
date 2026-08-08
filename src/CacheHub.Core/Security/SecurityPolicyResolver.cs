using System.Security.Cryptography;
using System.Text;
using CacheHub.Core.Configuration;

namespace CacheHub.Core.Security;

/// <summary>
/// Single source of truth for security policy resolution.
/// All entry points (CLI Context, Workflow, Desktop, Payload, Gateway) must use this resolver
/// to obtain the security policy, ensuring user configuration (e.g. Offline mode) is respected everywhere.
/// V7-W06: Version is now a real fingerprint of the policy content, not a static constant.
/// </summary>
public sealed class SecurityPolicyResolver
{
    /// <summary>
    /// V7-W06: Computes a fingerprint from the policy content.
    /// Changes when mode, secretScan, blockedExtensions, or rulesVersion change.
    /// </summary>
    public static string ComputeFingerprint(SecurityPolicy policy)
    {
        var sb = new StringBuilder();
        sb.Append(policy.Mode);
        sb.Append('|');
        sb.Append(policy.EnableSecretScan);
        sb.Append('|');
        if (policy.BlockedExtensions is not null)
        {
            foreach (var ext in policy.BlockedExtensions.OrderBy(e => e, StringComparer.Ordinal))
                sb.Append(ext).Append(',');
        }
        sb.Append('|');
        sb.Append(SecretScanner.Version);
        return Hash(sb.ToString());
    }

    private static string Hash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return "sec-" + Convert.ToHexString(hash).ToLowerInvariant()[..12];
    }

    /// <summary>
    /// Resolves the security policy from user configuration.
    /// If no config file exists, returns a permissive default (Standard mode).
    /// </summary>
    public static SecurityPolicy Resolve(ConfigManager? configManager = null)
    {
        configManager ??= new ConfigManager();
        var config = configManager.Load();

        if (config.Security is null)
        {
            var defaultPolicy = new SecurityPolicy { Version = "pending" };
            return defaultPolicy with { Version = ComputeFingerprint(defaultPolicy) };
        }

        var policy = new SecurityPolicy
        {
            Version = "pending",
            Mode = config.Security.Mode,
            EnableSecretScan = config.Security.EnableSecretScan,
            BlockedExtensions = config.Security.BlockedExtensions is not null
                ? new HashSet<string>(config.Security.BlockedExtensions, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        };
        return policy with { Version = ComputeFingerprint(policy) };
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
