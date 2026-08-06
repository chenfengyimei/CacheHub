using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AiKv.Core.Context;

namespace AiKv.Context.Cache;

/// <summary>
/// Cache key components for a Context Package.
/// </summary>
public sealed record CacheKey
{
    public required string TaskHash { get; init; }
    public required string SnapshotHash { get; init; }
    public required string ProfileHash { get; init; }
    public required string BudgetHash { get; init; }
    public required string SecurityHash { get; init; }

    public string FullKey => $"{TaskHash}|{SnapshotHash}|{ProfileHash}|{BudgetHash}|{SecurityHash}";

    public static CacheKey Build(
        string task,
        string indexSnapshotId,
        string rankingProfileId,
        int rankingProfileVersion,
        int contextTarget,
        int contextHardLimit,
        string? securityPolicyVersion,
        string? ignoreRulesHash)
    {
        var taskHash = Hash(task);
        var snapshotHash = Hash(indexSnapshotId);
        var profileHash = Hash($"{rankingProfileId}:{rankingProfileVersion}");
        var budgetHash = Hash($"{contextTarget}:{contextHardLimit}");
        var securityHash = Hash($"{securityPolicyVersion ?? "none"}|{ignoreRulesHash ?? "none"}");

        return new CacheKey
        {
            TaskHash = taskHash,
            SnapshotHash = snapshotHash,
            ProfileHash = profileHash,
            BudgetHash = budgetHash,
            SecurityHash = securityHash,
        };
    }

    private static string Hash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant()[..16];
    }
}

/// <summary>
/// Cache for Context Package manifests.
/// Strictly bound to task, snapshot, profile, budget, security, and version.
/// </summary>
public sealed class ContextPackageCache
{
    private readonly Dictionary<string, ContextPackageManifest> _cache = new();

    public ContextPackageManifest? TryGet(CacheKey key)
        => _cache.TryGetValue(key.FullKey, out var manifest) ? manifest : null;

    public void Put(CacheKey key, ContextPackageManifest manifest)
        => _cache[key.FullKey] = manifest;

    public bool TryGetOrBuild(CacheKey key, Func<ContextPackageManifest> builder, out ContextPackageManifest manifest)
    {
        if (_cache.TryGetValue(key.FullKey, out var cached))
        {
            manifest = cached;
            return true;
        }
        manifest = builder();
        _cache[key.FullKey] = manifest;
        return false;
    }

    public void Invalidate(CacheKey key) => _cache.Remove(key.FullKey);
    public void Clear() => _cache.Clear();
    public int Count => _cache.Count;
}
