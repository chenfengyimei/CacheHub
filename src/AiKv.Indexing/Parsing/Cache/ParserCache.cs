using AiKv.Core.Parsing;
using AiKv.Indexing.Parsing;

namespace AiKv.Indexing.Parsing.Cache;

/// <summary>
/// Caches parse results by file hash + parser ID + parser version + grammar version.
/// Avoids re-parsing unchanged files.
/// </summary>
public sealed class ParserCache
{
    private readonly Dictionary<string, ParseResult> _cache = new();
    private readonly Dictionary<string, string> _hashToKey = new();

    /// <summary>
    /// Gets a cached parse result if available.
    /// </summary>
    public ParseResult? TryGet(string fileHash, string parserId, string parserVersion, string? grammarVersion = null)
    {
        var key = BuildKey(fileHash, parserId, parserVersion, grammarVersion);
        return _cache.TryGetValue(key, out var result) ? result : null;
    }

    /// <summary>
    /// Stores a parse result in the cache.
    /// </summary>
    public void Put(string fileHash, string parserId, string parserVersion, ParseResult result, string? grammarVersion = null)
    {
        var key = BuildKey(fileHash, parserId, parserVersion, grammarVersion);
        _cache[key] = result;
    }

    /// <summary>
    /// Gets or parses a file, using the cache when possible.
    /// </summary>
    public ParseResult GetOrParse(string content, string filePath, string fileHash, ICodeParser parser)
    {
        var cached = TryGet(fileHash, parser.Id, parser.Version);
        if (cached is not null) return cached;

        var result = parser.Parse(content, filePath);
        Put(fileHash, parser.Id, parser.Version, result);
        return result;
    }

    /// <summary>
    /// Removes all cached entries for a specific file hash.
    /// </summary>
    public void Invalidate(string fileHash)
    {
        var keysToRemove = _cache.Keys
            .Where(k => k.StartsWith(fileHash + "|", StringComparison.Ordinal))
            .ToList();
        foreach (var key in keysToRemove)
            _cache.Remove(key);
    }

    /// <summary>
    /// Clears the entire cache.
    /// </summary>
    public void Clear() => _cache.Clear();

    /// <summary>
    /// Returns the number of cached entries.
    /// </summary>
    public int Count => _cache.Count;

    private static string BuildKey(string fileHash, string parserId, string parserVersion, string? grammarVersion)
        => $"{fileHash}|{parserId}|{parserVersion}|{grammarVersion ?? "default"}";
}
