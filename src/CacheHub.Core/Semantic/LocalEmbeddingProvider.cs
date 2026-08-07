using CacheHub.Core.Semantic;

namespace CacheHub.Core.Semantic;

/// <summary>
/// Local deterministic embedding provider: uses character-level hashing
/// to produce fixed-dimensional vectors. No external model dependency.
/// R7-W001: Local embedding provider with version management.
///
/// This is a baseline provider — it captures lexical similarity (shared character n-grams)
/// rather than deep semantic similarity. It's suitable as a reference-only recall signal.
/// </summary>
public sealed class LocalHashEmbeddingProvider : IEmbeddingProvider
{
    public string Id => "local-hash-embedding";
    public string Version => "1.0";
    public int Dimensions => 256;

    private const int NumBuckets = 256;

    public Task<EmbeddingResult> EmbedAsync(string text, CancellationToken ct = default)
    {
        var vector = ComputeEmbedding(text);
        return Task.FromResult(new EmbeddingResult
        {
            Text = text,
            Vector = vector,
            ModelVersion = Version,
        });
    }

    public Task<IReadOnlyList<EmbeddingResult>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var results = texts.Select(text => new EmbeddingResult
        {
            Text = text,
            Vector = ComputeEmbedding(text),
            ModelVersion = Version,
        }).ToList();

        return Task.FromResult<IReadOnlyList<EmbeddingResult>>(results);
    }

    /// <summary>
    /// Computes a 256-dimensional embedding using character n-gram hashing.
    /// Each n-gram maps to a bucket; values are accumulated and L2-normalized.
    /// Uses FNV-1a hash for cross-process stability (GetHashCode is not stable).
    /// </summary>
    private float[] ComputeEmbedding(string text)
    {
        var vector = new float[NumBuckets];

        if (string.IsNullOrEmpty(text))
            return vector;

        // Character bigrams
        for (var i = 0; i < text.Length - 1; i++)
        {
            var bigram = ((int)text[i] << 16) | (int)text[i + 1];
            var bucket = (bigram % NumBuckets + NumBuckets) % NumBuckets;
            vector[bucket] += 1.0f;
        }

        // Word-level trigrams for code identifiers
        var words = text.Split([' ', '\t', '\n', '\r', '.', ',', ';', '(', ')', '{', '}', '/', '\\', ':'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            if (word.Length < 2) continue;
            var hash = StableHashFnv1a(word);
            var bucket = (int)((hash % (uint)NumBuckets + (uint)NumBuckets) % (uint)NumBuckets);
            vector[bucket] += 0.5f;
        }

        // L2 normalize
        var norm = Math.Sqrt(vector.Sum(v => (double)v * v));
        if (norm > 0)
        {
            for (var i = 0; i < NumBuckets; i++)
                vector[i] = (float)(vector[i] / norm);
        }

        return vector;
    }

    /// <summary>
    /// FNV-1a hash: stable across processes and platforms.
    /// Replaces string.GetHashCode() which is randomized per process in .NET.
    /// Uses unchecked arithmetic — overflow is expected and part of the algorithm.
    /// </summary>
    private static uint StableHashFnv1a(string text)
    {
        const uint fnvOffset = 2166136261u;
        const uint fnvPrime = 16777619u;
        var hash = fnvOffset;
        foreach (var c in text)
        {
            hash = unchecked((hash ^ (uint)c) * fnvPrime);
        }
        return hash;
    }
}

/// <summary>
/// Persistent vector store with workspace isolation.
/// R7-W002: persistent vector index, workspace isolation, deletion, and invalidation.
/// Uses an in-memory store with optional persistence to a file.
/// </summary>
public sealed class PersistentVectorStore : IDisposable
{
    private readonly List<SemanticReference> _entries = [];
    private readonly Lock _lock = new();
    private readonly string? _persistPath;
    private bool _disposed;

    public PersistentVectorStore(string? persistPath = null)
    {
        _persistPath = persistPath;
        Load();
    }

