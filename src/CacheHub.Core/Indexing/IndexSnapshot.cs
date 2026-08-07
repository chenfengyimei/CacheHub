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
/// </summary>
public sealed record IndexSnapshot
{
    public required IndexSnapshotId Id { get; init; }
    public required WorkspaceId WorkspaceId { get; init; }
    public SnapshotStatus Status { get; init; } = SnapshotStatus.Building;
    public int FileCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; init; }

    public static IndexSnapshot Create(WorkspaceId workspaceId) =>
        new()
        {
            Id = IndexSnapshotId.New(),
            WorkspaceId = workspaceId,
        };
}
