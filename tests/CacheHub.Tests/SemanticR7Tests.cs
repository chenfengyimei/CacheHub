using CacheHub.Core.Semantic;

namespace CacheHub.Tests;

/// <summary>
/// Tests for R7: Semantic Reference module.
/// Local embedding, persistent vector store, workspace isolation, recall.
/// </summary>
public class SemanticR7Tests
{
    [Fact]
    public async Task LocalEmbedding_ProducesCorrectDimensions()
    {
        var provider = new LocalHashEmbeddingProvider();
        var result = await provider.EmbedAsync("Fix the login bug");
        Assert.Equal(256, result.Vector.Length);
        Assert.Equal("1.0", result.ModelVersion);
    }

    [Fact]
    public async Task LocalEmbedding_SimilarTexts_HaveHigherSimilarity()
    {
        var provider = new LocalHashEmbeddingProvider();
        var v1 = await provider.EmbedAsync("Fix the authentication bug in UserService");
        var v2 = await provider.EmbedAsync("Fix the auth bug in UserService");
        var v3 = await provider.EmbedAsync("Random completely different text about cooking");

        var sim12 = InMemoryVectorStore.CosineSimilarity(v1.Vector, v2.Vector);
        var sim13 = InMemoryVectorStore.CosineSimilarity(v1.Vector, v3.Vector);

        Assert.True(sim12 > sim13, "Similar texts should have higher similarity");
    }

    [Fact]
    public async Task LocalEmbedding_EmptyText_ReturnsZeroVector()
    {
        var provider = new LocalHashEmbeddingProvider();
        var result = await provider.EmbedAsync("");
        Assert.All(result.Vector, v => Assert.Equal(0, v));
    }

    [Fact]
    public async Task LocalEmbedding_Batch_ReturnsAllResults()
    {
        var provider = new LocalHashEmbeddingProvider();
        var results = await provider.EmbedBatchAsync(["hello", "world", "test"]);
        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Equal(256, r.Vector.Length));
    }

    [Fact]
    public async Task PersistentVectorStore_AddAndSearch_ReturnsResults()
    {
        var store = new PersistentVectorStore();
        var provider = new LocalHashEmbeddingProvider();
        var embedding = await provider.EmbedAsync("Fix login bug");

        store.Add(new SemanticReference
        {
            Id = "ref-1",
            Type = SemanticReferenceType.Task,
            Content = "Fix login bug in AuthService",
            Embedding = embedding.Vector,
            ModelVersion = "1.0",
            CreatedAt = DateTimeOffset.UtcNow,
            WorkspaceId = "ws-1",
            TaskDescription = "Fix login",
            TaskCompleted = true,
        });

        var results = store.Search(embedding.Vector, topK: 5, minSimilarity: 0.0, workspaceId: "ws-1");
        Assert.NotEmpty(results);
        Assert.Equal("ref-1", results[0].Reference.Id);
        Assert.True(results[0].Similarity > 0.9);
    }

    [Fact]
    public async Task PersistentVectorStore_WorkspaceIsolation_PreventsCrossLeakage()
    {
        var store = new PersistentVectorStore();
        var provider = new LocalHashEmbeddingProvider();

        var embedding = await provider.EmbedAsync("Fix auth");
        store.Add(new SemanticReference
        {
            Id = "ws-a-ref",
            Type = SemanticReferenceType.Task,
            Content = "Fix auth in workspace A",
            Embedding = embedding.Vector,
            ModelVersion = "1.0",
            CreatedAt = DateTimeOffset.UtcNow,
            WorkspaceId = "ws-a",
        });

        // Search in workspace B — should find nothing
        var results = store.Search(embedding.Vector, topK: 5, minSimilarity: 0.0, workspaceId: "ws-b");
        Assert.Empty(results);

        // Search in workspace A — should find the entry
        results = store.Search(embedding.Vector, topK: 5, minSimilarity: 0.0, workspaceId: "ws-a");
        Assert.NotEmpty(results);
    }

    [Fact]
    public async Task PersistentVectorStore_RemoveByWorkspace_DeletesEntries()
    {
        var store = new PersistentVectorStore();
        var provider = new LocalHashEmbeddingProvider();
        var embedding = await provider.EmbedAsync("test");

        store.Add(new SemanticReference
        {
            Id = "r1",
            Type = SemanticReferenceType.Task,
            Content = "test",
            Embedding = embedding.Vector,
            ModelVersion = "1.0",
            CreatedAt = DateTimeOffset.UtcNow,
            WorkspaceId = "ws-x",
        });

        Assert.Equal(1, store.CountByWorkspace("ws-x"));
        store.RemoveByWorkspace("ws-x");
        Assert.Equal(0, store.CountByWorkspace("ws-x"));
    }

    [Fact]
    public async Task SemanticReferenceRecall_RecordsAndRecalls()
    {
        var store = new PersistentVectorStore();
        var provider = new LocalHashEmbeddingProvider();
        var recall = new SemanticReferenceRecall(store, provider);

        await recall.RecordAsync("Fix authentication bug in AuthService", SemanticReferenceType.Task,
            workspaceId: "ws-1", taskDescription: "Fix auth", taskCompleted: true);

        var results = await recall.RecallAsync("Fix auth bug", workspaceId: "ws-1", topK: 5, minSimilarity: 0.0);
        Assert.NotEmpty(results);
        Assert.Contains("auth", results[0].Reference.Content.ToLowerInvariant());
    }

    [Fact]
    public async Task SemanticReferenceRecall_NoCrossWorkspaceLeakage()
    {
        var store = new PersistentVectorStore();
        var provider = new LocalHashEmbeddingProvider();
        var recall = new SemanticReferenceRecall(store, provider);

        await recall.RecordAsync("Secret workspace task", SemanticReferenceType.Task, workspaceId: "ws-secret");

        var results = await recall.RecallAsync("Secret workspace task", workspaceId: "ws-other", topK: 5);
        Assert.Empty(results);
    }

    [Fact]
    public async Task PersistentVectorStore_InvalidateByModelVersion_RemovesOldEntries()
    {
        var store = new PersistentVectorStore();
        var provider = new LocalHashEmbeddingProvider();
        var embedding = await provider.EmbedAsync("test");

        store.Add(new SemanticReference
        {
            Id = "old",
            Type = SemanticReferenceType.Task,
            Content = "old",
            Embedding = embedding.Vector,
            ModelVersion = "0.9",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        store.Add(new SemanticReference
        {
            Id = "new",
            Type = SemanticReferenceType.Task,
            Content = "new",
            Embedding = embedding.Vector,
            ModelVersion = "1.0",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        store.InvalidateByModelVersion("1.0");
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public async Task PersistentVectorStore_Persistence_CreatesFile()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"cachehub_semantic_{Guid.NewGuid():N}.json");
        try
        {
            var provider = new LocalHashEmbeddingProvider();
            var embedding = await provider.EmbedAsync("persistence test");

            using (var store = new PersistentVectorStore(tempPath))
            {
                store.Add(new SemanticReference
                {
                    Id = "persist-1",
                    Type = SemanticReferenceType.Task,
                    Content = "persistence test",
                    Embedding = embedding.Vector,
                    ModelVersion = "1.0",
                    CreatedAt = DateTimeOffset.UtcNow,
                });
            }

            Assert.True(File.Exists(tempPath));
            var content = File.ReadAllText(tempPath);
            Assert.Contains("persist-1", content);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }
}
