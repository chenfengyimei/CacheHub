using CacheHub.Core.Paths;
using Xunit;

namespace CacheHub.Tests;

/// <summary>
/// Security tests for symlink escape prevention and XSS content safety.
/// Fills gaps identified in the V2.0 acceptance gate verification.
/// </summary>
public class SecurityGapTests
{
    [Fact]
    public void SafePathResolver_SymlinkEscape_ReturnsNull()
    {
        // Create a workspace root and an external file
        var root = Path.Combine(Path.GetTempPath(), $"cachehub_symlink_root_{Guid.NewGuid():N}");
        var external = Path.Combine(Path.GetTempPath(), $"cachehub_symlink_ext_{Guid.NewGuid():N}.txt");
        Directory.CreateDirectory(root);
        File.WriteAllText(external, "SECRET_DATA");

        try
        {
            // Create a file symlink inside root pointing to external file
            var symlinkPath = Path.Combine(root, "escape_link.txt");
            try
            {
                File.CreateSymbolicLink(symlinkPath, external);
            }
            catch (PlatformNotSupportedException)
            {
                return; // Skip on platforms without symlink support
            }
            catch (UnauthorizedAccessException)
            {
                return; // Skip if no privilege
            }

            var resolver = new SafePathResolver(root);

            // Attempting to resolve the symlink should be rejected
            // because the symlink target is outside the workspace root
            var result = resolver.Resolve("escape_link.txt");
            Assert.Null(result);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
            try { File.Delete(external); } catch { }
        }
    }

    [Fact]
    public void SafePathResolver_SymlinkInsideRoot_ReturnsPath()
    {
        // A symlink pointing to a location WITHIN root should be allowed
        var root = Path.Combine(Path.GetTempPath(), $"cachehub_symlink_ok_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "real_dir"));
        File.WriteAllText(Path.Combine(root, "real_dir", "file.txt"), "content");

        try
        {
            // Create a symlink inside root pointing to another location inside root
            var symlinkPath = Path.Combine(root, "ok_link");
            try
            {
                Directory.CreateSymbolicLink(symlinkPath, Path.Combine(root, "real_dir"));
            }
            catch (PlatformNotSupportedException)
            {
                return; // Skip on platforms without symlink support
            }
            catch (UnauthorizedAccessException)
            {
                return; // Skip if no privilege
            }

            var resolver = new SafePathResolver(root);
            var result = resolver.Resolve("ok_link/file.txt");

            // Symlink within root should be allowed (target is within root)
            // Note: if the platform resolves the symlink and the target is within root, it should pass
            // If not supported, the test is skipped above
            Assert.NotNull(result);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void SafePathResolver_DeepTraversalChain_ReturnsNull()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cachehub_trav_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var resolver = new SafePathResolver(root);

            // Various traversal patterns that should be rejected
            Assert.Null(resolver.Resolve("../etc/passwd"));
            Assert.Null(resolver.Resolve("../../secret"));
            Assert.Null(resolver.Resolve("src/../../etc"));
            Assert.Null(resolver.Resolve("src/%2e%2e/%2e%2e/etc"));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void SecretScanner_MaliciousFilename_IsScanned()
    {
        // A file with a script-injectable name should still be scanned for secrets
        var scanner = new Core.Security.SecretScanner();
        var maliciousName = "<script>alert('xss')</script>.env";
        var result = scanner.Scan(maliciousName, "API_KEY=sk-test1234567890abcdef");

        Assert.False(result.Passed);
        Assert.NotEmpty(result.Findings);
    }

    [Fact]
    public void SecretScanner_XssInContent_IsScanned()
    {
        // Content with a real API key pattern should be detected even inside HTML
        var scanner = new Core.Security.SecretScanner();
        var content = "<script>var config = { api_key: 'sk-1234567890abcdefghijklmnop' };</script>";
        var result = scanner.Scan("page.html", content);

        Assert.False(result.Passed);
    }

    [Fact]
    public void SecurityPolicyEnforcer_OfflineMode_BlocksAllCloudSend()
    {
        var policy = new Core.Security.SecurityPolicy
        {
            Version = "v1",
            Mode = Core.Security.ExfiltrationMode.Offline,
        };
        var enforcer = new Core.Security.SecurityPolicyEnforcer(policy);

        // In Offline mode, no content should be allowed to be sent to cloud
        Assert.False(enforcer.IsCloudSendAllowed());

        var (allowed, scan, reason) = enforcer.CheckBeforeSend("src/app.ts", "export const x = 1;");
        Assert.False(allowed);
        Assert.Contains("Offline", reason);
    }

    [Fact]
    public void SecurityPolicyEnforcer_RestrictedMode_BlocksSensitiveFiles()
    {
        var policy = new Core.Security.SecurityPolicy
        {
            Version = "v1",
            Mode = Core.Security.ExfiltrationMode.Restricted,
            BlockedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".env", ".pem", ".key" },
        };
        var enforcer = new Core.Security.SecurityPolicyEnforcer(policy);

        // Blocked extensions should be blocked in Restricted mode
        Assert.False(enforcer.IsPathAllowed("config.env"));
        Assert.False(enforcer.IsPathAllowed("id_rsa.key"));
        Assert.False(enforcer.IsPathAllowed("cert.pem"));
        // Normal code files should be allowed
        Assert.True(enforcer.IsPathAllowed("src/app.ts"));
    }

    [Fact]
    public void PathNormalizer_ContainsTraversal_DetectsAllVariants()
    {
        // Verify traversal detection covers common attack patterns
        Assert.True(PathNormalizer.ContainsTraversal("../etc"));
        Assert.True(PathNormalizer.ContainsTraversal("..\\windows"));
        Assert.True(PathNormalizer.ContainsTraversal("src/../../etc"));
        Assert.True(PathNormalizer.ContainsTraversal("%2e%2e/etc"));
        Assert.True(PathNormalizer.ContainsTraversal("%2E%2E%2Fetc"));

        // Legitimate paths should not trigger
        Assert.False(PathNormalizer.ContainsTraversal("src/app.ts"));
        Assert.False(PathNormalizer.ContainsTraversal("tests/unit/test.cs"));
        Assert.False(PathNormalizer.ContainsTraversal(""));
    }
}
