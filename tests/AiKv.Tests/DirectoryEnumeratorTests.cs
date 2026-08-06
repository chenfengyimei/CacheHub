using AiKv.Indexing.Scanning;
using EnumerationOptions = AiKv.Indexing.Scanning.EnumerationOptions;

namespace AiKv.Tests;

public class DirectoryEnumeratorTests
{
    private static readonly EnumerationOptions DefaultOpts = new();

    [Fact]
    public async Task EnumerateAsync_ShouldFindFiles()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "a.ts"), "a");
        File.WriteAllText(Path.Combine(temp.Path, "b.py"), "b");
        Directory.CreateDirectory(Path.Combine(temp.Path, "sub"));
        File.WriteAllText(Path.Combine(temp.Path, "sub", "c.cs"), "c");

        var enumerator = new DirectoryEnumerator();
        var files = new List<DiscoveredFile>();
        await foreach (var f in enumerator.EnumerateAsync(temp.Path))
            files.Add(f);

        var fileEntries = files.Where(f => !f.IsDirectory).ToList();
        Assert.Equal(3, fileEntries.Count);
        Assert.Contains(fileEntries, f => f.Path.EndsWith("a.ts", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(fileEntries, f => f.Path.EndsWith("b.py", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(fileEntries, f => f.Path.EndsWith("c.cs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EnumerateAsync_ShouldRespectMaxDepth()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Path, "l1", "l2", "l3"));
        File.WriteAllText(Path.Combine(temp.Path, "l1", "l2", "l3", "deep.txt"), "deep");
        File.WriteAllText(Path.Combine(temp.Path, "shallow.txt"), "shallow");

        var enumerator = new DirectoryEnumerator(new EnumerationOptions { MaxDepth = 1 });
        var files = new List<DiscoveredFile>();
        await foreach (var f in enumerator.EnumerateAsync(temp.Path))
            files.Add(f);

        var fileEntries = files.Where(f => !f.IsDirectory).ToList();
        Assert.Single(fileEntries);
        Assert.EndsWith("shallow.txt", fileEntries[0].Path);
    }

    [Fact]
    public async Task EnumerateAsync_ShouldRespectMaxFileCount()
    {
        using var temp = new TempDir();
        for (var i = 0; i < 10; i++)
            File.WriteAllText(Path.Combine(temp.Path, $"file{i}.txt"), "x");

        var enumerator = new DirectoryEnumerator(new EnumerationOptions { MaxFileCount = 5 });
        var files = new List<DiscoveredFile>();
        await foreach (var f in enumerator.EnumerateAsync(temp.Path))
            files.Add(f);

        var fileEntries = files.Where(f => !f.IsDirectory).ToList();
        Assert.Equal(5, fileEntries.Count);
    }

    [Fact]
    public async Task EnumerateAsync_ShouldSetExtension()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "code.ts"), "x");

        var enumerator = new DirectoryEnumerator();
        var files = new List<DiscoveredFile>();
        await foreach (var f in enumerator.EnumerateAsync(temp.Path))
            files.Add(f);

        var fileEntry = files.First(f => !f.IsDirectory);
        Assert.Equal(".ts", fileEntry.Extension);
    }

    [Fact]
    public async Task EnumerateAsync_ShouldSkipLargeFiles()
    {
        using var temp = new TempDir();
        var largePath = Path.Combine(temp.Path, "large.txt");
        await File.WriteAllTextAsync(largePath, new string('x', 1024));
        File.WriteAllText(Path.Combine(temp.Path, "small.txt"), "s");

        var enumerator = new DirectoryEnumerator(new EnumerationOptions { MaxFileSizeBytes = 100 });
        var files = new List<DiscoveredFile>();
        await foreach (var f in enumerator.EnumerateAsync(temp.Path))
            files.Add(f);

        var fileEntries = files.Where(f => !f.IsDirectory).ToList();
        Assert.Single(fileEntries);
        Assert.EndsWith("small.txt", fileEntries[0].Path);
    }

    [Fact]
    public async Task EnumerateAsync_ShouldThrowForNonExistentDirectory()
    {
        var enumerator = new DirectoryEnumerator();
        await Assert.ThrowsAsync<DirectoryNotFoundException>(async () =>
        {
            await foreach (var _ in enumerator.EnumerateAsync(@"C:\nonexistent_dir_12345"))
            { }
        });
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; }
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aikv_enum_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
    }
}
