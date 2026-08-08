using CacheHub.Context.Engine;
using CacheHub.Context.Cache;
using CacheHub.Context.Budget;
using CacheHub.Context.Ranking;
using CacheHub.Context.Selection;
using CacheHub.Context.Chunking;
using CacheHub.Context.Recall;
using CacheHub.Core.Context;
using CacheHub.Core.Identifiers;
using CacheHub.Core.Tokens;
using Xunit;

namespace CacheHub.Tests;

/// <summary>
/// V8-P0-01: Context Strict Version Gate tests.
/// Verifies that stale context is rejected by default and cache is skipped when stale.
/// </summary>
public class V8StaleContextGateTests
{
    [Fact]
    public void ContextBuildRequest_CurrentWorkspaceFingerprint_DefaultsNull()
    {
        var request = new ContextBuildRequest
        {
            WorkspaceId = WorkspaceId.New(),
            IndexSnapshotId = IndexSnapshotId.New(),
            Task = "test task",
        };
        Assert.Null(request.CurrentWorkspaceFingerprint);
        Assert.False(request.AllowStale);
    }

    [Fact]
    public void ContextBuildRequest_AllowStale_DefaultsFalse()
    {
        var request = new ContextBuildRequest
        {
            WorkspaceId = WorkspaceId.New(),
            IndexSnapshotId = IndexSnapshotId.New(),
            Task = "test task",
            WorkspaceFingerprint = "snap-fp",
            CurrentWorkspaceFingerprint = "current-fp",
        };
        Assert.False(request.AllowStale);
    }

    [Fact]
    public void ContextBuildRequest_StaleState_DifferentFingerprints()
    {
        var request = new ContextBuildRequest
        {
            WorkspaceId = WorkspaceId.New(),
            IndexSnapshotId = IndexSnapshotId.New(),
            Task = "test task",
            WorkspaceFingerprint = "snapshot-fingerprint",
            CurrentWorkspaceFingerprint = "current-fingerprint",
            AllowStale = true,
        };
        Assert.NotEqual(request.WorkspaceFingerprint, request.CurrentWorkspaceFingerprint);
        Assert.True(request.AllowStale);
    }

    /// <summary>
    /// V8-P0-01: When stale (CurrentWorkspaceFingerprint != WorkspaceFingerprint) and AllowStale=false,
    /// the ContextEngine must skip cache lookup to prevent returning stale cached context.
    /// </summary>
    [Fact]
    public void ContextEngine_StaleWithoutAllowStale_SkipsCache()
    {
        // This test verifies the logic through the CacheKey path.
        // We test the skipCache logic indirectly by verifying that a stale request
        // with a previously cached result does NOT return the cached result.

        var wsId = WorkspaceId.New();
        var snapshotId = IndexSnapshotId.New();
        var snapshotFingerprint = "snap-fp-v1";
        var currentFingerprint = "current-fp-v2"; // Different = stale

        // Create a cache with a pre-existing entry using the snapshot fingerprint
        var cache = new ContextPackageCache();
        var engine = new ContextEngine(
            TokenizerRegistry.CreateWithDefaults(),
            securityPolicy: null,
            cache: cache);

        // First build: use snapshot fingerprint only (simulating initial build when fresh)
        var request1 = new ContextBuildRequest
        {
            WorkspaceId = wsId,
            IndexSnapshotId = snapshotId,
            Task = "implement auth",
            WorkspaceFingerprint = snapshotFingerprint,
            // CurrentWorkspaceFingerprint not set = fresh (no stale detection done)
        };

        var manifest1 = engine.Build(request1, ProvideFiles, ProvideContent, ProvideHash);
        Assert.NotNull(manifest1);

        // Second build: stale state (current != snapshot), AllowStale = false
        // This should NOT return the cached manifest from request1
        var request2 = new ContextBuildRequest
        {
            WorkspaceId = wsId,
            IndexSnapshotId = snapshotId,
            Task = "implement auth",
            WorkspaceFingerprint = snapshotFingerprint,
            CurrentWorkspaceFingerprint = currentFingerprint, // Stale!
            AllowStale = false, // Default: do not allow stale
        };

        var manifest2 = engine.Build(request2, ProvideFiles, ProvideContent, ProvideHash);
        Assert.NotNull(manifest2);
        // The manifests should be different objects (cache was skipped)
        Assert.NotSame(manifest1, manifest2);
        // But they should have different IDs (new build, not cached)
        Assert.NotEqual(manifest1.Id.Value, manifest2.Id.Value);
    }

