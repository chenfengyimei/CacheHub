namespace CacheHub.Context.Engine;

/// <summary>
/// Raised when a caller attempts to build context from an index snapshot that
/// does not match the currently observed workspace version without explicitly
/// opting in to stale context.
/// </summary>
public sealed class ContextStaleException : InvalidOperationException
{
    public ContextStaleException(string snapshotFingerprint, string currentFingerprint)
        : base("Workspace state differs from the index snapshot. Refresh the index or explicitly allow stale context.")
    {
        SnapshotFingerprint = snapshotFingerprint;
        CurrentFingerprint = currentFingerprint;
    }

    public string SnapshotFingerprint { get; }
    public string CurrentFingerprint { get; }
}
