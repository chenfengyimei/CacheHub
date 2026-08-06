using AiKv.Context.Payload;
using AiKv.Core.Context;
using AiKv.Core.Identifiers;
using AiKv.Storage;

namespace AiKv.Context.Export;

/// <summary>
/// File export protocol: writes .aikv/ directory with workspace.json,
/// latest-context.manifest.json, latest-context.md, and repomap.md.
/// Default export location is AI_KV data directory.
/// Only writes to repository .aikv/ when explicitly enabled by user.
/// </summary>
public sealed class FileExporter
{
    private static readonly System.Text.Json.JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };
    private readonly AppDataDirectory _appData;

    public FileExporter(AppDataDirectory? appData = null)
    {
        _appData = appData ?? new AppDataDirectory();
    }

    /// <summary>
    /// Exports a context package to the AI_KV data directory.
    /// </summary>
    public async Task<string> ExportAsync(
        ContextPackageManifest manifest,
        Func<string, string> contentProvider,
        string? workspaceId = null)
    {
        var exportDir = GetExportDir(workspaceId ?? manifest.WorkspaceId.Value);
        Directory.CreateDirectory(exportDir);

        // 1. workspace.json
        var workspaceJson = new
        {
            workspaceId = manifest.WorkspaceId.Value,
            indexSnapshotId = manifest.IndexSnapshotId.Value,
            schemaVersion = manifest.SchemaVersion,
            engineVersion = manifest.ContextEngineVersion,
        };
        await File.WriteAllTextAsync(
            Path.Combine(exportDir, "workspace.json"),
            System.Text.Json.JsonSerializer.Serialize(workspaceJson, _jsonOpts));

        // 2. latest-context.manifest.json
        var manifestJson = System.Text.Json.JsonSerializer.Serialize(manifest, _jsonOpts);
        await File.WriteAllTextAsync(
            Path.Combine(exportDir, "latest-context.manifest.json"),
            manifestJson);

        // 3. latest-context.md
        var generator = new PayloadGenerator();
        var markdown = generator.GenerateMarkdown(manifest, contentProvider);
        await File.WriteAllTextAsync(
            Path.Combine(exportDir, "latest-context.md"),
            markdown);

        // 4. repomap.md
        var repomap = GenerateRepoMap(manifest);
        await File.WriteAllTextAsync(
            Path.Combine(exportDir, "repomap.md"),
            repomap);

        return exportDir;
    }

    /// <summary>
    /// Exports to a repository's .aikv/ directory (requires explicit user opt-in).
    /// Also generates a .gitignore entry suggestion.
    /// </summary>
    public async Task<string> ExportToRepositoryAsync(
        string repositoryRoot,
        ContextPackageManifest manifest,
        Func<string, string> contentProvider)
    {
        var aikvDir = Path.Combine(repositoryRoot, ".aikv");
        Directory.CreateDirectory(aikvDir);

        // Write files
        var exportDir = await ExportAsync(manifest, contentProvider, manifest.WorkspaceId.Value);

        // Copy to .aikv/
        foreach (var file in Directory.GetFiles(exportDir))
        {
            File.Copy(file, Path.Combine(aikvDir, Path.GetFileName(file)), overwrite: true);
        }

        // Suggest .gitignore entry
        var gitignorePath = Path.Combine(repositoryRoot, ".gitignore");
        var gitignoreContent = File.Exists(gitignorePath) ? await File.ReadAllTextAsync(gitignorePath) : "";
        if (!gitignoreContent.Contains(".aikv/", StringComparison.Ordinal))
        {
            await File.AppendAllTextAsync(gitignorePath, "\n.aikv/\n");
        }

        return aikvDir;
    }

    /// <summary>
    /// Reads the latest exported manifest from the export directory.
    /// </summary>
    public ContextPackageManifest? ReadLatestManifest(string workspaceId)
    {
        var path = Path.Combine(GetExportDir(workspaceId), "latest-context.manifest.json");
        if (!File.Exists(path)) return null;

        var json = File.ReadAllText(path);
        return System.Text.Json.JsonSerializer.Deserialize<ContextPackageManifest>(json, _jsonOpts);
    }

    private string GetExportDir(string workspaceId) =>
        Path.Combine(_appData.Root, "exports", workspaceId);

    private static string GenerateRepoMap(ContextPackageManifest manifest)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Repository Map");
        sb.AppendLine();
        sb.AppendLine($"Generated from Context Package: {manifest.Id.Value}");
        sb.AppendLine($"Task: {manifest.Task.OriginalText}");
        sb.AppendLine();

        if (manifest.SelectedFiles.Count > 0)
        {
            sb.AppendLine("## Files in Context");
            sb.AppendLine();
            foreach (var f in manifest.SelectedFiles)
            {
                sb.AppendLine($"- {f.Path} [{f.Mode}] (score: {f.Score:F2})");
            }
            sb.AppendLine();
        }

        if (manifest.ExcludedCandidates.Count > 0)
        {
            sb.AppendLine("## Excluded Files");
            sb.AppendLine();
            foreach (var e in manifest.ExcludedCandidates)
            {
                sb.AppendLine($"- {e.Path} (score: {e.Score:F2}) — {e.Reason}");
            }
        }

        return sb.ToString();
    }
}
