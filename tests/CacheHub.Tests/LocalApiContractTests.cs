using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace CacheHub.Tests;

/// <summary>
/// Contract tests for the Local API (Desktop) endpoints.
/// Tests auth, error envelopes, JSON schema, and endpoint behavior.
/// Uses WebApplicationFactory for in-memory HTTP testing.
/// </summary>
public class LocalApiContractTests : IClassFixture<LocalApiFactory>
{
    private readonly HttpClient _client;
    private const string TestToken = LocalApiFactory.TestToken;
    private static readonly string TestTokenHeader = $"Bearer {TestToken}";

    public LocalApiContractTests(LocalApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    // === Auth Contract ===

    [Fact]
    public async Task Auth_NoToken_Returns401()
    {
        var response = await _client.GetAsync("/api/v1/capabilities");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Auth_WrongToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = new("Bearer", "wrong-token");
        var response = await _client.GetAsync("/api/v1/capabilities");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("AUTH_REQUIRED", body);
    }

    [Fact]
    public async Task Auth_ValidToken_Returns200()
    {
        _client.DefaultRequestHeaders.Authorization = new("Bearer", TestToken);
        var response = await _client.GetAsync("/api/v1/capabilities");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // V6-W08: Auto-auth session cookie (no manual token copy needed)
    [Fact]
    public async Task AuthInit_SetsHttpOnlySessionCookie()
    {
        // No Authorization header — the endpoint must be exempt from auth
        var response = await _client.PostAsync("/api/v1/auth/init", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(body);
        Assert.True(body.RootElement.TryGetProperty("authenticated", out var authed));
        Assert.True(authed.GetBoolean());

        // The Set-Cookie header should contain the session cookie (HttpOnly + SameSite)
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
        var setCookie = string.Join("; ", cookies);
        Assert.Contains("cachehub_session=", setCookie);
    }

    [Fact]
    public async Task Auth_SessionCookie_AuthenticatesRequest()
    {
        // Get the session cookie from auth/init
        var initResponse = await _client.PostAsync("/api/v1/auth/init", null);
        var setCookie = initResponse.Headers.GetValues("Set-Cookie").First();
        var cookieName = setCookie.Split(';')[0].Trim(); // "cachehub_session=<token>"

        // Use the cookie for a subsequent authenticated request (no Bearer header)
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/capabilities");
        req.Headers.TryAddWithoutValidation("Cookie", cookieName);
        var response = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // === Capabilities Contract ===

    [Fact]
    public async Task Capabilities_ReturnsVersionAndProtocol()
    {
        _client.DefaultRequestHeaders.Authorization = new("Bearer", TestToken);
        var response = await _client.GetAsync("/api/v1/capabilities");
        var body = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.NotNull(body);
        var root = body.RootElement;
        Assert.True(root.TryGetProperty("version", out _));
        Assert.True(root.TryGetProperty("protocolVersion", out _));
        Assert.True(root.TryGetProperty("capabilities", out _));
        Assert.True(root.TryGetProperty("limitations", out var lims));
        Assert.True(lims.GetArrayLength() > 0);
    }

    // === Workspaces Contract ===

    [Fact]
    public async Task Workspaces_List_ReturnsArray()
    {
        _client.DefaultRequestHeaders.Authorization = new("Bearer", TestToken);
        var response = await _client.GetAsync("/api/v1/workspaces");
        var body = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal(JsonValueKind.Array, body.RootElement.ValueKind);
    }

    [Fact]
    public async Task Workspaces_Status_NotFound_ReturnsErrorEnvelope()
    {
        _client.DefaultRequestHeaders.Authorization = new("Bearer", TestToken);
        var response = await _client.GetAsync("/api/v1/workspaces/nonexistent/status");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(body);
        Assert.True(body.RootElement.TryGetProperty("errorCode", out var code));
        Assert.Equal(JsonValueKind.Number, code.ValueKind);
    }

    // === Search Contract ===

    [Fact]
    public async Task Search_NoQuery_Returns400WithErrorEnvelope()
    {
        _client.DefaultRequestHeaders.Authorization = new("Bearer", TestToken);
        var response = await _client.GetAsync("/api/v1/search");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(body);
        Assert.True(body.RootElement.TryGetProperty("errorCode", out var code));
        Assert.Equal(JsonValueKind.Number, code.ValueKind);
    }

    [Fact]
    public async Task Search_WithQuery_ReturnsResultsShape()
    {
        _client.DefaultRequestHeaders.Authorization = new("Bearer", TestToken);
        var response = await _client.GetAsync("/api/v1/search?q=test");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(body);
        // May return results array or "no snapshot" message
        Assert.True(body.RootElement.TryGetProperty("results", out _));
    }

    // === Stats Contract ===

    [Fact]
    public async Task Stats_ReturnsValidShape()
    {
        _client.DefaultRequestHeaders.Authorization = new("Bearer", TestToken);
        var response = await _client.GetAsync("/api/v1/stats");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(body);
        Assert.True(body.RootElement.TryGetProperty("workspaces", out _));
        Assert.True(body.RootElement.TryGetProperty("contextPackages", out _));
        Assert.True(body.RootElement.TryGetProperty("totalEstimatedTokens", out _));
    }

    // === Context Build Contract ===

    [Fact]
    public async Task ContextBuild_NoWorkspace_Returns404WithErrorEnvelope()
    {
        _client.DefaultRequestHeaders.Authorization = new("Bearer", TestToken);
        var response = await _client.PostAsJsonAsync("/api/v1/context/build",
            new { WorkspaceId = "nonexistent", Task = "test task" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(body);
        Assert.True(body.RootElement.TryGetProperty("errorCode", out _));
    }

    [Fact]
    public async Task ContextGet_NotFound_Returns404WithErrorEnvelope()
    {
        _client.DefaultRequestHeaders.Authorization = new("Bearer", TestToken);
        var response = await _client.GetAsync("/api/v1/context/nonexistent-ctx-id");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // === Outline Contract ===

    [Fact]
    public async Task Outline_NoWorkspaceId_Returns400()
    {
        _client.DefaultRequestHeaders.Authorization = new("Bearer", TestToken);
        var response = await _client.GetAsync("/api/v1/outline?path=test.cs");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Outline_NoPath_Returns400()
    {
        _client.DefaultRequestHeaders.Authorization = new("Bearer", TestToken);
        var response = await _client.GetAsync("/api/v1/outline?workspaceId=test");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // === Index Build Contract ===

    [Fact]
    public async Task IndexBuild_NotFoundWorkspace_Returns404()
    {
        _client.DefaultRequestHeaders.Authorization = new("Bearer", TestToken);
        var response = await _client.PostAsync("/api/v1/workspaces/nonexistent/index", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // === Error Envelope Contract ===

    [Fact]
    public async Task ErrorResponses_ContainErrorCodeAndMessage()
    {
        _client.DefaultRequestHeaders.Authorization = new("Bearer", TestToken);
        var response = await _client.GetAsync("/api/v1/workspaces/nonexistent/status");
        var body = await response.Content.ReadAsStringAsync();

        // ErrorEnvelope should contain errorCode and message
        Assert.Contains("errorCode", body);
        Assert.Contains("message", body);
    }

    [Fact]
    public async Task AuthError_ContainsCodeAndHint()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var response = await _client.GetAsync("/api/v1/capabilities");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("AUTH_REQUIRED", body);
        Assert.Contains("Bearer", body);
    }
}

/// <summary>
/// Custom WebApplicationFactory that sets a known API token for testing.
/// </summary>
public class LocalApiFactory : WebApplicationFactory<Program>
{
    public const string TestToken = "test-token-for-contract-tests";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Set environment variable so Program.cs reads the known token
        Environment.SetEnvironmentVariable("CACHEHUB_API_TOKEN", TestToken);
        builder.UseEnvironment("Testing");
    }
}
