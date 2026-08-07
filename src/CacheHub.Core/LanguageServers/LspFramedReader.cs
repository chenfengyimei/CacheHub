using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using CacheHub.Core.LanguageServers.JsonRpc;

namespace CacheHub.Core.LanguageServers;

/// <summary>
/// LSP wire protocol reader: parses Content-Length framed JSON-RPC messages from a stream.
/// R8-W002: JSON-RPC framed reader, request correlation, cancel, notifications.
/// </summary>
public sealed class LspFramedReader : IDisposable
{
    private readonly Stream _stream;
    private readonly byte[] _headerBuffer = new byte[4096];
    private bool _disposed;

    public LspFramedReader(Stream stream)
    {
        _stream = stream;
    }

    /// <summary>
    /// Reads a single LSP message (Content-Length header + JSON body).
    /// Returns null when the stream is closed.
    /// </summary>
    public async Task<JsonElement?> ReadMessageAsync(CancellationToken ct = default)
    {
        // Read headers until blank line
        var contentLength = -1;
        string? line;
        while ((line = await ReadLineAsync(ct)) is not null)
        {
            if (line.Length == 0) break; // End of headers
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                var value = line["Content-Length:".Length..].Trim();
                if (int.TryParse(value, out var parsed))
                    contentLength = parsed;
            }
        }

        if (contentLength <= 0) return null;

        // Read body
        var body = new byte[contentLength];
        var offset = 0;
        while (offset < contentLength)
        {
            var read = await _stream.ReadAsync(body.AsMemory(offset, contentLength - offset), ct);
            if (read == 0) return null; // Stream closed
            offset += read;
        }

        var json = Encoding.UTF8.GetString(body);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        var sb = new StringBuilder();
        var byteBuf = new byte[1];
        while (true)
        {
            var read = await _stream.ReadAsync(byteBuf.AsMemory(0, 1), ct);
            var b = read > 0 ? byteBuf[0] : -1;
            if (b == -1) return sb.Length > 0 ? sb.ToString() : null;
            if (b == '\r')
            {
                // Expect \n
                read = await _stream.ReadAsync(byteBuf.AsMemory(0, 1), ct);
                var next = read > 0 ? byteBuf[0] : -1;
                if (next == '\n') return sb.ToString();
                // Malformed, but continue
                if (next != -1) sb.Append((char)b).Append((char)next);
            }
            else if (b == '\n')
            {
                return sb.ToString();
            }
            else
            {
                sb.Append((char)b);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}

/// <summary>
/// LSP message type classifier.
/// </summary>
public enum LspMessageType
{
    Request,
    Response,
    Notification,
    Unknown,
}

/// <summary>
/// Request correlation manager: tracks pending requests and matches responses.
/// R8-W002: request correlation, cancel, notifications.
/// </summary>
public sealed class LspRequestCorrelator : IDisposable
{
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private int _nextId = 1;
    private bool _disposed;

    /// <summary>
    /// Registers a pending request and returns its ID.
    /// </summary>
    public int RegisterPending()
    {
        var id = Interlocked.Increment(ref _nextId) - 1;
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        return id;
    }

    /// <summary>
    /// Completes a pending request with a response.
    /// </summary>
    public void CompleteResponse(int id, JsonElement result)
    {
        if (_pending.TryRemove(id, out var tcs))
            tcs.TrySetResult(result);
    }

    /// <summary>
    /// Completes a pending request with an error.
    /// </summary>
    public void CompleteError(int id, string error)
    {
        if (_pending.TryRemove(id, out var tcs))
            tcs.TrySetException(new InvalidOperationException(error));
    }

    /// <summary>
    /// Cancels a pending request.
    /// </summary>
    public void Cancel(int id)
    {
        if (_pending.TryRemove(id, out var tcs))
            tcs.TrySetCanceled();
    }

    /// <summary>
    /// Awaits the response for a registered request.
    /// </summary>
    public Task<JsonElement> AwaitResponseAsync(int id, CancellationToken ct = default)
    {
        if (_pending.TryGetValue(id, out var tcs))
        {
            ct.Register(() => Cancel(id));
            return tcs.Task;
        }
        throw new InvalidOperationException($"No pending request with id {id}");
    }

    /// <summary>
    /// Gets the number of pending requests.
    /// </summary>
    public int PendingCount => _pending.Count;

    /// <summary>
    /// Cancels all pending requests.
    /// </summary>
    public void CancelAll()
    {
        foreach (var kvp in _pending)
        {
            kvp.Value.TrySetCanceled();
            _pending.TryRemove(kvp.Key, out _);
        }
    }

    /// <summary>
    /// Classifies a message as request, response, or notification.
    /// </summary>
    public static LspMessageType ClassifyMessage(JsonElement message)
    {
        var hasId = message.TryGetProperty("id", out _);
        var hasMethod = message.TryGetProperty("method", out _);
        var hasResult = message.TryGetProperty("result", out _);
        var hasError = message.TryGetProperty("error", out _);

        if (hasId && (hasResult || hasError)) return LspMessageType.Response;
        if (hasId && hasMethod) return LspMessageType.Request;
        if (!hasId && hasMethod) return LspMessageType.Notification;
        return LspMessageType.Unknown;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelAll();
    }
}

/// <summary>
/// LSP approval model: requires explicit user approval before starting or using LSP.
/// R8-W001: independent process with approval model.
/// </summary>
public sealed class LspApprovalModel
{
    private readonly HashSet<string> _approvedServers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    /// <summary>
    /// Requests approval to start an LSP server.
    /// Returns true if the server was previously approved or auto-approved.
    /// </summary>
    public bool RequestApproval(string serverId, bool autoApprove = false)
    {
        lock (_lock)
        {
            if (_approvedServers.Contains(serverId)) return true;
            if (autoApprove)
            {
                _approvedServers.Add(serverId);
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Grants approval for a server.
    /// </summary>
    public void GrantApproval(string serverId)
    {
        lock (_lock) { _approvedServers.Add(serverId); }
    }

    /// <summary>
    /// Revokes approval for a server.
    /// </summary>
    public void RevokeApproval(string serverId)
    {
        lock (_lock) { _approvedServers.Remove(serverId); }
    }

    /// <summary>
    /// Checks if a server is approved.
    /// </summary>
    public bool IsApproved(string serverId)
    {
        lock (_lock) { return _approvedServers.Contains(serverId); }
    }
}
