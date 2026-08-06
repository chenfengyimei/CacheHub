using System.Security.Cryptography;

namespace AiKv.Indexing.Hashing;

/// <summary>
/// Provides layered file hashing: full SHA-256 for small files, fast fingerprint for large files.
/// </summary>
public static class FileHasher
{
    private const long LargeFileThreshold = 1 * 1024 * 1024; // 1 MB
    private const int FingerprintSampleSize = 4096;

    /// <summary>
    /// Computes hash for a file. Uses full SHA-256 for small files,
    /// fast fingerprint (size + first/last samples) for large files.
    /// </summary>
    public static async Task<FileHash> HashAsync(string filePath, long fileSize, CancellationToken ct = default)
    {
        if (fileSize <= LargeFileThreshold)
        {
            var fullHash = await ComputeFullHashAsync(filePath, ct);
            return new FileHash(fullHash, fileSize, IsFullHash: true);
        }

        var fingerprint = await ComputeFastFingerprintAsync(filePath, fileSize, ct);
        return new FileHash(fingerprint, fileSize, IsFullHash: false);
    }

    /// <summary>
    /// Computes full SHA-256 hash on demand (e.g., before including in Context Package).
    /// </summary>
    public static async Task<string> ComputeFullHashAsync(string filePath, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<string> ComputeFastFingerprintAsync(string filePath, long fileSize, CancellationToken ct)
    {
        await using var stream = File.OpenRead(filePath);
        var sampleSize = (int)Math.Min(FingerprintSampleSize, fileSize);

        var head = new byte[sampleSize];
        var headRead = await stream.ReadAsync(head.AsMemory(), ct);

        var tailRead = 0;
        var tail = new byte[sampleSize];
        if (fileSize > sampleSize * 2)
        {
            stream.Seek(-sampleSize, SeekOrigin.End);
            tailRead = await stream.ReadAsync(tail.AsMemory(), ct);
        }

        var combined = new byte[headRead + tailRead];
        Buffer.BlockCopy(head, 0, combined, 0, headRead);
        Buffer.BlockCopy(tail, 0, combined, headRead, tailRead);

        var hash = SHA256.HashData(combined);
        return $"fp:{fileSize}:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}

/// <summary>
/// File hash result with metadata.
/// </summary>
public sealed record FileHash(string Hash, long FileSize, bool IsFullHash);
