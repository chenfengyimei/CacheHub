using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CacheHub.Core.Security;

/// <summary>
/// Exfiltration mode for a workspace.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExfiltrationMode
{
    Standard,
    Restricted,
    PreviewRequired,
    Offline,
}

/// <summary>
/// Security policy for a workspace.
/// </summary>
public sealed record SecurityPolicy
{
    public required string Version { get; init; }
    public ExfiltrationMode Mode { get; init; } = ExfiltrationMode.Standard;
    public IReadOnlySet<string> BlockedPaths { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<string> BlockedExtensions { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<string> BlockedPatterns { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public bool EnableSecretScan { get; init; } = true;
    public bool EnablePathTraversalCheck { get; init; } = true;
}

/// <summary>
/// Result of a security check on file content.
/// </summary>
public sealed record SecurityScanResult
{
    public required bool Passed { get; init; }
    public required IReadOnlyList<SecurityFinding> Findings { get; init; }
    public required string ScannerVersion { get; init; }
}

/// <summary>
/// A security finding (secret, sensitive data, etc.).
/// </summary>
public sealed record SecurityFinding
{
    public required SecurityFindingType Type { get; init; }
    public required string FilePath { get; init; }
    public required int Line { get; init; }
    public required string Description { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SecurityFindingType
{
    ApiKey,
    Password,
    PrivateKey,
    Certificate,
    Token,
    ConnectionString,
    PathTraversal,
    BlockedPath,
    BlockedExtension,
}

/// <summary>
/// Default sensitive file patterns.
/// </summary>
public static class DefaultSensitivePatterns
{
    public static readonly IReadOnlySet<string> SensitiveFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".env", ".env.local", ".env.production", ".env.staging",
        "id_rsa", "id_ed25519", "id_ecdsa",
        "credentials.json", "service-account.json",
        "secrets.yaml", "secrets.yml", "secrets.json",
        "keystore", "keystore.jks",
    };

    public static readonly IReadOnlySet<string> SensitiveExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".pem", ".key", ".p12", ".pfx", ".crt", ".cer", ".der",
        ".mobileprovision",
    };
}

/// <summary>
/// Scans file content for secrets and sensitive data.
/// </summary>
public sealed partial class SecretScanner
{
    public const string Version = "secret-scanner-v1";

    public SecurityScanResult Scan(string filePath, string content)
    {
        var findings = new List<SecurityFinding>();
        var lines = content.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');

            // API keys
            if (ApiKeyRegex().IsMatch(line))
                findings.Add(new SecurityFinding { Type = SecurityFindingType.ApiKey, FilePath = filePath, Line = i + 1, Description = "Potential API key detected" });

            // Passwords
            if (PasswordRegex().IsMatch(line))
                findings.Add(new SecurityFinding { Type = SecurityFindingType.Password, FilePath = filePath, Line = i + 1, Description = "Potential password in assignment" });

            // Private keys
            if (PrivateKeyRegex().IsMatch(line))
                findings.Add(new SecurityFinding { Type = SecurityFindingType.PrivateKey, FilePath = filePath, Line = i + 1, Description = "Private key block detected" });

            // Connection strings with credentials
            if (ConnectionStringRegex().IsMatch(line))
                findings.Add(new SecurityFinding { Type = SecurityFindingType.ConnectionString, FilePath = filePath, Line = i + 1, Description = "Connection string with credentials" });

            // Bearer tokens
            if (BearerTokenRegex().IsMatch(line))
                findings.Add(new SecurityFinding { Type = SecurityFindingType.Token, FilePath = filePath, Line = i + 1, Description = "Bearer token detected" });
        }

        return new SecurityScanResult
        {
            Passed = findings.Count == 0,
            Findings = findings,
            ScannerVersion = Version,
        };
    }

    /// <summary>
    /// Checks if a file path matches sensitive patterns.
    /// </summary>
    public static bool IsSensitiveFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var ext = Path.GetExtension(filePath);

        return DefaultSensitivePatterns.SensitiveFiles.Contains(fileName) ||
               DefaultSensitivePatterns.SensitiveExtensions.Contains(ext);
    }

    [GeneratedRegex(@"(?i)(api[_-]?key|apikey)\s*[:=]\s*['""]?[a-zA-Z0-9\-_.]{20,}")]
    private static partial Regex ApiKeyRegex();

    [GeneratedRegex(@"(?i)password\s*[:=]\s*['""]?[^\s'""$]{6,}")]
    private static partial Regex PasswordRegex();

    [GeneratedRegex(@"-----BEGIN\s+(RSA\s+|EC\s+|OPENSSH\s+)?PRIVATE\s+KEY-----")]
    private static partial Regex PrivateKeyRegex();

    [GeneratedRegex(@"(?i)(server|data\s+source)\s*=\s*[^;]*;\s*password\s*=\s*[^\s;]+")]
    private static partial Regex ConnectionStringRegex();

    [GeneratedRegex(@"(?i)bearer\s+[a-zA-Z0-9\-._~+\/]+=*")]
    private static partial Regex BearerTokenRegex();
}

/// <summary>
/// Enforces security policy on file paths and content before cloud send.
/// </summary>
public sealed class SecurityPolicyEnforcer
{
    private readonly SecurityPolicy _policy;
    private readonly SecretScanner _scanner = new();

