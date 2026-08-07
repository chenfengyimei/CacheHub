using System.Net;
using System.Text;
using CacheHub.Cli.Commands;
using Xunit;

namespace CacheHub.Tests;

/// <summary>
/// Tests the GatewayAgentModelExecutor used by `cachehub benchmark agent`.
/// Verifies it calls the gateway and parses model response + usage correctly.
/// </summary>
public class GatewayAgentModelExecutorTests
{
    [Fact]
    public async Task GenerateAsync_ProxyWithMock_ReturnsContentAndUsage()
    {
        using var gateway = new MockGateway();
        var executor = new GatewayAgentModelExecutor(
            $"http://127.0.0.1:{gateway.Port}", "real-token", "gpt-4o-mini");

        var response = await executor.GenerateAsync("system-prompt", "user-content", CancellationToken.None);

        Assert.NotEmpty(response.Content);
        Assert.True(response.PromptTokens > 0);
        Assert.True(response.CompletionTokens > 0);
        Assert.True(response.Cost.HasValue);

        // The gateway received the Authorization header we sent.
        Assert.Single(gateway.ReceivedResponses);
        Assert.Equal("Bearer real-token", gateway.ReceivedResponses[0]);
    }

    [Fact]
    public async Task GenerateAsync_WhenGatewayReturnsError_Throws()
    {
        using var gateway = new MockGateway { FailNext = true };
        var executor = new GatewayAgentModelExecutor(
            $"http://127.0.0.1:{gateway.Port}", "", "gpt-4o-mini");

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            executor.GenerateAsync("s", "u", CancellationToken.None));
    }

    /// <summary>
    /// Minimal OpenAI-compatible mock gateway using HttpListener on an ephemeral port.
    /// Default response reports usage so the executor can parse tokens.
    /// </summary>
    private sealed class MockGateway : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        public int Port { get; }
        public bool FailNext { get; set; }
        public List<string> ReceivedResponses { get; } = [];

        public MockGateway()
        {
            Port = FindFreePort();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Start();
            _ = Task.Run(() => ServeLoop(_cts.Token));
        }

        private async Task ServeLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch { break; }

                try
                {
                    var auth = ctx.Request.Headers["Authorization"];
                    ReceivedResponses.Add(auth ?? "");

                    if (FailNext)
                    {
                        FailNext = false;
                        var err = "{\"error\":{\"message\":\"rate limit\"}}";
                        var errBytes = System.Text.Encoding.UTF8.GetBytes(err);
                        ctx.Response.StatusCode = 429;
                        ctx.Response.Headers.Set("Content-Type", "application/json");
                        await ctx.Response.OutputStream.WriteAsync(errBytes, ct);
                        ctx.Response.Close();
                        continue;
                    }

                    // Parse the request to count message token roughly; respond with usage.
                    var json = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"```csharp\\n// fixed\\n```\"}}],\"usage\":{\"prompt_tokens\":42,\"completion_tokens\":7,\"total_tokens\":49}}";
                    var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                    ctx.Response.StatusCode = 200;
                    ctx.Response.Headers.Set("Content-Type", "application/json");
                    await ctx.Response.OutputStream.WriteAsync(bytes, ct);
                    ctx.Response.Close();
                }
                catch
                {
                    try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
                }
            }
        }

        private static int FindFreePort()
        {
            var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { }
            _cts.Dispose();
        }
    }
}
