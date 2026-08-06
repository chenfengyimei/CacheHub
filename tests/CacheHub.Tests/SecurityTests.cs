using CacheHub.Core.Security;

namespace CacheHub.Tests;

public class SecurityTests
{
    [Fact]
    public void SecretScanner_ShouldDetectApiKey()
    {
        var scanner = new SecretScanner();
        var result = scanner.Scan("config.env", "api_key=sk-1234567890abcdefghijklmnopqrstuvwxyz");

        Assert.False(result.Passed);
        Assert.Contains(result.Findings, f => f.Type == SecurityFindingType.ApiKey);
    }

    [Fact]
    public void SecretScanner_ShouldDetectPassword()
    {
        var scanner = new SecretScanner();
        var result = scanner.Scan("db.config", "password=mysecretpass123");

        Assert.False(result.Passed);
        Assert.Contains(result.Findings, f => f.Type == SecurityFindingType.Password);
    }

    [Fact]
    public void SecretScanner_ShouldDetectPrivateKey()
    {
        var scanner = new SecretScanner();
        var content = "-----BEGIN RSA PRIVATE KEY-----\nMIIEpAIBAAKCAQEA...\n-----END RSA PRIVATE KEY-----";

        var result = scanner.Scan("key.pem", content);

        Assert.False(result.Passed);
        Assert.Contains(result.Findings, f => f.Type == SecurityFindingType.PrivateKey);
    }

    [Fact]
    public void SecretScanner_ShouldDetectBearerToken()
    {
        var scanner = new SecretScanner();
        var result = scanner.Scan("request.txt", "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...");

        Assert.False(result.Passed);
        Assert.Contains(result.Findings, f => f.Type == SecurityFindingType.Token);
    }

    [Fact]
    public void SecretScanner_ShouldPassCleanContent()
    {
        var scanner = new SecretScanner();
        var result = scanner.Scan("app.ts", "export function hello() { return 'world'; }");

        Assert.True(result.Passed);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void SecretScanner_IsSensitiveFile_ShouldDetectEnvFiles()
    {
        Assert.True(SecretScanner.IsSensitiveFile(".env"));
        Assert.True(SecretScanner.IsSensitiveFile(".env.local"));
        Assert.True(SecretScanner.IsSensitiveFile("id_rsa"));
        Assert.True(SecretScanner.IsSensitiveFile("cert.pem"));
    }

    [Fact]
    public void SecretScanner_IsSensitiveFile_ShouldNotFlagNormalFiles()
    {
        Assert.False(SecretScanner.IsSensitiveFile("app.ts"));
        Assert.False(SecretScanner.IsSensitiveFile("README.md"));
        Assert.False(SecretScanner.IsSensitiveFile("config.json"));
    }

    [Fact]
    public void SecurityPolicyEnforcer_IsPathAllowed_ShouldBlockSensitiveFiles()
    {
        var enforcer = new SecurityPolicyEnforcer();

        Assert.False(enforcer.IsPathAllowed(".env"));
        Assert.False(enforcer.IsPathAllowed("certs/server.pem"));
        Assert.True(enforcer.IsPathAllowed("src/app.ts"));
    }

    [Fact]
    public void SecurityPolicyEnforcer_OfflineMode_ShouldBlockAll()
    {
        var policy = new SecurityPolicy { Version = "v1", Mode = ExfiltrationMode.Offline };
        var enforcer = new SecurityPolicyEnforcer(policy);

        Assert.False(enforcer.IsCloudSendAllowed());
    }

    [Fact]
    public void SecurityPolicyEnforcer_StandardMode_ShouldAllow()
    {
        var policy = new SecurityPolicy { Version = "v1", Mode = ExfiltrationMode.Standard };
        var enforcer = new SecurityPolicyEnforcer(policy);

        Assert.True(enforcer.IsCloudSendAllowed());
    }

    [Fact]
    public void SecurityPolicyEnforcer_CheckBeforeSend_ShouldBlockSecrets()
    {
        var enforcer = new SecurityPolicyEnforcer();
        var (allowed, scan, reason) = enforcer.CheckBeforeSend("config.txt", "api_key=sk-1234567890abcdefghijklmnopqrstuvwxyz");

        Assert.False(allowed);
        Assert.NotNull(scan);
        Assert.False(scan!.Passed);
    }

    [Fact]
    public void SecurityPolicyEnforcer_CheckBeforeSend_ShouldBlockSensitivePath()
    {
        var enforcer = new SecurityPolicyEnforcer();
        var (allowed, _, reason) = enforcer.CheckBeforeSend(".env", "DATABASE_URL=postgres://localhost");

        Assert.False(allowed);
        Assert.Contains("blocked", reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SecurityPolicyEnforcer_CheckBeforeSend_ShouldAllowCleanFile()
    {
        var enforcer = new SecurityPolicyEnforcer();
        var (allowed, scan, reason) = enforcer.CheckBeforeSend("src/app.ts", "export const x = 1;");

        Assert.True(allowed);
        Assert.NotNull(scan);
        Assert.True(scan!.Passed);
    }

    [Fact]
    public void SecurityPolicyEnforcer_PreviewRequired_ShouldAllowButFlag()
    {
        var policy = new SecurityPolicy { Version = "v1", Mode = ExfiltrationMode.PreviewRequired };
        var enforcer = new SecurityPolicyEnforcer(policy);
        var (allowed, _, reason) = enforcer.CheckBeforeSend("src/app.ts", "export const x = 1;");

        Assert.True(allowed);
        Assert.Contains("Preview required", reason);
    }

    [Fact]
    public void SecurityPolicyEnforcer_BlockedExtensions_ShouldBlock()
    {
        var policy = new SecurityPolicy
        {
            Version = "v1",
            BlockedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".log", ".tmp" },
        };
        var enforcer = new SecurityPolicyEnforcer(policy);

        Assert.False(enforcer.IsPathAllowed("app.log"));
        Assert.False(enforcer.IsPathAllowed("temp.tmp"));
        Assert.True(enforcer.IsPathAllowed("app.ts"));
    }

    [Fact]
    public void SecurityScanResult_ShouldIncludeScannerVersion()
    {
        var scanner = new SecretScanner();
        var result = scanner.Scan("test.txt", "clean content");

        Assert.Equal("secret-scanner-v1", result.ScannerVersion);
    }
}
