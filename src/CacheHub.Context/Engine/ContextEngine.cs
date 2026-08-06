using CacheHub.Context.Budget;
using CacheHub.Context.Chunking;
using CacheHub.Context.Parsing;
using CacheHub.Context.Ranking;
using CacheHub.Context.Recall;
using CacheHub.Context.Selection;
using CacheHub.Core.Context;
using CacheHub.Core.Identifiers;
using CacheHub.Core.Security;
using CacheHub.Core.Tokens;

namespace CacheHub.Context.Engine;

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
    public string? ModelId { get; init; }
    public SecurityPolicy? SecurityPolicy { get; init; }
}

/// <summary>
/// Context Engine: builds deterministically ranked, budget-constrained Context Packages.
/// Integrates TokenizerRegistry for accurate token estimation and SecurityPolicyEnforcer for pre-send checks.
/// </summary>
public sealed class ContextEngine
{
    private readonly TaskParser _taskParser = new();
    private readonly RecallPipeline _recall = new();
    private readonly RankingEngine _ranking = new();
    private readonly SelectionEngine _selection = new();
    private readonly TokenizerRegistry _tokenizers;
    private readonly SecurityPolicyEnforcer? _securityEnforcer;

    public ContextEngine(TokenizerRegistry? tokenizers = null, SecurityPolicy? securityPolicy = null)
    {
        _tokenizers = tokenizers ?? new TokenizerRegistry();
        _securityEnforcer = securityPolicy is not null ? new SecurityPolicyEnforcer(securityPolicy) : null;
    }

    /// <summary>
    /// Builds a Context Package manifest with integrated tokenization and security checks.
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
        var tokenizer = request.ModelId is not null
            ? _tokenizers.GetForModel(request.ModelId)
            : _tokenizers.Default;

        var parsedTask = _taskParser.Parse(request.Task);
        var candidates = _recall.Recall(parsedTask, indexedFilesProvider(), request.GitDiffFiles, request.CurrentFile);
        var ranked = _ranking.Rank(candidates, profile, parsedTask, request.CurrentFile);
        var selected = _selection.Select(ranked, budget, contentProvider, hashProvider);

        // Security scan on selected files
        var securityPassed = true;
        var securityFindings = new List<string>();
        var cloudSendAllowed = true;
        var scannerVersion = "none";

        if (_securityEnforcer is not null)
        {
            cloudSendAllowed = _securityEnforcer.IsCloudSendAllowed();
            scannerVersion = SecretScanner.Version;

            foreach (var file in selected.SelectedFiles)
            {
                var content = contentProvider(file.Path);
                var (allowed, scan, reason) = _securityEnforcer.CheckBeforeSend(file.Path, content);
                if (!allowed)
                {
                    securityPassed = false;
                    securityFindings.Add($"{file.Path}: {reason}");
                }
            }
        }

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
                Tokenizer = tokenizer.Id,
                TokenizerVersion = tokenizer.Version,
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
                CloudSendAllowed = cloudSendAllowed,
                SecretsScanPassed = securityPassed,
                IgnoreRulesHash = request.IgnoreRulesHash,
                SecurityPolicyVersion = request.SecurityPolicyVersion ?? _securityEnforcer?.IsCloudSendAllowed().ToString(),
                SecretScannerVersion = scannerVersion,
                SensitiveExclusions = securityFindings.Count > 0 ? securityFindings : null,
            },
            ContextEngineVersion = "0.2.0",
            ChunkingStrategyVersion = ChunkingStrategy.Version,
            TokenBudgetPolicyVersion = DefaultTokenBudgetPolicy.Version,
            RepoMapVersion = request.RepoMapVersion,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        return manifest;
    }
}
