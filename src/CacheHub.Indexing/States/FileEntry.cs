namespace CacheHub.Indexing.States;

/// <summary>
/// File indexing state machine.
/// </summary>
public enum FileState
{
    Discovered,
    Indexed,
    Ignored,
    Failed,
    Deleted,
    Stale,
}

/// <summary>
/// Tracks the state of a file during indexing.
/// </summary>
public sealed record FileEntry
{
    public required string Path { get; init; }
    public required string NormalizedPath { get; init; }
    public required long Size { get; init; }
    public required DateTimeOffset LastModified { get; init; }
    public string? ContentHash { get; init; }
    public string? FastFingerprint { get; init; }
    public string? Language { get; init; }
    public bool IsBinary { get; init; }
    public FileState State { get; init; } = FileState.Discovered;
    public string? Error { get; init; }

    public FileEntry MarkIndexed(string contentHash, string language)
        => this with { State = FileState.Indexed, ContentHash = contentHash, Language = language };

    public FileEntry MarkIgnored()
        => this with { State = FileState.Ignored };

    public FileEntry MarkFailed(string error)
        => this with { State = FileState.Failed, Error = error };

    public FileEntry MarkDeleted()
        => this with { State = FileState.Deleted };

    public FileEntry MarkStale()
        => this with { State = FileState.Stale };
}
