using CacheHub.Core.Semantic;

namespace CacheHub.Tests;

public class SemanticTests
{
    [Fact]
    public void CosineSimilarity_ShouldReturn1ForIdenticalVectors()
    {
        var v = new float[] { 1, 2, 3 };
        var sim = InMemoryVectorStore.CosineSimilarity(v, v);

        Assert.True(sim > 0.999);
    }

    [Fact]
    public void CosineSimilarity_ShouldReturn0ForOrthogonalVectors()
    {
        var a = new float[] { 1, 0 };
        var b = new float[] { 0, 1 };
        var sim = InMemoryVectorStore.CosineSimilarity(a, b);

        Assert.True(Math.Abs(sim) < 0.001);
    }

    [Fact]
    public void CosineSimilarity_ShouldHandleDifferentLengths()
    {
        var a = new float[] { 1, 2, 3 };
        var b = new float[] { 1, 2 };

        var sim = InMemoryVectorStore.CosineSimilarity(a, b);

        Assert.Equal(0, sim);
    }

    [Fact]
    public void InMemoryVectorStore_AddAndSearch_ShouldReturnResults()
    {
        var store = new InMemoryVectorStore();
        store.Add(CreateReference("ref1", [1.0f, 0.0f, 0.0f]));
        store.Add(CreateReference("ref2", [0.0f, 1.0f, 0.0f]));
        store.Add(CreateReference("ref3", [0.9f, 0.1f, 0.0f]));

        var results = store.Search([1.0f, 0.0f, 0.0f], topK: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("ref1", results[0].Reference.Id);
        Assert.True(results[0].Similarity > results[1].Similarity);
    }

    [Fact]
    public void InMemoryVectorStore_Search_ShouldRespectMinSimilarity()
    {
        var store = new InMemoryVectorStore();
        store.Add(CreateReference("ref1", [1.0f, 0.0f]));
        store.Add(CreateReference("ref2", [0.0f, 1.0f]));

        var results = store.Search([1.0f, 0.0f], topK: 10, minSimilarity: 0.5);

        Assert.Single(results);
        Assert.Equal("ref1", results[0].Reference.Id);
    }

    [Fact]
    public void InMemoryVectorStore_RemoveByWorkspace_ShouldRemoveEntries()
    {
        var store = new InMemoryVectorStore();
        store.Add(CreateReference("ref1", [1, 0], "ws1"));
        store.Add(CreateReference("ref2", [0, 1], "ws2"));

        store.RemoveByWorkspace("ws1");

        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void InMemoryVectorStore_Clear_ShouldRemoveAll()
    {
        var store = new InMemoryVectorStore();
        store.Add(CreateReference("r1", [1, 0]));
        store.Add(CreateReference("r2", [0, 1]));
        store.Clear();

        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void SemanticReference_ShouldStoreMetadata()
    {
        var ref1 = CreateReference("ref1", [1, 0], "ws1");
        ref1 = ref1 with { TaskDescription = "Fix login bug", TaskCompleted = true };

        Assert.Equal("Fix login bug", ref1.TaskDescription);
        Assert.True(ref1.TaskCompleted);
        Assert.Equal(SemanticReferenceType.Task, ref1.Type);
    }

    [Fact]
    public void SemanticSearchResult_ShouldIncludeSource()
    {
        var result = new SemanticSearchResult
        {
            Reference = CreateReference("r1", [1, 0]),
            Similarity = 0.95,
            Source = "semantic",
        };

        Assert.Equal("semantic", result.Source);
        Assert.True(result.Similarity > 0.9);
    }

    private static SemanticReference CreateReference(string id, float[] embedding, string? workspaceId = null) => new()
    {
        Id = id,
        Type = SemanticReferenceType.Task,
        Content = "test content",
        Embedding = embedding,
        ModelVersion = "test-v1",
        CreatedAt = DateTimeOffset.UtcNow,
        WorkspaceId = workspaceId,
    };
}
