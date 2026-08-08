using System.Text;
using CacheHub.Core.Context;
using CacheHub.Core.Identifiers;
using CacheHub.Core.Security;
using CacheHub.Context.Chunking;
using System.Security.Cryptography;

namespace CacheHub.Context.Payload;

/// <summary>
/// Generates Context Package Payload from a Manifest and file contents.
/// Payload contains actual code content, separated from Manifest metadata.
/// Security: if a SecurityPolicyEnforcer is provided, every file is evaluated
/// before inclusion in the Payload. Denied files are excluded; ApprovalRequired
/// files are excluded from content but listed as requiring approval.
/// V8-P0-02: ContentHash verification — payload is immutable per ContextPackageId.
/// </summary>
public sealed class PayloadGenerator
{
    private readonly ChunkingStrategy _chunker = new();

    /// <summary>
    /// Generates a Payload from a Manifest and content provider.
    /// If securityEnforcer is provided, files are filtered by policy.
    /// V8-P0-02: Verifies ContentHash before emitting content.
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

            // V8-P0-02: Verify content hash to ensure payload immutability.
            // Skip verification for fingerprint hashes (fp:) and expanded files (sha256:expanded).
            VerifyContentHash(file.Path, file.ContentHash, content);

            // Security: evaluate file before including in payload
            if (securityEnforcer is not null)
            {
                var decision = securityEnforcer.EvaluateFile(file.Path, content);
                if (!decision.IsAllowed)
                {
                    continue;
                }
            }

            // R5-W006: Use Manifest ranges (immutable PayloadPlan) when available
            // instead of re-chunking, to prevent Manifest/Payload divergence
            if (file.Ranges is not null && file.Ranges.Count > 0)
            {
                var lines = content.Split('\n');
                foreach (var range in file.Ranges)
                {
                    var startIdx = Math.Max(0, range.StartLine - 1);
                    var endIdx = Math.Min(lines.Length - 1, range.EndLine - 1);
                    if (startIdx > endIdx) continue;
                    var rangeContent = string.Join('\n', lines.Skip(startIdx).Take(endIdx - startIdx + 1));
                    var tokens = ChunkingStrategy.EstimateTokens(rangeContent);

                    items.Add(new PayloadItem
                    {
                        Path = file.Path,
                        Mode = file.Mode,
                        Content = rangeContent,
                        StartLine = range.StartLine,
                        EndLine = range.EndLine,
                    });
                    totalTokens += tokens;
                }
            }
            else
            {
                // Fallback: re-chunk when no ranges in Manifest (backward compat)
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
    /// V8-P0-02: Verifies ContentHash before emitting content.
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

            // V8-P0-02: Verify content hash to ensure payload immutability
            VerifyContentHash(file.Path, file.ContentHash, content);

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

    /// <summary>
    /// V8-P0-02: Verifies that the content matches the manifest's ContentHash.
    /// Throws ContextVersionMismatchException if the file has been modified since the Context Package was built.
    /// Only verifies full SHA-256 hashes (sha256:hexstring format).
    /// Skips: fingerprint hashes (fp:), placeholder hashes (sha256:expanded, sha256:pending),
    /// empty/null hashes, and non-standard hash formats.
    /// </summary>
    private static void VerifyContentHash(string filePath, string? expectedHash, string content)
    {
        if (string.IsNullOrEmpty(expectedHash))
            return;

        // Only verify hashes that start with "sha256:" (full content hashes)
        if (!expectedHash.StartsWith("sha256:", StringComparison.Ordinal))
            return;

        // Skip placeholder hashes — expanded files or pending hashes
        if (expectedHash == "sha256:expanded" || expectedHash == "sha256:pending")
            return;

        // Compute SHA-256 of the content
        var contentBytes = Encoding.UTF8.GetBytes(content);
        var actualHashBytes = SHA256.HashData(contentBytes);
        var actualHash = "sha256:" + Convert.ToHexString(actualHashBytes).ToLowerInvariant();

        if (!string.Equals(expectedHash, actualHash, StringComparison.Ordinal))
        {
            throw new ContextVersionMismatchException(filePath, expectedHash, actualHash);
        }
    }
}
