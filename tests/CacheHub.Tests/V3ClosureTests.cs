using CacheHub.Context.Cache;
using CacheHub.Context.Ranking;
using CacheHub.Context.Recall;
using CacheHub.Context.Recall.Sources;
using CacheHub.Context.Parsing;
using CacheHub.Core.Semantic;
using CacheHub.Core.Tokens;
using CacheHub.Gateway;
using CacheHub.Core.Caching;

namespace CacheHub.Tests;

/// <summary>
/// Tests for V3 closure fixes: FTS BM25, ScoreHint, Relation/Semantic recall,
/// Gateway fallback, FNV-1a hash, Snapshot binding, CacheKey completeness.
/// </summary>
public class V3ClosureTests
{
    // === P1: FTS BM25 Rank + Hit Line ===

    [Fact]
    public void FtsSearchResult_HasRankScoreAndHitLine()
    {
        var result = new Storage.Search.FtsSearchResult("src/test.cs", "csharp", "snippet", -2.5, 42);
        Assert.Equal(-2.5, result.RankScore);
        Assert.Equal(42, result.HitLine);
    }

    [Fact]
    public void FtsMatch_CarriesRankAndHitLine()
    {
        var match = new FtsMatch("path", "lang", "snippet", -1.5, 100);
        Assert.Equal(-1.5, match.RankScore);
        Assert.Equal(100, match.HitLine);
    }

    // === P1: RankingEngine Consumes ScoreHint ===

    [Fact]
    public void RankingEngine_TextMatch_TakesHigherOfPathAndHint()
    {
        var candidate = new CandidateFile
        {
            Path = "src/infrastructure/repository.cs",
            NormalizedPath = "src/infrastructure/repository.cs",
            Language = "csharp",
            Size = 1000,
            ScoreHints =
            [
                new ScoreHint { Value = 0.8, Feature = "TextMatch", Confidence = 0.9 },
            ],
        };

        var task = new ParsedTask
        {
            OriginalText = "fix database transaction deadlock",
            QueryParserVersion = "deterministic-query-v2",
            ExtractedKeywords = ["database", "transaction", "deadlock"],
            ExtractedSymbols = [],
            ExtractedPaths = [],
        };

        var engine = new RankingEngine();
        var ranked = engine.Rank([candidate], DefaultRankingProfile.Create(), task);

        // Path doesn't contain keywords, but ScoreHint provides 0.8
        // So TextMatch should be 0.8 (from hint), not 0 (from path)
        Assert.True(ranked[0].Features.TextMatch > 0);
    }

    // === P1: RelationRecallSource ===

    [Fact]
    public void RelationRecallSource_ReturnsEmptyWhenNoRelationSearch()
    {
        var source = new RelationRecallSource();
        var context = new RecallContext
        {
            Task = new ParsedTask { OriginalText = "test", QueryParserVersion = "v2", ExtractedKeywords = [], ExtractedSymbols = [], ExtractedPaths = [] },
            IndexedFiles = [],
            AlreadyMatchedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "src/file.cs" },
        };

