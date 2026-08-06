using CacheHub.Core.Parsing;
using CacheHub.Core.Parsing.Outline;
using CacheHub.Indexing.Parsing;

namespace CacheHub.Tests;

public class ParserTests
{
    [Fact]
    public void CSharpRegexParser_ShouldExtractNamespace()
    {
        var code = """
            namespace MyApp.Services;

            public class UserService { }
            """;
        var parser = new CSharpRegexParser();
        var result = parser.Parse(code, "UserService.cs");

        Assert.Contains(result.Symbols, s => s.Kind == SymbolKind.Namespace && s.Name == "MyApp.Services");
    }

    [Fact]
    public void CSharpRegexParser_ShouldExtractClasses()
    {
        var code = """
            public class UserController { }
            public interface IRepository<T> { }
            """;
        var parser = new CSharpRegexParser();
        var result = parser.Parse(code, "test.cs");

        Assert.Contains(result.Symbols, s => s.Kind == SymbolKind.Class && s.Name == "UserController");
        Assert.Contains(result.Symbols, s => s.Kind == SymbolKind.Interface && s.Name == "IRepository");
    }

    [Fact]
    public void CSharpRegexParser_ShouldExtractUsingImports()
    {
        var code = """
            using System;
            using System.Collections.Generic;
            """;
        var parser = new CSharpRegexParser();
        var result = parser.Parse(code, "test.cs");

        Assert.Equal(2, result.Imports.Count);
        Assert.Contains(result.Imports, i => i.Module == "System");
        Assert.Contains(result.Imports, i => i.Module == "System.Collections.Generic");
    }

    [Fact]
    public void CSharpRegexParser_ShouldExtractMethods()
    {
        var code = """
            public class UserService
            {
                public async Task<User> GetUserAsync(int id) { }
            }
            """;
        var parser = new CSharpRegexParser();
        var result = parser.Parse(code, "test.cs");

        Assert.Contains(result.Symbols, s => s.Kind == SymbolKind.Method && s.Name == "GetUserAsync");
    }

    [Fact]
    public void CSharpRegexParser_CallRelations_ShouldBeHeuristic()
    {
        var code = """
            public class Service
            {
                public void Run()
                {
                    DoSomething();
                    _logger.Log("msg");
                }
            }
            """;
        var parser = new CSharpRegexParser();
        var result = parser.Parse(code, "test.cs");

        Assert.NotEmpty(result.CallExpressions);
        Assert.All(result.Relations, r =>
        {
            Assert.Equal(RelationType.Heuristic, r.RelationType);
            Assert.True(r.Confidence is > 0 and <= 1);
            Assert.Equal("csharp-regex", r.Source);
        });
    }

    [Fact]
    public void TypeScriptRegexParser_ShouldExtractExports()
    {
        var code = """
            export class AuthService { }
            export interface IUser { id: string; }
            export function login(user: string) { }
            """;
        var parser = new TypeScriptRegexParser();
        var result = parser.Parse(code, "auth.ts");

        Assert.Contains(result.Symbols, s => s.Kind == SymbolKind.Class && s.Name == "AuthService");
        Assert.Contains(result.Symbols, s => s.Kind == SymbolKind.Interface && s.Name == "IUser");
        Assert.Contains(result.Symbols, s => s.Kind == SymbolKind.Function && s.Name == "login");
    }

    [Fact]
    public void TypeScriptRegexParser_ShouldExtractImports()
    {
        var code = """
            import { AuthService } from './auth';
            import express from 'express';
            """;
        var parser = new TypeScriptRegexParser();
        var result = parser.Parse(code, "app.ts");

        Assert.Equal(2, result.Imports.Count);
        Assert.Contains(result.Imports, i => i.Module == "./auth");
        Assert.Contains(result.Imports, i => i.Module == "express");
    }

    [Fact]
    public void PythonRegexParser_ShouldExtractClasses()
    {
        var code = """
            class UserRepository:
                def __init__(self):
                    pass

            class AuthService:
                def login(self):
                    pass
            """;
        var parser = new PythonRegexParser();
        var result = parser.Parse(code, "repo.py");

        Assert.Contains(result.Symbols, s => s.Kind == SymbolKind.Class && s.Name == "UserRepository");
        Assert.Contains(result.Symbols, s => s.Kind == SymbolKind.Class && s.Name == "AuthService");
    }

