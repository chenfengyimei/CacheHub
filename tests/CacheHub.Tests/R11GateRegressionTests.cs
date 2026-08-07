using CacheHub.Core.Errors;
using CacheHub.Core.Security;

namespace CacheHub.Tests;

/// <summary>
/// R11 Gate regression tests: security policy enforcement at all output points.
/// </summary>
public class R11GateRegressionTests
{
    // R11 Gate: Security enforcer evaluates before output
    [Fact]
    public void Gate_SecurityEnforcer_EvaluatesBeforeOutput()
    {
        var enforcer = new SecurityPolicyEnforcer();
        var decision = enforcer.EvaluateFile("src/main.ts", "export class App {}");

        Assert.True(decision.IsAllowed);
    }

    // R11 Gate: Offline mode blocks all cloud send
    [Fact]
    public void Gate_OfflineMode_BlocksCloudSend()
    {
        var enforcer = new SecurityPolicyEnforcer(new SecurityPolicy
        {
            Version = "sec-v1",
            Mode = ExfiltrationMode.Offline,
        });

        Assert.False(enforcer.IsCloudSendAllowed());

        var decision = enforcer.EvaluateFile("src/main.ts", "code");
        Assert.False(decision.IsAllowed);
    }

    // R11 Gate: Sensitive files (.pem) blocked
    [Fact]
    public void Gate_SensitiveFiles_BlockedByPolicy()
    {
        var enforcer = new SecurityPolicyEnforcer();
        // .pem is a sensitive extension
        var decision = enforcer.EvaluateFile("cert.pem", "certificate content");
        Assert.False(decision.IsAllowed);
    }

    // R11 Gate: Path traversal prevention
    [Fact]
    public void Gate_PathTraversal_PreventedInResolver()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"cachehub_r11_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tempRoot, "src"));
        File.WriteAllText(Path.Combine(tempRoot, "src", "main.ts"), "code");

        try
        {
            var resolver = new CacheHub.Core.Paths.SafePathResolver(tempRoot);

            Assert.Null(resolver.Resolve("../../../etc/passwd"));
            Assert.Null(resolver.Resolve("..\\..\\windows\\system32"));
            Assert.Null(resolver.Resolve("/etc/passwd"));
            Assert.NotNull(resolver.Resolve("src/main.ts"));
        }
        finally { try { Directory.Delete(tempRoot, true); } catch { } }
    }

    // R11 Gate: Error envelope is stable and structured
    [Fact]
    public void Gate_ErrorEnvelope_StableFormat()
    {
        var envelope = ErrorEnvelope.From(ErrorCode.WorkspaceNotFound, "Workspace not found");

        Assert.False(envelope.Success);
        Assert.True(envelope.ErrorCode > 0);
        Assert.NotEmpty(envelope.Message);
        Assert.NotEmpty(envelope.SuggestedActions);
    }

    // R11 Gate: Secret scanner detects known patterns
    [Fact]
    public void Gate_SecretScanner_DetectsSecrets()
    {
        var scanner = new SecretScanner();
        // Use the api_key pattern that SecretScanner recognizes
        var scan = scanner.Scan("config.ts", "api_key = 'sk-1234567890abcdefGHIJKLmnopqrstuvwxyz'");

        Assert.False(scan.Passed);
        Assert.NotEmpty(scan.Findings);
    }

    // R11 Gate: Secret scanner passes for safe code
    [Fact]
    public void Gate_SecretScanner_PassesSafeCode()
    {
        var scanner = new SecretScanner();
        var scan = scanner.Scan("main.ts", "export class App { constructor() {} }");

        Assert.True(scan.Passed);
    }

    // R11 Gate: Blocked extensions rejected
    [Fact]
    public void Gate_BlockedExtensions_Rejected()
    {
        var enforcer = new SecurityPolicyEnforcer(new SecurityPolicy
        {
            Version = "sec-v1",
            BlockedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pem", ".key" },
        });

        var decision = enforcer.EvaluateFile("cert.pem", "certificate content");
        Assert.False(decision.IsAllowed);
    }

    // R11 Gate: PreviewRequired mode requires approval
    [Fact]
    public void Gate_PreviewRequired_RequiresApproval()
    {
        var enforcer = new SecurityPolicyEnforcer(new SecurityPolicy
        {
            Version = "sec-v1",
            Mode = ExfiltrationMode.PreviewRequired,
        });

        var decision = enforcer.EvaluateFile("src/main.ts", "safe code");
        Assert.False(decision.IsAllowed);
        Assert.True(decision.IsApprovalRequired);
    }
}