    public void Add(SemanticReference entry)
    {
        lock (_lock)
        {
            _entries.Add(entry);
            Save();
        }
    }

    public void AddRange(IEnumerable<SemanticReference> entries)
    {
        lock (_lock)
        {
            _entries.AddRange(entries);
            Save();
        }
    }

    /// <summary>
    /// Searches for similar entries, optionally filtered by workspace.
    /// </summary>
    public IReadOnlyList<SemanticSearchResult> Search(
        float[] query,
        int topK = 5,
        double minSimilarity = 0.0,
        string? workspaceId = null)
    {
        lock (_lock)
        {
            var filtered = workspaceId is not null
                ? _entries.Where(e => e.WorkspaceId == workspaceId)
                : _entries;

            // Exclude stale entries (snapshot superseded or content hash changed)
            filtered = filtered.Where(e => !e.IsStale);

            return filtered
                .Select(e => new SemanticSearchResult
                {
                    Reference = e,
                    Similarity = InMemoryVectorStore.CosineSimilarity(query, e.Embedding),
                    Source = "semantic-reference",
                })
                .Where(r => r.Similarity >= minSimilarity)
                .OrderByDescending(r => r.Similarity)
                .Take(topK)
                .ToList();
        }
    }

    public void RemoveByWorkspace(string workspaceId)
    {
        lock (_lock)
        {
            _entries.RemoveAll(e => e.WorkspaceId == workspaceId);
            Save();
        }
    }

    public void RemoveById(string id)
    {
        lock (_lock)
        {
            _entries.RemoveAll(e => e.Id == id);
            Save();
        }
    }

    /// <summary>
    /// Invalidates entries older than the specified model version.
    /// </summary>
    public void InvalidateByModelVersion(string minVersion)
    {
        lock (_lock)
        {
            _entries.RemoveAll(e => string.Compare(e.ModelVersion, minVersion, StringComparison.Ordinal) < 0);
            Save();
        }
    }

    /// <summary>
    /// Marks entries as stale when the snapshot has been superseded or content hash changed.
    /// Stale entries are excluded from search results.
    /// </summary>
    public void InvalidateBySnapshot(string? currentSnapshotId, string? currentContentHash)
    {
        lock (_lock)
        {
            for (var i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                var stale = false;

                // If entry was bound to a snapshot and it's different now, mark stale
                if (!string.IsNullOrEmpty(entry.SnapshotId) &&
                    !string.IsNullOrEmpty(currentSnapshotId) &&
                    !string.Equals(entry.SnapshotId, currentSnapshotId, StringComparison.OrdinalIgnoreCase))
                {
                    stale = true;
                }

                // If entry was bound to a content hash and it's different now, mark stale
                if (!stale &&
                    !string.IsNullOrEmpty(entry.WorkspaceContentHash) &&
                    !string.IsNullOrEmpty(currentContentHash) &&
                    !string.Equals(entry.WorkspaceContentHash, currentContentHash, StringComparison.OrdinalIgnoreCase))
                {
                    stale = true;
                }

                if (stale)
                {
                    _entries[i] = entry with { IsStale = true };
                }
            }
            Save();
        }
    }

    public int Count
    {
        get { lock (_lock) return _entries.Count; }
    }

