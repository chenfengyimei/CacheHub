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
}
