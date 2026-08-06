namespace CacheHub.Core.Errors;

/// <summary>
/// Stable error codes used across CLI, Local API, and GUI.
/// Error codes must not contain temporary implementation names.
/// </summary>
public enum ErrorCode
{
    // General
    Unknown = 0,
    InvalidArgument = 1001,
    NotSupported = 1002,
    OperationCancelled = 1003,

    // Workspace
    WorkspaceNotFound = 2001,
    WorkspaceNotUnique = 2002,
    WorkspacePathEscape = 2003,
    WorkspaceAlreadyExists = 2004,
    WorkspaceArchived = 2005,

    // Index
    IndexNotFound = 3001,
    IndexSnapshotMismatch = 3002,
    IndexCorrupted = 3003,

    // Context
    ContextPackageNotFound = 4001,
    ContextBudgetExceeded = 4002,
    ContextBuildFailed = 4003,
    ContextExpandFailed = 4004,

    // Security
    SecurityPolicyViolation = 5001,
    SecretDetected = 5002,
    CloudSendDenied = 5003,
    PathTraversalDetected = 5004,

    // Repository
    RepositoryCloneFailed = 6001,
    RepositoryPullConflict = 6002,
    RepositoryUrlInvalid = 6003,

    // Gateway
    GatewayUnavailable = 7001,
    ProviderError = 7002,
}
