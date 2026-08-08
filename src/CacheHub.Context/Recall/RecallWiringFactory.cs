using CacheHub.Core.Identifiers;
using CacheHub.Storage.Database;
using CacheHub.Storage.Query;

namespace CacheHub.Context.Recall;

/// <summary>
/// V7-W06: Unified recall callback wiring factory.
/// Eliminates duplicated callback assembly across CLI/Workflow/Desktop/Benchmark.
/// Ensures reverseRelationSearch and fileSymbolsProvider are always wired.
/// </summary>
public sealed class RecallWiringFactory
{
    private readonly SqliteConnectionFactory _factory;

    public RecallWiringFactory(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Creates all standard recall callbacks for a given snapshot.
    /// Returns FTS, Symbol, Import, SymbolDetailed, Relation, ReverseRelation, FileSymbols callbacks.
    /// Does NOT include semanticSearch (caller-specific, depends on SemanticReferenceHelper).
    /// </summary>
    public RecallCallbacks Create(IndexSnapshotId snapshotId)
    {
        var querySvc = new SqliteIndexQueryService(_factory);

        return new RecallCallbacks
        {
            FtsSearch = keyword =>
            {
                var results = querySvc.SearchFtsAsync(snapshotId, keyword, 50).GetAwaiter().GetResult();
                return results.Select(r => new FtsMatch(r.Path, r.Language, r.Snippet, r.RankScore, r.HitLine)).ToList();
            },
            SymbolSearch = symbol =>
            {
                var results = querySvc.SearchSymbolsAsync(snapshotId, symbol).GetAwaiter().GetResult();
                return results.Select(r => r.NormalizedPath).ToList();
            },
            ImportSearch = symbol =>
            {
                var results = querySvc.GetFilesByImportedSymbolAsync(snapshotId, symbol).GetAwaiter().GetResult();
                return results.ToList();
            },
            SymbolSearchDetailed = symbol =>
            {
                var results = querySvc.SearchSymbolsAsync(snapshotId, symbol).GetAwaiter().GetResult();
                return results.Select(r => new SymbolHit
                {
                    NormalizedPath = r.NormalizedPath,
                    Name = r.Name,
                    Kind = r.Kind,
                    StartLine = r.StartLine,
                    EndLine = r.EndLine,
                    ExactMatch = r.ExactMatch,
                }).ToList();
            },
            RelationSearch = filePath =>
            {
                var results = querySvc.GetFileRelationsAsync(snapshotId, filePath).GetAwaiter().GetResult();
                return results.Select(r => new RelationHit
                {
                    TargetName = r.TargetName,
                    RelationType = r.RelationType,
                    Relation = r.Relation,
                    Confidence = r.Confidence,
                }).ToList();
            },
            ReverseRelationSearch = target =>
            {
                var results = querySvc.GetFilesByRelationTargetAsync(snapshotId, target).GetAwaiter().GetResult();
                return results.Select(r => new RelationHit
                {
                    TargetName = r.TargetName,
                    RelationType = r.RelationType,
                    Relation = r.Relation,
                    Confidence = r.Confidence,
                    SourcePath = r.NormalizedPath,
                }).ToList();
            },
            FileSymbolsProvider = path =>
            {
                var results = querySvc.GetFileSymbolsAsync(snapshotId, path).GetAwaiter().GetResult();
                return results.Select(r => new SymbolHit
                {
                    NormalizedPath = path,
                    Name = r.Name,
                    Kind = r.Kind,
                    StartLine = r.StartLine,
                    EndLine = r.EndLine,
                    ExactMatch = true,
                }).ToList();
            },
        };
    }
}

/// <summary>
/// Container for all recall callbacks.
/// </summary>
public sealed record RecallCallbacks
{
    public required Func<string, IReadOnlyList<FtsMatch>> FtsSearch { get; init; }
    public required Func<string, IReadOnlyList<string>> SymbolSearch { get; init; }
    public required Func<string, IReadOnlyList<string>> ImportSearch { get; init; }
    public required Func<string, IReadOnlyList<SymbolHit>> SymbolSearchDetailed { get; init; }
    public required Func<string, IReadOnlyList<RelationHit>> RelationSearch { get; init; }
    public required Func<string, IReadOnlyList<RelationHit>> ReverseRelationSearch { get; init; }
    public required Func<string, IReadOnlyList<SymbolHit>> FileSymbolsProvider { get; init; }
}
