using CacheHub.Storage;

namespace CacheHub.Tests;

public class AppDataDirectoryTests
{
    [Fact]
    public void AppDataDirectory_ShouldHaveDistinctSubdirectories()
    {
        using var temp = new TempDir();
        var appData = new AppDataDirectory(temp.Path);

        Assert.NotEqual(appData.ConfigPath, appData.IndexPath);
        Assert.NotEqual(appData.ConfigPath, appData.CachePath);
        Assert.NotEqual(appData.ConfigPath, appData.LogsPath);
        Assert.NotEqual(appData.ConfigPath, appData.TempPath);
        Assert.NotEqual(appData.IndexPath, appData.CachePath);
        Assert.NotEqual(appData.IndexPath, appData.LogsPath);
        Assert.NotEqual(appData.IndexPath, appData.TempPath);
    }

    [Fact]
    public void EnsureCreated_ShouldCreateAllDirectories()
    {
        using var temp = new TempDir();
        var appData = new AppDataDirectory(temp.Path);

        appData.EnsureCreated();

        Assert.True(Directory.Exists(appData.ConfigPath));
        Assert.True(Directory.Exists(appData.IndexPath));
        Assert.True(Directory.Exists(appData.CachePath));
        Assert.True(Directory.Exists(appData.LogsPath));
        Assert.True(Directory.Exists(appData.TempPath));
    }

    [Fact]
    public void GetWorkspaceDatabasePath_ShouldReturnPathInIndexDir()
    {
        using var temp = new TempDir();
        var appData = new AppDataDirectory(temp.Path);

        var dbPath = appData.GetWorkspaceDatabasePath("ws_001");

        Assert.Contains("ws_001.db", dbPath);
        Assert.Contains(appData.IndexPath, dbPath);
    }

    [Fact]
    public void GetWorkspaceCachePath_ShouldReturnPathInCacheDir()
    {
        using var temp = new TempDir();
        var appData = new AppDataDirectory(temp.Path);

        var cachePath = appData.GetWorkspaceCachePath("ws_001");

        Assert.Contains("ws_001", cachePath);
        Assert.Contains(appData.CachePath, cachePath);
    }

    [Fact]
    public void CleanTempOlderThan_ShouldDeleteOldFilesOnly()
    {
        using var temp = new TempDir();
        var appData = new AppDataDirectory(temp.Path);
        appData.EnsureCreated();

        var oldFile = Path.Combine(appData.TempPath, "old.tmp");
        var newFile = Path.Combine(appData.TempPath, "new.tmp");
        File.WriteAllText(oldFile, "old");
        File.WriteAllText(newFile, "new");

        // Set old file's last write time to 2 days ago
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-2));

        appData.CleanTempOlderThan(TimeSpan.FromDays(1));

        Assert.False(File.Exists(oldFile));
        Assert.True(File.Exists(newFile));
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; }

        public TempDir()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "cachehub_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
