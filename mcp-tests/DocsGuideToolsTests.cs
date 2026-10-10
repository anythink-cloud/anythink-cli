using System.Text;
using System.Text.Json;
using AnythinkCli.Client;
using AnythinkMcp.Cli;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RichardSzalay.MockHttp;

namespace AnythinkMcp.Tests;

[Collection("SequentialConfig")]
public class DocsGuideToolsTests : McpTestBase
{
    private const string Docs = "https://docs.anythink.cloud";
    private const string Page = "/guides/workflows/how-workflows-work";

    private const string SearchJson = """
        {"query":"workflows","results":[{"title":"How workflows work","url":"/building_my_app/automation/workflows","excerpt":"Workflows run your backend logic.","type":"page"}],"total":1}
        """;

    private readonly MockHttpMessageHandler _docs = new();

    public DocsGuideToolsTests()
    {
        _docs.When($"{Docs}/api/search").Respond("application/json", SearchJson);
        _docs.When($"{Docs}{Page}.md").Respond(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("# How workflows work\n\nSteps and triggers.", Encoding.UTF8, "text/markdown")
        });
    }

    private DocsSiteClient DocsClient => new(new HttpClient(_docs));

    private static CliCommandTool Tool(string name, CliToolScope scope) =>
        CliCommandTool.All(scope).Single(t => t.ProtocolTool.Name == name);

    private static Dictionary<string, JsonElement> Args(object values) =>
        JsonSerializer.SerializeToElement(values).EnumerateObject().ToDictionary(p => p.Name, p => p.Value);

    public static TheoryData<CliToolScope> AllScopes => new() { CliToolScope.Local, CliToolScope.Internal, CliToolScope.Hosted };

    // ── Rule: what the CLI can do with the guides, every MCP mode can do ──────────

    [Theory]
    [MemberData(nameof(AllScopes))]
    public void DocsGuideTools_ExistInEveryMode_AsReadOnlyToolsWithoutAProject(CliToolScope scope)
    {
        var search = Tool("docs_search", scope);
        var read = Tool("docs_read", scope);

        search.ProtocolTool.Annotations!.ReadOnlyHint.Should().BeTrue();
        read.ProtocolTool.Annotations!.ReadOnlyHint.Should().BeTrue();
        search.NeedsProject.Should().BeFalse();
        Properties(search).Should().BeEquivalentTo("query", "limit");
        Properties(read).Should().BeEquivalentTo("slug_or_url");
        CliCommandTool.All(scope).Select(t => t.ProtocolTool.Name).Should().Contain("docs");
    }

    private static IEnumerable<string> Properties(CliCommandTool tool) =>
        tool.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name);

    [Theory]
    [MemberData(nameof(AllScopes))]
    public async Task DocsSearchTool_RunsTheCliCommand_WithoutCredentials(CliToolScope scope)
    {
        DocsSiteClient.Current = DocsClient;

        var result = await Tool("docs_search", scope).RunAsync(Args(new { query = "how do workflows run?", limit = 3 }), client: null);

        result.ExitCode.Should().Be(0, result.Output);
        var json = JsonSerializer.Deserialize<JsonElement>(result.Output);
        json.GetProperty("results")[0].GetProperty("url").GetString().Should().Be($"{Docs}/building_my_app/automation/workflows");
    }

    [Theory]
    [MemberData(nameof(AllScopes))]
    public async Task DocsReadTool_AcceptsAFullDocsUrl_InEveryMode(CliToolScope scope)
    {
        DocsSiteClient.Current = DocsClient;

        var result = await Tool("docs_read", scope).RunAsync(Args(new { slug_or_url = Docs + Page }), client: null);

        result.ExitCode.Should().Be(0, result.Output);
        JsonSerializer.Deserialize<JsonElement>(result.Output).GetProperty("markdown").GetString().Should().Contain("Steps and triggers.");
    }

    [Fact]
    public async Task DocsReadTool_RefusesAUrlOffTheDocsSite()
    {
        DocsSiteClient.Current = DocsClient;
        var anywhere = _docs.When("*").Respond("text/plain", "not ours");

        var result = await Tool("docs_read", CliToolScope.Hosted).RunAsync(Args(new { slug_or_url = "https://169.254.169.254/latest" }), client: null);

        result.ExitCode.Should().Be(1);
        _docs.GetMatchCount(anywhere).Should().Be(0);
    }

    [Fact]
    public async Task InternalRest_DocsSearch_NeedsNoRequestCredentials()
    {
        DocsSiteClient.Current = DocsClient;
        var services = new ServiceCollection().AddSingleton(CreateFactory(new MockHttpMessageHandler())).BuildServiceProvider();

        var result = await McpToolRegistry.ExecuteToolAsync(
            "docs_search", JsonSerializer.SerializeToElement(new { query = "workflows" }), services);

        result.IsError.Should().BeFalse(result.Output);
        result.Output.Should().Contain("How workflows work");
    }

    [Fact]
    public async Task HttpMode_DocsSearch_WorksWithoutCredentials()
    {
        DocsSiteClient.Current = DocsClient;
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(CreateFactory(new MockHttpMessageHandler()));
        builder.Services.AddMcpServer()
            .WithHttpTransport(http => http.SessionMode = HttpServerSessionMode.Stateless)
            .WithTools(CliCommandTool.All(CliToolScope.Internal));
        await using var app = builder.Build();
        app.MapMcp("/mcp");
        await app.StartAsync();
        using var http = app.GetTestClient();
        await using var mcp = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri("http://localhost/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        }, http, ownsHttpClient: false));

        var result = await mcp.CallToolAsync("docs_search", new Dictionary<string, object?> { ["query"] = "workflows" });

        result.IsError.Should().NotBe(true);
        ((TextContentBlock)result.Content[0]).Text.Should().Contain("How workflows work");
        await app.StopAsync();
    }
}
