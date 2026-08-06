using System.Text.Json.Serialization;

namespace AiKv.Core.Context;

/// <summary>
/// Payload of a Context Package: actual code content, outlines, diffs, repo map.
/// Separated from Manifest to allow clients to inspect metadata without downloading content.
/// </summary>
public sealed record ContextPackagePayload
{
    [JsonPropertyName("contextPackageId")]
    public required string ContextPackageId { get; init; }

    [JsonPropertyName("format")]
    public required PayloadFormat Format { get; init; }

    [JsonPropertyName("items")]
    public required IReadOnlyList<PayloadItem> Items { get; init; }

    [JsonPropertyName("repoMap")]
    public string? RepoMap { get; init; }

    [JsonPropertyName("totalEstimatedTokens")]
    public required int TotalEstimatedTokens { get; init; }
}

public sealed record PayloadItem
{
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("mode")]
    public required SelectionMode Mode { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }

    [JsonPropertyName("startLine")]
    public int? StartLine { get; init; }

    [JsonPropertyName("endLine")]
    public int? EndLine { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PayloadFormat
{
    Markdown,
    Json,
    PlainText,
}
