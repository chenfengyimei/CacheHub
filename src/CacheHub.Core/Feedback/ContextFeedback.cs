using System.Text.Json;
using System.Text.Json.Serialization;

namespace CacheHub.Core.Feedback;

/// <summary>
/// Feedback from a client about a Context Package.
/// Used to improve ranking quality and detect misses.
/// </summary>
public sealed record ContextFeedback
{
    [JsonPropertyName("context_package_id")]
    public required string ContextPackageId { get; init; }

    [JsonPropertyName("client_id")]
    public string? ClientId { get; init; }

    [JsonPropertyName("client_version")]
    public string? ClientVersion { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("files_actually_read")]
    public IReadOnlyList<string> FilesActuallyRead { get; init; } = [];

    [JsonPropertyName("additional_files_requested")]
    public IReadOnlyList<string> AdditionalFilesRequested { get; init; } = [];

    [JsonPropertyName("selected_files_used")]
    public IReadOnlyList<string> SelectedFilesUsed { get; init; } = [];

    [JsonPropertyName("selected_files_ignored")]
    public IReadOnlyList<string> SelectedFilesIgnored { get; init; } = [];

    [JsonPropertyName("patch_files")]
    public IReadOnlyList<string> PatchFiles { get; init; } = [];

    [JsonPropertyName("tests_run")]
    public IReadOnlyList<string> TestsRun { get; init; } = [];

    [JsonPropertyName("tests_passed")]
    public bool? TestsPassed { get; init; }

    [JsonPropertyName("task_completed")]
    public bool TaskCompleted { get; init; }

    [JsonPropertyName("missing_context_reported")]
    public bool MissingContextReported { get; init; }

    [JsonPropertyName("user_intervention_count")]
    public int UserInterventionCount { get; init; }

    [JsonPropertyName("total_workflow_input_tokens")]
    public int? TotalWorkflowInputTokens { get; init; }

    [JsonPropertyName("total_workflow_output_tokens")]
    public int? TotalWorkflowOutputTokens { get; init; }

    public static ContextFeedback? ParseJson(string json)
        => JsonSerializer.Deserialize<ContextFeedback>(json);
}
