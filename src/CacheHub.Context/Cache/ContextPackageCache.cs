using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CacheHub.Core.Caching;
using CacheHub.Core.Context;

namespace CacheHub.Context.Cache;

/// <summary>
/// Cache key components for a Context Package.
/// Includes all factors that affect context output: task, snapshot, profile, budget,
/// security, ignore rules, current file, git diff, model, and tokenizer.
/// </summary>
public sealed record CacheKey
{
    public required string TaskHash { get; init; }
    public required string SnapshotHash { get; init; }
    public required string ProfileHash { get; init; }
    public required string BudgetHash { get; init; }
    public required string SecurityHash { get; init; }
    public required string ContextHash { get; init; }

    public string FullKey => $"{TaskHash}|{SnapshotHash}|{ProfileHash}|{BudgetHash}|{SecurityHash}|{ContextHash}";

    public static CacheKey Build(
        string task,
        string indexSnapshotId,
        string rankingProfileId,
        int rankingProfileVersion,
        int contextTarget,
        int contextHardLimit,
        string? securityPolicyVersion,
        string? ignoreRulesHash,
        string? currentFile = null,
        string? gitDiffHash = null,
        string? modelId = null,
        string? tokenizerId = null)
    {
        var taskHash = Hash(task);
        var snapshotHash = Hash(indexSnapshotId);
        var profileHash = Hash($"{rankingProfileId}:{rankingProfileVersion}");
        var budgetHash = Hash($"{contextTarget}:{contextHardLimit}");
        var securityHash = Hash($"{securityPolicyVersion ?? "none"}|{ignoreRulesHash ?? "none"}");
        var contextHash = Hash($"{currentFile ?? "none"}|{gitDiffHash ?? "none"}|{modelId ?? "none"}|{tokenizerId ?? "none"}");

        return new CacheKey
        {
            TaskHash = taskHash,
            SnapshotHash = snapshotHash,
            ProfileHash = profileHash,
            BudgetHash = budgetHash,
            SecurityHash = securityHash,
            ContextHash = contextHash,
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
/// Optional ICacheStore backend enables cross-process persistence (CLI cache key survives restart).
/// </summary>
public sealed class ContextPackageCache
{
    private readonly Dictionary<string, ContextPackageManifest> _cache = new();
    private readonly ICacheStore? _persistentStore;

    public ContextPackageCache(ICacheStore? persistentStore = null)
    {
        _persistentStore = persistentStore;
    }

    public ContextPackageManifest? TryGet(CacheKey key)
    {
        // Layer 1: in-memory
        if (_cache.TryGetValue(key.FullKey, out var manifest))
            return manifest;

        // Layer 2: persistent store (survives process restart)
        if (_persistentStore is not null)
        {
            var entry = _persistentStore.TryGet(key.FullKey, CacheType.Context);
            if (entry is not null)
            {
                var blob = _persistentStore.GetBlob(key.FullKey);
                if (blob is not null && blob.Length > 0)
                {
                    try
                    {
                        var json = Encoding.UTF8.GetString(blob);
                        var restored = JsonSerializer.Deserialize<ContextPackageManifest>(json);
                        if (restored is not null)
                        {
                            // Backfill in-memory cache for subsequent lookups
                            _cache[key.FullKey] = restored;
                            return restored;
                        }
                    }
                    catch { /* corrupt entry — fall through to miss */ }
                }
            }
        }

        return null;
    }

    public void Put(CacheKey key, ContextPackageManifest manifest)
    {
        _cache[key.FullKey] = manifest;

        // Persist to store if available
        if (_persistentStore is not null)
        {
            try
            {
                var json = JsonSerializer.SerializeToUtf8Bytes(manifest);
                _persistentStore.Put(new CacheEntry
                {
                    Key = key.FullKey,
                    Type = CacheType.Context,
                    Version = "v1",
                    CreatedAt = DateTimeOffset.UtcNow,
                    SizeBytes = json.Length,
                    ProducerVersion = "schema-v" + manifest.SchemaVersion,
                }, json);
            }
            catch { /* best effort — don't fail the build if cache persistence fails */ }
        }
    }

    public bool TryGetOrBuild(CacheKey key, Func<ContextPackageManifest> builder, out ContextPackageManifest manifest)
    {
        if (TryGet(key) is { } cached)
        {
            manifest = cached;
            return true;
        }
        manifest = builder();
        Put(key, manifest);
        return false;
    }

    public void Invalidate(CacheKey key)
    {
        _cache.Remove(key.FullKey);
        _persistentStore?.InvalidateByDependency(key.FullKey);
    }

    public void Clear()
    {
        _cache.Clear();
        _persistentStore?.InvalidateType(CacheType.Context);
    }

    public int Count => _cache.Count;
}