    public int CountByWorkspace(string workspaceId)
    {
        lock (_lock) return _entries.Count(e => e.WorkspaceId == workspaceId);
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
            Save();
        }
    }

    private void Save()
    {
        if (_persistPath is null) return;
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(_entries.Select(e => new
            {
                e.Id,
                Type = e.Type.ToString(),
                e.Content,
                e.Embedding,
                e.ModelVersion,
                e.CreatedAt,
                e.WorkspaceId,
                e.TaskDescription,
                e.TaskCompleted,
                e.SnapshotId,
                e.WorkspaceContentHash,
                e.IsStale,
            }));
            File.WriteAllText(_persistPath, json);
        }
        catch { }
    }

    private void Load()
    {
        if (_persistPath is null || !File.Exists(_persistPath)) return;
        try
        {
            // Simplified load — in production, use proper deserialization
            var json = File.ReadAllText(_persistPath);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var typeStr = item.TryGetProperty("Type", out var t) ? t.GetString() : "Task";
                var type = Enum.TryParse<SemanticReferenceType>(typeStr, out var parsed) ? parsed : SemanticReferenceType.Task;
                var embedding = item.TryGetProperty("Embedding", out var emb) && emb.ValueKind == System.Text.Json.JsonValueKind.Array
                    ? emb.EnumerateArray().Select(v => v.GetSingle()).ToArray()
                    : [];
                _entries.Add(new SemanticReference
                {
                    Id = item.TryGetProperty("Id", out var id) ? id.GetString() ?? "" : "",
                    Type = type,
                    Content = item.TryGetProperty("Content", out var c) ? c.GetString() ?? "" : "",
                    Embedding = embedding,
                    ModelVersion = item.TryGetProperty("ModelVersion", out var mv) ? mv.GetString() ?? "1.0" : "1.0",
                    CreatedAt = item.TryGetProperty("CreatedAt", out var ca) ? ca.GetDateTimeOffset() : DateTimeOffset.UtcNow,
                    WorkspaceId = item.TryGetProperty("WorkspaceId", out var ws) ? ws.GetString() : null,
                    TaskDescription = item.TryGetProperty("TaskDescription", out var td) ? td.GetString() : null,
                    TaskCompleted = item.TryGetProperty("TaskCompleted", out var tc) ? tc.GetBoolean() : null,
                    SnapshotId = item.TryGetProperty("SnapshotId", out var si) ? si.GetString() : null,
                    WorkspaceContentHash = item.TryGetProperty("WorkspaceContentHash", out var wch) ? wch.GetString() : null,
                    IsStale = item.TryGetProperty("IsStale", out var isv) && isv.GetBoolean(),
                });
            }
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Save();
    }
}

/// <summary>
/// Semantic reference recall: retrieves historical task/error/feedback references
/// and marks them as semantic-reference for the context engine.
/// R7-W003: historical task/error/context/feedback recall with semantic-reference tag.
/// </summary>
public sealed class SemanticReferenceRecall
{
    private readonly PersistentVectorStore _store;
    private readonly IEmbeddingProvider _embedding;

    public SemanticReferenceRecall(PersistentVectorStore store, IEmbeddingProvider embedding)
    {
        _store = store;
        _embedding = embedding;
    }

    /// <summary>
    /// Recalls similar historical references for a given query text.
    /// Only used as reference — does not directly reuse old answers.
    /// </summary>
    public async Task<IReadOnlyList<SemanticSearchResult>> RecallAsync(
        string queryText,
        string? workspaceId = null,
        int topK = 5,
        double minSimilarity = 0.1)
    {
        var embedding = await _embedding.EmbedAsync(queryText);
        return _store.Search(embedding.Vector, topK, minSimilarity, workspaceId);
    }

    /// <summary>
    /// Records a historical reference for future recall.
    /// </summary>
    public async Task RecordAsync(
        string content,
        SemanticReferenceType type,
        string? workspaceId = null,
        string? taskDescription = null,
        bool? taskCompleted = null,
        string? snapshotId = null,
        string? workspaceContentHash = null)
    {
        var embedding = await _embedding.EmbedAsync(content);
        _store.Add(new SemanticReference
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = type,
            Content = content,
            Embedding = embedding.Vector,
            ModelVersion = _embedding.Version,
            CreatedAt = DateTimeOffset.UtcNow,
            WorkspaceId = workspaceId,
            TaskDescription = taskDescription,
            TaskCompleted = taskCompleted,
            SnapshotId = snapshotId,
            WorkspaceContentHash = workspaceContentHash,
        });
    }
}