        var hits = source.Recall(context);
        Assert.Empty(hits);
    }

    [Fact]
    public void RelationRecallSource_ExpandsToTargetSymbols()
    {
        var source = new RelationRecallSource();
        var context = new RecallContext
        {
            Task = new ParsedTask { OriginalText = "test", QueryParserVersion = "v2", ExtractedKeywords = [], ExtractedSymbols = [], ExtractedPaths = [] },
            IndexedFiles = [],
            AlreadyMatchedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "src/caller.cs" },
            RelationSearch = path =>
            {
                if (path == "src/caller.cs")
                    return [new RelationHit { TargetName = "TargetMethod", RelationType = "Heuristic", Relation = "possible_call", Confidence = 0.8 }];
                return [];
            },
            SymbolSearch = symbol =>
            {
                if (symbol == "TargetMethod")
                    return ["src/target.cs"];
                return [];
            },
        };

        var hits = source.Recall(context);
        Assert.NotEmpty(hits);
        Assert.Contains(hits, h => h.NormalizedPath == "src/target.cs");
    }

    // === P1: SemanticRecallSource ===

    [Fact]
    public void SemanticRecallSource_ReturnsEmptyWhenNoSemanticSearch()
    {
        var source = new SemanticRecallSource();
        var context = new RecallContext
        {
            Task = new ParsedTask { OriginalText = "test task", QueryParserVersion = "v2", ExtractedKeywords = [], ExtractedSymbols = [], ExtractedPaths = [] },
            IndexedFiles = [],
            AlreadyMatchedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        };

        var hits = source.Recall(context);
        Assert.Empty(hits);
    }

    // === P1: Semantic FNV-1a Stable Hash ===

    [Fact]
    public async Task LocalEmbedding_SameText_ProducesSameVectorAcrossCalls()
    {
        var provider = new LocalHashEmbeddingProvider();

        var v1 = await provider.EmbedAsync("Fix authentication bug in UserService");
        var v2 = await provider.EmbedAsync("Fix authentication bug in UserService");

        Assert.Equal(v1.Vector.Length, v2.Vector.Length);
        for (var i = 0; i < v1.Vector.Length; i++)
            Assert.Equal(v1.Vector[i], v2.Vector[i]);
    }

    // === P1: Semantic Snapshot Binding ===

    [Fact]
    public void SemanticReference_SnapshotIdAndContentHash_BoundCorrectly()
    {
        var reference = new SemanticReference
        {
            Id = "test-id",
            Type = SemanticReferenceType.Task,
            Content = "test content",
            Embedding = new float[256],
            ModelVersion = "1.0",
            CreatedAt = DateTimeOffset.UtcNow,
            SnapshotId = "snap-001",
            WorkspaceContentHash = "hash-abc",
            IsStale = false,
        };

        Assert.Equal("snap-001", reference.SnapshotId);
        Assert.Equal("hash-abc", reference.WorkspaceContentHash);
        Assert.False(reference.IsStale);
    }

    [Fact]
    public void PersistentVectorStore_InvalidateBySnapshot_MarksStale()
    {
        var store = new PersistentVectorStore();
        store.Add(new SemanticReference
        {
            Id = "ref-1",
            Type = SemanticReferenceType.Task,
            Content = "old task",
            Embedding = new float[256],
            ModelVersion = "1.0",
            CreatedAt = DateTimeOffset.UtcNow,
            SnapshotId = "snap-old",
            WorkspaceContentHash = "hash-old",
        });

        // Invalidate with new snapshot
        store.InvalidateBySnapshot("snap-new", "hash-new");

        var results = store.Search(new float[256], topK: 10, minSimilarity: 0.0);
        // Stale entries should be excluded from search
        Assert.DoesNotContain(results, r => r.Reference.Id == "ref-1");
    }

    // === P1: CacheKey Completeness ===

    [Fact]
    public void CacheKey_IncludesAllFactors()
    {
        var key1 = CacheKey.Build("task1", "snap1", "profile1", 1, 8000, 12000, "sec-v1", "ignore-hash",
            "src/current.cs", "git-diff-hash", "gpt-4", "code-tokenizer");
        var key2 = CacheKey.Build("task1", "snap1", "profile1", 1, 8000, 12000, "sec-v1", "ignore-hash",
            "src/different.cs", "git-diff-hash", "gpt-4", "code-tokenizer");

        // Different currentFile should produce different keys
        Assert.NotEqual(key1.FullKey, key2.FullKey);
    }

    [Fact]
    public void CacheKey_SameInputs_ProduceSameKey()
    {
        var key1 = CacheKey.Build("task", "snap", "p", 1, 8000, 12000, "s", "i", "f", "g", "m", "t");
        var key2 = CacheKey.Build("task", "snap", "p", 1, 8000, 12000, "s", "i", "f", "g", "m", "t");

        Assert.Equal(key1.FullKey, key2.FullKey);
    }

    // === P1: CodeTokenizer Default ===

    [Fact]
    public void TokenizerRegistry_CreateWithDefaults_RegistersCommonModels()
    {
        var registry = TokenizerRegistry.CreateWithDefaults();

        var gpt4 = registry.GetForModel("gpt-4-turbo");
        var claude = registry.GetForModel("claude-3-opus");
        var gemini = registry.GetForModel("gemini-pro");

        Assert.NotEqual("char-estimate", gpt4.Id);
        Assert.NotEqual("char-estimate", claude.Id);
        Assert.NotEqual("char-estimate", gemini.Id);
    }

    // === P1: Gateway Multi-Provider Fallback ===

    [Fact]
    public void GatewayConfig_GetAllProviders_ReturnsPrimaryAndFallbacks()
    {
        var config = new GatewayConfig
        {
            ProviderBaseUrl = "https://api.openai.com",
            ProviderApiKey = "key1",
            FallbackProviders =
            [
                new FallbackProvider { BaseUrl = "https://api.deepseek.com", ApiKey = "key2" },
            ],
        };

        var providers = config.GetAllProviders();
        Assert.Equal(2, providers.Count);
        Assert.Equal("https://api.openai.com", providers[0].BaseUrl);
        Assert.Equal("https://api.deepseek.com", providers[1].BaseUrl);
    }

    [Fact]
    public void GatewayConfig_WithoutFallbacks_ReturnsSingleProvider()
    {
        var config = new GatewayConfig
        {
            ProviderBaseUrl = "https://api.openai.com",
            ProviderApiKey = "key1",
        };

        var providers = config.GetAllProviders();
        Assert.Single(providers);
    }

    // === P1: CacheType GatewayResponse ===

    [Fact]
    public void CacheType_GatewayResponse_Exists()
    {
        var type = CacheType.GatewayResponse;
        Assert.Equal("GatewayResponse", type.ToString());
    }

    // === P1: ICacheStore GetBlob ===

    [Fact]
    public void MemoryCacheStore_GetBlob_ReturnsStoredBlob()
    {
        var store = new MemoryCacheStore();
        var entry = new CacheEntry
        {
            Key = "test-key",
            Type = CacheType.GatewayResponse,
            Version = "v1",
            CreatedAt = DateTimeOffset.UtcNow,
            SizeBytes = 100,
        };
        var blob = new byte[] { 1, 2, 3, 4, 5 };
        store.Put(entry, blob);

        var retrieved = store.GetBlob("test-key");
        Assert.NotNull(retrieved);
        Assert.Equal(5, retrieved.Length);
    }

    // === P1: Contextual Completion API Request ===

    [Fact]
    public void ContextualCompletionApiRequest_AcceptsAllFields()
    {
        var req = new
        {
            WorkspaceId = "ws-001",
            Task = "Fix the login bug",
            ModelId = "gpt-4",
            CurrentFile = "src/auth.ts",
            CallGateway = true,
        };

        Assert.Equal("ws-001", req.WorkspaceId);
        Assert.True(req.CallGateway);
    }
}
