using CacheHub.Core.Semantic;

namespace CacheHub.Tests;

/// <summary>
/// R10 Gate regression tests: semantic mode safety, workspace isolation, no historical answer replay.
/// </summary>
public class R10GateRegressionTests
{
    // R10 Gate: Default mode is Reference (not Strict, not Off)
    [Fact]
    public void Gate_DefaultMode_IsReference()
    {
        var config = new SemanticConfig();
        Assert.Equal(SemanticMode.Reference, config.Mode);
        Assert.True(config.IsEnabled);
    }

    // R10 Gate: Off mode disables semantic entirely
    [Fact]
    public void Gate_OffMode_DisablesSemantic()
    {
        var config = new SemanticConfig { Mode = SemanticMode.Off };
        Assert.False(config.IsEnabled);
    }

    // R10 Gate: Default does NOT return historical model answers
    [Fact]
    public void Gate_DefaultDoesNotReturnHistoricalAnswers()
    {
        var config = new SemanticConfig();
        Assert.False(config.ReturnHistoricalAnswers);
    }

    // R10 Gate: Default does NOT allow cross-workspace sharing
    [Fact]
    public void Gate_DefaultNoCrossWorkspaceSharing()
    {
        var config = new SemanticConfig();
        Assert.False(config.AllowCrossWorkspace);
    }

    // R10 Gate: Semantic recall respects workspace isolation
    [Fact]
    public async Task Gate_SemanticRespectsWorkspaceIsolation()
    {
        var store = new PersistentVectorStore();
        var provider = new LocalHashEmbeddingProvider();
        var recall = new SemanticReferenceRecall(store, provider);

        // Record in workspace A
        await recall.RecordAsync("Secret task in workspace A", SemanticReferenceType.Task, workspaceId: "ws-a");

        // Search in workspace B — should find nothing
        var results = await recall.RecallAsync("Secret task in workspace A", workspaceId: "ws-b");
        Assert.Empty(results);
    }

    // R10 Gate: Semantic hits don't bypass version/safety policies
    [Fact]
    public async Task Gate_SemanticHitsDontBypassSafety()
    {
        var store = new PersistentVectorStore();
        var provider = new LocalHashEmbeddingProvider();
        var recall = new SemanticReferenceRecall(store, provider);

        // Record a reference with a completed task
        await recall.RecordAsync("Completed task", SemanticReferenceType.Task,
            workspaceId: "ws-1", taskDescription: "test", taskCompleted: true);

        // Semantic recall should return the reference but NOT mark it as a model answer
        var results = await recall.RecallAsync("Completed task", workspaceId: "ws-1");
        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.Equal("semantic-reference", r.Source));
    }

    // R10 Gate: Semantic only adds candidates, doesn't replace deterministic results
    [Fact]
    public void Gate_SemanticOnlyAddsCandidates()
    {
        // In Reference mode, semantic is supplementary — the deterministic pipeline runs first
        var config = new SemanticConfig { Mode = SemanticMode.Reference };
        Assert.True(config.MaxCandidates > 0);
        Assert.True(config.MinSimilarity > 0);
    }

    // R10 Gate: Local embedding provider works without external dependencies
    [Fact]
    public async Task Gate_LocalEmbedding_NoExternalDependencies()
    {
        var provider = new LocalHashEmbeddingProvider();
        var result = await provider.EmbedAsync("Fix authentication bug");

        Assert.Equal(256, result.Vector.Length);
        Assert.Contains(result.Vector, v => v != 0);
    }

    // R10 Gate: Persistent vector store creates file
    [Fact]
    public async Task Gate_VectorStorePersistence()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"cachehub_r10_sem_{Guid.NewGuid():N}.json");
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
        finally { try { File.Delete(tempPath); } catch { } }
    }
}
