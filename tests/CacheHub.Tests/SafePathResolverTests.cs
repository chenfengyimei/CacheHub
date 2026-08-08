using CacheHub.Core.Paths;
using Xunit;

namespace CacheHub.Tests;

/// <summary>
/// Tests for SafePathResolver: path traversal prevention, symlink escape, and workspace scoping.
/// </summary>
public class SafePathResolverTests
{
    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cachehub_spr_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        // Create a test file
        File.WriteAllText(Path.Combine(root, "test.cs"), "// test");
        // Create a subdirectory with a file
        Directory.CreateDirectory(Path.Combine(root, "src"));
        File.WriteAllText(Path.Combine(root, "src", "app.cs"), "// app");
        return root;
    }

    [Fact]
    public void Resolve_ValidRelativePath_ReturnsFullPath()
    {
        var root = CreateTempRoot();
        try
        {
            var resolver = new SafePathResolver(root);
            var result = resolver.Resolve("test.cs");
            Assert.NotNull(result);
            Assert.Equal(Path.Combine(root, "test.cs"), result);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Resolve_NestedRelativePath_ReturnsFullPath()
    {
        var root = CreateTempRoot();
        try
        {
            var resolver = new SafePathResolver(root);
            var result = resolver.Resolve("src/app.cs");
            Assert.NotNull(result);
            Assert.Equal(Path.Combine(root, "src", "app.cs"), result);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Resolve_PathTraversal_ReturnsNull()
    {
        var root = CreateTempRoot();
        try
        {
            var resolver = new SafePathResolver(root);
            Assert.Null(resolver.Resolve("../etc/passwd"));
            Assert.Null(resolver.Resolve("../../secret"));
            Assert.Null(resolver.Resolve("src/../../secret"));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Resolve_UrlEncodedTraversal_ReturnsNull()
    {
        var root = CreateTempRoot();
        try
        {
            var resolver = new SafePathResolver(root);
            Assert.Null(resolver.Resolve("%2e%2e/secret"));
            Assert.Null(resolver.Resolve("%2E%2E%2Fsecret"));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Resolve_AbsolutePath_ReturnsNull()
    {
        var root = CreateTempRoot();
        try
        {
            var resolver = new SafePathResolver(root);
            // Absolute paths should be rejected — only workspace-relative paths are allowed
            var absPath = Path.Combine(Path.GetTempPath(), "some_other_file.txt");
            Assert.Null(resolver.Resolve(absPath));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Resolve_EmptyOrNull_ReturnsNull()
    {
        var root = CreateTempRoot();
        try
        {
            var resolver = new SafePathResolver(root);
            Assert.Null(resolver.Resolve(null));
            Assert.Null(resolver.Resolve(""));
            Assert.Null(resolver.Resolve("   "));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void ResolveFile_NonExistentFile_ReturnsNull()
    {
        var root = CreateTempRoot();
        try
        {
            var resolver = new SafePathResolver(root);
            Assert.Null(resolver.ResolveFile("nonexistent.cs"));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void ResolveFile_ExistingFile_ReturnsFullPath()
    {
        var root = CreateTempRoot();
        try
        {
            var resolver = new SafePathResolver(root);
            var result = resolver.ResolveFile("test.cs");
            Assert.NotNull(result);
            Assert.True(File.Exists(result));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Resolve_ForwardSlashPath_Works()
    {
        var root = CreateTempRoot();
        try
        {
            var resolver = new SafePathResolver(root);
            var result = resolver.Resolve("src/app.cs");
            Assert.NotNull(result);
            Assert.True(File.Exists(result));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Resolve_BackslashPath_Works()
    {
        var root = CreateTempRoot();
        try
        {
            var resolver = new SafePathResolver(root);
            var result = resolver.Resolve("src\\app.cs");
            Assert.NotNull(result);
            Assert.True(File.Exists(result));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    // V8-P0-03: Prefix boundary tests — the old Desktop SafeResolvePath used naive StartsWith
    // which allowed "/tmp/project-secret" to match root "/tmp/project".

    [Fact]
    public void Resolve_PrefixBoundary_DoesNotMatchSiblingDirectory()
    {
        // Create sibling directories: /tmp/cachehub_root and /tmp/cachehub_root-secret
        var baseDir = Path.Combine(Path.GetTempPath(), $"cachehub_prefix_{Guid.NewGuid():N}");
        var root = Path.Combine(baseDir, "project");
        var sibling = Path.Combine(baseDir, "project-secret");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(sibling);
        File.WriteAllText(Path.Combine(root, "safe.txt"), "safe");
        File.WriteAllText(Path.Combine(sibling, "secret.txt"), "secret");
        try
        {
            var resolver = new SafePathResolver(root);
            // "project-secret" should NOT be accessible from root "project"
            // This would have passed with the old StartsWith check
            var result = resolver.Resolve("../project-secret/secret.txt");
            Assert.Null(result); // Traversal is caught
        }
        finally { try { Directory.Delete(baseDir, true); } catch { } }
    }

    [Fact]
    public void Resolve_PrefixBoundary_RootEndsWithSeparator()
    {
        // Verify that IsWithinRoot uses separator-aware comparison
        // "project" root should not match "project-other" path
        var baseDir = Path.Combine(Path.GetTempPath(), $"cachehub_sep_{Guid.NewGuid():N}");
        var root = Path.Combine(baseDir, "myproject");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "file.txt"), "content");
        Directory.CreateDirectory(Path.Combine(baseDir, "myproject-other"));
        File.WriteAllText(Path.Combine(baseDir, "myproject-other", "steal.txt"), "stolen");
        try
        {
            var resolver = new SafePathResolver(root);
            // Direct absolute path to sibling should be rejected
            Assert.Null(resolver.Resolve(Path.Combine(baseDir, "myproject-other", "steal.txt")));
        }
        finally { try { Directory.Delete(baseDir, true); } catch { } }
    }

    // V8-P0-03: Parent directory symlink traversal tests

    [Fact]
    public void Resolve_ParentDirectorySymlink_ReturnsNull()
    {
        // Skip on platforms where symlink creation requires elevated privileges
        var root = CreateTempRoot();
        var outsideDir = Path.Combine(Path.GetTempPath(), $"cachehub_outside_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outsideDir);
        File.WriteAllText(Path.Combine(outsideDir, "secret.pem"), "private key");
        var linkDir = Path.Combine(root, "link-dir");
        try
        {
            // Try to create a directory symlink pointing outside root
            Directory.CreateSymbolicLink(linkDir, outsideDir);
        }
        catch
        {
            // Symlink creation not supported or no permission — skip test
            try { Directory.Delete(root, true); } catch { }
            try { Directory.Delete(outsideDir, true); } catch { }
            return;
        }

        try
        {
            var resolver = new SafePathResolver(root);
            // Accessing a file through the symlink directory should be rejected
            var result = resolver.Resolve("link-dir/secret.pem");
            Assert.Null(result);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
            try { Directory.Delete(outsideDir, true); } catch { }
        }
    }

    [Fact]
    public void Resolve_DeepParentDirectorySymlink_ReturnsNull()
    {
        // Test symlink in a deeper directory structure
        var root = CreateTempRoot();
        var outsideDir = Path.Combine(Path.GetTempPath(), $"cachehub_deep_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outsideDir);
        File.WriteAllText(Path.Combine(outsideDir, "stolen.cs"), "// stolen");
        var deepDir = Path.Combine(root, "a", "b");
        Directory.CreateDirectory(deepDir);
        var linkPath = Path.Combine(deepDir, "escape");
        try
        {
            Directory.CreateSymbolicLink(linkPath, outsideDir);
        }
        catch
        {
            try { Directory.Delete(root, true); } catch { }
            try { Directory.Delete(outsideDir, true); } catch { }
            return;
        }

        try
        {
            var resolver = new SafePathResolver(root);
            // Deep path through symlink should be rejected
            var result = resolver.Resolve("a/b/escape/stolen.cs");
            Assert.Null(result);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
            try { Directory.Delete(outsideDir, true); } catch { }
        }
    }

    [Fact]
    public void Resolve_NormalSubdirectory_WorksAfterChainCheck()
    {
        // Ensure normal (non-symlink) subdirectories still work after adding chain check
        var root = CreateTempRoot();
        Directory.CreateDirectory(Path.Combine(root, "src", "models"));
        File.WriteAllText(Path.Combine(root, "src", "models", "user.cs"), "// user model");
        try
        {
            var resolver = new SafePathResolver(root);
            var result = resolver.Resolve("src/models/user.cs");
            Assert.NotNull(result);
            Assert.True(File.Exists(result));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
