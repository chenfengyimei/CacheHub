using CacheHub.Core.Paths;
using CacheHub.Indexing.Detection;
using CacheHub.Indexing.Hashing;
using CacheHub.Indexing.IgnoreRules;
using CacheHub.Indexing.Scanning;

namespace CacheHub.Indexing.Pipeline;

/// <summary>
/// Shared source collection phase for every index entry point. It applies the
/// same ignore rules, file detection, hashing, and text loading before callers
/// persist a snapshot. Database activation intentionally remains a separate
/// concern so a caller can keep its current snapshot atomicity policy.
/// </summary>
public sealed record IndexSourceDocument(
    string RelativePath,
    string FullPath,
    long Size,
    string Language,
    bool IsBinary,
    string ContentHash,
    string Content);

public sealed record IndexSourceCollectionResult(
    IReadOnlyList<IndexSourceDocument> Documents,
    int IgnoredCount,
    int FailedCount,
    string IgnoreRulesHash,
    IReadOnlyList<string> Failures);

public sealed class IndexSourceCollector
{
    public async Task<IndexSourceCollectionResult> CollectAsync(string workspaceRoot, CancellationToken ct = default)
    {
        var ignoreEngine = new IgnoreRuleEngine()
            .WithDefaults()
            .WithGitIgnore(Path.Combine(workspaceRoot, ".gitignore"))
            .WithCacheHubIgnore(Path.Combine(workspaceRoot, ".cachehubignore"));
        var documents = new List<IndexSourceDocument>();
        var failures = new List<string>();
        var ignored = 0;
        var enumerator = new DirectoryEnumerator();

        await foreach (var file in enumerator.EnumerateAsync(workspaceRoot, ct))
        {
            if (file.IsDirectory) continue;

            var relativePath = PathNormalizer.GetRelativePath(workspaceRoot, file.Path);
            if (ignoreEngine.IsIgnored(relativePath))
            {
                ignored++;
                continue;
            }

            try
            {
                var typeInfo = FileTypeDetector.Detect(file.Path, file.Size);
                if (!typeInfo.ShouldIndex)
                {
                    ignored++;
                    continue;
                }

                var hash = await FileHasher.HashAsync(file.Path, file.Size, ct);
                var content = await File.ReadAllTextAsync(file.Path, ct);
                documents.Add(new IndexSourceDocument(
                    relativePath, file.Path, file.Size, typeInfo.Language,
                    typeInfo.IsBinary, hash.Hash, content));
            }
            catch (Exception ex)
            {
                failures.Add($"{relativePath}: {ex.Message}");
            }
        }

        return new IndexSourceCollectionResult(documents, ignored, failures.Count, ignoreEngine.GetRulesHash(), failures);
    }
}
