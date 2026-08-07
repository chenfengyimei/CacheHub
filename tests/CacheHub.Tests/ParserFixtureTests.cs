using CacheHub.Core.Parsing;
using CacheHub.Indexing.Parsing;

namespace CacheHub.Tests;

/// <summary>
/// Fixture corpus tests: verify parser coverage on realistic code samples.
/// Each test covers a specific language feature that the regex parser should handle.
/// </summary>
public class ParserFixtureTests
{
    [Fact]
    public void CSharp_ShouldExtractRecordTypes()
    {
        var code = """
            public record User(string Name, int Age);
            public record TokenCache : IDisposable { }
            """;
        var result = new CSharpRegexParser().Parse(code, "test.cs");

        Assert.Contains(result.Symbols, s => s.Kind == SymbolKind.Class && s.Name == "User");
        Assert.Contains(result.Symbols, s => s.Kind == SymbolKind.Class && s.Name == "TokenCache");
    }

    [Fact]
    public void CSharp_ShouldExtractConstructors()
    {
        var code = """
            public class UserService
            {
                public UserService() { }
            }
            """;
        var result = new CSharpRegexParser().Parse(code, "test.cs");

        Assert.Contains(result.Symbols, s => s.Kind == SymbolKind.Method && s.Name == ".ctor");
    }

    [Fact]
    public void CSharp_ShouldExtractFields()
    {
        var code = """
            public class Config
            {
                private readonly string _name;
                public const int MaxSize = 100;
            }
            """;
        var result = new CSharpRegexParser().Parse(code, "test.cs");

        Assert.Contains(result.Symbols, s => s.Kind == SymbolKind.Field && s.Name == "_name");
        Assert.Contains(result.Symbols, s => s.Kind == SymbolKind.Constant && s.Name == "MaxSize");
    }

    [Fact]
    public void CSharp_ShouldExtractExpressionBodiedMethods()
    {
        var code = """
            public class Calculator
            {
                public int Add(int a, int b) => a + b;
            }
            """;
        var result = new CSharpRegexParser().Parse(code, "test.cs");

        Assert.Contains(result.Symbols, s => s.Kind == SymbolKind.Method && s.Name == "Add");
    }

    [Fact]
    public void CSharp_ShouldExtractInheritanceRelations()
    {
        var code = """
            public class UserController : Controller, IDisposable { }
            """;
        var result = new CSharpRegexParser().Parse(code, "test.cs");

        Assert.Contains(result.Relations, r => r.Relation == "inherits" && r.TargetName == "Controller");
        Assert.Contains(result.Relations, r => r.Relation == "inherits" && r.TargetName == "IDisposable");
        Assert.All(result.Relations.Where(r => r.Relation == "inherits"), r =>
        {
            Assert.Equal(RelationType.Syntactic, r.RelationType);
            Assert.True(r.Confidence >= 0.9);
        });
    }

    [Fact]
    public void CSharp_ImportRelations_ShouldBeSyntactic()
    {
        var code = """
            using System;
            using System.Linq;
            """;
        var result = new CSharpRegexParser().Parse(code, "test.cs");

        Assert.Contains(result.Relations, r => r.Relation == "imports" && r.TargetName == "System");
        Assert.All(result.Relations.Where(r => r.Relation == "imports"), r =>
        {
            Assert.Equal(RelationType.Syntactic, r.RelationType);
            Assert.Equal(1.0, r.Confidence);
        });
    }

    [Fact]
    public void CSharp_CallRegex_ShouldExcludeControlFlowKeywords()
    {
        var code = """
            public class Service
            {
                public void Process()
                {
                    if (true) { }
                    for (int i = 0; i < 10; i++) { }
                    DoWork();
                }
            }
            """;
        var result = new CSharpRegexParser().Parse(code, "test.cs");

        Assert.DoesNotContain(result.CallExpressions, c => c.FunctionName == "if");
        Assert.DoesNotContain(result.CallExpressions, c => c.FunctionName == "for");
        Assert.Contains(result.CallExpressions, c => c.FunctionName == "DoWork");
    }

    [Fact]
    public void CSharp_ParserVersion_ShouldBe2()
    {
        var parser = new CSharpRegexParser();
        Assert.Equal("2.0", parser.Version);
        Assert.Equal("csharp-regex-baseline", parser.Id);
    }

    [Fact]
    public void TypeScript_ShouldExtractArrowFunctions()
    {
        var code = """
            const handleClick = (event: Event) => { };
            const fetchData = async (url: string) => { };
            """;
        var result = new TypeScriptRegexParser().Parse(code, "test.ts");

        Assert.Contains(result.Symbols, s => s.Kind == SymbolKind.Function && s.Name == "handleClick");
        Assert.Contains(result.Symbols, s => s.Kind == SymbolKind.Function && s.Name == "fetchData");
    }

    [Fact]
    public void TypeScript_ShouldExtractInheritanceRelations()
    {
        var code = """
            export class AuthService extends BaseService implements IAuth { }
            """;
        var result = new TypeScriptRegexParser().Parse(code, "test.ts");

        Assert.Contains(result.Relations, r => r.Relation == "extends" && r.TargetName == "BaseService");
        Assert.Contains(result.Relations, r => r.Relation == "extends" && r.TargetName == "IAuth");
    }

    [Fact]
    public void TypeScript_ImportRelations_ShouldBeSyntactic()
    {
        var code = """
            import { AuthService } from './auth';
            """;
        var result = new TypeScriptRegexParser().Parse(code, "test.ts");

        Assert.Contains(result.Relations, r => r.Relation == "imports" && r.TargetName == "./auth");
        Assert.All(result.Relations, r =>
        {
            Assert.Equal(RelationType.Syntactic, r.RelationType);
        });
    }

