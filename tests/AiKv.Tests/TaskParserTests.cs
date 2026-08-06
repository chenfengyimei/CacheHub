using AiKv.Context.Parsing;

namespace AiKv.Tests;

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

        Assert.Equal("deterministic-query-v1", result.QueryParserVersion);
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
}
