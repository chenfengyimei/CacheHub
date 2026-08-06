using CacheHub.Core.Errors;
using CacheHub.Core.Results;

namespace CacheHub.Tests;

public class ErrorModelTests
{
    [Fact]
    public void CacheHubException_ShouldCarryErrorCode()
    {
        var ex = new CacheHubException(ErrorCode.WorkspaceNotFound, "Workspace not found");

        Assert.Equal(ErrorCode.WorkspaceNotFound, ex.Code);
        Assert.Equal("Workspace not found", ex.Message);
        Assert.False(ex.Recoverable);
        Assert.Empty(ex.Details);
    }

    [Fact]
    public void CacheHubException_ShouldAcceptInnerException()
    {
        var inner = new InvalidOperationException("io error");
        var ex = new CacheHubException(ErrorCode.IndexCorrupted, "Index corrupted", inner);

        Assert.Equal(ErrorCode.IndexCorrupted, ex.Code);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void CacheHubException_ShouldAcceptDetailsAndRecoverable()
    {
        var details = new Dictionary<string, object?> { ["field"] = "path" };
        var ex = new CacheHubException(
            ErrorCode.WorkspacePathEscape,
            "Path escape",
            null,
            details,
            recoverable: true);

        Assert.True(ex.Recoverable);
        Assert.Single(ex.Details);
        Assert.Equal("path", ex.Details["field"]);
    }

    [Fact]
    public void Result_Success_ShouldCarryValue()
    {
        var result = Result<int>.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Result_Failure_ShouldCarryError()
    {
        var result = Result<int>.Failure(ErrorCode.WorkspaceNotFound, "not found");

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.WorkspaceNotFound, result.Error);
        Assert.Equal("not found", result.ErrorMessage);
    }

    [Fact]
    public void Result_Failure_FromException_ShouldExtractCode()
    {
        var ex = new CacheHubException(ErrorCode.InvalidArgument, "bad arg");
        var result = Result<string>.Failure(ex);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.InvalidArgument, result.Error);
    }

    [Fact]
    public void Result_Match_ShouldInvokeCorrectBranch()
    {
        var success = Result<int>.Success(10);
        var failure = Result<int>.Failure(ErrorCode.Unknown, "err");

        var s = success.Match(v => v * 2, (c, m) => -1);
        var f = failure.Match(v => v * 2, (c, m) => -1);

        Assert.Equal(20, s);
        Assert.Equal(-1, f);
    }

    [Fact]
    public void ErrorCode_ShouldHaveStableIntegerValues()
    {
        // Error codes must be stable integers, not auto-assigned.
        Assert.Equal(2001, (int)ErrorCode.WorkspaceNotFound);
        Assert.Equal(2003, (int)ErrorCode.WorkspacePathEscape);
        Assert.Equal(5001, (int)ErrorCode.SecurityPolicyViolation);
    }
}