    [Fact]
    public void TypeScript_CallRegex_ShouldExcludeKeywords()
    {
        var code = """
            export function process(items: string[]) {
                if (items.length > 0) {
                    return items.map(i => transform(i));
                }
            }
            """;
        var result = new TypeScriptRegexParser().Parse(code, "test.ts");

        Assert.DoesNotContain(result.CallExpressions, c => c.FunctionName == "if");
        Assert.DoesNotContain(result.CallExpressions, c => c.FunctionName == "return");
    }

    [Fact]
    public void TypeScript_ParserVersion_ShouldBe2()
    {
        var parser = new TypeScriptRegexParser();
        Assert.Equal("2.0", parser.Version);
        Assert.Equal("typescript-regex-baseline", parser.Id);
    }

    [Fact]
    public void Python_ShouldExtractInheritanceRelations()
    {
        var code = """
            class Dog(Animal, IComparable):
                pass
            """;
        var result = new PythonRegexParser().Parse(code, "test.py");

        Assert.Contains(result.Relations, r => r.Relation == "inherits" && r.TargetName == "Animal");
        Assert.Contains(result.Relations, r => r.Relation == "inherits" && r.TargetName == "IComparable");
    }

    [Fact]
    public void Python_ShouldExtractDecoratorRelations()
    {
        var code = """
            @app.route('/api')
            def get_data():
                pass
            """;
        var result = new PythonRegexParser().Parse(code, "test.py");

        Assert.Contains(result.Relations, r => r.Relation == "decorated_by" && r.TargetName == "app");
    }

    [Fact]
    public void Python_ShouldExtractModuleConstants()
    {
        var code = """
            MAX_CONNECTIONS = 100
            DEFAULT_TIMEOUT = 30

            class Client:
                pass
            """;
        var result = new PythonRegexParser().Parse(code, "test.py");

        Assert.Contains(result.Symbols, s => s.Kind == SymbolKind.Constant && s.Name == "MAX_CONNECTIONS");
        Assert.Contains(result.Symbols, s => s.Kind == SymbolKind.Constant && s.Name == "DEFAULT_TIMEOUT");
    }

    [Fact]
    public void Python_ImportRelations_ShouldBeSyntactic()
    {
        var code = """
            import os
            from typing import List
            """;
        var result = new PythonRegexParser().Parse(code, "test.py");

        Assert.Contains(result.Relations, r => r.Relation == "imports" && r.TargetName == "os");
        Assert.Contains(result.Relations, r => r.Relation == "imports" && r.TargetName == "typing");
        Assert.All(result.Relations.Where(r => r.Relation == "imports"), r =>
        {
            Assert.Equal(RelationType.Syntactic, r.RelationType);
            Assert.Equal(1.0, r.Confidence);
        });
    }

    [Fact]
    public void Python_CallRegex_ShouldExcludeKeywords()
    {
        var code = """
            def process(data):
                if data:
                    return transform(data)
            """;
        var result = new PythonRegexParser().Parse(code, "test.py");

        Assert.DoesNotContain(result.CallExpressions, c => c.FunctionName == "if");
        Assert.DoesNotContain(result.CallExpressions, c => c.FunctionName == "return");
        Assert.Contains(result.CallExpressions, c => c.FunctionName == "transform");
    }

    [Fact]
    public void Python_ParserVersion_ShouldBe2()
    {
        var parser = new PythonRegexParser();
        Assert.Equal("2.0", parser.Version);
        Assert.Equal("python-regex-baseline", parser.Id);
    }

    [Fact]
    public void Python_ShouldDistinguishMethodFromModuleFunction()
    {
        var code = """
            def module_helper():
                return 1

            class Service:
                def handle(self):
                    return module_helper()

            def another_top():
                pass
            """;
        var result = new PythonRegexParser().Parse(code, "test.py");

        // Module-level functions → SymbolKind.Function
        Assert.Contains(result.Symbols, s => s.Name == "module_helper" && s.Kind == SymbolKind.Function);
        Assert.Contains(result.Symbols, s => s.Name == "another_top" && s.Kind == SymbolKind.Function);
        Assert.DoesNotContain(result.Symbols, s => s.Name == "module_helper" && s.Kind == SymbolKind.Method);

        // Method inside class → SymbolKind.Method
        Assert.Contains(result.Symbols, s => s.Name == "handle" && s.Kind == SymbolKind.Method);
        Assert.DoesNotContain(result.Symbols, s => s.Name == "handle" && s.Kind == SymbolKind.Function);
    }

    [Fact]
    public void Python_NestedClassMethods_RemainMethods()
    {
        var code = """
            class Outer:
                class Inner:
                    def inner_method(self):
                        pass
                def outer_method(self):
                    pass
            """;
        var result = new PythonRegexParser().Parse(code, "test.py");

        Assert.Contains(result.Symbols, s => s.Name == "inner_method" && s.Kind == SymbolKind.Method);
        Assert.Contains(result.Symbols, s => s.Name == "outer_method" && s.Kind == SymbolKind.Method);
    }

    [Fact]
    public void TypeScript_ShouldExtractDefaultExports()
    {
        var code = """
            export default function connect(config) {
                return config;
            }
            export default class Logger {
                log(msg) {}
            }
            """;
        var result = new TypeScriptRegexParser().Parse(code, "test.ts");

        Assert.Contains(result.Symbols, s => s.Name == "connect" && s.Kind == SymbolKind.Function);
        Assert.Contains(result.Symbols, s => s.Name == "Logger" && s.Kind == SymbolKind.Class);
    }
}
