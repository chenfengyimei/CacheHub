using AiKv.Core.Paths;
using AiKv.Core.Workspaces;

namespace AiKv.Tests;

public class PathAndWorkspaceTests
{
    [Fact]
    public void PathNormalizer_Normalize_ShouldUseForwardSlashes()
    {
        var path = @"C:\Users\test\project";

        var result = PathNormalizer.Normalize(path);

        Assert.DoesNotContain("\\", result);
        Assert.Contains("/", result);
    }

    [Fact]
    public void PathNormalizer_IsWithinRoot_ShouldAcceptChildPaths()
    {
        Assert.True(PathNormalizer.IsWithinRoot(@"C:\root", @"C:\root\sub\file.ts"));
        Assert.True(PathNormalizer.IsWithinRoot(@"C:\root", @"C:\root"));
    }

    [Fact]
    public void PathNormalizer_IsWithinRoot_ShouldRejectOutsidePaths()
    {
        Assert.False(PathNormalizer.IsWithinRoot(@"C:\root", @"C:\other\file.ts"));
        Assert.False(PathNormalizer.IsWithinRoot(@"C:\root", @"C:\rootmalicious\file.ts"));
    }

    [Fact]
    public void PathNormalizer_ContainsTraversal_ShouldDetectDotDot()
    {
        Assert.True(PathNormalizer.ContainsTraversal("../../../etc/passwd"));
        Assert.True(PathNormalizer.ContainsTraversal("sub/../../escape"));
        Assert.True(PathNormalizer.ContainsTraversal("%2e%2e/escape"));
    }

    [Fact]
    public void PathNormalizer_ContainsTraversal_ShouldAcceptNormalPaths()
    {
        Assert.False(PathNormalizer.ContainsTraversal("src/auth/token.ts"));
        Assert.False(PathNormalizer.ContainsTraversal("C:/project/src"));
    }

    [Fact]
    public void PathNormalizer_ComputePathHash_ShouldBeDeterministic()
    {
        var hash1 = PathNormalizer.ComputePathHash("c:/project/src");
        var hash2 = PathNormalizer.ComputePathHash("c:/project/src");

        Assert.Equal(hash1, hash2);
        Assert.Equal(64, hash1.Length); // SHA-256 hex = 64 chars
    }

    [Fact]
    public void PathNormalizer_GetRelativePath_ShouldReturnForwardSlashPath()
    {
        var relative = PathNormalizer.GetRelativePath(@"C:\root", @"C:\root\src\auth.ts");

        Assert.Equal("src/auth.ts", relative);
    }

    [Fact]
    public void Workspace_Create_ShouldInitializeCorrectly()
    {
        var ws = Workspace.Create("test-project", @"C:\projects\test");

        Assert.False(string.IsNullOrEmpty(ws.Id.Value));
        Assert.Equal("test-project", ws.Name);
        Assert.DoesNotContain("\\", ws.RootPath);
        Assert.False(string.IsNullOrEmpty(ws.RootPathHash));
        Assert.Equal(WorkspaceStatus.Imported, ws.Status);
    }

    [Fact]
    public void Workspace_Create_ShouldRejectEmptyArgs()
    {
        Assert.Throws<ArgumentException>(() => Workspace.Create("", @"C:\test"));
        Assert.Throws<ArgumentException>(() => Workspace.Create("test", ""));
        Assert.Throws<ArgumentNullException>(() => Workspace.Create(null!, @"C:\test"));
    }

    [Fact]
    public void Workspace_Create_SamePath_ShouldProduceSameHash()
    {
        var ws1 = Workspace.Create("test1", @"C:\projects\test");
        var ws2 = Workspace.Create("test2", @"C:\projects\test");

        Assert.Equal(ws1.RootPathHash, ws2.RootPathHash);
    }
}
