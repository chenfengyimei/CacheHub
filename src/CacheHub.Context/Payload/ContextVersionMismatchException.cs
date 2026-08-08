namespace CacheHub.Context.Payload;

/// <summary>
/// V8-P0-02: Thrown when payload content hash does not match the manifest's ContentHash.
/// This prevents a ContextPackageId from producing different content across requests.
/// </summary>
public sealed class ContextVersionMismatchException : Exception
{
    /// <summary>The file path that had a hash mismatch.</summary>
    public string FilePath { get; }

    /// <summary>The expected hash from the manifest.</summary>
    public string ExpectedHash { get; }

    /// <summary>The actual hash computed from current content.</summary>
    public string ActualHash { get; }

    public ContextVersionMismatchException(string filePath, string expectedHash, string actualHash)
        : base($"Content version mismatch for '{filePath}': expected {expectedHash}, got {actualHash}. " +
               "The file has changed since the Context Package was built. " +
               "Run 'cachehub index refresh' and rebuild the context.")
    {
        FilePath = filePath;
        ExpectedHash = expectedHash;
        ActualHash = actualHash;
    }
}
