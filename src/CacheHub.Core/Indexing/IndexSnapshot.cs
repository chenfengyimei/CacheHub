using CacheHub.Core.Identifiers;

namespace CacheHub.Core.Indexing;

/// <summary>
/// Index snapshot status.
/// </summary>
public enum SnapshotStatus
{
    Building,
    Active,
    ActiveDegraded,
    Superseded,
    Failed,
    Cancelled,
}

/// <summary>
/// Index snapshot: a point-in-time view of the workspace's indexed files.
/// New snapshots are built in 'Building' state, then atomically switched to 'Active'.
/// V7-W01: Now includes Git state (commit, branch, dirty state hash, workspace fingerprint)
/// for version-aware Context Packages and stale detection.
/// </summary>
public sealed record IndexSnapshot
{
    public required IndexSnapshotId Id { get; init; }
    public required WorkspaceId WorkspaceId { get; init; }
    public SnapshotStatus Status { get; init; } = SnapshotStatus.Building;
    public int FileCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>Git commit hash at snapshot creation time (null if not a git repo).</summary>
    public string? RepositoryCommit { get; init; }

    /// <summary>Git branch name at snapshot creation time (null if detached or non-git).</summary>
    public string? Branch { get; init; }

    /// <summary>True if the working tree had uncommitted changes when the snapshot was created.</summary>
    public bool IsDirty { get; init; }

    /// <summary>SHA-256 fingerprint of the workspace version state (commit + branch + dirty file hashes).</summary>
    public string? WorkspaceFingerprint { get; init; }

    public static IndexSnapshot Create(WorkspaceId workspaceId) =>
        new()
        {
            Id = IndexSnapshotId.New(),
            WorkspaceId = workspaceId,
        };
}
