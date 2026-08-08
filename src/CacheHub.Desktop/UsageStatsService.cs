namespace CacheHub.Desktop;

/// <summary>
/// In-memory usage statistics tracker for Dashboard display.
/// Records model/gateway usage metrics from the contextual-completion workflow.
/// V7-W12: Separates Estimated Context Tokens Saved from Actual Cache Tokens Avoided.
/// </summary>
public sealed class UsageStatsService
{
    private int _totalRequests;
    private int _cacheHits;
    private long _totalPromptTokens;
    private long _totalCompletionTokens;
    private long _estimatedContextSaved;  // V7-W12: estimated baseline - actual prompt
    private long _actualCacheTokensSaved;  // V7-W12: tokens saved by Gateway exact cache hit
    private long _totalLatencyMs;
    private int _latencyCount;

    public void RecordRequest(int promptTokens, int completionTokens, bool cacheHit, int tokensSaved, long latencyMs)
    {
        Interlocked.Increment(ref _totalRequests);
        if (cacheHit) Interlocked.Increment(ref _cacheHits);
        Interlocked.Add(ref _totalPromptTokens, promptTokens);
        Interlocked.Add(ref _totalCompletionTokens, completionTokens);
        Interlocked.Add(ref _estimatedContextSaved, tokensSaved);
        // V7-W12: When cache hits, the saved tokens = the full prompt tokens (Gateway served from cache)
        if (cacheHit)
            Interlocked.Add(ref _actualCacheTokensSaved, promptTokens);
        Interlocked.Add(ref _totalLatencyMs, latencyMs);
        Interlocked.Increment(ref _latencyCount);
    }

    public UsageStatsSnapshot GetStats()
    {
        var req = Interlocked.CompareExchange(ref _totalRequests, 0, 0);
        var hits = Interlocked.CompareExchange(ref _cacheHits, 0, 0);
        var lat = Interlocked.CompareExchange(ref _totalLatencyMs, 0, 0);
        var latCount = Interlocked.CompareExchange(ref _latencyCount, 0, 0);

        return new UsageStatsSnapshot(
            TotalRequests: req,
            CacheHits: hits,
            CacheHitRate: req > 0 ? (double)hits / req : 0,
            TotalPromptTokens: Interlocked.CompareExchange(ref _totalPromptTokens, 0, 0),
            TotalCompletionTokens: Interlocked.CompareExchange(ref _totalCompletionTokens, 0, 0),
            EstimatedContextSaved: Interlocked.CompareExchange(ref _estimatedContextSaved, 0, 0),
            ActualCacheTokensSaved: Interlocked.CompareExchange(ref _actualCacheTokensSaved, 0, 0),
            AvgLatencyMs: latCount > 0 ? (double)lat / latCount : 0);
    }
}

public sealed record UsageStatsSnapshot(
    int TotalRequests,
    int CacheHits,
    double CacheHitRate,
    long TotalPromptTokens,
    long TotalCompletionTokens,
    long EstimatedContextSaved,
    long ActualCacheTokensSaved,
    double AvgLatencyMs);
