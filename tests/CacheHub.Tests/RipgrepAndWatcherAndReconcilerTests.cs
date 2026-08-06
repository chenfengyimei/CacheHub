using CacheHub.Indexing.Reconciliation;
using CacheHub.Indexing.Search;
using CacheHub.Indexing.Watching;

namespace CacheHub.Tests;

public class RipgrepAndWatcherAndReconcilerTests
{
    [Fact]
    public async Task RipgrepSearcher_SearchFallback_ShouldFindMatches()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "test.ts"), "export function hello() { return 'world'; }");

        var searcher = new RipgrepSearcher(ripgrepPath: null, autoDetect: false); // Forces fallback
        var results = await searcher.SearchAsync(temp.Path, "hello");

        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Path.EndsWith("test.ts"));
        Assert.Equal(SearchSource.Fallback, results[0].Source);
    }

    [Fact]
    public async Task RipgrepSearcher_SearchFallback_ShouldReturnEmptyForNoMatch()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "test.ts"), "export function hello() {}");

        var searcher = new RipgrepSearcher(ripgrepPath: null, autoDetect: false);
        var results = await searcher.SearchAsync(temp.Path, "nonexistent_function");

        Assert.Empty(results);
    }

    [Fact]
    public void FileWatcher_ShouldEnqueueEvents()
    {
        using var temp = new TempDir();
        using var watcher = new FileWatcher(temp.Path, TimeSpan.FromMilliseconds(100));

        watcher.Start();
        Thread.Sleep(200);

        File.WriteAllText(Path.Combine(temp.Path, "newfile.ts"), "content");
        Thread.Sleep(500); // Wait for debounce flush

        watcher.Stop();
        var events = watcher.DequeueAll();

        Assert.NotEmpty(events);
        Assert.Contains(events, e => e.Path.EndsWith("newfile.ts"));
    }

    [Fact]
    public void FileWatcher_DequeueAll_ShouldReturnEmptyWhenNoEvents()
    {
        using var temp = new TempDir();
        using var watcher = new FileWatcher(temp.Path);

        var events = watcher.DequeueAll();
        Assert.Empty(events);
    }

    [Fact]
    public void ConsistencyReconciler_ShouldDetectAddedFiles()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "a.ts"), "a");

        var result = ConsistencyReconciler.Reconcile(temp.Path, new Dictionary<string, IndexedFileEntry>());

        Assert.Equal(1, result.AddedFiles);
        Assert.False(result.IsConsistent);
    }

    [Fact]
    public void ConsistencyReconciler_ShouldDetectModifiedFiles()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "a.ts"), "short");

        var indexed = new Dictionary<string, IndexedFileEntry>
        {
            ["a.ts"] = new IndexedFileEntry { VirtualPath = "a.ts", Size = 999, Mtime = "2020-01-01T00:00:00.0000000Z" }
        };

        var result = ConsistencyReconciler.Reconcile(temp.Path, indexed);

        Assert.Equal(1, result.ModifiedFiles);
        Assert.False(result.IsConsistent);
    }

    [Fact]
    public void ConsistencyReconciler_ShouldDetectDeletedFiles()
    {
        using var temp = new TempDir();

        var indexed = new Dictionary<string, IndexedFileEntry>
        {
            ["deleted.ts"] = new IndexedFileEntry { VirtualPath = "deleted.ts", Size = 100 }
        };

        var result = ConsistencyReconciler.Reconcile(temp.Path, indexed);

        Assert.Equal(1, result.DeletedFiles);
        Assert.False(result.IsConsistent);
    }

    [Fact]
    public void ConsistencyReconciler_ShouldReportConsistentWhenNothingChanged()
    {
        using var temp = new TempDir();
        var info = new FileInfo(Path.Combine(temp.Path, "stable.ts"));
        File.WriteAllText(info.FullName, "content");

        var indexed = new Dictionary<string, IndexedFileEntry>
        {
            ["stable.ts"] = new IndexedFileEntry
            {
                VirtualPath = "stable.ts",
                Size = info.Length,
                Mtime = info.LastWriteTimeUtc.ToString("O")
            }
        };

        var result = ConsistencyReconciler.Reconcile(temp.Path, indexed);

        Assert.True(result.IsConsistent);
        Assert.Equal(1, result.UnchangedFiles);
    }

    [Fact]
    public void ConsistencyReconciler_ShouldRespectIgnorePatterns()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Path, "node_modules"));
        File.WriteAllText(Path.Combine(temp.Path, "node_modules", "lib.js"), "lib");
        File.WriteAllText(Path.Combine(temp.Path, "app.ts"), "app");

        var result = ConsistencyReconciler.Reconcile(temp.Path, new Dictionary<string, IndexedFileEntry>(),
            ignorePatterns: new HashSet<string> { "node_modules" });

        Assert.Equal(1, result.AddedFiles);
        Assert.DoesNotContain(result.AddedPaths, p => p.Contains("node_modules"));
    }

    [Fact]
    public void ConsistencyReconciler_ShouldDetectSameSizeMutation_ViaMtime()
    {
        using var temp = new TempDir();
        var info = new FileInfo(Path.Combine(temp.Path, "mutated.ts"));
        File.WriteAllText(info.FullName, "12345678"); // 8 bytes

        // Same size but different mtime
        var indexed = new Dictionary<string, IndexedFileEntry>
        {
            ["mutated.ts"] = new IndexedFileEntry
            {
                VirtualPath = "mutated.ts",
                Size = 8, // Same size!
                Mtime = "2020-01-01T00:00:00.0000000Z" // Old mtime
            }
        };

        var result = ConsistencyReconciler.Reconcile(temp.Path, indexed);

        Assert.Equal(1, result.ModifiedFiles);
        Assert.False(result.IsConsistent);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; }
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cachehub_rw_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }
}
