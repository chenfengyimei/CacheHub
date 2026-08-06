using System.Collections.Concurrent;

namespace AiKv.Indexing.Watching;

/// <summary>
/// Represents a file system change event.
/// </summary>
public sealed record FileChangeEvent
{
    public required string Path { get; init; }
    public required ChangeType ChangeType { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Type of file system change.
/// </summary>
public enum ChangeType
{
    Created,
    Modified,
    Deleted,
    Renamed,
}

/// <summary>
/// Watches a directory for file changes and enqueues events with debouncing.
/// File system events are acceleration signals only; the final truth is file fingerprint reconciliation.
/// </summary>
public sealed class FileWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly ConcurrentQueue<FileChangeEvent> _eventQueue = new();
    private readonly TimeSpan _debounceInterval;
    private readonly Timer _flushTimer;
    private ConcurrentDictionary<string, FileChangeEvent> _pendingEvents = new();
    private int _overflowCount;
    private bool _disposed;

    public int QueueCount => _eventQueue.Count;
    public int OverflowCount => _overflowCount;
    public bool HasOverflow => _overflowCount > 0;

    public FileWatcher(string directory, TimeSpan? debounceInterval = null)
    {
        _debounceInterval = debounceInterval ?? TimeSpan.FromMilliseconds(500);
        _watcher = new FileSystemWatcher(directory)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.DirectoryName,
        };

        _watcher.Created += OnCreated;
        _watcher.Changed += OnChanged;
        _watcher.Deleted += OnDeleted;
        _watcher.Renamed += OnRenamed;
        _watcher.Error += OnError;

        _flushTimer = new Timer(FlushPending, null, _debounceInterval, _debounceInterval);
    }

    public void Start() => _watcher.EnableRaisingEvents = true;
    public void Stop() => _watcher.EnableRaisingEvents = false;

    /// <summary>
    /// Dequeues all pending events. Returns empty if no events.
    /// </summary>
    public IReadOnlyList<FileChangeEvent> DequeueAll()
    {
        var results = new List<FileChangeEvent>();
        while (_eventQueue.TryDequeue(out var evt))
            results.Add(evt);
        return results;
    }

    /// <summary>
    /// Clears the overflow counter (e.g., after reconciliation has been triggered).
    /// </summary>
    public void ClearOverflow() => Interlocked.Exchange(ref _overflowCount, 0);

    private void OnCreated(object sender, FileSystemEventArgs e) =>
        EnqueueDebounced(e.FullPath, ChangeType.Created);

    private void OnChanged(object sender, FileSystemEventArgs e) =>
        EnqueueDebounced(e.FullPath, ChangeType.Modified);

    private void OnDeleted(object sender, FileSystemEventArgs e) =>
        EnqueueDebounced(e.FullPath, ChangeType.Deleted);

    private void OnRenamed(object sender, RenamedEventArgs e) =>
        EnqueueDebounced(e.FullPath, ChangeType.Renamed);

    private void OnError(object sender, ErrorEventArgs e)
    {
        // Internal buffer overflow — mark for reconciliation.
        Interlocked.Increment(ref _overflowCount);
    }

    private void EnqueueDebounced(string path, ChangeType type)
    {
        _pendingEvents[path] = new FileChangeEvent
        {
            Path = path,
            ChangeType = type,
        };
    }

    private void FlushPending(object? state)
    {
        if (_pendingEvents.IsEmpty) return;

        var oldPending = Interlocked.Exchange(ref _pendingEvents, new ConcurrentDictionary<string, FileChangeEvent>());

        foreach (var kvp in oldPending)
            _eventQueue.Enqueue(kvp.Value);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _flushTimer.Dispose();
        _watcher.Dispose();
    }
}
