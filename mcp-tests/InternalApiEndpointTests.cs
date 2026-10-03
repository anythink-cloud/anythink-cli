using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;

namespace AnythinkMcp.Tests;

public class InternalApiEndpointTests : IAsyncLifetime
{
    private const string InternalToken = "internal-secret";
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        _app = builder.Build();
        InternalApi.MapEndpoints(_app, new InternalApiOptions { Bind = "0.0.0.0", Token = InternalToken });
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
    }

    private static HttpRequestMessage ToolCall(string tool, string? internalToken = InternalToken, string instanceUrl = "https://api.my.anythink.cloud")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/tools/call")
        {
            Content = new StringContent($$$"""{"name":"{{{tool}}}","arguments":{}}""", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Authorization", "Bearer project-token");
        request.Headers.Add("X-Org-Id", "42");
        request.Headers.Add("X-Instance-Url", instanceUrl);
        if (internalToken is not null) request.Headers.Add(InternalApiOptions.TokenHeader, internalToken);
        return request;
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("wrong", HttpStatusCode.Unauthorized)]
    [InlineData(InternalToken, HttpStatusCode.OK)]
    public async Task Tools_RequiresTheInternalToken(string? token, HttpStatusCode expected)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/tools");
        if (token is not null) request.Headers.Add(InternalApiOptions.TokenHeader, token);

        (await _client.SendAsync(request)).StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task ToolsCall_WithoutTheInternalToken_Returns401() =>
        (await _client.SendAsync(ToolCall("entities_list", internalToken: null))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    [Theory]
    [InlineData("https://attacker.example")]
    [InlineData("http://api.my.anythink.cloud")]
    public async Task ToolsCall_WithAnInstanceUrlOffTheAllowlist_Returns400(string instanceUrl) =>
        (await _client.SendAsync(ToolCall("entities_list", instanceUrl: instanceUrl))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

    [Fact]
    public async Task ToolsCall_ForAConfigChangingTool_Returns403() =>
        (await _client.SendAsync(ToolCall("config_use"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
}
