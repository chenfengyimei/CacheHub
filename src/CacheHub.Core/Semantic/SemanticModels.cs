using System.Text.Json.Serialization;

namespace CacheHub.Core.Semantic;

/// <summary>
/// Embedding provider contract: local or remote, versioned.
/// </summary>
public interface IEmbeddingProvider
{
    string Id { get; }
    string Version { get; }
    int Dimensions { get; }
    Task<EmbeddingResult> EmbedAsync(string text, CancellationToken ct = default);
    Task<IReadOnlyList<EmbeddingResult>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}

/// <summary>
/// A single embedding result.
/// </summary>
public sealed record EmbeddingResult
{
    public required string Text { get; init; }
    public required float[] Vector { get; init; }
    public required string ModelVersion { get; init; }
}

/// <summary>
/// Type of semantic reference.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SemanticReferenceType
{
    Task,
    Error,
    ContextPackage,
    Feedback,
}

/// <summary>
/// A semantic reference entry in the history store.
/// </summary>
public sealed record SemanticReference
{
    public required string Id { get; init; }
    public required SemanticReferenceType Type { get; init; }
    public required string Content { get; init; }
    public required float[] Embedding { get; init; }
    public required string ModelVersion { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public string? WorkspaceId { get; init; }
    public string? TaskDescription { get; init; }
    public bool? TaskCompleted { get; init; }
}

/// <summary>
/// Semantic search result with similarity score.
/// </summary>
public sealed record SemanticSearchResult
{
    public required SemanticReference Reference { get; init; }
    public required double Similarity { get; init; }
    public required string Source { get; init; }
}

/// <summary>
/// Simple in-memory vector store with cosine similarity.
/// </summary>
public sealed class InMemoryVectorStore
{
    private readonly List<SemanticReference> _entries = [];
    private readonly Lock _lock = new();

    public void Add(SemanticReference entry)
    {
        lock (_lock) { _entries.Add(entry); }
    }

    public void AddRange(IEnumerable<SemanticReference> entries)
    {
        lock (_lock) { _entries.AddRange(entries); }
    }

    public IReadOnlyList<SemanticSearchResult> Search(float[] query, int topK = 5, double minSimilarity = 0.0)
    {
        lock (_lock)
        {
            return _entries
                .Select(e => new SemanticSearchResult
                {
                    Reference = e,
                    Similarity = CosineSimilarity(query, e.Embedding),
                    Source = "semantic",
                })
                .Where(r => r.Similarity >= minSimilarity)
                .OrderByDescending(r => r.Similarity)
                .Take(topK)
                .ToList();
        }
    }

    public void Clear()
    {
        lock (_lock) { _entries.Clear(); }
    }

    public int Count
    {
        get { lock (_lock) return _entries.Count; }
    }

    public void RemoveByWorkspace(string workspaceId)
    {
        lock (_lock)
        {
            _entries.RemoveAll(e => e.WorkspaceId == workspaceId);
        }
    }

    /// <summary>
    /// Cosine similarity between two vectors.
    /// </summary>
    public static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;

        double dot = 0, magA = 0, magB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        var denom = Math.Sqrt(magA) * Math.Sqrt(magB);
        return denom > 0 ? dot / denom : 0;
    }
}
