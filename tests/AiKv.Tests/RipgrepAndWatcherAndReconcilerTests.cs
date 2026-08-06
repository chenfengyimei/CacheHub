using AiKv.Indexing.Reconciliation;
using AiKv.Indexing.Search;
using AiKv.Indexing.Watching;

namespace AiKv.Tests;

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

        var result = ConsistencyReconciler.Reconcile(temp.Path, new Dictionary<string, long>());

        Assert.Equal(1, result.AddedFiles);
        Assert.False(result.IsConsistent);
    }

    [Fact]
    public void ConsistencyReconciler_ShouldDetectModifiedFiles()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "a.ts"), "short");

        var normalized = Path.Combine(temp.Path, "a.ts").Replace('\\', '/');
        var indexed = new Dictionary<string, long> { [normalized] = 999 };

        var result = ConsistencyReconciler.Reconcile(temp.Path, indexed);

        Assert.Equal(1, result.ModifiedFiles);
        Assert.False(result.IsConsistent);
    }

    [Fact]
    public void ConsistencyReconciler_ShouldDetectDeletedFiles()
    {
        using var temp = new TempDir();
        var deletedPath = Path.Combine(temp.Path, "deleted.ts").Replace('\\', '/');

        var indexed = new Dictionary<string, long> { [deletedPath] = 100 };

        var result = ConsistencyReconciler.Reconcile(temp.Path, indexed);

        Assert.Equal(1, result.DeletedFiles);
        Assert.False(result.IsConsistent);
    }

    [Fact]
    public void ConsistencyReconciler_ShouldReportConsistentWhenNothingChanged()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "stable.ts"), "content");
        var normalized = Path.Combine(temp.Path, "stable.ts").Replace('\\', '/');

        var indexed = new Dictionary<string, long> { [normalized] = 7 }; // "content" = 7 bytes

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

        var result = ConsistencyReconciler.Reconcile(temp.Path, new Dictionary<string, long>(),
            ignorePatterns: new HashSet<string> { "node_modules" });

        Assert.Equal(1, result.AddedFiles);
        Assert.DoesNotContain(result.AddedPaths, p => p.Contains("node_modules"));
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; }
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aikv_rw_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }
}
