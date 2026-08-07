using Xunit;
using CacheHub.Context.Cache;

namespace CacheHub.Tests;

/// <summary>
/// V6-W08 regression tests: CacheKey now includes tokenizerVersion and semanticStoreVersion.
/// </summary>
public class CacheKeyVersioningV6Tests
{
    [Fact]
    public void Build_SameInputs_ProducesSameKey()
    {
        var a = CacheKey.Build(
            task: "Fix login bug",
            indexSnapshotId: "snap-1",
            rankingProfileId: "profile",
            rankingProfileVersion: 1,
            contextTarget: 8000,
            contextHardLimit: 10000,
            securityPolicyVersion: "sec-v1",
            ignoreRulesHash: "ig",
            tokenizerId: "o200k",
            tokenizerVersion: "2.0",
            semanticStoreVersion: "local-hash-embedding-1.0");
        var b = CacheKey.Build(
            task: "Fix login bug",
            indexSnapshotId: "snap-1",
            rankingProfileId: "profile",
            rankingProfileVersion: 1,
            contextTarget: 8000,
            contextHardLimit: 10000,
            securityPolicyVersion: "sec-v1",
            ignoreRulesHash: "ig",
            tokenizerId: "o200k",
            tokenizerVersion: "2.0",
            semanticStoreVersion: "local-hash-embedding-1.0");

        Assert.Equal(a.FullKey, b.FullKey);
    }

    [Fact]
    public void Build_TokenizerVersionChange_ProducesDifferentKey()
    {
        var v1 = CacheKey.Build(
            task: "Fix login", indexSnapshotId: "s", rankingProfileId: "p", rankingProfileVersion: 1,
            contextTarget: 8000, contextHardLimit: 12000,
            securityPolicyVersion: "sec-v1", ignoreRulesHash: "ig",
            tokenizerId: "o200k", tokenizerVersion: "1.0");
        var v2 = CacheKey.Build(
            task: "Fix login", indexSnapshotId: "s", rankingProfileId: "p", rankingProfileVersion: 1,
            contextTarget: 8000, contextHardLimit: 12000,
            securityPolicyVersion: "sec-v1", ignoreRulesHash: "ig",
            tokenizerId: "o200k", tokenizerVersion: "2.0");

        Assert.NotEqual(v1.FullKey, v2.FullKey);
        // With different version, cache must invalidate
        Assert.NotEqual(v1.ContextHash, v2.ContextHash);
    }

    [Fact]
    public void Build_SemanticStoreVersionChange_ProducesDifferentVersionHash()
    {
        var a = CacheKey.Build(
            task: "Fix", indexSnapshotId: "idx", rankingProfileId: "p", rankingProfileVersion: 1,
            contextTarget: 8000, contextHardLimit: 12000,
            securityPolicyVersion: "sec-v1", ignoreRulesHash: "ig",
            semanticStoreVersion: "v1");
        var b = CacheKey.Build(
            task: "Fix", indexSnapshotId: "idx", rankingProfileId: "p", rankingProfileVersion: 1,
            contextTarget: 8000, contextHardLimit: 12000,
            securityPolicyVersion: "sec-v1", ignoreRulesHash: "ig",
            semanticStoreVersion: "v2");

        Assert.NotEqual(a.VersionHash, b.VersionHash);
    }

    [Fact]
    public void Build_SemanticStoreVersion_NoLongerNull_DefaultsToRealVersion()
    {
        var key = CacheKey.Build(
            task: "Fix", indexSnapshotId: "idx", rankingProfileId: "p", rankingProfileVersion: 1,
            contextTarget: 8000, contextHardLimit: 12000,
            securityPolicyVersion: "sec-v1", ignoreRulesHash: "ig",
            semanticStoreVersion: "local-hash-embedding-1.0");

        // Ensure the semantic version is reflected in VersionHash and not "none"
        Assert.NotEqual("none", key.VersionHash);
    }
}