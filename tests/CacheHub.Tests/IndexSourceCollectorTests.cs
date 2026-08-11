using CacheHub.Indexing.Pipeline;

namespace CacheHub.Tests;

public sealed class IndexSourceCollectorTests
{
    [Fact]
    public async Task CollectAsync_AppliesSharedIgnoreRulesAndReturnsIndexedContent()
    {
        var root = Path.Combine(Path.GetTempPath(), "cachehub-collector-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "src"));
        Directory.CreateDirectory(Path.Combine(root, "node_modules", "package"));
        await File.WriteAllTextAsync(Path.Combine(root, "src", "service.cs"), "public sealed class Service { }");
        await File.WriteAllTextAsync(Path.Combine(root, "node_modules", "package", "index.js"), "module.exports = 1;");

        try
        {
            var result = await new IndexSourceCollector().CollectAsync(root);

            var document = Assert.Single(result.Documents);
            Assert.Equal("src/service.cs", document.RelativePath);
            Assert.Equal("csharp", document.Language);
            Assert.StartsWith("sha256:", document.ContentHash);
            Assert.Contains("Service", document.Content);
            Assert.True(result.IgnoredCount >= 1);
            Assert.Empty(result.Failures);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
