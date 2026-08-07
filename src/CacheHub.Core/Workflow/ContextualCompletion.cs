using CacheHub.Core.Context;
using CacheHub.Core.Identifiers;
using CacheHub.Core.Security;

namespace CacheHub.Core.Workflow;

/// <summary>
/// R9-W001: Contextual Completion request — combines context build and model call.
/// The request explicitly includes workspace_id, task, model, budget, and security mode.
/// The service builds a Context Package first, then optionally calls the Gateway.
/// </summary>
public sealed record ContextualCompletionRequest
{
    /// <summary>Required: the workspace to build context from.</summary>
    public required WorkspaceId WorkspaceId { get; init; }

    /// <summary>Required: the task description for context building.</summary>
    public required string Task { get; init; }

    /// <summary>Optional: model ID for token budget calculation.</summary>
    public string? ModelId { get; init; }

    /// <summary>Optional: explicit budget override.</summary>
    public Context.BudgetInfo? BudgetOverride { get; init; }

    /// <summary>Optional: security mode (Standard, Offline, Restricted, PreviewRequired).</summary>
    public ExfiltrationMode? SecurityMode { get; init; }

    /// <summary>Optional: files changed in git diff.</summary>
    public IReadOnlyList<string>? GitDiffFiles { get; init; }

    /// <summary>Optional: file the user is currently editing.</summary>
    public string? CurrentFile { get; init; }

    /// <summary>Optional: whether to call the Gateway after building context. Default: false (manifest only).</summary>
    public bool CallGateway { get; init; }

    /// <summary>Optional: client ID for tracking.</summary>
    public string? ClientId { get; init; }
}

/// <summary>
/// R9-W001: Contextual Completion response.
/// Contains the built Context Package and, if CallGateway was true, the model response.
/// </summary>
public sealed record ContextualCompletionResponse
{
    /// <summary>The built Context Package Manifest.</summary>
    public required ContextPackageManifest Manifest { get; init; }

    /// <summary>Whether the Gateway was called.</summary>
    public bool GatewayCalled { get; init; }

    /// <summary>The model response body (if Gateway was called).</summary>
    public string? ModelResponse { get; init; }

    /// <summary>The HTTP status code from the provider (if Gateway was called).</summary>
    public int? StatusCode { get; init; }

    /// <summary>Token usage from the model (if available).</summary>
    public int? PromptTokens { get; init; }

    /// <summary>Token usage from the model (if available).</summary>
    public int? CompletionTokens { get; init; }

    /// <summary>Total tokens used: context + prompt + completion.</summary>
    public int TotalLifecycleTokens => Manifest.Budget.ActualEstimate + (PromptTokens ?? 0) + (CompletionTokens ?? 0);
}

/// <summary>
/// R9-W002: Gateway metadata contract.
/// Supports context_package_id, snapshot_id, dirty_state_hash, client_id.
/// When no metadata is present, the request is treated as a plain Gateway request.
/// </summary>
public sealed record GatewayMetadata
{
    /// <summary>The Context Package ID that was built for this request.</summary>
    public ContextPackageId? ContextPackageId { get; init; }

    /// <summary>The Index Snapshot ID used for context building.</summary>
    public IndexSnapshotId? SnapshotId { get; init; }

    /// <summary>Hash of the workspace dirty state (uncommitted changes).</summary>
    public string? DirtyStateHash { get; init; }

    /// <summary>Client identifier for tracking.</summary>
    public string? ClientId { get; init; }

    /// <summary>Whether this metadata is present (vs plain Gateway request).</summary>
    public bool HasMetadata => ContextPackageId is not null || SnapshotId is not null;
}