    [Fact]
    public void PythonRegexParser_ShouldExtractFunctions()
    {
        var code = """
            def login(username, password):
                pass

            async def fetch_data(url):
                pass
            """;
        var parser = new PythonRegexParser();
        var result = parser.Parse(code, "auth.py");

        Assert.Contains(result.Symbols, s => s.Kind == SymbolKind.Function && s.Name == "login");
        Assert.Contains(result.Symbols, s => s.Kind == SymbolKind.Function && s.Name == "fetch_data");
    }

    [Fact]
    public void PythonRegexParser_ShouldExtractImports()
    {
        var code = """
            import os
            from typing import List
            """;
        var parser = new PythonRegexParser();
        var result = parser.Parse(code, "test.py");

        Assert.Equal(2, result.Imports.Count);
        Assert.Contains(result.Imports, i => i.Module == "os");
        Assert.Contains(result.Imports, i => i.Module == "typing" && i.ImportedName == "List");
    }

    [Fact]
    public void MarkdownParser_ShouldExtractHeadings()
    {
        var content = """
            # Title

            ## Section 1

            ### Subsection

            ## Section 2
            """;
        var parser = new MarkdownParser();
        var result = parser.Parse(content, "README.md");

        Assert.Contains(result.Symbols, s => s.Name == "Title");
        Assert.Contains(result.Symbols, s => s.Name == "Section 1");
        Assert.Contains(result.Symbols, s => s.Name == "Subsection");
        Assert.Contains(result.Symbols, s => s.Name == "Section 2");
    }

    [Fact]
    public void MarkdownParser_ShouldExtractCodeBlocks()
    {
        var content = """
            ```typescript
            const x = 1;
            ```

            ```python
            x = 1
            ```
            """;
        var parser = new MarkdownParser();
        var result = parser.Parse(content, "README.md");

        Assert.Contains(result.Symbols, s => s.Name == "code:typescript");
        Assert.Contains(result.Symbols, s => s.Name == "code:python");
    }

    [Fact]
    public void DeterministicOutline_ShouldSortByLineThenName()
    {
        var parseResult = new ParseResult
        {
            ParserId = "test",
            ParserVersion = "1.0",
            Language = "csharp",
            Symbols =
            [
                new CodeSymbol { Name = "Zebra", Kind = SymbolKind.Class, StartLine = 10, EndLine = 20 },
                new CodeSymbol { Name = "Alpha", Kind = SymbolKind.Method, StartLine = 5, EndLine = 8 },
                new CodeSymbol { Name = "Beta", Kind = SymbolKind.Method, StartLine = 5, EndLine = 6 },
            ],
        };

        var outline = DeterministicOutlineGenerator.Generate(parseResult, "test.cs");

        Assert.Equal("Alpha", outline.Symbols[0].Name); // Line 5, alphabetical
        Assert.Equal("Beta", outline.Symbols[1].Name);  // Line 5, after Alpha
        Assert.Equal("Zebra", outline.Symbols[2].Name);  // Line 10
    }

    [Fact]
    public void DeterministicOutline_ShouldIncludeParserInfo()
    {
        var parseResult = new ParseResult
        {
            ParserId = "csharp-regex",
            ParserVersion = "1.0",
            Language = "csharp",
        };

        var outline = DeterministicOutlineGenerator.Generate(parseResult, "test.cs");

        Assert.Equal("csharp-regex", outline.ParserId);
        Assert.Equal("1.0", outline.ParserVersion);
        Assert.Equal("csharp", outline.Language);
    }

    [Fact]
    public void CodeRelation_ShouldHaveRequiredFields()
    {
        var relation = new CodeRelation
        {
            RelationType = RelationType.Syntactic,
            Relation = "import",
            TargetName = "System.Linq",
            Confidence = 1.0,
            Source = "csharp-regex",
        };

        Assert.Equal(RelationType.Syntactic, relation.RelationType);
        Assert.Equal(1.0, relation.Confidence);
    }
}
