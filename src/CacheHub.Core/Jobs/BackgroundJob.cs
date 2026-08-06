using CacheHub.Core.Identifiers;

namespace CacheHub.Core.Jobs;

/// <summary>
/// Background job status state machine.
/// </summary>
public enum JobStatus
{
    Queued,
    Running,
    WaitingForApproval,
    Paused,
    Completed,
    Failed,
    Cancelled,
    Recovering,
}

/// <summary>
/// Background job model for tracking long-running operations.
/// </summary>
public sealed record BackgroundJob
{
    public required JobId Id { get; init; }
    public WorkspaceId? WorkspaceId { get; init; }
    public required string Type { get; init; }
    public JobStatus Status { get; init; } = JobStatus.Queued;
    public int Progress { get; init; }
    public int Total { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }

    public static BackgroundJob Create(string type, WorkspaceId? workspaceId = null, int total = 0)
    {
        return new BackgroundJob
        {
            Id = JobId.New(),
            WorkspaceId = workspaceId,
            Type = type,
            Total = total,
        };
    }
}
