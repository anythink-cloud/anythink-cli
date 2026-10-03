using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RichardSzalay.MockHttp;

namespace AnythinkMcp.Tests;

[Collection("SequentialConfig")]
public class LocalHttpModeTests : McpTestBase, IAsyncLifetime
{
    private const string Endpoint = "http://localhost:5300/mcp";
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        _app = LocalHttpMode.BuildApp(builder, CreateFactory(new MockHttpMessageHandler()));
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
    }

    private Task<McpClient> Connect() => McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
    {
        Endpoint = new Uri(Endpoint),
        TransportMode = HttpTransportMode.StreamableHttp,
    }, _client, ownsHttpClient: false));

    private Task<HttpResponseMessage> PostMcp(string host, string? origin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"http://{host}/mcp")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        if (origin is not null) request.Headers.Add("Origin", origin);
        return _client.SendAsync(request);
    }

    [Fact]
    public async Task ToolsList_IsTheStdioToolSet()
    {
        await using var mcp = await Connect();

        var tools = await mcp.ListToolsAsync();

        tools.Select(t => t.Name).Should().BeEquivalentTo(StdioToolRegistrationTests.ExpectedStdioTools);
    }

    [Fact]
    public async Task ToolCall_UsesTheSavedCliLogin()
    {
        SetupProjectProfile(name: "local-project");
        await using var mcp = await Connect();

        var result = await mcp.CallToolAsync("config_show");

        ((TextContentBlock)result.Content[0]).Text.Should().Contain("local-project");
    }

    [Theory]
    [InlineData("localhost:5300")]
    [InlineData("127.0.0.1:5300")]
    [InlineData("[::1]:5300")]
    public async Task LoopbackHost_IsAccepted(string host) =>
        (await PostMcp(host)).StatusCode.Should().NotBe(HttpStatusCode.Forbidden);

    [Fact]
    public async Task ForeignHost_IsRefused() =>
        (await PostMcp("attacker.example")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

    [Theory]
    [InlineData("https://attacker.example")]
    [InlineData("http://localhost:9999")]
    public async Task AnyBrowserOrigin_IsRefused(string origin) =>
        (await PostMcp("localhost:5300", origin)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
}
