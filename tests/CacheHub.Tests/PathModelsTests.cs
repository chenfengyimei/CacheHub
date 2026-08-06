using CacheHub.Core.Paths;
using Xunit;

namespace CacheHub.Tests;

/// <summary>
/// Tests for PhysicalPath, VirtualPath, and PathComparer platform-aware semantics.
/// </summary>
public class PathModelsTests
{
    [Fact]
    public void VirtualPath_AlwaysCaseSensitive()
    {
        var a = new VirtualPath("src/App.ts");
        var b = new VirtualPath("src/app.ts");
        Assert.NotEqual(a, b);
        Assert.False(PathComparer.VirtualEquals(a.Value, b.Value));
    }

    [Fact]
    public void VirtualPath_NormalizesBackslashes()
    {
        var vp = new VirtualPath("src\\app.ts");
        Assert.Equal("src/app.ts", vp.Value);
    }

    [Fact]
    public void VirtualPath_StripsLeadingSeparator()
    {
        var vp = new VirtualPath("/src/app.ts");
        Assert.Equal("src/app.ts", vp.Value);
    }

    [Fact]
    public void PhysicalPath_NormalizesToFullPath()
    {
        var pp = new PhysicalPath("/tmp/../tmp/test.txt");
        // After normalization, .. should be resolved
        Assert.EndsWith("test.txt", pp.Value);
        Assert.DoesNotContain("..", pp.Value);
    }

    [Fact]
    public void PhysicalPath_FromRooted_RejectsRelative()
    {
        Assert.Throws<ArgumentException>(() => PhysicalPath.FromRooted("relative/path"));
    }

    [Fact]
    public void ToVirtualPath_RootNotInChild_ReturnsNull()
    {
        var root = Path.GetTempPath();
        var child = Path.Combine(root, "test_subdir", "file.txt");
        var vp = PathComparer.ToVirtualPath(root, child);
        Assert.NotNull(vp);
        Assert.Equal("test_subdir/file.txt", vp!.Value.Value);
    }

    [Fact]
    public void ToVirtualPath_OutsideRoot_ReturnsNull()
    {
        var root = Path.Combine(Path.GetTempPath(), "cachehub_root_a");
        var child = Path.Combine(Path.GetTempPath(), "cachehub_root_b", "file.txt");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.GetDirectoryName(child)!);
        try
        {
            var vp = PathComparer.ToVirtualPath(root, child);
            Assert.Null(vp);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
            try { Directory.Delete(Path.GetDirectoryName(child)!, true); } catch { }
        }
    }

    [Fact]
    public void ToPhysicalPath_RoundTripsWithToVirtualPath()
    {
        var root = Path.GetTempPath();
        var physical = Path.Combine(root, "sub", "file.cs");
        var vp = PathComparer.ToVirtualPath(root, physical);
        Assert.NotNull(vp);
        var back = PathComparer.ToPhysicalPath(root, vp!.Value);
        // On Windows case may differ in root, but the relative part should match
        Assert.EndsWith("sub" + Path.DirectorySeparatorChar + "file.cs", back.Value);
    }

    [Fact]
    public void IsWithinRoot_ChildInRoot_ReturnsTrue()
    {
        var root = Path.GetTempPath();
        var child = Path.Combine(root, "subdir", "file.txt");
        Assert.True(PathComparer.IsWithinRoot(root, child));
    }

    [Fact]
    public void IsWithinRoot_ChildOutsideRoot_ReturnsFalse()
    {
        var root = Path.Combine(Path.GetTempPath(), "cachehub_isroot_1");
        var child = Path.Combine(Path.GetTempPath(), "cachehub_isroot_2", "file.txt");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.GetDirectoryName(child)!);
        try
        {
            Assert.False(PathComparer.IsWithinRoot(root, child));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
            try { Directory.Delete(Path.GetDirectoryName(child)!, true); } catch { }
        }
    }

    [Fact]
    public void PhysicalEquals_OnSamePath_ReturnsTrue()
    {
        var path = Path.Combine(Path.GetTempPath(), "test.txt");
        Assert.True(PathComparer.PhysicalEquals(path, path));
    }
}
