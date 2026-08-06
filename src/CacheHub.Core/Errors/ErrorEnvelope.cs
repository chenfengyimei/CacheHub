namespace CacheHub.Core.Errors;

/// <summary>
/// Standardized error envelope returned by CLI JSON output and Local API.
/// Format: {success, errorCode, message, recoverable, suggestedActions, details}.
/// </summary>
public sealed record ErrorEnvelope
{
    public bool Success { get; init; }
    public int ErrorCode { get; init; }
    public string Message { get; init; } = "";
    public bool Recoverable { get; init; }
    public IReadOnlyList<string> SuggestedActions { get; init; } = [];
    public IReadOnlyDictionary<string, object?>? Details { get; init; }

    /// <summary>
    /// Creates an error envelope from a CacheHubException.
    /// </summary>
    public static ErrorEnvelope From(CacheHubException ex) => new()
    {
        Success = false,
        ErrorCode = (int)ex.Code,
        Message = ex.Message,
        Recoverable = ex.Recoverable,
        Details = ex.Details,
        SuggestedActions = GetSuggestedActions(ex.Code),
    };

    /// <summary>
    /// Creates an error envelope from an error code and message.
    /// </summary>
    public static ErrorEnvelope From(Errors.ErrorCode code, string message, bool recoverable = false) => new()
    {
        Success = false,
        ErrorCode = (int)code,
        Message = message,
        Recoverable = recoverable,
        SuggestedActions = GetSuggestedActions(code),
    };

    private static IReadOnlyList<string> GetSuggestedActions(Errors.ErrorCode code) => code switch
    {
        Errors.ErrorCode.WorkspaceNotFound => ["Check workspace ID with: cachehub workspace list"],
        Errors.ErrorCode.WorkspaceNotUnique => ["Specify --id explicitly"],
        Errors.ErrorCode.WorkspacePathEscape => ["Use a path within the workspace root"],
        Errors.ErrorCode.IndexNotFound => ["Build index first: cachehub index build --id=<workspace-id>"],
        Errors.ErrorCode.ContextPackageNotFound => ["Check context ID with: cachehub context list --workspace=<id>"],
        Errors.ErrorCode.ContextBudgetExceeded => ["Reduce task scope or increase token budget"],
        Errors.ErrorCode.SecurityPolicyViolation => ["Review security mode: cachehub config show"],
        Errors.ErrorCode.SecretDetected => ["Remove secrets from files or use Restricted mode"],
        Errors.ErrorCode.CloudSendDenied => ["Switch to Standard mode or use offline workflow"],
        Errors.ErrorCode.PathTraversalDetected => ["Use relative paths within the workspace root"],
        Errors.ErrorCode.RepositoryCloneFailed => ["Check URL, permissions, and destination path"],
        Errors.ErrorCode.RepositoryPullConflict => ["Resolve local changes manually; CacheHub does not auto-merge"],
        Errors.ErrorCode.GatewayUnavailable => ["Start gateway: cachehub gateway start --provider-url=<url>"],
        Errors.ErrorCode.ProviderError => ["Check provider URL and API key"],
        _ => ["Check the documentation or run: cachehub help"],
    };
}
