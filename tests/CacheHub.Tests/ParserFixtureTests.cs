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

    // === Go Parser Tests ===

    [Fact]
    public void Go_ShouldExtractFunctionsAndMethods()
    {
        var code = """
            package main

            func ComputeHash(data string) string {
                return data
            }

            func (s *Server) HandleRequest() {
                return
            }
            """;
        var result = new GoRegexParser().Parse(code, "main.go");

        Assert.Contains(result.Symbols, s => s.Name == "ComputeHash" && s.Kind == SymbolKind.Function);
        Assert.Contains(result.Symbols, s => s.Name == "HandleRequest" && s.Kind == SymbolKind.Method);
    }

    [Fact]
    public void Go_ShouldExtractImports()
    {
        var code = """
            package main

            import "fmt"
            import (
                "os"
                "strings"
            )
            """;
        var result = new GoRegexParser().Parse(code, "main.go");

        Assert.Contains(result.Imports, i => i.Module == "fmt");
        Assert.Contains(result.Imports, i => i.Module == "os");
        Assert.Contains(result.Imports, i => i.Module == "strings");
        Assert.Contains(result.Relations, r => r.Relation == "imports" && r.TargetName == "fmt");
    }

    [Fact]
    public void Go_ShouldExtractTypes()
    {
        var code = """
            type Config struct {
                Port int
            }
            type Handler interface {
                Serve()
            }
            type Score float64
            """;
        var result = new GoRegexParser().Parse(code, "types.go");

        Assert.Contains(result.Symbols, s => s.Name == "Config" && s.Kind == SymbolKind.Struct);
        Assert.Contains(result.Symbols, s => s.Name == "Handler" && s.Kind == SymbolKind.Interface);
        Assert.Contains(result.Symbols, s => s.Name == "Score" && s.Kind == SymbolKind.TypeAlias);
    }

    [Fact]
    public void Go_ParserVersion_ShouldBe1()
    {
        var parser = new GoRegexParser();
        Assert.Equal("1.0", parser.Version);
        Assert.Equal("go-regex-baseline", parser.Id);
    }

    // === Rust Parser Tests ===

    [Fact]
    public void Rust_ShouldExtractFunctionsAndMethods()
    {
        var code = """
            fn compute_hash(data: &str) -> String {
                return data.to_string();
            }

            impl Server {
                fn handle_request(&self) {
                    return;
                }
            }
            """;
        var result = new RustRegexParser().Parse(code, "main.rs");

        Assert.Contains(result.Symbols, s => s.Name == "compute_hash" && s.Kind == SymbolKind.Function);
        Assert.Contains(result.Symbols, s => s.Name == "handle_request" && s.Kind == SymbolKind.Method);
    }

    [Fact]
    public void Rust_ShouldExtractUseDeclarations()
    {
        var code = """
            use std::collections::HashMap;
            use std::io::{Read, Write};
            """;
        var result = new RustRegexParser().Parse(code, "main.rs");

        Assert.Contains(result.Imports, i => i.Module == "std::collections::HashMap");
        Assert.Contains(result.Imports, i => i.Module == "std::io");
        Assert.Contains(result.Relations, r => r.Relation == "imports" && r.TargetName == "std::collections::HashMap");
    }

    [Fact]
    public void Rust_ShouldExtractTypes()
    {
        var code = """
            struct Config {
                port: u16,
            }
            enum Status {
                Active,
                Inactive,
            }
            trait Handler {
                fn serve(&self);
            }
            """;
        var result = new RustRegexParser().Parse(code, "types.rs");

        Assert.Contains(result.Symbols, s => s.Name == "Config" && s.Kind == SymbolKind.Struct);
        Assert.Contains(result.Symbols, s => s.Name == "Status" && s.Kind == SymbolKind.Enum);
        Assert.Contains(result.Symbols, s => s.Name == "Handler" && s.Kind == SymbolKind.Interface);
    }

    [Fact]
    public void Rust_ShouldExtractImplRelations()
    {
        var code = """
            impl Handler for Server {
                fn serve(&self) {}
            }
            """;
        var result = new RustRegexParser().Parse(code, "impl.rs");

        Assert.Contains(result.Relations, r => r.Relation == "implements" && r.TargetName == "Handler");
    }

    [Fact]
    public void Rust_ParserVersion_ShouldBe1()
    {
        var parser = new RustRegexParser();
        Assert.Equal("1.0", parser.Version);
        Assert.Equal("rust-regex-baseline", parser.Id);
    }

    // === Java Parser Tests ===

    [Fact]
    public void Java_ShouldExtractClassAndMethods()
    {
        var code = """
            public class UserService {
                public void handleRequest() {
                    return;
                }
                private int computeScore(String data) {
                    return 0;
                }
            }
            """;
        var result = new JavaRegexParser().Parse(code, "UserService.java");

        Assert.Contains(result.Symbols, s => s.Name == "UserService" && s.Kind == SymbolKind.Class);
        Assert.Contains(result.Symbols, s => s.Name == "handleRequest" && s.Kind == SymbolKind.Method);
        Assert.Contains(result.Symbols, s => s.Name == "computeScore" && s.Kind == SymbolKind.Method);
    }

    [Fact]
    public void Java_ShouldExtractImports()
    {
        var code = """
            import java.util.List;
            import java.io.File;
            """;
        var result = new JavaRegexParser().Parse(code, "Test.java");

        Assert.Contains(result.Imports, i => i.Module == "java.util.List");
        Assert.Contains(result.Imports, i => i.Module == "java.io.File");
        Assert.Contains(result.Relations, r => r.Relation == "imports" && r.TargetName == "java.util.List");
    }

    [Fact]
    public void Java_ShouldExtractInterfaceAndEnum()
    {
        var code = """
            public interface Repository {
                void save();
            }
            public enum Status {
                ACTIVE, INACTIVE
            }
            """;
        var result = new JavaRegexParser().Parse(code, "Types.java");

        Assert.Contains(result.Symbols, s => s.Name == "Repository" && s.Kind == SymbolKind.Interface);
        Assert.Contains(result.Symbols, s => s.Name == "Status" && s.Kind == SymbolKind.Enum);
    }

    [Fact]
    public void Java_ShouldExtractExtendsAndImplements()
    {
        var code = """
            public class UserController extends BaseController implements Serializable {
            }
            """;
        var result = new JavaRegexParser().Parse(code, "UserController.java");

        Assert.Contains(result.Relations, r => r.Relation == "inherits" && r.TargetName == "BaseController");
        Assert.Contains(result.Relations, r => r.Relation == "implements" && r.TargetName == "Serializable");
    }

    [Fact]
    public void Java_ParserVersion_ShouldBe1()
    {
        var parser = new JavaRegexParser();
        Assert.Equal("1.0", parser.Version);
        Assert.Equal("java-regex-baseline", parser.Id);
    }

    // === C/C++ Parser Tests ===

    [Fact]
    public void Cpp_ShouldExtractFunctions()
    {
        var code = """
            int compute_hash(const char* data) {
                return 0;
            }
            void process_request(int id) {
                return;
            }
            """;
        var result = new CppRegexParser().Parse(code, "main.c");

        Assert.Contains(result.Symbols, s => s.Name == "compute_hash" && s.Kind == SymbolKind.Function);
        Assert.Contains(result.Symbols, s => s.Name == "process_request" && s.Kind == SymbolKind.Function);
    }

    [Fact]
    public void Cpp_ShouldExtractIncludes()
    {
        var code = """
            #include <stdio.h>
            #include "utils.h"
            """;
        var result = new CppRegexParser().Parse(code, "main.c");

        Assert.Contains(result.Imports, i => i.Module == "stdio.h");
        Assert.Contains(result.Imports, i => i.Module == "utils.h");
        Assert.Contains(result.Relations, r => r.Relation == "imports" && r.TargetName == "stdio.h");
    }

    [Fact]
    public void Cpp_ShouldExtractClassAndStruct()
    {
        var code = """
            class Server {
            public:
                void start();
            };
            struct Config {
                int port;
            };
            """;
        var result = new CppRegexParser().Parse(code, "types.h");

        Assert.Contains(result.Symbols, s => s.Name == "Server" && s.Kind == SymbolKind.Class);
        Assert.Contains(result.Symbols, s => s.Name == "Config" && s.Kind == SymbolKind.Struct);
        // Methods inside class body
        Assert.Contains(result.Symbols, s => s.Name == "start" && s.Kind == SymbolKind.Method);
    }

    [Fact]
    public void Cpp_ShouldExtractEnumAndDefine()
    {
        var code = """
            #define MAX_CONNECTIONS 100
            enum Status {
                ACTIVE,
                INACTIVE
            };
            """;
        var result = new CppRegexParser().Parse(code, "constants.h");

        Assert.Contains(result.Symbols, s => s.Name == "MAX_CONNECTIONS" && s.Kind == SymbolKind.Constant);
        Assert.Contains(result.Symbols, s => s.Name == "Status" && s.Kind == SymbolKind.Enum);
    }

    [Fact]
    public void Cpp_ShouldExtractInheritance()
    {
        var code = """
            class Dog : public Animal {
            };
            """;
        var result = new CppRegexParser().Parse(code, "dog.h");

        Assert.Contains(result.Relations, r => r.Relation == "inherits" && r.TargetName == "Animal");
    }

    [Fact]
    public void Cpp_ParserVersion_ShouldBe1()
    {
        var parser = new CppRegexParser();
        Assert.Equal("1.0", parser.Version);
        Assert.Equal("cpp-regex-baseline", parser.Id);
    }

    // === PHP Parser Tests ===

    [Fact]
    public void Php_ShouldExtractClassAndMethods()
    {
        var code = """
            <?php
            class UserService {
                public function handleRequest() {
                    return;
                }
                private function computeScore($data) {
                    return 0;
                }
            }
            """;
        var result = new PhpRegexParser().Parse(code, "UserService.php");

        Assert.Contains(result.Symbols, s => s.Name == "UserService" && s.Kind == SymbolKind.Class);
        Assert.Contains(result.Symbols, s => s.Name == "handleRequest" && s.Kind == SymbolKind.Method);
        Assert.Contains(result.Symbols, s => s.Name == "computeScore" && s.Kind == SymbolKind.Method);
    }

    [Fact]
    public void Php_ShouldExtractUseStatements()
    {
        var code = """
            <?php
            use App\Services\AuthService;
            use App\Models\User as UserModel;
            """;
        var result = new PhpRegexParser().Parse(code, "test.php");

        Assert.Contains(result.Imports, i => i.Module == "App\\Services\\AuthService");
        Assert.Contains(result.Imports, i => i.Module == "App\\Models\\User" && i.ImportedName == "UserModel");
    }

    [Fact]
    public void Php_ShouldExtractInterfaceAndTrait()
    {
        var code = """
            <?php
            interface Repository {
                public function save();
            }
            trait Loggable {
                public function log() {}
            }
            """;
        var result = new PhpRegexParser().Parse(code, "Types.php");

        Assert.Contains(result.Symbols, s => s.Name == "Repository" && s.Kind == SymbolKind.Interface);
        Assert.Contains(result.Symbols, s => s.Name == "Loggable" && s.Kind == SymbolKind.Struct);
    }

    [Fact]
    public void Php_ShouldExtractExtendsAndImplements()
    {
        var code = """
            <?php
            class UserController extends BaseController implements JsonSerializable, Countable {
            }
            """;
        var result = new PhpRegexParser().Parse(code, "UserController.php");

        Assert.Contains(result.Relations, r => r.Relation == "inherits" && r.TargetName == "BaseController");
        Assert.Contains(result.Relations, r => r.Relation == "implements" && r.TargetName == "JsonSerializable");
    }

    [Fact]
    public void Php_ParserVersion_ShouldBe1()
    {
        var parser = new PhpRegexParser();
        Assert.Equal("1.0", parser.Version);
        Assert.Equal("php-regex-baseline", parser.Id);
    }

    // === Ruby Parser Tests ===

    [Fact]
    public void Ruby_ShouldExtractClassAndMethods()
    {
        var code = """
            class UserService
              def handle_request
                return
              end

              def compute_score(data)
                return 0
              end
            end
            """;
        var result = new RubyRegexParser().Parse(code, "user_service.rb");

        Assert.Contains(result.Symbols, s => s.Name == "UserService" && s.Kind == SymbolKind.Class);
        Assert.Contains(result.Symbols, s => s.Name == "handle_request" && s.Kind == SymbolKind.Method);
        Assert.Contains(result.Symbols, s => s.Name == "compute_score" && s.Kind == SymbolKind.Method);
    }

    [Fact]
    public void Ruby_ShouldExtractRequires()
    {
        var code = """
            require 'json'
            require_relative 'config'
            """;
        var result = new RubyRegexParser().Parse(code, "app.rb");

        Assert.Contains(result.Imports, i => i.Module == "json");
        Assert.Contains(result.Imports, i => i.Module == "config");
        Assert.Contains(result.Relations, r => r.Relation == "imports" && r.TargetName == "json");
    }

    [Fact]
    public void Ruby_ShouldExtractInheritance()
    {
        var code = """
            class AdminUser < User
            end
            """;
        var result = new RubyRegexParser().Parse(code, "admin_user.rb");

        Assert.Contains(result.Relations, r => r.Relation == "inherits" && r.TargetName == "User");
    }

    [Fact]
    public void Ruby_ShouldExtractModule()
    {
        var code = """
            module Auth
              def authenticate
                return
              end
            end
            """;
        var result = new RubyRegexParser().Parse(code, "auth.rb");

        Assert.Contains(result.Symbols, s => s.Name == "Auth" && s.Kind == SymbolKind.Namespace);
        Assert.Contains(result.Symbols, s => s.Name == "authenticate" && s.Kind == SymbolKind.Method);
    }

    [Fact]
    public void Ruby_ParserVersion_ShouldBe1()
    {
        var parser = new RubyRegexParser();
        Assert.Equal("1.0", parser.Version);
        Assert.Equal("ruby-regex-baseline", parser.Id);
    }

    // === Kotlin Parser Tests ===

    [Fact]
    public void Kotlin_ShouldExtractClassAndMethods()
    {
        var code = """
            class UserService {
                fun handleRequest() {
                    return
                }
                private fun computeScore(data: String): Int {
                    return 0
                }
            }
            """;
        var result = new KotlinRegexParser().Parse(code, "UserService.kt");

        Assert.Contains(result.Symbols, s => s.Name == "UserService" && s.Kind == SymbolKind.Class);
        Assert.Contains(result.Symbols, s => s.Name == "handleRequest" && s.Kind == SymbolKind.Method);
        Assert.Contains(result.Symbols, s => s.Name == "computeScore" && s.Kind == SymbolKind.Method);
    }

    [Fact]
    public void Kotlin_ShouldExtractImports()
    {
        var code = """
            import kotlin.collections.List
            import com.example.Service as Svc
            """;
        var result = new KotlinRegexParser().Parse(code, "main.kt");

        Assert.Contains(result.Imports, i => i.Module == "kotlin.collections.List");
        Assert.Contains(result.Imports, i => i.Module == "com.example.Service" && i.ImportedName == "Svc");
    }

    [Fact]
    public void Kotlin_ShouldExtractInterfaceAndObject()
    {
        var code = """
            interface Repository {
                fun save()
            }
            object Config {
                val port: Int = 8080
            }
            """;
        var result = new KotlinRegexParser().Parse(code, "types.kt");

        Assert.Contains(result.Symbols, s => s.Name == "Repository" && s.Kind == SymbolKind.Interface);
        Assert.Contains(result.Symbols, s => s.Name == "Config" && s.Kind == SymbolKind.Class);
    }

    [Fact]
    public void Kotlin_ShouldExtractInheritance()
    {
        var code = """
            class AdminUser : User, Serializable {
            }
            """;
        var result = new KotlinRegexParser().Parse(code, "admin.kt");

        Assert.Contains(result.Relations, r => r.Relation == "inherits" && r.TargetName == "User");
        Assert.Contains(result.Relations, r => r.Relation == "inherits" && r.TargetName == "Serializable");
    }

    [Fact]
    public void Kotlin_ParserVersion_ShouldBe1()
    {
        var parser = new KotlinRegexParser();
        Assert.Equal("1.0", parser.Version);
        Assert.Equal("kotlin-regex-baseline", parser.Id);
    }

    // === Swift Parser Tests ===

    [Fact]
    public void Swift_ShouldExtractClassAndMethods()
    {
        var code = """
            class UserService {
                func handleRequest() {
                    return
                }
                private func computeScore(_ data: String) -> Int {
                    return 0
                }
            }
            """;
        var result = new SwiftRegexParser().Parse(code, "UserService.swift");

        Assert.Contains(result.Symbols, s => s.Name == "UserService" && s.Kind == SymbolKind.Class);
        Assert.Contains(result.Symbols, s => s.Name == "handleRequest" && s.Kind == SymbolKind.Method);
        Assert.Contains(result.Symbols, s => s.Name == "computeScore" && s.Kind == SymbolKind.Method);
    }

    [Fact]
    public void Swift_ShouldExtractImports()
    {
        var code = """
            import Foundation
            import UIKit
            """;
        var result = new SwiftRegexParser().Parse(code, "main.swift");

        Assert.Contains(result.Imports, i => i.Module == "Foundation");
        Assert.Contains(result.Imports, i => i.Module == "UIKit");
        Assert.Contains(result.Relations, r => r.Relation == "imports" && r.TargetName == "Foundation");
    }

    [Fact]
    public void Swift_ShouldExtractStructAndProtocol()
    {
        var code = """
            struct Config {
                let port: Int
            }
            protocol Repository {
                func save()
            }
            """;
        var result = new SwiftRegexParser().Parse(code, "types.swift");

        Assert.Contains(result.Symbols, s => s.Name == "Config" && s.Kind == SymbolKind.Struct);
        Assert.Contains(result.Symbols, s => s.Name == "Repository" && s.Kind == SymbolKind.Interface);
    }

    [Fact]
    public void Swift_ShouldExtractInheritance()
    {
        var code = """
            class AdminViewController : UIViewController, UITableViewDelegate {
            }
            """;
        var result = new SwiftRegexParser().Parse(code, "admin.swift");

        Assert.Contains(result.Relations, r => r.Relation == "inherits" && r.TargetName == "UIViewController");
        Assert.Contains(result.Relations, r => r.Relation == "inherits" && r.TargetName == "UITableViewDelegate");
    }

    [Fact]
    public void Swift_ParserVersion_ShouldBe1()
    {
        var parser = new SwiftRegexParser();
        Assert.Equal("1.0", parser.Version);
        Assert.Equal("swift-regex-baseline", parser.Id);
    }
}
