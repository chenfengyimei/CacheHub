using System.Text.Json;
using CacheHub.Core.Gateway;
using CacheHub.Core.Providers;
using CacheHub.Core.Tokens;

namespace CacheHub.Core.Gateway.Streaming;

/// <summary>
/// SSE (Server-Sent Events) frame for streaming responses.
/// </summary>
public sealed record SseFrame
{
    public required string Event { get; init; }
    public required string Data { get; init; }
    public string? Id { get; init; }

    public string ToSseString()
    {
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(Event))
            sb.Append("event: ").Append(Event).Append('\n');
        if (!string.IsNullOrEmpty(Id))
            sb.Append("id: ").Append(Id).Append('\n');
        sb.Append("data: ").Append(Data).Append("\n\n");
        return sb.ToString();
    }
}

/// <summary>
/// Parses OpenAI-compatible SSE stream chunks.
/// Extracts delta content and usage information.
/// </summary>
public static class SseStreamParser
{
    /// <summary>
    /// Parses a single SSE data line (the part after "data: ").
    /// Returns null for [DONE] or unparseable lines.
    /// </summary>
    public static StreamChunk? ParseChunk(string dataLine)
    {
        if (string.IsNullOrWhiteSpace(dataLine)) return null;
        if (dataLine.Trim() == "[DONE]") return null;

        try
        {
            using var doc = JsonDocument.Parse(dataLine);
            var root = doc.RootElement;

            var choices = root.TryGetProperty("choices", out var c) ? c : default;
            if (choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
                return new StreamChunk { Delta = "", FinishReason = null, Usage = null };

            var firstChoice = choices[0];
            var delta = firstChoice.TryGetProperty("delta", out var d) &&
                        d.TryGetProperty("content", out var content)
                ? content.GetString() ?? ""
                : "";
            var finishReason = firstChoice.TryGetProperty("finish_reason", out var fr)
                ? fr.GetString()
                : null;

            ModelUsageInfo? usage = null;
            if (root.TryGetProperty("usage", out var u))
            {
                usage = new ModelUsageInfo
                {
                    PromptTokens = u.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0,
                    CompletionTokens = u.TryGetProperty("completion_tokens", out var ct) ? ct.GetInt32() : 0,
                    TotalTokens = u.TryGetProperty("total_tokens", out var tt) ? tt.GetInt32() : 0,
                };
            }

            return new StreamChunk { Delta = delta, FinishReason = finishReason, Usage = usage };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parses a full SSE response body (multiple "data: ..." lines).
    /// </summary>
    public static ParsedStreamResult ParseStream(string sseBody)
    {
        var contentBuilder = new System.Text.StringBuilder();
        ModelUsageInfo? usage = null;
        string? finishReason = null;

        var lines = sseBody.Split('\n');
        foreach (var line in lines)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;
            var data = line["data: ".Length..];
            var chunk = ParseChunk(data);
            if (chunk is null) continue;

            contentBuilder.Append(chunk.Delta);
            if (chunk.FinishReason is not null)
                finishReason = chunk.FinishReason;
            if (chunk.Usage is not null)
                usage = chunk.Usage;
        }

        return new ParsedStreamResult
        {
            Content = contentBuilder.ToString(),
            FinishReason = finishReason,
            Usage = usage,
        };
    }
}

/// <summary>
/// A parsed chunk from an SSE stream.
/// </summary>
public sealed record StreamChunk
{
    public required string Delta { get; init; }
    public string? FinishReason { get; init; }
    public ModelUsageInfo? Usage { get; init; }
}

/// <summary>
/// Result of parsing a complete SSE stream.
/// </summary>
public sealed record ParsedStreamResult
{
    public required string Content { get; init; }
    public string? FinishReason { get; init; }
    public ModelUsageInfo? Usage { get; init; }
}
