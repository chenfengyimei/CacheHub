using System.Globalization;
using System.Text;
using CacheHub.Core.Context;
using CacheHub.Core.Identifiers;
using CacheHub.Core.Workflow;

namespace CacheHub.Core.Workflow;

/// <summary>
/// R9-W003: Prompt Assembly Service.
/// Assembles the system prompt and context payload for model calls.
/// Only uses explicit templates — does not guess Agent-private prompts.
/// </summary>
public sealed class PromptAssemblyService
{
    /// <summary>
    /// Assembles a complete prompt from a Context Package Manifest and payload.
    /// Returns (systemPrompt, userContent) tuple.
    /// </summary>
    public (string systemPrompt, string userContent) Assemble(
        ContextPackageManifest manifest,
        string payloadContent,
        PromptAssemblyOptions? options = null)
    {
        var opts = options ?? new PromptAssemblyOptions();
        var systemPrompt = BuildSystemPrompt(manifest, opts);
        var userContent = BuildUserContent(manifest, payloadContent, opts);
        return (systemPrompt, userContent);
    }

    /// <summary>
    /// Returns manifest-only mode (no payload assembly).
    /// </summary>
    public string ManifestOnly(ContextPackageManifest manifest)
    {
        return string.Format(CultureInfo.InvariantCulture,
            "Context Package: {0}\nTask: {1}\nSelected Files: {2}\nBudget: {3}/{4} tokens\nEngine: {5}",
            manifest.Id.Value, manifest.Task.OriginalText, manifest.SelectedFiles.Count,
            manifest.Budget.ActualEstimate, manifest.Budget.ContextTarget, manifest.ContextEngineVersion);
    }

    private static string BuildSystemPrompt(ContextPackageManifest manifest, PromptAssemblyOptions opts)
    {
        var sb = new StringBuilder();

        if (opts.IncludeSystemHeader)
        {
            sb.AppendLine("# CacheHub Context");
            sb.AppendLine();
        }

        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Task: {manifest.Task.OriginalText}"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Context Package ID: {manifest.Id.Value}"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Selected Files: {manifest.SelectedFiles.Count}"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Estimated Tokens: {manifest.Budget.ActualEstimate}"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Snapshot: {manifest.IndexSnapshotId.Value}"));

        if (opts.IncludeSafetyInfo)
        {
            sb.AppendLine();
            sb.AppendLine("## Security");
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"- Cloud Send Allowed: {manifest.Safety.CloudSendAllowed}"));
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"- Secrets Scan Passed: {manifest.Safety.SecretsScanPassed}"));
        }

        if (opts.IncludeFileList)
        {
            sb.AppendLine();
            sb.AppendLine("## Selected Files");
            foreach (var file in manifest.SelectedFiles)
            {
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"- {file.Path} (mode: {file.Mode}, score: {file.Score:F2})"));
            }
        }

        return sb.ToString();
    }

    private static string BuildUserContent(
        ContextPackageManifest manifest,
        string payloadContent,
        PromptAssemblyOptions opts)
    {
        if (!opts.IncludePayload) return "";

        var sb = new StringBuilder();
        sb.AppendLine("## Code Context");
        sb.AppendLine();
        sb.AppendLine(payloadContent);
        sb.AppendLine();
        sb.AppendLine("## Task");
        sb.AppendLine(manifest.Task.OriginalText);
        return sb.ToString();
    }
}

/// <summary>
/// Options for prompt assembly.
/// </summary>
public sealed record PromptAssemblyOptions
{
    public bool IncludeSystemHeader { get; init; } = true;
    public bool IncludeSafetyInfo { get; init; } = true;
    public bool IncludeFileList { get; init; } = true;
    public bool IncludePayload { get; init; } = true;
}

/// <summary>
/// R9-W004: Workspace resolution result.
/// When a path cannot be uniquely mapped to a workspace, returns WORKSPACE_NOT_UNIQUE.
/// </summary>
public sealed record WorkspaceResolution
{
    public required bool IsUnique { get; init; }
    public WorkspaceId? WorkspaceId { get; init; }
    public string? Error { get; init; }

    public static WorkspaceResolution Unique(WorkspaceId id) => new() { IsUnique = true, WorkspaceId = id };
    public static WorkspaceResolution NotUnique(string reason) => new() { IsUnique = false, Error = reason };
    public static WorkspaceResolution NotFound(string reason) => new() { IsUnique = false, Error = reason };
}
