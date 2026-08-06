using System.Text;
using CacheHub.Core.Context;
using CacheHub.Core.Identifiers;
using CacheHub.Core.Security;
using CacheHub.Context.Chunking;

namespace CacheHub.Context.Payload;

/// <summary>
/// Generates Context Package Payload from a Manifest and file contents.
/// Payload contains actual code content, separated from Manifest metadata.
/// Security: if a SecurityPolicyEnforcer is provided, every file is evaluated
/// before inclusion in the Payload. Denied files are excluded; ApprovalRequired
/// files are excluded from content but listed as requiring approval.
/// </summary>
public sealed class PayloadGenerator
{
    private readonly ChunkingStrategy _chunker = new();

    /// <summary>
    /// Generates a Payload from a Manifest and content provider.
    /// If securityEnforcer is provided, files are filtered by policy.
    /// </summary>
    public ContextPackagePayload Generate(
        ContextPackageManifest manifest,
        Func<string, string> contentProvider,
        SecurityPolicyEnforcer? securityEnforcer = null)
    {
        var items = new List<PayloadItem>();
        var totalTokens = 0;
        var chunkBudget = Math.Max(manifest.Budget.ContextTarget, 1000);

        foreach (var file in manifest.SelectedFiles)
        {
            var content = contentProvider(file.Path);
            if (string.IsNullOrEmpty(content)) continue;

            // Security: evaluate file before including in payload
            if (securityEnforcer is not null)
            {
                var decision = securityEnforcer.EvaluateFile(file.Path, content);
                if (!decision.IsAllowed)
                {
                    // Denied or ApprovalRequired — skip content, don't output
                    continue;
                }
            }

            var chunks = _chunker.Chunk(file.Path, content, file.Mode, chunkBudget);

            foreach (var chunk in chunks)
            {
                items.Add(new PayloadItem
                {
                    Path = file.Path,
                    Mode = file.Mode,
                    Content = chunk.Content,
                    StartLine = chunk.StartLine > 0 ? chunk.StartLine : null,
                    EndLine = chunk.EndLine > 0 ? chunk.EndLine : null,
                });
                totalTokens += chunk.EstimatedTokens;
            }
        }

        return new ContextPackagePayload
        {
            ContextPackageId = manifest.Id.Value,
            Format = PayloadFormat.Markdown,
            Items = items,
            TotalEstimatedTokens = totalTokens,
        };
    }

    /// <summary>
    /// Generates a Markdown-formatted payload string.
    /// If securityEnforcer is provided, files are filtered by policy.
    /// </summary>
    public string GenerateMarkdown(
        ContextPackageManifest manifest,
        Func<string, string> contentProvider,
        SecurityPolicyEnforcer? securityEnforcer = null)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"# Context Package: {manifest.Id.Value}");
        sb.AppendLine();
        sb.AppendLine($"**Task:** {manifest.Task.OriginalText}");
        sb.AppendLine($"**Budget:** {manifest.Budget.ActualEstimate} / {manifest.Budget.ContextTarget} tokens");
        sb.AppendLine($"**Engine:** {manifest.ContextEngineVersion}");
        sb.AppendLine();

        foreach (var file in manifest.SelectedFiles)
        {
            var content = contentProvider(file.Path);
            if (string.IsNullOrEmpty(content)) continue;

            // Security: evaluate file before including in payload
            if (securityEnforcer is not null)
            {
                var decision = securityEnforcer.EvaluateFile(file.Path, content);
                if (!decision.IsAllowed)
                {
                    if (decision.IsApprovalRequired)
                        sb.AppendLine($"## ⚠ {file.Path} (requires approval)");
                    else
                        sb.AppendLine($"## 🚫 {file.Path} (blocked by security policy)");
                    sb.AppendLine($"- Reason: {decision.Reason}");
                    sb.AppendLine();
                    continue;
                }
            }

            var ext = Path.GetExtension(file.Path).TrimStart('.');
            sb.AppendLine($"## {file.Path}");
            sb.AppendLine();
            sb.AppendLine($"- Mode: {file.Mode}");
            sb.AppendLine($"- Score: {file.Score:F2}");
            sb.AppendLine($"- Reasons: {string.Join(", ", file.Reasons)}");
            sb.AppendLine();

            if (file.Mode == SelectionMode.Full)
            {
                sb.AppendLine($"```{ext}");
                sb.AppendLine(content);
                sb.AppendLine("```");
            }
            else if (file.Mode == SelectionMode.Chunks && file.Ranges is not null)
            {
                var lines = content.Split('\n');
                foreach (var range in file.Ranges)
                {
                    var start = Math.Max(0, range.StartLine - 1);
                    var end = Math.Min(lines.Length, range.EndLine);
                    sb.AppendLine($"```{ext} (lines {range.StartLine}-{range.EndLine})");
                    sb.AppendLine(string.Join('\n', lines[start..end]));
                    sb.AppendLine("```");
                    sb.AppendLine();
                }
            }
            else
            {
                // Outline / Summary / Metadata — just include the chunked content
                var chunks = _chunker.Chunk(file.Path, content, file.Mode, 5000);
                foreach (var chunk in chunks)
                {
                    sb.AppendLine($"```{ext}");
                    sb.AppendLine(chunk.Content);
                    sb.AppendLine("```");
                    sb.AppendLine();
                }
            }

            sb.AppendLine();
        }

        if (manifest.ExcludedCandidates.Count > 0)
        {
            sb.AppendLine("## Excluded");
            sb.AppendLine();
            foreach (var e in manifest.ExcludedCandidates)
            {
                sb.AppendLine($"- {e.Path} (score: {e.Score:F2}) — {e.Reason}");
            }
        }

        return sb.ToString();
    }
}
