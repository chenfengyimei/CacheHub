using CacheHub.Core.Repository;

namespace CacheHub.Core.Indexing;

/// <summary>
/// Result of stale detection check.
/// </summary>
public sealed record StaleDetectionResult
{
    /// <summary>True if the workspace state matches the snapshot.</summary>
    public required bool IsFresh { get; init; }

    /// <summary>The fingerprint stored in the snapshot.</summary>
    public string? SnapshotFingerprint { get; init; }

    /// <summary>The current workspace fingerprint.</summary>
    public string? CurrentFingerprint { get; init; }

    /// <summary>Human-readable message for the user.</summary>
    public required string Message { get; init; }

    /// <summary>True if the snapshot has no fingerprint (pre-V7 snapshot).</summary>
    public bool NoFingerprint { get; init; }
}

/// <summary>
/// Detects whether the workspace state has changed since the index snapshot was created.
/// V7-W02: Prevents "old hash + new content" inconsistency in Context Packages.
/// </summary>
public static class StaleDetector
{
    /// <summary>
    /// Checks if the workspace state matches the snapshot.
    /// Returns IsFresh=true if fingerprints match.
    /// V8-P1-01: fileFilter parameter allows callers to scope fingerprint to indexed files only.
    /// </summary>
    public static async Task<StaleDetectionResult> CheckAsync(
        string workspaceRoot,
        string? snapshotFingerprint,
        GitStateProvider? gitStateProvider = null,
        Func<string, bool>? fileFilter = null,
        CancellationToken ct = default)
    {
        // No fingerprint means pre-V7 snapshot — can't check, allow with warning
        if (string.IsNullOrEmpty(snapshotFingerprint))
        {
            return new StaleDetectionResult
            {
                IsFresh = true,
                SnapshotFingerprint = null,
                CurrentFingerprint = null,
                Message = "Snapshot has no workspace fingerprint (pre-V7). Stale detection skipped.",
                NoFingerprint = true,
            };
        }

        var provider = gitStateProvider ?? new GitStateProvider();
        var currentState = await provider.CaptureAsync(workspaceRoot, fileFilter, ct);

        if (string.IsNullOrEmpty(currentState.Fingerprint))
        {
            return new StaleDetectionResult
            {
                IsFresh = true,
                SnapshotFingerprint = snapshotFingerprint,
                CurrentFingerprint = null,
                Message = "Unable to capture current workspace state. Stale detection skipped.",
            };
        }

        var isFresh = string.Equals(snapshotFingerprint, currentState.Fingerprint, StringComparison.Ordinal);

        return new StaleDetectionResult
        {
            IsFresh = isFresh,
            SnapshotFingerprint = snapshotFingerprint,
            CurrentFingerprint = currentState.Fingerprint,
            Message = isFresh
                ? "Workspace state matches snapshot."
                : "Workspace state has changed since last index build. Run 'cachehub index refresh' to update the index.",
        };
    }
}
