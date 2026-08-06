using CacheHub.Core.Paths;

namespace CacheHub.Indexing.Scanning;

/// <summary>
/// Configuration for directory enumeration.
/// </summary>
public sealed record EnumerationOptions
{
    public int MaxDepth { get; init; } = 50;
    public int MaxFileCount { get; init; } = 500_000;
    public long MaxFileSizeBytes { get; init; } = 100 * 1024 * 1024; // 100 MB
    public bool FollowSymlinks { get; init; }
}

/// <summary>
/// Represents a discovered file during enumeration.
/// </summary>
public sealed record DiscoveredFile
{
    public required string Path { get; init; }
    public required string NormalizedPath { get; init; }
    public required long Size { get; init; }
    public required DateTimeOffset LastModified { get; init; }
    public required bool IsDirectory { get; init; }
    public string? Extension { get; init; }
}

/// <summary>
/// Streams files from a directory tree with depth/count limits, symlink protection, and error resilience.
/// </summary>
public sealed class DirectoryEnumerator(EnumerationOptions? options = null)
{
    private readonly EnumerationOptions _options = options ?? new EnumerationOptions();

    /// <summary>
    /// Enumerates files in a directory tree. Yields results lazily.
    /// Stops when MaxFileCount is reached.
    /// </summary>
    public async IAsyncEnumerable<DiscoveredFile> EnumerateAsync(
        string rootPath,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        if (!Directory.Exists(rootPath))
            throw new DirectoryNotFoundException($"Directory not found: {rootPath}");

        var normalizedRoot = PathNormalizer.Normalize(rootPath);
        var visitedSymlinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var count = 0;

        await foreach (var file in EnumerateDirectoryAsync(normalizedRoot, 0, visitedSymlinks, ct))
        {
            if (count >= _options.MaxFileCount)
            {
                yield break;
            }
            count++;
            yield return file;
        }
    }

    private async IAsyncEnumerable<DiscoveredFile> EnumerateDirectoryAsync(
        string currentPath,
        int depth,
        HashSet<string> visitedSymlinks,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        if (depth > _options.MaxDepth) yield break;

        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(currentPath, "*", new System.IO.EnumerationOptions
            {
                ReturnSpecialDirectories = false,
                RecurseSubdirectories = false,
                IgnoreInaccessible = true,
                AttributesToSkip = 0,
            });
        }
        catch (UnauthorizedAccessException) { yield break; }
        catch (DirectoryNotFoundException) { yield break; }

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            var normalized = PathNormalizer.Normalize(entry);
            bool isSymlink = IsSymlink(entry);

            if (isSymlink)
            {
                if (!_options.FollowSymlinks) continue;
                var resolved = ResolveSymlink(entry);
                if (resolved is null || visitedSymlinks.Contains(resolved)) continue;
                visitedSymlinks.Add(resolved);
            }

            bool isDir;
            long size = 0;
            DateTimeOffset lastModified;

            try
            {
                var info = new FileInfo(entry);
                isDir = info.Attributes.HasFlag(FileAttributes.Directory);
                size = isDir ? 0 : info.Length;
                lastModified = info.LastWriteTimeUtc;
            }
            catch (UnauthorizedAccessException) { continue; }
            catch (FileNotFoundException) { continue; }
            catch (PathTooLongException) { continue; }

            if (isDir)
            {
                yield return new DiscoveredFile
                {
                    Path = entry,
                    NormalizedPath = normalized,
                    Size = 0,
                    LastModified = lastModified,
                    IsDirectory = true,
                };

                await foreach (var sub in EnumerateDirectoryAsync(entry, depth + 1, visitedSymlinks, ct))
                {
                    yield return sub;
                }
            }
            else
            {
                if (size > _options.MaxFileSizeBytes) continue;

                yield return new DiscoveredFile
                {
                    Path = entry,
                    NormalizedPath = normalized,
                    Size = size,
                    LastModified = lastModified,
                    IsDirectory = false,
                    Extension = global::System.IO.Path.GetExtension(entry).ToLowerInvariant(),
                };
            }
        }
    }

    private static bool IsSymlink(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch { return false; }
    }

    private static string? ResolveSymlink(string path)
    {
        try
        {
            // Use FileSystemInfo.LinkTarget (.NET 9) to resolve the actual symlink target
            var fileInfo = new FileInfo(path);
            if (fileInfo.LinkTarget is not null)
            {
                // If the link target is relative, resolve it relative to the link's directory
                var target = fileInfo.LinkTarget;
                if (!Path.IsPathRooted(target))
                    target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, target));
                return Path.GetFullPath(target);
            }

            var dirInfo = new DirectoryInfo(path);
            if (dirInfo.LinkTarget is not null)
            {
                var target = dirInfo.LinkTarget;
                if (!Path.IsPathRooted(target))
                    target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, target));
                return Path.GetFullPath(target);
            }

            // Not a symlink — return the normalized full path
            return Path.GetFullPath(path);
        }
        catch { return null; }
    }
}
