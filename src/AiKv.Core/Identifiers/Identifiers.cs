namespace AiKv.Core.Identifiers;

/// <summary>
/// Unique identifier for a workspace.
/// </summary>
public sealed record WorkspaceId(string Value) : StrongId(Value)
{
    public static WorkspaceId New() => new(Guid.NewGuid().ToString("N"));
    public static WorkspaceId Parse(string value) => new(value);
}

/// <summary>
/// Unique identifier for a repository within a workspace.
/// </summary>
public sealed record RepositoryId(string Value) : StrongId(Value)
{
    public static RepositoryId New() => new(Guid.NewGuid().ToString("N"));
    public static RepositoryId Parse(string value) => new(value);
}

/// <summary>
/// Unique identifier for a component within a workspace.
/// </summary>
public sealed record ComponentId(string Value) : StrongId(Value)
{
    public static ComponentId New() => new(Guid.NewGuid().ToString("N"));
    public static ComponentId Parse(string value) => new(value);
}

/// <summary>
/// Unique identifier for an indexed file.
/// </summary>
public sealed record FileId(string Value) : StrongId(Value)
{
    public static FileId New() => new(Guid.NewGuid().ToString("N"));
    public static FileId Parse(string value) => new(value);
}

/// <summary>
/// Unique identifier for an index snapshot.
/// </summary>
public sealed record IndexSnapshotId(string Value) : StrongId(Value)
{
    public static IndexSnapshotId New() => new(Guid.NewGuid().ToString("N"));
    public static IndexSnapshotId Parse(string value) => new(value);
}

/// <summary>
/// Unique identifier for a Context Package.
/// </summary>
public sealed record ContextPackageId(string Value) : StrongId(Value)
{
    public static ContextPackageId New() => new(Guid.NewGuid().ToString("N"));
    public static ContextPackageId Parse(string value) => new(value);
}

/// <summary>
/// Unique identifier for a background job.
/// </summary>
public sealed record JobId(string Value) : StrongId(Value)
{
    public static JobId New() => new(Guid.NewGuid().ToString("N"));
    public static JobId Parse(string value) => new(value);
}
