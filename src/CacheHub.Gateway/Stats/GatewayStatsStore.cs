using Microsoft.Data.Sqlite;

namespace CacheHub.Gateway.Stats;

/// <summary>
/// V7-W14: Persistent Gateway usage stats store.
/// Gateway writes request stats to a SQLite file that Desktop Dashboard can read.
/// Enables cross-process stats visibility: Codex → Gateway → stats.db → Desktop Dashboard.
/// </summary>
public sealed class GatewayStatsStore : IDisposable
{
    private readonly string _connectionString;
    private readonly object _lock = new();
    private bool _initialized;

    public GatewayStatsStore(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _connectionString = $"Data Source={dbPath}";
        Initialize();
    }

    private void Initialize()
    {
        lock (_lock)
        {
            if (_initialized) return;
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS gateway_stats (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    timestamp TEXT NOT NULL,
                    endpoint TEXT NOT NULL,
                    model TEXT,
                    status_code INTEGER NOT NULL,
                    cached INTEGER NOT NULL DEFAULT 0,
                    streaming INTEGER NOT NULL DEFAULT 0,
                    prompt_tokens INTEGER NOT NULL DEFAULT 0,
                    completion_tokens INTEGER NOT NULL DEFAULT 0,
                    latency_ms INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS idx_gw_stats_ts ON gateway_stats(timestamp);
                """;
            cmd.ExecuteNonQuery();
            _initialized = true;
        }
    }

    /// <summary>
    /// Records a single gateway request to the persistent store.
    /// </summary>
    public void RecordRequest(string endpoint, string? model, int statusCode, bool cached,
        bool streaming, int promptTokens, int completionTokens, long latencyMs)
    {
        lock (_lock)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO gateway_stats (timestamp, endpoint, model, status_code, cached, streaming, prompt_tokens, completion_tokens, latency_ms)
                VALUES ($ts, $ep, $model, $status, $cached, $streaming, $pt, $ct, $lat);
                """;
            cmd.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$ep", endpoint);
            cmd.Parameters.AddWithValue("$model", (object?)model ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$status", statusCode);
            cmd.Parameters.AddWithValue("$cached", cached ? 1 : 0);
            cmd.Parameters.AddWithValue("$streaming", streaming ? 1 : 0);
            cmd.Parameters.AddWithValue("$pt", promptTokens);
            cmd.Parameters.AddWithValue("$ct", completionTokens);
            cmd.Parameters.AddWithValue("$lat", latencyMs);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Gets aggregated stats from the persistent store.
    /// </summary>
    public PersistentGatewayStats GetAggregatedStats()
    {
        lock (_lock)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT
                    COUNT(*) as total_requests,
                    SUM(CASE WHEN cached = 1 THEN 1 ELSE 0 END) as cache_hits,
                    SUM(CASE WHEN cached = 0 THEN 1 ELSE 0 END) as cache_misses,
                    SUM(prompt_tokens) as total_prompt_tokens,
                    SUM(completion_tokens) as total_completion_tokens,
                    SUM(CASE WHEN cached = 1 THEN prompt_tokens ELSE 0 END) as cached_tokens_saved,
                    AVG(latency_ms) as avg_latency_ms
                FROM gateway_stats;
                """;
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                var total = reader.IsDBNull(0) ? 0 : reader.GetInt64(0);
                var hits = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
                var misses = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
                var promptTokens = reader.IsDBNull(3) ? 0 : reader.GetInt64(3);
                var completionTokens = reader.IsDBNull(4) ? 0 : reader.GetInt64(4);
                var cachedSaved = reader.IsDBNull(5) ? 0 : reader.GetInt64(5);
                var avgLatency = reader.IsDBNull(6) ? 0.0 : reader.GetDouble(6);

                return new PersistentGatewayStats
                {
                    TotalRequests = total,
                    CacheHits = hits,
                    CacheMisses = misses,
                    CacheHitRate = total > 0 ? (double)hits / total : 0,
                    TotalPromptTokens = promptTokens,
                    TotalCompletionTokens = completionTokens,
                    CachedTokensSaved = cachedSaved,
                    AvgLatencyMs = avgLatency,
                };
            }
            return new PersistentGatewayStats();
        }
    }

    public void Dispose()
    {
        // SQLite connections are opened/closed per operation, nothing to dispose
    }
}

/// <summary>
/// Aggregated stats read from the persistent store.
/// </summary>
public sealed record PersistentGatewayStats
{
    public long TotalRequests { get; init; }
    public long CacheHits { get; init; }
    public long CacheMisses { get; init; }
    public double CacheHitRate { get; init; }
    public long TotalPromptTokens { get; init; }
    public long TotalCompletionTokens { get; init; }
    public long CachedTokensSaved { get; init; }
    public double AvgLatencyMs { get; init; }
}
