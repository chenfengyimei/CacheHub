using CacheHub.Core.Identifiers;
using CacheHub.Core.Paths;

namespace CacheHub.Core.Workspaces;

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
/// Workspace aggregate: the highest project container in CacheHub.
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

    /// <summary>
    /// Creates a workspace and validates that the root directory exists and is accessible.
    /// Use this for real imports; use Create() for tests/mocks.
    /// </summary>
    public static Workspace CreateValidated(string name, string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        if (!System.IO.Directory.Exists(rootPath))
            throw new ArgumentException($"Directory does not exist or is not accessible: {rootPath}", nameof(rootPath));

        return Create(name, rootPath);
    }
}
