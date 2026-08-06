using AiKv.Core.Parsing;
using AiKv.Core.Parsing.Outline;
using AiKv.Core.Parsing.RepoMap;
using AiKv.Indexing.Parsing;
using AiKv.Indexing.Parsing.Cache;

namespace AiKv.Tests;

public class RepoMapAndCacheTests
{
    [Fact]
    public void RepoMapGenerator_ShouldCreateTreeFromOutlines()
    {
        var files = new List<(string, FileOutline)>
        {
            ("src/auth.ts", new FileOutline
            {
                FilePath = "src/auth.ts",
                Language = "typescript",
                ParserId = "typescript-regex",
                ParserVersion = "1.0",
                Symbols =
                [
                    new OutlineEntry { Name = "AuthService", Kind = SymbolKind.Class, StartLine = 1, EndLine = 10 },
                    new OutlineEntry { Name = "login", Kind = SymbolKind.Function, StartLine = 3, EndLine = 5 },
                ],
                Imports = [],
            }),
            ("src/api.ts", new FileOutline
            {
                FilePath = "src/api.ts",
                Language = "typescript",
                ParserId = "typescript-regex",
                ParserVersion = "1.0",
                Symbols =
                [
                    new OutlineEntry { Name = "fetchData", Kind = SymbolKind.Function, StartLine = 1, EndLine = 5 },
                ],
                Imports = [],
            }),
        };

        var map = RepoMapGenerator.Generate("project", files);

        Assert.Equal(2, map.TotalFiles);
        Assert.Equal(3, map.TotalSymbols);
        Assert.True(map.EstimatedTokens > 0);
    }

    [Fact]
    public void RepoMapGenerator_ShouldIncludeKeySymbols()
    {
        var files = new List<(string, FileOutline)>
        {
            ("service.cs", new FileOutline
            {
                FilePath = "service.cs",
                Language = "csharp",
                ParserId = "csharp-regex",
                ParserVersion = "1.0",
                Symbols =
                [
                    new OutlineEntry { Name = "UserService", Kind = SymbolKind.Class, StartLine = 1, EndLine = 20 },
                    new OutlineEntry { Name = "GetUser", Kind = SymbolKind.Method, StartLine = 5, EndLine = 8 },
                    new OutlineEntry { Name = "field1", Kind = SymbolKind.Field, StartLine = 2, EndLine = 2 },
                ],
                Imports = [],
            }),
        };

        var map = RepoMapGenerator.Generate("project", files);
        var fileNode = map.Root.Children.First();

        Assert.Equal(RepoMapNodeType.File, fileNode.Type);
        Assert.True(fileNode.KeySymbols.Count <= 5);
        Assert.Contains(fileNode.KeySymbols, s => s.Name == "UserService");
        Assert.Contains(fileNode.KeySymbols, s => s.Name == "GetUser");
        // Fields are not key symbols
        Assert.DoesNotContain(fileNode.KeySymbols, s => s.Name == "field1");
    }

    [Fact]
    public void ParserCache_GetOrParse_ShouldCacheResults()
    {
        var cache = new ParserCache();
        var parser = new CSharpRegexParser();
        var content = "public class Test { }";

        var result1 = cache.GetOrParse(content, "test.cs", "hash123", parser);
        var result2 = cache.GetOrParse(content, "test.cs", "hash123", parser);

        Assert.Same(result1, result2);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void ParserCache_ShouldReturnNullForMiss()
    {
        var cache = new ParserCache();
        var result = cache.TryGet("nonexistent", "parser", "1.0");

        Assert.Null(result);
    }

    [Fact]
    public void ParserCache_Invalidate_ShouldRemoveEntries()
    {
        var cache = new ParserCache();
        var parser = new CSharpRegexParser();

        cache.GetOrParse("public class A { }", "a.cs", "hash_a", parser);
        cache.GetOrParse("public class B { }", "b.cs", "hash_b", parser);
        Assert.Equal(2, cache.Count);

        cache.Invalidate("hash_a");
        Assert.Equal(1, cache.Count);
        Assert.Null(cache.TryGet("hash_a", parser.Id, parser.Version));
        Assert.NotNull(cache.TryGet("hash_b", parser.Id, parser.Version));
    }

    [Fact]
    public void ParserCache_Clear_ShouldEmptyCache()
    {
        var cache = new ParserCache();
        var parser = new CSharpRegexParser();

        cache.GetOrParse("public class A { }", "a.cs", "hash_a", parser);
        cache.Clear();

        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void ParserCache_DifferentParsers_ShouldStoreSeparately()
    {
        var cache = new ParserCache();
        var csParser = new CSharpRegexParser();
        var mdParser = new MarkdownParser();

        var content = "# Hello\npublic class Test { }";

        cache.Put("hash1", csParser.Id, csParser.Version, csParser.Parse(content, "test.cs"));
        cache.Put("hash1", mdParser.Id, mdParser.Version, mdParser.Parse(content, "test.md"));

        Assert.Equal(2, cache.Count);
        var csResult = cache.TryGet("hash1", csParser.Id, csParser.Version);
        var mdResult = cache.TryGet("hash1", mdParser.Id, mdParser.Version);
        Assert.NotNull(csResult);
        Assert.NotNull(mdResult);
        Assert.NotSame(csResult, mdResult);
    }
}
