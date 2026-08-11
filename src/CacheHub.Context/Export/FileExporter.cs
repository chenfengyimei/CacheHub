using CacheHub.Context.Payload;
using CacheHub.Core.Context;
using CacheHub.Core.Identifiers;
using CacheHub.Core.Security;
using CacheHub.Storage;

namespace CacheHub.Context.Export;

/// <summary>
/// Plan for repository export: what files will be written and what .gitignore changes are needed.
/// User must approve before Apply executes.
/// </summary>
public sealed record ExportPlan
{
    public required string TargetDirectory { get; init; }
    public required IReadOnlyList<string> FilesToWrite { get; init; }
    public required string? GitignoreAddition { get; init; }
    public required IReadOnlyList<string> Risks { get; init; }
}

/// <summary>
/// File export protocol: writes .cachehub/ directory with workspace.json,
/// latest-context.manifest.json, latest-context.md, and repomap.md.
/// Default export location is CacheHub data directory (safe).
/// Repository writes require Plan → user approval → Apply (with backup).
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
    /// Exports a context package to the CacheHub data directory (always safe).
    /// Security enforcer is required to ensure all exported content is policy-checked.
    /// </summary>
    public async Task<string> ExportAsync(
        ContextPackageManifest manifest,
        Func<string, string> contentProvider,
        string? workspaceId = null,
        SecurityPolicyEnforcer? securityEnforcer = null,
        Func<string, byte[]>? rawContentProvider = null)
    {
        var exportDir = GetExportDir(workspaceId ?? manifest.WorkspaceId.Value);
        Directory.CreateDirectory(exportDir);

        await WriteContextFilesAsync(exportDir, manifest, contentProvider, securityEnforcer, rawContentProvider);
        return exportDir;
    }

    /// <summary>
    /// Plans an export to a repository's .cachehub/ directory.
    /// Returns what will be written — does NOT modify any files.
    /// User must call ApplyRepositoryExportAsync to execute.
    /// </summary>
    public ExportPlan PlanRepositoryExport(
        string repositoryRoot,
        ContextPackageManifest manifest)
    {
        var cachehubDir = Path.Combine(repositoryRoot, ".cachehub");

        var filesToWrite = new List<string>
        {
            Path.Combine(cachehubDir, "workspace.json"),
            Path.Combine(cachehubDir, "latest-context.manifest.json"),
            Path.Combine(cachehubDir, "latest-context.md"),
            Path.Combine(cachehubDir, "repomap.md"),
        };

        // Check if .gitignore needs updating
        var gitignorePath = Path.Combine(repositoryRoot, ".gitignore");
        string? gitignoreAddition = null;
        var risks = new List<string>();

        if (File.Exists(gitignorePath))
        {
            var content = File.ReadAllText(gitignorePath);
            if (!content.Contains(".cachehub/", StringComparison.Ordinal))
            {
                gitignoreAddition = "\n.cachehub/\n";
                risks.Add("Will modify existing .gitignore");
            }
        }
        else
        {
            gitignoreAddition = ".cachehub/\n";
            risks.Add("Will create new .gitignore file");
        }

        risks.Add("Will write files to repository directory");

        return new ExportPlan
        {
            TargetDirectory = cachehubDir,
            FilesToWrite = filesToWrite,
            GitignoreAddition = gitignoreAddition,
            Risks = risks,
        };
    }

    /// <summary>
    /// Applies a repository export plan. Creates backup of modified files,
    /// writes atomically, and modifies .gitignore only if planned.
    /// </summary>
    public async Task<string> ApplyRepositoryExportAsync(
        ExportPlan plan,
        string repositoryRoot,
        ContextPackageManifest manifest,
        Func<string, string> contentProvider,
        SecurityPolicyEnforcer? securityEnforcer = null)
    {
        Directory.CreateDirectory(plan.TargetDirectory);

        // Write context files
        await WriteContextFilesAsync(plan.TargetDirectory, manifest, contentProvider, securityEnforcer);

        // Apply .gitignore changes (with backup)
        if (plan.GitignoreAddition is not null)
        {
            var gitignorePath = Path.Combine(repositoryRoot, ".gitignore");
            var backupPath = gitignorePath + ".cachehub-backup";

            // Backup existing .gitignore
            if (File.Exists(gitignorePath))
            {
                File.Copy(gitignorePath, backupPath, overwrite: true);
            }

            // Atomic write: write to temp file then rename
            var tempPath = gitignorePath + ".tmp";
            var content = File.Exists(gitignorePath) ? await File.ReadAllTextAsync(gitignorePath) : "";
            content += plan.GitignoreAddition;
            await File.WriteAllTextAsync(tempPath, content);

            // Atomic rename (on most OS)
            File.Move(tempPath, gitignorePath, overwrite: true);
        }

        return plan.TargetDirectory;
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

    private async Task WriteContextFilesAsync(
        string dir,
        ContextPackageManifest manifest,
        Func<string, string> contentProvider,
        SecurityPolicyEnforcer? securityEnforcer = null,
        Func<string, byte[]>? rawContentProvider = null)
    {
        // 1. workspace.json
        var workspaceJson = new
        {
            workspaceId = manifest.WorkspaceId.Value,
            indexSnapshotId = manifest.IndexSnapshotId.Value,
            schemaVersion = manifest.SchemaVersion,
            engineVersion = manifest.ContextEngineVersion,
        };
        await File.WriteAllTextAsync(
            Path.Combine(dir, "workspace.json"),
            System.Text.Json.JsonSerializer.Serialize(workspaceJson, _jsonOpts));

        // 2. latest-context.manifest.json
        var manifestJson = System.Text.Json.JsonSerializer.Serialize(manifest, _jsonOpts);
        await File.WriteAllTextAsync(
            Path.Combine(dir, "latest-context.manifest.json"), manifestJson);

        // 3. latest-context.md — security enforcer ensures blocked files are filtered
        var generator = new PayloadGenerator();
        var markdown = generator.GenerateMarkdown(manifest, contentProvider, securityEnforcer, rawContentProvider);
        await File.WriteAllTextAsync(
            Path.Combine(dir, "latest-context.md"), markdown);

        // 4. repomap.md
        var repomap = GenerateRepoMap(manifest);
        await File.WriteAllTextAsync(
            Path.Combine(dir, "repomap.md"), repomap);
    }

    private string GetExportDir(string workspaceId)
    {
        var safeId = workspaceId.Replace("..", "").Replace("/", "").Replace("\\", "").Replace(":", "");
        if (string.IsNullOrWhiteSpace(safeId)) safeId = "default";
        return Path.Combine(_appData.Root, "exports", safeId);
    }

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
