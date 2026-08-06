namespace CacheHub.Core.Results;

/// <summary>
/// Discriminated result that carries either a value or an error.
/// Avoids throwing exceptions for expected failure paths.
/// </summary>
public readonly record struct Result<T>
{
    public bool IsSuccess { get; }

    public T? Value { get; }

    public Errors.ErrorCode? Error { get; }

    public string? ErrorMessage { get; }

    private Result(T value)
    {
        IsSuccess = true;
        Value = value;
        Error = null;
        ErrorMessage = null;
    }

    private Result(Errors.ErrorCode code, string message)
    {
        IsSuccess = false;
        Value = default;
        Error = code;
        ErrorMessage = message;
    }

    public static Result<T> Success(T value) => new(value);

    public static Result<T> Failure(Errors.ErrorCode code, string message) => new(code, message);

    public static Result<T> Failure(Errors.CacheHubException ex) => new(ex.Code, ex.Message);

    public TResult Match<TResult>(
        Func<T, TResult> onSuccess,
        Func<Errors.ErrorCode, string, TResult> onFailure)
        => IsSuccess ? onSuccess(Value!) : onFailure(Error!.Value, ErrorMessage!);

    public void Match(
        Action<T> onSuccess,
        Action<Errors.ErrorCode, string> onFailure)
    {
        if (IsSuccess)
        {
            onSuccess(Value!);
        }
        else
        {
            onFailure(Error!.Value, ErrorMessage!);
        }
    }
}
