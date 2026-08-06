using AiKv.Core.Identifiers;
using AiKv.Core.Paths;

namespace AiKv.Core.Workspaces;

/// <summary>
/// Workspace status state machine.
/// </summary>
public enum WorkspaceStatus
{
    Unregistered,
    Imported,
    Indexing,
    Ready,
    Dirty,
    Degraded,
    Blocked,
    Archived,
}

/// <summary>
/// Workspace aggregate: the highest project container in AI_KV.
/// Can contain a single repo, monorepo, multi-repo collection, or non-Git directory.
/// </summary>
public sealed record Workspace
{
    public required WorkspaceId Id { get; init; }
    public required string Name { get; init; }
    public required string RootPath { get; init; }
    public required string RootPathHash { get; init; }
    public WorkspaceStatus Status { get; init; } = WorkspaceStatus.Imported;
    public string? SecurityPolicyVersion { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    public static Workspace Create(string name, string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        var normalized = PathNormalizer.Normalize(rootPath);
        var hash = PathNormalizer.ComputePathHash(normalized);

        return new Workspace
        {
            Id = WorkspaceId.New(),
            Name = name,
            RootPath = normalized,
            RootPathHash = hash,
        };
    }
}
