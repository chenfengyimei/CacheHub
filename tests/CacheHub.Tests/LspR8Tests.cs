using CacheHub.Core.LanguageServers;
using CacheHub.Core.LanguageServers.JsonRpc;

namespace CacheHub.Tests;

/// <summary>
/// Tests for R8: LSP optional module.
/// JSON-RPC framed reader, request correlation, approval model, lifecycle.
/// </summary>
public class LspR8Tests
{
    [Fact]
    public void LspApproval_Default_NotApproved()
    {
        var model = new LspApprovalModel();
        Assert.False(model.IsApproved("csharp-ls"));
    }

    [Fact]
    public void LspApproval_GrantAndRevoke()
    {
        var model = new LspApprovalModel();
        model.GrantApproval("csharp-ls");
        Assert.True(model.IsApproved("csharp-ls"));

        model.RevokeApproval("csharp-ls");
        Assert.False(model.IsApproved("csharp-ls"));
    }

    [Fact]
    public void LspApproval_RequestAutoApprove_GrantsImmediately()
    {
        var model = new LspApprovalModel();
        var result = model.RequestApproval("ts-ls", autoApprove: true);
        Assert.True(result);
        Assert.True(model.IsApproved("ts-ls"));
    }

    [Fact]
    public void LspApproval_RequestWithoutAuto_ReturnsFalse()
    {
        var model = new LspApprovalModel();
        var result = model.RequestApproval("py-ls", autoApprove: false);
        Assert.False(result);
    }

    [Fact]
    public void LspLifecycle_Initialize_SetsReady()
    {
        var lifecycle = new LspLifecycle(new LspServerConfig
        {
            ServerId = "test-ls",
            Command = "test",
            WorkingDirectory = "/tmp",
        });

        lifecycle.Initialize();
        Assert.Equal(LspState.Ready, lifecycle.State);
        Assert.True(lifecycle.IsReady);
    }

    [Fact]
    public void LspLifecycle_Disable_SetsDisabled()
    {
        var lifecycle = new LspLifecycle(new LspServerConfig
        {
            ServerId = "test-ls",
            Command = "test",
            WorkingDirectory = "/tmp",
        });

        lifecycle.Initialize();
        lifecycle.Disable();
        Assert.Equal(LspState.Disabled, lifecycle.State);
        Assert.False(lifecycle.IsReady);
    }

    [Fact]
    public void LspLifecycle_Crash_IncrementsRestartCount()
    {
        var lifecycle = new LspLifecycle(new LspServerConfig
        {
            ServerId = "test-ls",
            Command = "test",
            WorkingDirectory = "/tmp",
            AutoRestart = true,
            MaxRestarts = 3,
        });

        lifecycle.Initialize();
        lifecycle.ReportCrash();

        Assert.Equal(1, lifecycle.RestartCount);
        Assert.NotNull(lifecycle.LastCrashAt);
    }

    [Fact]
    public void LspLifecycle_MaxRestarts_StopsRestarting()
    {
        var lifecycle = new LspLifecycle(new LspServerConfig
        {
            ServerId = "test-ls",
            Command = "test",
            WorkingDirectory = "/tmp",
            AutoRestart = true,
            MaxRestarts = 2,
        });

        lifecycle.Initialize();
        lifecycle.ReportCrash();
        Assert.True(lifecycle.RestartCount <= 2);
    }

    [Fact]
    public async Task LspRequestCorrelator_RegisterAndComplete()
    {
        using var correlator = new LspRequestCorrelator();
        var id = correlator.RegisterPending();

        var responseJson = """{"jsonrpc":"2.0","id":1,"result":{"capabilities":{}}}""";
        var element = System.Text.Json.JsonDocument.Parse(responseJson).RootElement.Clone();

        // AwaitResponse first (async), then complete
        var responseTask = correlator.AwaitResponseAsync(id);
        correlator.CompleteResponse(id, element);
        var result = await responseTask;
        Assert.Equal("2.0", result.GetProperty("jsonrpc").GetString());
    }