    /// <summary>
    /// V8-P0-01: When stale with AllowStale=true, the engine proceeds and uses
    /// the current fingerprint for cache key (not snapshot's), so old cache is not hit.
    /// </summary>
    [Fact]
    public void ContextEngine_StaleWithAllowStale_UsesCurrentFingerprintForCache()
    {
        var wsId = WorkspaceId.New();
        var snapshotId = IndexSnapshotId.New();
        var snapshotFingerprint = "snap-fp-v1";
        var currentFingerprint = "current-fp-v2";

        var cache = new ContextPackageCache();
        var engine = new ContextEngine(
            TokenizerRegistry.CreateWithDefaults(),
            securityPolicy: null,
            cache: cache);

        // Build with stale state + AllowStale = true
        var request1 = new ContextBuildRequest
        {
            WorkspaceId = wsId,
            IndexSnapshotId = snapshotId,
            Task = "implement feature",
            WorkspaceFingerprint = snapshotFingerprint,
            CurrentWorkspaceFingerprint = currentFingerprint,
            AllowStale = true,
        };

        var manifest1 = engine.Build(request1, ProvideFiles, ProvideContent, ProvideHash);
        Assert.NotNull(manifest1);

        // Second identical build should hit cache (same current fingerprint used for key)
        var request2 = new ContextBuildRequest
        {
            WorkspaceId = wsId,
            IndexSnapshotId = snapshotId,
            Task = "implement feature",
            WorkspaceFingerprint = snapshotFingerprint,
            CurrentWorkspaceFingerprint = currentFingerprint,
            AllowStale = true,
        };

        var manifest2 = engine.Build(request2, ProvideFiles, ProvideContent, ProvideHash);
        Assert.Same(manifest1, manifest2); // Cache hit — same object
    }

    /// <summary>
    /// V8-P0-01: When fresh (Current == Snapshot), cache works normally.
    /// </summary>
    [Fact]
    public void ContextEngine_FreshState_CacheWorksNormally()
    {
        var wsId = WorkspaceId.New();
        var snapshotId = IndexSnapshotId.New();
        var fingerprint = "same-fp";

        var cache = new ContextPackageCache();
        var engine = new ContextEngine(
            TokenizerRegistry.CreateWithDefaults(),
            securityPolicy: null,
            cache: cache);

        var request = new ContextBuildRequest
        {
            WorkspaceId = wsId,
            IndexSnapshotId = snapshotId,
            Task = "fix bug",
            WorkspaceFingerprint = fingerprint,
            CurrentWorkspaceFingerprint = fingerprint, // Same = fresh
        };

        var manifest1 = engine.Build(request, ProvideFiles, ProvideContent, ProvideHash);
        var manifest2 = engine.Build(request, ProvideFiles, ProvideContent, ProvideHash);
        Assert.Same(manifest1, manifest2); // Cache hit
    }

    // Helpers
    private static IReadOnlyList<IndexedFileInfo> ProvideFiles() =>
    [
        new IndexedFileInfo
        {
            Path = "src/auth.cs",
            NormalizedPath = "src/auth.cs",
            Language = "csharp",
            Size = 500,
            ContentHash = "sha256:abc",
        },
    ];

    private static string ProvideContent(string path) =>
        path == "src/auth.cs" ? "public class Auth { }" : "";

    private static string ProvideHash(string path) =>
        path == "src/auth.cs" ? "sha256:abc" : "sha256:unknown";
}
