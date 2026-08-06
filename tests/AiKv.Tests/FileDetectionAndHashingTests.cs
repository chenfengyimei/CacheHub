using AiKv.Indexing.Detection;
using AiKv.Indexing.Hashing;
using AiKv.Indexing.States;

namespace AiKv.Tests;

public class FileDetectionAndHashingTests
{
    [Fact]
    public async Task FileTypeDetector_ShouldDetectCSharp()
    {
        var tmp = Path.ChangeExtension(Path.GetTempFileName(), ".cs");
        await File.WriteAllTextAsync(tmp, "namespace Test;");
        try
        {
            var info = FileTypeDetector.Detect(tmp, new FileInfo(tmp).Length);
            Assert.Equal("csharp", info.Language);
            Assert.False(info.IsBinary);
            Assert.True(info.ShouldIndex);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public async Task FileTypeDetector_ShouldDetectTypeScript()
    {
        var tmp = Path.ChangeExtension(Path.GetTempFileName(), ".ts");
        await File.WriteAllTextAsync(tmp, "export const x = 1;");
        try
        {
            var info = FileTypeDetector.Detect(tmp, new FileInfo(tmp).Length);
            Assert.Equal("typescript", info.Language);
            Assert.True(info.ShouldIndex);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public async Task FileTypeDetector_ShouldDetectBinary()
    {
        var tmp = Path.ChangeExtension(Path.GetTempFileName(), ".exe");
        await File.WriteAllBytesAsync(tmp, new byte[] { 0x4D, 0x5A, 0x90, 0x00 });
        try
        {
            var info = FileTypeDetector.Detect(tmp, 4);
            Assert.True(info.IsBinary);
            Assert.False(info.ShouldIndex);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void FileTypeDetector_ShouldDetectCertificate()
    {
        var tmp = Path.ChangeExtension(Path.GetTempFileName(), ".pem");
        File.WriteAllText(tmp, "-----BEGIN CERTIFICATE-----");
        try
        {
            var info = FileTypeDetector.Detect(tmp, 27);
            Assert.Equal(FileCategory.Certificate, info.Category);
            Assert.False(info.ShouldIndex);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void FileTypeDetector_ShouldReturnEmptyForZeroSize()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var info = FileTypeDetector.Detect(tmp, 0);
            Assert.Equal(FileCategory.Empty, info.Category);
            Assert.False(info.ShouldIndex);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public async Task FileHasher_ShouldHashSmallFile()
    {
        var tmp = Path.GetTempFileName();
        await File.WriteAllTextAsync(tmp, "hello world");
        try
        {
            var hash = await FileHasher.HashAsync(tmp, new FileInfo(tmp).Length);
            Assert.True(hash.IsFullHash);
            Assert.StartsWith("sha256:", hash.Hash);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public async Task FileHasher_ShouldFingerprintLargeFile()
    {
        var tmp = Path.GetTempFileName();
        var data = new byte[2 * 1024 * 1024]; // 2 MB > 1 MB threshold
        new Random(42).NextBytes(data);
        await File.WriteAllBytesAsync(tmp, data);
        try
        {
            var hash = await FileHasher.HashAsync(tmp, data.Length);
            Assert.False(hash.IsFullHash);
            Assert.StartsWith("fp:", hash.Hash);
            Assert.Contains(data.Length.ToString(), hash.Hash);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public async Task FileHasher_ShouldProduceDeterministicHash()
    {
        var tmp = Path.GetTempFileName();
        await File.WriteAllTextAsync(tmp, "deterministic content");
        try
        {
            var hash1 = await FileHasher.HashAsync(tmp, new FileInfo(tmp).Length);
            var hash2 = await FileHasher.HashAsync(tmp, new FileInfo(tmp).Length);
            Assert.Equal(hash1.Hash, hash2.Hash);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public async Task FileHasher_ComputeFullHashAsync_ShouldReturnSha256()
    {
        var tmp = Path.GetTempFileName();
        await File.WriteAllTextAsync(tmp, "test");
        try
        {
            var hash = await FileHasher.ComputeFullHashAsync(tmp);
            Assert.StartsWith("sha256:", hash);
            Assert.Equal(71, hash.Length); // "sha256:" + 64 hex chars
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void FileEntry_MarkIndexed_ShouldUpdateState()
    {
        var entry = new FileEntry
        {
            Path = "src/app.ts",
            NormalizedPath = "src/app.ts",
            Size = 100,
            LastModified = DateTimeOffset.UtcNow,
        };

        var indexed = entry.MarkIndexed("sha256:abc", "typescript");

        Assert.Equal(FileState.Indexed, indexed.State);
        Assert.Equal("sha256:abc", indexed.ContentHash);
        Assert.Equal("typescript", indexed.Language);
    }

    [Fact]
    public void FileEntry_MarkIgnored_ShouldUpdateState()
    {
        var entry = new FileEntry
        {
            Path = "secrets.pem",
            NormalizedPath = "secrets.pem",
            Size = 100,
            LastModified = DateTimeOffset.UtcNow,
        };

        var ignored = entry.MarkIgnored();
        Assert.Equal(FileState.Ignored, ignored.State);
    }

    [Fact]
    public void FileEntry_MarkFailed_ShouldRecordError()
    {
        var entry = new FileEntry
        {
            Path = "broken.ts",
            NormalizedPath = "broken.ts",
            Size = 100,
            LastModified = DateTimeOffset.UtcNow,
        };

        var failed = entry.MarkFailed("parse error");
        Assert.Equal(FileState.Failed, failed.State);
        Assert.Equal("parse error", failed.Error);
    }

    [Fact]
    public void FileEntry_MarkStale_ShouldUpdateState()
    {
        var entry = new FileEntry
        {
            Path = "old.ts",
            NormalizedPath = "old.ts",
            Size = 100,
            LastModified = DateTimeOffset.UtcNow,
        };

        var stale = entry.MarkStale();
        Assert.Equal(FileState.Stale, stale.State);
    }
}
