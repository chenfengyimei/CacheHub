using AiKv.Core.Identifiers;

namespace AiKv.Tests;

public class StrongIdTests
{
    [Fact]
    public void StrongId_ShouldRejectEmptyValue()
    {
        Assert.Throws<ArgumentException>(() => new WorkspaceId(""));
        Assert.Throws<ArgumentException>(() => new WorkspaceId("  "));
        Assert.Throws<ArgumentException>(() => new WorkspaceId(null!));
    }

    [Fact]
    public void StrongId_ShouldStoreValue()
    {
        var id = new WorkspaceId("ws_abc123");
        Assert.Equal("ws_abc123", id.Value);
    }

    [Fact]
    public void StrongId_New_ShouldGenerateNonEmptyId()
    {
        var id = WorkspaceId.New();
        Assert.False(string.IsNullOrWhiteSpace(id.Value));
        Assert.Equal(32, id.Value.Length); // Guid.ToString("N") = 32 chars
    }

    [Fact]
    public void StrongId_Parse_ShouldCreateFromValue()
    {
        var id = FileId.Parse("file_001");
        Assert.Equal("file_001", id.Value);
    }

    [Fact]
    public void StrongId_Equality_ShouldCompareByValue()
    {
        var a = new WorkspaceId("ws_001");
        var b = new WorkspaceId("ws_001");
        var c = new WorkspaceId("ws_002");

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.True(a != c);
    }

    [Fact]
    public void StrongId_ShouldNotMixDifferentTypes()
    {
        // Even if values are equal, different ID types should not be equal.
        var wsId = new WorkspaceId("abc");
        var fileId = new FileId("abc");

        // Different types cannot be compared with == since they are different record types.
        Assert.False(wsId.Equals(fileId));
    }

    [Fact]
    public void StrongId_CompareTo_ShouldOrderCorrectly()
    {
        var a = new JobId("job_001");
        var b = new JobId("job_002");

        Assert.True(a.CompareTo(b) < 0);
        Assert.True(b.CompareTo(a) > 0);
        Assert.Equal(0, a.CompareTo(new JobId("job_001")));
    }

    [Fact]
    public void StrongId_GetHashCode_ShouldBeConsistent()
    {
        var a = new ContextPackageId("ctx_001");
        var b = new ContextPackageId("ctx_001");

        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }
}