    [Fact]
    public void LspRequestCorrelator_Cancel_RemovesPending()
    {
        using var correlator = new LspRequestCorrelator();
        var id = correlator.RegisterPending();

        Assert.Equal(1, correlator.PendingCount);
        correlator.Cancel(id);
        Assert.Equal(0, correlator.PendingCount);
    }

    [Fact]
    public void LspRequestCorrelator_CancelAll_ClearsAll()
    {
        using var correlator = new LspRequestCorrelator();
        correlator.RegisterPending();
        correlator.RegisterPending();
        correlator.RegisterPending();

        Assert.Equal(3, correlator.PendingCount);
        correlator.CancelAll();
        Assert.Equal(0, correlator.PendingCount);
    }

    [Fact]
    public void LspRequestCorrelator_ClassifyMessage_Request()
    {
        var json = """{"jsonrpc":"2.0","id":1,"method":"textDocument/definition","params":{}}""";
        var element = System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();
        var type = LspRequestCorrelator.ClassifyMessage(element);
        Assert.Equal(LspMessageType.Request, type);
    }

    [Fact]
    public void LspRequestCorrelator_ClassifyMessage_Response()
    {
        var json = """{"jsonrpc":"2.0","id":1,"result":{}}""";
        var element = System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();
        var type = LspRequestCorrelator.ClassifyMessage(element);
        Assert.Equal(LspMessageType.Response, type);
    }

    [Fact]
    public void LspRequestCorrelator_ClassifyMessage_Notification()
    {
        var json = """{"jsonrpc":"2.0","method":"textDocument/publishDiagnostics","params":{}}""";
        var element = System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();
        var type = LspRequestCorrelator.ClassifyMessage(element);
        Assert.Equal(LspMessageType.Notification, type);
    }

    [Fact]
    public async Task LspFramedReader_ReadsValidMessage()
    {
        var body = """{"jsonrpc":"2.0","method":"test","params":{}}""";
        var bodyBytes = System.Text.Encoding.UTF8.GetBytes(body);
        var header = System.Text.Encoding.ASCII.GetBytes($"Content-Length: {bodyBytes.Length}\r\n\r\n");
        var fullMessage = header.Concat(bodyBytes).ToArray();

        using var stream = new MemoryStream(fullMessage);
        using var reader = new LspFramedReader(stream);
        var result = await reader.ReadMessageAsync();

        Assert.NotNull(result);
        Assert.Equal("test", result.Value.GetProperty("method").GetString());
    }

    [Fact]
    public async Task LspFramedReader_ReturnsNullOnClosedStream()
    {
        using var stream = new MemoryStream([]);
        using var reader = new LspFramedReader(stream);
        var result = await reader.ReadMessageAsync();
        Assert.Null(result);
    }

    [Fact]
    public void JsonRpcSerializer_CreateRequest_HasCorrectFields()
    {
        var request = JsonRpcSerializer.CreateRequest(1, "initialize", new { processId = 1 });
        Assert.Equal("2.0", request.JsonRpc);
        Assert.Equal(1, request.Id);
        Assert.Equal("initialize", request.Method);
    }

    [Fact]
    public void JsonRpcSerializer_CreateNotification_HasNoId()
    {
        var notification = JsonRpcSerializer.CreateNotification("initialized");
        Assert.Equal("2.0", notification.JsonRpc);
        Assert.Equal("initialized", notification.Method);
    }

    [Fact]
    public void JsonRpcSerializer_ToLspMessage_HasContentLengthHeader()
    {
        var request = JsonRpcSerializer.CreateRequest(1, "test");
        var bytes = JsonRpcSerializer.ToLspMessage(request);
        var header = System.Text.Encoding.ASCII.GetString(bytes);
        Assert.Contains("Content-Length:", header);
    }
}
