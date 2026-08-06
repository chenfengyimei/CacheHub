using System.Text.Json;
using System.Text.Json.Serialization;

namespace CacheHub.Core.LanguageServers.JsonRpc;

/// <summary>
/// JSON-RPC 2.0 request message.
/// </summary>
public sealed record JsonRpcRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; init; } = "2.0";

    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("method")]
    public required string Method { get; init; }

    [JsonPropertyName("params")]
    public JsonElement? Params { get; init; }
}

/// <summary>
/// JSON-RPC 2.0 notification (no id, no response expected).
/// </summary>
public sealed record JsonRpcNotification
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; init; } = "2.0";

    [JsonPropertyName("method")]
    public required string Method { get; init; }

    [JsonPropertyName("params")]
    public JsonElement? Params { get; init; }
}

/// <summary>
/// JSON-RPC 2.0 response message.
/// </summary>
public sealed record JsonRpcResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; init; } = "2.0";

    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("result")]
    public JsonElement? Result { get; init; }

    [JsonPropertyName("error")]
    public JsonRpcError? Error { get; init; }
}

/// <summary>
/// JSON-RPC error object.
/// </summary>
public sealed record JsonRpcError
{
    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("data")]
    public JsonElement? Data { get; init; }
}

/// <summary>
/// Standard LSP error codes.
/// </summary>
public static class LspErrorCodes
{
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;
    public const int ServerNotInitialized = -32002;
    public const int RequestCancelled = -32800;
    public const int ContentModified = -32801;
}

/// <summary>
/// Serializes and deserializes JSON-RPC messages for LSP communication.
/// Uses Content-Length header framing (LSP standard).
/// </summary>
public static class JsonRpcSerializer
{
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Serializes a JSON-RPC message to LSP wire format (Content-Length header + JSON body).
    /// </summary>
    public static byte[] ToLspMessage(object message)
    {
        var json = JsonSerializer.Serialize(message, _options);
        var body = System.Text.Encoding.UTF8.GetBytes(json);
        var header = System.Text.Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        return [.. header, .. body];
    }

    /// <summary>
    /// Parses a JSON-RPC message from a JSON string.
    /// </summary>
    public static JsonElement ParseMessage(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Creates a request message.
    /// </summary>
    public static JsonRpcRequest CreateRequest(int id, string method, object? parameters = null) => new()
    {
        Id = id,
        Method = method,
        Params = parameters is not null
            ? JsonSerializer.SerializeToElement(parameters, _options)
            : null,
    };

    /// <summary>
    /// Creates a notification message.
    /// </summary>
    public static JsonRpcNotification CreateNotification(string method, object? parameters = null) => new()
    {
        Method = method,
        Params = parameters is not null
            ? JsonSerializer.SerializeToElement(parameters, _options)
            : null,
    };

    /// <summary>
    /// Creates a success response.
    /// </summary>
    public static JsonRpcResponse CreateResponse(int id, object result) => new()
    {
        Id = id,
        Result = JsonSerializer.SerializeToElement(result, _options),
    };

    /// <summary>
    /// Creates an error response.
    /// </summary>
    public static JsonRpcResponse CreateErrorResponse(int id, int code, string message) => new()
    {
        Id = id,
        Error = new JsonRpcError { Code = code, Message = message },
    };
}

/// <summary>
/// LSP initialize parameters.
/// </summary>
public sealed record LspInitializeParams
{
    [JsonPropertyName("processId")]
    public int? ProcessId { get; init; }

    [JsonPropertyName("rootUri")]
    public string? RootUri { get; init; }

    [JsonPropertyName("capabilities")]
    public JsonElement? Capabilities { get; init; }
}

/// <summary>
/// LSP initialize result.
/// </summary>
public sealed record LspInitializeResult
{
    [JsonPropertyName("capabilities")]
    public required LspCapabilities Capabilities { get; init; }

    [JsonPropertyName("serverInfo")]
    public LspServerInfo? ServerInfo { get; init; }
}

/// <summary>
/// LSP server info.
/// </summary>
public sealed record LspServerInfo
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }
}

/// <summary>
/// LSP text document identifier.
/// </summary>
public sealed record TextDocumentIdentifier
{
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }
}

/// <summary>
/// LSP position in a document.
/// </summary>
public sealed record LspPosition
{
    [JsonPropertyName("line")]
    public required int Line { get; init; }

    [JsonPropertyName("character")]
    public required int Character { get; init; }
}

/// <summary>
/// LSP text document position params.
/// </summary>
public sealed record TextDocumentPositionParams
{
    [JsonPropertyName("textDocument")]
    public required TextDocumentIdentifier TextDocument { get; init; }

    [JsonPropertyName("position")]
    public required LspPosition Position { get; init; }
}

/// <summary>
/// LSP didOpen text document params.
/// </summary>
public sealed record DidOpenTextDocumentParams
{
    [JsonPropertyName("textDocument")]
    public required TextDocumentItem TextDocument { get; init; }
}

/// <summary>
/// LSP text document item (full content).
/// </summary>
public sealed record TextDocumentItem
{
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    [JsonPropertyName("languageId")]
    public required string LanguageId { get; init; }

    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("text")]
    public required string Text { get; init; }
}
