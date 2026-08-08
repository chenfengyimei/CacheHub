using CacheHub.Core.Indexing;
using CacheHub.Core.Repository;
using Xunit;

namespace CacheHub.Tests;

/// <summary>
/// V7-W02: Stale detection tests.
/// </summary>
public class StaleDetectionTests
{
    [Fact]
    public async Task StaleDetector_NoFingerprint_ReturnsFreshWithWarning()
    {
        var result = await StaleDetector.CheckAsync("/tmp", null);
        Assert.True(result.IsFresh);
        Assert.True(result.NoFingerprint);
    }

    [Fact]
    public async Task StaleDetector_EmptyFingerprint_ReturnsFreshWithWarning()
    {
        var result = await StaleDetector.CheckAsync("/tmp", "");
        Assert.True(result.IsFresh);
        Assert.True(result.NoFingerprint);
    }

    [Fact]
    public async Task StaleDetector_NonExistentDirectory_ReturnsFresh()
    {
        var result = await StaleDetector.CheckAsync(
            "/nonexistent/path/that/does/not/exist",
            "some-fingerprint");
        Assert.True(result.IsFresh);
    }

    [Fact]
    public void StaleDetectionResult_FreshMatch_IsFreshTrue()
    {
        var result = new StaleDetectionResult
        {
            IsFresh = true,
            SnapshotFingerprint = "abc",
            CurrentFingerprint = "abc",
            Message = "match",
        };
        Assert.True(result.IsFresh);
        Assert.Equal("abc", result.SnapshotFingerprint);
        Assert.Equal("abc", result.CurrentFingerprint);
    }

    [Fact]
    public void StaleDetectionResult_StaleMatch_IsFreshFalse()
    {
        var result = new StaleDetectionResult
        {
            IsFresh = false,
            SnapshotFingerprint = "abc",
            CurrentFingerprint = "def",
            Message = "changed",
        };
        Assert.False(result.IsFresh);
        Assert.NotEqual(result.SnapshotFingerprint, result.CurrentFingerprint);
    }
}
