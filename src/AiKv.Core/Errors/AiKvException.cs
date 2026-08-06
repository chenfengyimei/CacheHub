namespace AiKv.Core.Errors;

/// <summary>
/// Exception carrying a stable <see cref="ErrorCode"/> and structured details.
/// CLI/API error responses must derive from this exception, not from exception message strings.
/// </summary>
public sealed class AiKvException : Exception
{
    public ErrorCode Code { get; }

    public string? TraceId { get; init; }

    public IReadOnlyDictionary<string, object?> Details { get; }

    public bool Recoverable { get; init; }

    public AiKvException(ErrorCode code, string message)
        : this(code, message, null, null, false)
    {
    }

    public AiKvException(ErrorCode code, string message, Exception? innerException)
        : this(code, message, innerException, null, false)
    {
    }

    public AiKvException(
        ErrorCode code,
        string message,
        Exception? innerException,
        IReadOnlyDictionary<string, object?>? details,
        bool recoverable)
        : base(message, innerException)
    {
        Code = code;
        Details = details ?? new Dictionary<string, object?>();
        Recoverable = recoverable;
    }
}
