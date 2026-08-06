namespace AiKv.Storage;

/// <summary>
/// Manages AI_KV application data directory structure.
/// All AI_KV data (config, index, cache, logs, temp) lives here, never in user source directories.
/// </summary>
public sealed class AppDataDirectory
{
    public string Root { get; }
    public string ConfigPath { get; }
    public string IndexPath { get; }
    public string CachePath { get; }
    public string LogsPath { get; }
    public string TempPath { get; }

    public AppDataDirectory(string? rootPath = null)
    {
        Root = rootPath ?? GetDefaultRoot();
        ConfigPath = Path.Combine(Root, "config");
        IndexPath = Path.Combine(Root, "index");
        CachePath = Path.Combine(Root, "cache");
        LogsPath = Path.Combine(Root, "logs");
        TempPath = Path.Combine(Root, "temp");
    }

    /// <summary>
    /// Ensures all subdirectories exist. Creates them if missing.
    /// </summary>
    public void EnsureCreated()
    {
        foreach (var dir in new[] { Root, ConfigPath, IndexPath, CachePath, LogsPath, TempPath })
        {
            Directory.CreateDirectory(dir);
        }
    }

    /// <summary>
    /// Returns the database path for a specific workspace.
    /// </summary>
    public string GetWorkspaceDatabasePath(string workspaceId) =>
        Path.Combine(IndexPath, $"{workspaceId}.db");

    /// <summary>
    /// Returns the cache directory for a specific workspace.
    /// </summary>
    public string GetWorkspaceCachePath(string workspaceId) =>
        Path.Combine(CachePath, workspaceId);

    /// <summary>
    /// Cleans temporary files older than the specified cutoff.
    /// </summary>
    public void CleanTempOlderThan(TimeSpan age)
    {
        if (!Directory.Exists(TempPath)) return;

        var cutoff = DateTimeOffset.UtcNow - age;
        foreach (var file in Directory.EnumerateFiles(TempPath))
        {
            var lastWrite = new DateTimeOffset(File.GetLastWriteTimeUtc(file));
            if (lastWrite < cutoff)
            {
                File.Delete(file);
            }
        }
    }

    private static string GetDefaultRoot()
    {
        var baseDir = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
        return Path.Combine(baseDir, "AI_KV");
    }
}