    public SecurityPolicyEnforcer(SecurityPolicy? policy = null)
    {
        _policy = policy ?? new SecurityPolicy { Version = "sec-v1" };
    }

    /// <summary>
    /// The policy decision for a file: Allow, Deny, or ApprovalRequired.
    /// </summary>
    public PolicyDecision EvaluateFile(string filePath, string content)
    {
        if (!IsCloudSendAllowed())
            return PolicyDecision.Deny("Workspace is in Offline mode");

        if (!IsPathAllowed(filePath))
            return PolicyDecision.Deny($"File path blocked by policy: {filePath}");

        var scan = ScanContent(filePath, content);
        if (!scan.Passed)
            return PolicyDecision.Deny($"Secret scan failed: {scan.Findings.Count} finding(s) in {filePath}", scan);

        if (_policy.Mode == ExfiltrationMode.PreviewRequired)
            return PolicyDecision.ApprovalRequired(scan);

        return PolicyDecision.Allow(scan);
    }

    /// <summary>
    /// Checks if a file path is allowed for cloud send.
    /// </summary>
    public bool IsPathAllowed(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var ext = Path.GetExtension(filePath);

        if (_policy.BlockedExtensions.Contains(ext)) return false;
        if (_policy.BlockedPaths.Any(p => filePath.Contains(p, StringComparison.OrdinalIgnoreCase))) return false;
        if (SecretScanner.IsSensitiveFile(filePath)) return false;

        return true;
    }

    /// <summary>
    /// Checks if cloud send is allowed for the workspace.
    /// </summary>
    public bool IsCloudSendAllowed() => _policy.Mode != ExfiltrationMode.Offline;

    /// <summary>
    /// Scans content for secrets before cloud send.
    /// </summary>
    public SecurityScanResult ScanContent(string filePath, string content)
    {
        if (!_policy.EnableSecretScan)
            return new SecurityScanResult { Passed = true, Findings = [], ScannerVersion = SecretScanner.Version };

        return _scanner.Scan(filePath, content);
    }

    /// <summary>
    /// Full pre-send check: path + content scan + mode.
    /// </summary>
    public (bool allowed, SecurityScanResult? scanResult, string? reason) CheckBeforeSend(string filePath, string content)
    {
        var decision = EvaluateFile(filePath, content);
        return decision switch
        {
            { IsAllowed: true, IsApprovalRequired: false } => (true, decision.ScanResult, null),
            { IsApprovalRequired: true } => (true, decision.ScanResult, "Preview required before send"),
            _ => (false, decision.ScanResult, decision.Reason),
        };
    }
}

/// <summary>
/// Security policy decision for a file: Allow, Deny, or ApprovalRequired.
/// All Payload output paths must call EvaluateFile and respect the decision.
/// </summary>
public sealed record PolicyDecision
{
    public bool IsAllowed { get; init; }
    public bool IsApprovalRequired { get; init; }
    public string? Reason { get; init; }
    public SecurityScanResult? ScanResult { get; init; }

    public static PolicyDecision Allow(SecurityScanResult? scanResult = null) => new() { IsAllowed = true, ScanResult = scanResult };
    public static PolicyDecision Deny(string reason, SecurityScanResult? scanResult = null) => new() { IsAllowed = false, Reason = reason, ScanResult = scanResult };
    public static PolicyDecision ApprovalRequired(SecurityScanResult? scanResult = null) =>
        new() { IsAllowed = false, IsApprovalRequired = true, Reason = "Preview required before send", ScanResult = scanResult };
}
