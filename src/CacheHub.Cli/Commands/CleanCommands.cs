using CacheHub.Storage;

namespace CacheHub.Cli.Commands;

public static class CleanCommands
{
    public static int Handle(string[] args)
    {
        var what = args.FirstOrDefault() ?? "temp";
        var dryRun = args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);

        var appData = new AppDataDirectory();

        return what switch
        {
            "temp" => CleanTemp(appData, dryRun),
            "cache" => CleanCache(appData, dryRun),
            "exports" => CleanExports(appData, dryRun),
            "all" => CleanAll(appData, dryRun),
            _ => PrintUsage(),
        };
    }

    private static int CleanTemp(AppDataDirectory appData, bool dryRun)
    {
        var tempDir = appData.TempPath;
        if (!Directory.Exists(tempDir))
        {
            Console.WriteLine("Temp directory is empty.");
            return 0;
        }

        var files = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories);
        Console.WriteLine($"Temp files: {files.Length}");

        if (dryRun)
        {
            Console.WriteLine("[dry-run] Would delete all temp files.");
            return 0;
        }

        var deletedCount = 0;
        var failedCount = 0;
        foreach (var file in files)
        {
            try { File.Delete(file); deletedCount++; }
            catch (Exception ex) { Console.Error.WriteLine($"  Failed to delete: {file} - {ex.Message}"); failedCount++; }
        }

        Console.WriteLine($"Deleted {deletedCount} temp files." + (failedCount > 0 ? $" ({failedCount} failed)" : ""));
        return 0;
    }

    private static int CleanCache(AppDataDirectory appData, bool dryRun)
    {
        var cacheDir = appData.CachePath;
        if (!Directory.Exists(cacheDir))
        {
            Console.WriteLine("Cache directory is empty.");
            return 0;
        }

        var files = Directory.GetFiles(cacheDir, "*", SearchOption.AllDirectories);
        var totalSize = files.Sum(f => new FileInfo(f).Length);
        Console.WriteLine($"Cache files: {files.Length}");
        Console.WriteLine($"Cache size: {totalSize / 1024.0 / 1024.0:F2} MB");

        if (dryRun)
        {
            Console.WriteLine("[dry-run] Would delete all cache files.");
            return 0;
        }

        var deletedCount = 0;
        var failedCount = 0;
        foreach (var file in files)
        {
            try { File.Delete(file); deletedCount++; }
            catch (Exception ex) { Console.Error.WriteLine($"  Failed to delete: {file} - {ex.Message}"); failedCount++; }
        }

        Console.WriteLine($"Deleted {deletedCount} cache files ({totalSize / 1024.0 / 1024.0:F2} MB)." + (failedCount > 0 ? $" ({failedCount} failed)" : ""));
        return 0;
    }

    private static int CleanExports(AppDataDirectory appData, bool dryRun)
    {
        var exportsDir = Path.Combine(appData.Root, "exports");
        if (!Directory.Exists(exportsDir))
        {
            Console.WriteLine("Exports directory is empty.");
            return 0;
        }

        var dirs = Directory.GetDirectories(exportsDir);
        Console.WriteLine($"Export directories: {dirs.Length}");

        if (dryRun)
        {
            Console.WriteLine("[dry-run] Would delete all export directories.");
            return 0;
        }

        var deletedCount = 0;
        var failedCount = 0;
        foreach (var dir in dirs)
        {
            try { Directory.Delete(dir, recursive: true); deletedCount++; }
            catch (Exception ex) { Console.Error.WriteLine($"  Failed to delete: {dir} - {ex.Message}"); failedCount++; }
        }

        Console.WriteLine($"Deleted {deletedCount} export directories." + (failedCount > 0 ? $" ({failedCount} failed)" : ""));
        return 0;
    }

    private static int CleanAll(AppDataDirectory appData, bool dryRun)
    {
        Console.WriteLine("Cleaning all...");
        CleanTemp(appData, dryRun);
        Console.WriteLine();
        CleanCache(appData, dryRun);
        Console.WriteLine();
        CleanExports(appData, dryRun);
        Console.WriteLine();
        Console.WriteLine("Clean complete.");
        return 0;
    }

    private static int PrintUsage()
    {
        Console.WriteLine("Usage: cachehub clean <temp|cache|exports|all> [--dry-run]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  temp      Clean temporary files");
        Console.WriteLine("  cache     Clean cache files");
        Console.WriteLine("  exports   Clean exported context packages");
        Console.WriteLine("  all       Clean everything above");
        Console.WriteLine("  --dry-run Show what would be deleted without actually deleting");
        return 1;
    }
}
