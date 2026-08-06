using CacheHub.Context.Parsing;

namespace CacheHub.Tests;

public class TaskParserTests
{
    private readonly TaskParser _parser = new();

    [Fact]
    public void Parse_ShouldExtractFilePaths()
    {
        var result = _parser.Parse("Fix the bug in src/auth/token.ts where refresh fails");

        Assert.NotEmpty(result.ExtractedPaths);
        Assert.Contains(result.ExtractedPaths, p => p.Contains("token.ts"));
    }

    [Fact]
    public void Parse_ShouldExtractPascalCaseSymbols()
    {
        var result = _parser.Parse("Update UserService to handle null in GetUserAsync");

        Assert.Contains(result.ExtractedSymbols, s => s == "UserService");
        Assert.Contains(result.ExtractedSymbols, s => s == "GetUserAsync");
    }

    [Fact]
    public void Parse_ShouldExtractSnakeCaseSymbols()
    {
        var result = _parser.Parse("Fix the user_repository connection in fetch_user_data");

        Assert.Contains(result.ExtractedSymbols, s => s == "user_repository");
        Assert.Contains(result.ExtractedSymbols, s => s == "fetch_user_data");
    }

    [Fact]
    public void Parse_ShouldExtractKeywords()
    {
        var result = _parser.Parse("Fix login token refresh issue");

        Assert.Contains(result.ExtractedKeywords, k => k == "login");
        Assert.Contains(result.ExtractedKeywords, k => k == "token");
        Assert.Contains(result.ExtractedKeywords, k => k == "refresh");
        Assert.Contains(result.ExtractedKeywords, k => k == "issue");
    }

    [Fact]
    public void Parse_ShouldFilterStopWords()
    {
        var result = _parser.Parse("the bug in the code was fixed");

        Assert.DoesNotContain(result.ExtractedKeywords, k => k == "the");
        Assert.DoesNotContain(result.ExtractedKeywords, k => k == "was");
    }

    [Fact]
    public void Parse_ShouldDetectFixOperation()
    {
        var result = _parser.Parse("Fix the login bug");

        Assert.Equal(TaskOperationType.Fix, result.OperationType);
    }

    [Fact]
    public void Parse_ShouldDetectAddOperation()
    {
        var result = _parser.Parse("Add new feature for user profile");

        Assert.Equal(TaskOperationType.Add, result.OperationType);
    }

    [Fact]
    public void Parse_ShouldDetectRefactorOperation()
    {
        var result = _parser.Parse("Refactor the authentication module");

        Assert.Equal(TaskOperationType.Refactor, result.OperationType);
    }

    [Fact]
    public void Parse_ShouldDetectTestOperation()
    {
        var result = _parser.Parse("Write test for the UserService class");

        Assert.Equal(TaskOperationType.Test, result.OperationType);
    }

    [Fact]
    public void Parse_ShouldSetVersion()
    {
        var result = _parser.Parse("any task");

        Assert.Equal("deterministic-query-v2", result.QueryParserVersion);
    }

    [Fact]
    public void Parse_ShouldExtractErrorStackReferences()
    {
        var result = _parser.Parse("Error at MyApp.Services.UserService.GetUserAsync(String id) in line 42");

        Assert.NotEmpty(result.ErrorStackReferences);
    }

    [Fact]
    public void Parse_ShouldHandleEmptyText()
    {
        Assert.Throws<ArgumentException>(() => _parser.Parse(""));
        Assert.Throws<ArgumentException>(() => _parser.Parse("   "));
    }

    // === Unicode/Chinese tests (R2-W001) ===

    [Fact]
    public void Parse_ChineseTask_ShouldDetectFixOperation()
    {
        var result = _parser.Parse("修复登录模块的 token 刷新问题");
        Assert.Equal(TaskOperationType.Fix, result.OperationType);
    }

    [Fact]
    public void Parse_ChineseTask_ShouldDetectAddOperation()
    {
        var result = _parser.Parse("新增用户权限管理功能");
        Assert.Equal(TaskOperationType.Add, result.OperationType);
    }

    [Fact]
    public void Parse_ChineseTask_ShouldDetectRefactorOperation()
    {
        var result = _parser.Parse("重构认证模块的代码结构");
        Assert.Equal(TaskOperationType.Refactor, result.OperationType);
    }

    [Fact]
    public void Parse_ChineseTask_ShouldDetectTestOperation()
    {
        var result = _parser.Parse("测试 UserService 的功能");
        Assert.Equal(TaskOperationType.Test, result.OperationType);
    }

    [Fact]
    public void Parse_ChineseTask_ShouldExtractChineseKeywords()
    {
        var result = _parser.Parse("修复登录模块的 token 刷新问题");
        // Should extract Chinese 2-grams as keywords
        Assert.NotEmpty(result.ExtractedKeywords);
        Assert.Contains(result.ExtractedKeywords, k => k == "登录");
        Assert.Contains(result.ExtractedKeywords, k => k == "刷新");
    }

    [Fact]
    public void Parse_ChineseTask_ShouldExtractCodeIdentifiers()
    {
        var result = _parser.Parse("在 AuthService 中修复 refreshToken 方法");
        Assert.Contains(result.ExtractedSymbols, s => s == "AuthService");
        Assert.Contains(result.ExtractedSymbols, s => s == "refreshToken");
    }

    [Fact]
    public void Parse_MixedTask_ShouldExtractBothLanguages()
    {
        var result = _parser.Parse("Fix the 登录 bug in src/auth/token.ts, AuthService.refreshToken 失败");

        // English path
        Assert.Contains(result.ExtractedPaths, p => p.Contains("token.ts"));
        // English symbols
        Assert.Contains(result.ExtractedSymbols, s => s == "AuthService");
        Assert.Contains(result.ExtractedSymbols, s => s == "refreshToken");
        // Chinese keywords
        Assert.Contains(result.ExtractedKeywords, k => k == "登录");
        Assert.Contains(result.ExtractedKeywords, k => k == "失败");
        // Operation type
        Assert.Equal(TaskOperationType.Fix, result.OperationType);
    }

    [Fact]
    public void Parse_WindowsPath_ShouldExtractBackslashPath()
    {
        var result = _parser.Parse("Fix bug in src\\auth\\token.ts");

        Assert.NotEmpty(result.ExtractedPaths);
        Assert.Contains(result.ExtractedPaths, p => p.Contains("token.ts"));
    }

    [Fact]
    public void Parse_ShouldExtractKebabCaseSymbols()
    {
        var result = _parser.Parse("Update auth-service and user-controller");

        Assert.Contains(result.ExtractedSymbols, s => s == "auth-service");
        Assert.Contains(result.ExtractedSymbols, s => s == "user-controller");
    }

    [Fact]
    public void Parse_ShouldExtractCamelCaseSymbols()
    {
        var result = _parser.Parse("Fix refreshToken and getUserById functions");

        Assert.Contains(result.ExtractedSymbols, s => s == "refreshToken");
        Assert.Contains(result.ExtractedSymbols, s => s == "getUserById");
    }

    [Fact]
    public void Parse_PythonStackTrace_ShouldExtract()
    {
        var result = _parser.Parse("Error: File \"src/app.py\", line 42, in handle_request");

        Assert.NotEmpty(result.ErrorStackReferences);
        Assert.Contains(result.ErrorStackReferences, r => r.Contains("app.py"));
    }
}
