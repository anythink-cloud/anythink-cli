using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using RichardSzalay.MockHttp;

namespace AnythinkMcp.Tests;

public class InternalApiEndpointTests : IAsyncLifetime
{
    private const string InternalToken = "internal-secret";
    private const string ProjectApi = "https://api.my.anythink.cloud";
    private readonly MockHttpMessageHandler _upstream = new();
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(new McpClientFactory(null, _upstream));
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

    private static HttpRequestMessage ToolCall(
        string tool, string? internalToken = InternalToken, string instanceUrl = ProjectApi, string arguments = "{}")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/tools/call")
        {
            Content = new StringContent($$$"""{"name":"{{{tool}}}","arguments":{{{arguments}}}}""", Encoding.UTF8, "application/json")
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
    public async Task ToolsCall_ForAToolNotOfferedRemotely_Returns404() =>
        (await _client.SendAsync(ToolCall("config_use"))).StatusCode.Should().Be(HttpStatusCode.NotFound);

    private async Task<JsonElement> Body(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    // ── Contract with the internal callers: /tools carries annotations, /tools/call says whether it failed ──

    [Fact]
    public async Task Tools_CarriesAnnotationsForEveryTool()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/tools");
        request.Headers.Add(InternalApiOptions.TokenHeader, InternalToken);

        var tools = (await Body(await _client.SendAsync(request))).EnumerateArray().ToDictionary(t => t.GetProperty("name").GetString()!);

        tools.Values.Should().AllSatisfy(tool =>
        {
            var annotations = tool.GetProperty("annotations");
            annotations.GetProperty("title").GetString().Should().NotBeNullOrWhiteSpace();
            annotations.GetProperty("read_only_hint").ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
            annotations.GetProperty("destructive_hint").ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
            annotations.GetProperty("open_world_hint").ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
        });
        tools["entities_list"].GetProperty("annotations").GetProperty("read_only_hint").GetBoolean().Should().BeTrue();
        tools["entities_list"].GetProperty("annotations").GetProperty("destructive_hint").GetBoolean().Should().BeFalse();
        tools["entities_delete"].GetProperty("annotations").GetProperty("read_only_hint").GetBoolean().Should().BeFalse();
        tools["entities_delete"].GetProperty("annotations").GetProperty("destructive_hint").GetBoolean().Should().BeTrue();
        tools["integrations_execute"].GetProperty("annotations").GetProperty("open_world_hint").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ToolsCall_ASuccessfulCommand_IsNotMarkedAsAnError()
    {
        _upstream.When($"{ProjectApi}/org/42/entities").Respond("application/json",
            """[{"name":"posts","table_name":"posts","fields":[]}]""");

        var response = await _client.SendAsync(ToolCall("entities_list"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await Body(response);
        body.GetProperty("is_error").GetBoolean().Should().BeFalse();
        body.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString().Should().Contain("posts");
    }

    [Fact]
    public async Task ToolsCall_ACommandThatFails_IsMarkedAsAnError()
    {
        _upstream.When($"{ProjectApi}/org/42/entities").Respond(HttpStatusCode.InternalServerError);

        var response = await _client.SendAsync(ToolCall("entities_list"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Body(response)).GetProperty("is_error").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ToolsCall_AnUnknownParameter_IsMarkedAsAnError_AndNothingIsSent()
    {
        var any = _upstream.When("*").Respond("application/json", "[]");

        var response = await _client.SendAsync(ToolCall("entities_list", arguments: """{"bogus":1}"""));

        var body = await Body(response);
        body.GetProperty("is_error").GetBoolean().Should().BeTrue();
        body.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString().Should().Contain("Unknown parameter: bogus");
        _upstream.GetMatchCount(any).Should().Be(0);
    }
}
