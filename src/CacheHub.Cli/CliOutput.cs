using System.Text.Json;
using CacheHub.Core.Errors;

namespace CacheHub.Cli;

/// <summary>
/// Utility for consistent CLI output: JSON mode outputs ErrorEnvelope, text mode outputs to stderr.
/// </summary>
public static class CliOutput
{
    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    /// <summary>
    /// Outputs an error in the appropriate format.
    /// JSON mode: ErrorEnvelope to stdout. Text mode: message to stderr.
    /// </summary>
    public static int Error(string message, ErrorCode code = ErrorCode.Unknown, bool recoverable = false,
        bool outputJson = false)
    {
        if (outputJson)
        {
            var envelope = ErrorEnvelope.From(code, message, recoverable);
            Console.WriteLine(JsonSerializer.Serialize(envelope, _jsonOpts));
        }
        else
        {
            Console.Error.WriteLine($"Error: {message}");
        }
        return 1;
    }

    /// <summary>
    /// Outputs a success result in the appropriate format.
    /// JSON mode: object to stdout. Text mode: nothing (caller handles).
    /// </summary>
    public static void Success(object result, bool outputJson)
    {
        if (outputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, _jsonOpts));
        }
    }

    /// <summary>
    /// Checks if JSON output is requested.
    /// </summary>
    public static bool IsJsonOutput(string[] args) =>
        args.Contains("--output=json", StringComparer.OrdinalIgnoreCase) ||
        args.Contains("--json", StringComparer.OrdinalIgnoreCase);
}
