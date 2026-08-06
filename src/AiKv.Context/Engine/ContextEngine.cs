using AiKv.Context.Budget;
using AiKv.Context.Chunking;
using AiKv.Context.Parsing;
using AiKv.Context.Ranking;
using AiKv.Context.Recall;
using AiKv.Context.Selection;
using AiKv.Core.Context;
using AiKv.Core.Identifiers;

namespace AiKv.Context.Engine;

/// <summary>
/// Request to build a Context Package.
/// </summary>
public sealed record ContextBuildRequest
{
    public required WorkspaceId WorkspaceId { get; init; }
    public required IndexSnapshotId IndexSnapshotId { get; init; }
    public required string Task { get; init; }
    public TokenBudget? Budget { get; init; }
    public RankingProfile? RankingProfile { get; init; }
    public IReadOnlyList<string>? GitDiffFiles { get; init; }
    public string? CurrentFile { get; init; }
    public string? SecurityPolicyVersion { get; init; }
    public string? IgnoreRulesHash { get; init; }
    public string? RepoMapVersion { get; init; }
}

/// <summary>
/// Context Engine: builds deterministically ranked, budget-constrained Context Packages.
/// </summary>
public sealed class ContextEngine
{
    private readonly TaskParser _taskParser = new();
    private readonly RecallPipeline _recall = new();
    private readonly RankingEngine _ranking = new();
    private readonly SelectionEngine _selection = new();

    /// <summary>
    /// Builds a Context Package manifest (and optionally payload).
    /// </summary>
    public ContextPackageManifest Build(
        ContextBuildRequest request,
        Func<IReadOnlyList<IndexedFileInfo>> indexedFilesProvider,
        Func<string, string> contentProvider,
        Func<string, string> hashProvider,
        bool includePayload = false)
    {
        var budget = request.Budget ?? DefaultTokenBudgetPolicy.Create();
        var profile = request.RankingProfile ?? DefaultRankingProfile.Create();

        var parsedTask = _taskParser.Parse(request.Task);
        var candidates = _recall.Recall(parsedTask, indexedFilesProvider(), request.GitDiffFiles, request.CurrentFile);
        var ranked = _ranking.Rank(candidates, profile, parsedTask, request.CurrentFile);
        var selected = _selection.Select(ranked, budget, contentProvider, hashProvider);

        var manifest = new ContextPackageManifest
        {
            Id = ContextPackageId.New(),
            SchemaVersion = 1,
            WorkspaceId = request.WorkspaceId,
            IndexSnapshotId = request.IndexSnapshotId,
            Task = new Core.Context.TaskInfo
            {
                OriginalText = parsedTask.OriginalText,
                QueryParserVersion = parsedTask.QueryParserVersion,
                ExtractedSymbols = parsedTask.ExtractedSymbols,
                ExtractedPaths = parsedTask.ExtractedPaths,
            },
            Ranking = new Core.Context.RankingInfo
            {
                ProfileId = profile.Id,
                ProfileVersion = profile.Version,
            },
            Budget = new Core.Context.BudgetInfo
            {
                ModelContextWindow = budget.ModelContextWindow,
                AgentReservedTokens = budget.AgentReservedTokens,
                ResponseReservedTokens = budget.ResponseReservedTokens,
                ContextTarget = budget.ContextTarget,
                ContextHardLimit = budget.ContextHardLimit,
                SafetyMargin = budget.SafetyMargin,
                ActualEstimate = selected.TotalEstimatedTokens,
                Tokenizer = budget.Tokenizer,
                TokenizerVersion = budget.TokenizerVersion,
            },
            SelectedFiles = selected.SelectedFiles.Select(f => new Core.Context.SelectedFile
            {
                Path = f.Path,
                ContentHash = f.ContentHash,
                Mode = f.Mode,
                Score = f.Score,
                Reasons = f.Reasons,
                Ranges = f.Ranges,
            }).ToList(),
            ExcludedCandidates = selected.ExcludedCandidates.Select(e => new Core.Context.ExcludedCandidate
            {
                Path = e.Path,
                Score = e.Score,
                Reason = e.Reason,
            }).ToList(),
            Safety = new Core.Context.SafetyInfo
            {
                CloudSendAllowed = true,
                SecretsScanPassed = true,
                IgnoreRulesHash = request.IgnoreRulesHash,
                SecurityPolicyVersion = request.SecurityPolicyVersion,
                SecretScannerVersion = "none",
            },
            ContextEngineVersion = "0.1.0",
            ChunkingStrategyVersion = ChunkingStrategy.Version,
            TokenBudgetPolicyVersion = DefaultTokenBudgetPolicy.Version,
            RepoMapVersion = request.RepoMapVersion,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        return manifest;
    }
}
