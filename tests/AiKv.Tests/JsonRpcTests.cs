using AiKv.Core.LanguageServers;
using AiKv.Core.LanguageServers.JsonRpc;

namespace AiKv.Tests;

public class JsonRpcTests
{
    [Fact]
    public void JsonRpcSerializer_CreateRequest_ShouldSetFields()
    {
        var req = JsonRpcSerializer.CreateRequest(1, "initialize", new { processId = 123 });

        Assert.Equal("2.0", req.JsonRpc);
        Assert.Equal(1, req.Id);
        Assert.Equal("initialize", req.Method);
        Assert.NotNull(req.Params);
    }

    [Fact]
    public void JsonRpcSerializer_CreateNotification_ShouldNotHaveId()
    {
        var notif = JsonRpcSerializer.CreateNotification("textDocument/didOpen", new { });

        Assert.Equal("2.0", notif.JsonRpc);
        Assert.Equal("textDocument/didOpen", notif.Method);
    }

    [Fact]
    public void JsonRpcSerializer_CreateResponse_ShouldContainResult()
    {
        var resp = JsonRpcSerializer.CreateResponse(1, new { ok = true });

        Assert.Equal("2.0", resp.JsonRpc);
        Assert.Equal(1, resp.Id);
        Assert.NotNull(resp.Result);
        Assert.Null(resp.Error);
    }

    [Fact]
    public void JsonRpcSerializer_CreateErrorResponse_ShouldContainError()
    {
        var resp = JsonRpcSerializer.CreateErrorResponse(1, LspErrorCodes.MethodNotFound, "Method not found");

        Assert.Equal(1, resp.Id);
        Assert.NotNull(resp.Error);
        Assert.Equal(LspErrorCodes.MethodNotFound, resp.Error!.Code);
        Assert.Equal("Method not found", resp.Error.Message);
    }

    [Fact]
    public void JsonRpcSerializer_ToLspMessage_ShouldIncludeContentLengthHeader()
    {
        var req = JsonRpcSerializer.CreateRequest(1, "initialize");
        var bytes = JsonRpcSerializer.ToLspMessage(req);

        var header = System.Text.Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 100));
        Assert.Contains("Content-Length:", header);
        Assert.Contains("\r\n\r\n", header);
    }

    [Fact]
    public void JsonRpcSerializer_ToLspMessage_ShouldContainJsonBody()
    {
        var req = JsonRpcSerializer.CreateRequest(42, "shutdown");
        var bytes = JsonRpcSerializer.ToLspMessage(req);

        var str = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Contains("\"method\":\"shutdown\"", str);
        Assert.Contains("\"id\":42", str);
    }

    [Fact]
    public void JsonRpcSerializer_ParseMessage_ShouldParseJson()
    {
        var json = """{"jsonrpc":"2.0","id":1,"method":"test","params":{}}""";

        var element = JsonRpcSerializer.ParseMessage(json);

        Assert.Equal("2.0", element.GetProperty("jsonrpc").GetString());
        Assert.Equal("test", element.GetProperty("method").GetString());
    }

    [Fact]
    public void LspErrorCodes_ShouldHaveStandardValues()
    {
        Assert.Equal(-32700, LspErrorCodes.ParseError);
        Assert.Equal(-32600, LspErrorCodes.InvalidRequest);
        Assert.Equal(-32601, LspErrorCodes.MethodNotFound);
        Assert.Equal(-32800, LspErrorCodes.RequestCancelled);
    }

    [Fact]
    public void LspInitializeParams_ShouldStoreRootUri()
    {
        var p = new LspInitializeParams
        {
            ProcessId = 123,
            RootUri = "file:///project",
        };

        Assert.Equal("file:///project", p.RootUri);
        Assert.Equal(123, p.ProcessId);
    }

    [Fact]
    public void LspInitializeResult_ShouldContainCapabilities()
    {
        var result = new LspInitializeResult
        {
            Capabilities = new LspCapabilities { SupportsDefinition = true },
            ServerInfo = new LspServerInfo { Name = "test-ls", Version = "1.0" },
        };

        Assert.True(result.Capabilities.SupportsDefinition);
        Assert.Equal("test-ls", result.ServerInfo!.Name);
    }

    [Fact]
    public void TextDocumentItem_ShouldStoreContent()
    {
        var doc = new TextDocumentItem
        {
            Uri = "file:///src/app.ts",
            LanguageId = "typescript",
            Version = 1,
            Text = "const x = 1;",
        };

        Assert.Equal("file:///src/app.ts", doc.Uri);
        Assert.Equal("typescript", doc.LanguageId);
        Assert.Equal("const x = 1;", doc.Text);
    }

    [Fact]
    public void LspPosition_ShouldStoreLineAndCharacter()
    {
        var pos = new LspPosition { Line = 10, Character = 5 };

        Assert.Equal(10, pos.Line);
        Assert.Equal(5, pos.Character);
    }

    [Fact]
    public void TextDocumentPositionParams_ShouldCombineDocAndPosition()
    {
        var params_ = new TextDocumentPositionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = "file:///test.ts" },
            Position = new LspPosition { Line = 0, Character = 0 },
        };

        Assert.Equal("file:///test.ts", params_.TextDocument.Uri);
        Assert.Equal(0, params_.Position.Line);
    }
}
