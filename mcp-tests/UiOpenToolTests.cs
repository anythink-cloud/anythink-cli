using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using AnythinkMcp.Ui;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RichardSzalay.MockHttp;

namespace AnythinkMcp.Tests;

public class UiOpenToolTests : IAsyncLifetime
{
    private const string Issuer = "https://issuer.test";
    private const string PublicUrl = "https://mcp.test/mcp";
    private const string InstanceUrl = "https://api.uk01-lon.anythink.cloud";
    private const string DashboardOrigin = "https://uk01-lon.anythink.cloud";
    private const string MyAnythinkOrigin = "https://my.anythink.cloud";
    private const string TransferToken = "tok-9f3a";
    private static readonly Guid ProjectId = Guid.Parse("dddddddd-1111-2222-3333-444444444444");
    private static readonly Guid OtherProjectId = Guid.Parse("eeeeeeee-1111-2222-3333-444444444444");
    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");

    private RSA _rsa = null!;
    private RsaSecurityKey _key = null!;
    private MockHttpMessageHandler _mock = null!;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync() => StartAsync(new Dictionary<string, string> { [InstanceUrl] = DashboardOrigin });

    private async Task StartAsync(Dictionary<string, string> dashboards)
    {
        _rsa = RSA.Create(2048);
        _key = new RsaSecurityKey(_rsa) { KeyId = "test-key-1" };
        _mock = new MockHttpMessageHandler();
        HostedTestSupport.ConfigureIssuer(_mock, Issuer, _key);
        _mock.When(HttpMethod.Post, $"{Issuer}/oauth/v1/token").Respond("application/json", """{"access_token":"exchanged","expires_in":3600}""");

        var options = new HostedMode.Options
        {
            PublicUrl = PublicUrl,
            Issuer = Issuer,
            Audience = PublicUrl,
            Exchange = new TokenExchangeOptions { ClientId = "mcp", ClientSecret = "s3cret", Audience = "anythink-oauth" },
            Ui = new McpUiOptions(dashboards, MyAnythinkOrigin, "x.claudemcpcontent.com"),
        };
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        _app = HostedMode.BuildPublicApp(builder, options, new McpClientFactory(null, _mock),
            jwt => jwt.BackchannelHttpHandler = _mock, exchangeHandler: _mock);
        await _app.StartAsync();
        _client = _app.GetTestClient();
        _client.BaseAddress = new Uri("https://mcp.test/");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        _rsa.Dispose();
    }

    private string SingleProjectToken(string scope = "offline_access account") =>
        HostedTestSupport.CreateToken(_key, Issuer, PublicUrl, tid: "42", instanceUrl: InstanceUrl,
            extraClaims: new Dictionary<string, object> { ["project_id"] = ProjectId.ToString(), ["scope"] = scope });

    private string AllProjectsToken() =>
        HostedTestSupport.CreateToken(_key, Issuer, PublicUrl, tid: null, instanceUrl: null,
            extraClaims: new Dictionary<string, object> { ["projects"] = "all", ["scope"] = "offline_access account" });

    private Task<McpClient> Connect(string token) => McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
    {
        Endpoint = new Uri(PublicUrl),
        TransportMode = HttpTransportMode.StreamableHttp,
        AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" }
    }, _client, ownsHttpClient: false));

    private MockedRequest ProjectsAre(params (Guid Project, Guid Account)[] projects) =>
        _mock.When(HttpMethod.Get, $"{Issuer}/oauth/v1/projects").Respond("application/json",
            "[" + string.Join(',', projects.Select(p => $$"""{"project_id":"{{p.Project}}","account_id":"{{p.Account}}","name":"Shop"}""")) + "]");

    private MockedRequest TransferTokenIs(Guid project, string token) =>
        _mock.When(HttpMethod.Post, $"{Issuer}/v1/accounts/{AccountId}/shared-tenants/{project}/transfer-token")
            .Respond("application/json", $$"""{"transfer_token":"{{token}}"}""");

    private MockedRequest AnyBillingRequest() => _mock.When($"{Issuer}/v1/*").Respond("application/json", "[]");

    private static Dictionary<string, object?> OpenArgs(string tool, object? arguments = null, string? path = null) =>
        new() { ["tool"] = tool, ["arguments"] = arguments, ["path"] = path };

    private static string Text(CallToolResult result) => ((TextContentBlock)result.Content[0]).Text;

    private static JsonElement Opened(CallToolResult result)
    {
        result.IsError.Should().NotBe(true, Text(result));
        return JsonDocument.Parse(Text(result)).RootElement.Clone();
    }

    private static string SignedUrl(string redirect) =>
        $"{DashboardOrigin}/org/42/auth/transfer?transfer_token={TransferToken}&target_org_id=42&redirect_to={Uri.EscapeDataString(redirect)}&embed=true";

    [Fact]
    public async Task UiOpen_WithAccountAccess_ReturnsSignedTransferUrlForTheMappedPage()
    {
        ProjectsAre((ProjectId, AccountId));
        TransferTokenIs(ProjectId, TransferToken);
        await using var mcp = await Connect(SingleProjectToken());

        var opened = Opened(await mcp.CallToolAsync("ui_open", OpenArgs("data_list", new { entity = "posts" })));

        opened.GetProperty("url").GetString().Should().Be(
            "https://uk01-lon.anythink.cloud/org/42/auth/transfer?transfer_token=tok-9f3a&target_org_id=42&redirect_to=%2Forg%2F42%2Fentities%2Fposts&embed=true");
        opened.GetProperty("origin").GetString().Should().Be(DashboardOrigin);
        opened.GetProperty("target").GetString().Should().Be("dashboard");
        opened.GetProperty("signedIn").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task UiOpen_WithoutAccountAccess_ReturnsUnsignedPageUrl_AndMakesNoBillingCall()
    {
        var billing = AnyBillingRequest();
        await using var mcp = await Connect(SingleProjectToken("offline_access"));

        var opened = Opened(await mcp.CallToolAsync("ui_open", OpenArgs("data_list", new { entity = "posts" })));

        opened.GetProperty("url").GetString().Should().Be("https://uk01-lon.anythink.cloud/org/42/entities/posts");
        opened.GetProperty("signedIn").GetBoolean().Should().BeFalse();
        _mock.GetMatchCount(billing).Should().Be(0);
    }

    [Fact]
    public async Task UiOpen_BillingFails_FallsBackToUnsignedUrl()
    {
        ProjectsAre((ProjectId, AccountId));
        _mock.When(HttpMethod.Post, $"{Issuer}/v1/accounts/{AccountId}/shared-tenants/{ProjectId}/transfer-token").Respond(HttpStatusCode.InternalServerError);
        await using var mcp = await Connect(SingleProjectToken());

        var opened = Opened(await mcp.CallToolAsync("ui_open", OpenArgs("data_list", new { entity = "posts" })));

        opened.GetProperty("url").GetString().Should().Be("https://uk01-lon.anythink.cloud/org/42/entities/posts");
        opened.GetProperty("signedIn").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task UiOpen_ProjectMissingFromTheAccountList_FallsBackToUnsignedUrl()
    {
        ProjectsAre((OtherProjectId, AccountId));
        var billing = AnyBillingRequest();
        await using var mcp = await Connect(SingleProjectToken());

        var opened = Opened(await mcp.CallToolAsync("ui_open", OpenArgs("data_list", new { entity = "posts" })));

        opened.GetProperty("signedIn").GetBoolean().Should().BeFalse();
        _mock.GetMatchCount(billing).Should().Be(0);
    }

    [Fact]
    public async Task UiOpen_SingleProjectConnection_RefusesAnotherProject()
    {
        ProjectsAre((OtherProjectId, AccountId));
        await using var mcp = await Connect(SingleProjectToken());

        var result = await mcp.CallToolAsync("ui_open", OpenArgs("data_list", new { entity = "posts", project = OtherProjectId.ToString() }));

        result.IsError.Should().BeTrue();
        Text(result).Should().Contain("one project only").And.NotContain("tok");
    }

    [Fact]
    public async Task UiOpen_AllProjectsConnection_SignsForTheChosenProjectsAccount()
    {
        var exchanged = HostedTestSupport.CreateToken(_key, Issuer, PublicUrl, tid: "42", instanceUrl: InstanceUrl,
            extraClaims: new Dictionary<string, object> { ["project_id"] = OtherProjectId.ToString() });
        _mock.Clear();
        HostedTestSupport.ConfigureIssuer(_mock, Issuer, _key);
        _mock.When(HttpMethod.Post, $"{Issuer}/oauth/v1/token").Respond("application/json", $$"""{"access_token":"{{exchanged}}","expires_in":3600}""");
        ProjectsAre((ProjectId, AccountId), (OtherProjectId, AccountId));
        var transfer = _mock.When(HttpMethod.Post, $"{Issuer}/v1/accounts/{AccountId}/shared-tenants/{OtherProjectId}/transfer-token")
            .Respond("application/json", $$"""{"transfer_token":"{{TransferToken}}"}""");
        await using var mcp = await Connect(AllProjectsToken());

        var opened = Opened(await mcp.CallToolAsync("ui_open", OpenArgs("data_list", new { entity = "posts", project = OtherProjectId.ToString() })));

        opened.GetProperty("url").GetString().Should().Be(SignedUrl("/org/42/entities/posts"));
        _mock.GetMatchCount(transfer).Should().Be(1);
    }

    [Fact]
    public async Task UiOpen_AllProjectsConnection_WithoutAProject_IsRefused()
    {
        var billing = AnyBillingRequest();
        await using var mcp = await Connect(AllProjectsToken());

        var result = await mcp.CallToolAsync("ui_open", OpenArgs("data_list", new { entity = "posts" }));

        result.IsError.Should().BeTrue();
        _mock.GetMatchCount(billing).Should().Be(0);
    }

    [Fact]
    public async Task UiOpen_InstanceWithoutDashboard_SaysSo()
    {
        await DisposeAsync();
        await StartAsync(new Dictionary<string, string> { ["https://api.other.anythink.cloud"] = "https://other.anythink.cloud" });
        await using var mcp = await Connect(SingleProjectToken());

        var result = await mcp.CallToolAsync("ui_open", OpenArgs("data_list", new { entity = "posts" }));

        result.IsError.Should().BeTrue();
        Text(result).Should().Contain("dashboard can't be shown here for this project's region");
    }

    [Theory]
    [InlineData("/org/42/../43/x")]
    [InlineData("/org/42//evil.com")]
    [InlineData("/org/42/a\\b")]
    [InlineData("/org/42/https://x")]
    [InlineData("/org/42/a\nb")]
    [InlineData("/org/42/%2e%2e/43/x")]
    [InlineData("/org/42/.%2E/43/x")]
    [InlineData("/org/42/%2e./43/x")]
    [InlineData("/org/42/%5c43")]
    [InlineData("/org/42/%00")]
    [InlineData("/org/42/a:b")]
    [InlineData("/org/42/%252e%252e/43/x")]
    [InlineData("/org/42/%25252e%25252e/43/x")]
    [InlineData("/org/42/%255c43")]
    public async Task UiOpen_Path_RejectsEscapes(string path)
    {
        var billing = AnyBillingRequest();
        await using var mcp = await Connect(SingleProjectToken());

        var result = await mcp.CallToolAsync("ui_open", OpenArgs("data_list", path: path));

        result.IsError.Should().BeTrue();
        Text(result).Should().Contain("isn't a valid dashboard path");
        _mock.GetMatchCount(billing).Should().Be(0);
    }

    [Theory]
    [InlineData("//evil.com")]
    [InlineData("https://x")]
    [InlineData("no-leading-slash")]
    public async Task UiOpen_Path_NotUnderTheOrg_IsRefused(string path)
    {
        await using var mcp = await Connect(SingleProjectToken());

        var result = await mcp.CallToolAsync("ui_open", OpenArgs("data_list", path: path));

        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task UiOpen_Path_Over512Characters_IsRefused()
    {
        await using var mcp = await Connect(SingleProjectToken("offline_access"));

        var result = await mcp.CallToolAsync("ui_open", OpenArgs("data_list", path: LongPath(513)));

        result.IsError.Should().BeTrue();
        Text(result).Should().Contain("isn't a valid dashboard path");
    }

    [Fact]
    public async Task UiOpen_Path_Of512Characters_IsAccepted()
    {
        await using var mcp = await Connect(SingleProjectToken("offline_access"));

        var opened = Opened(await mcp.CallToolAsync("ui_open", OpenArgs("data_list", path: LongPath(512))));

        opened.GetProperty("url").GetString().Should().Be(DashboardOrigin + LongPath(512));
    }

    private static string LongPath(int length) => "/org/42/" + new string('a', length - "/org/42/".Length);

    [Fact]
    public async Task UiOpen_Path_WithEncodedSpace_IsStillAccepted()
    {
        await using var mcp = await Connect(SingleProjectToken("offline_access"));

        var opened = Opened(await mcp.CallToolAsync("ui_open", OpenArgs("data_list", path: "/org/42/entities/my%20posts")));

        opened.GetProperty("url").GetString().Should().Be(DashboardOrigin + "/org/42/entities/my%20posts");
    }

    [Fact]
    public async Task UiOpen_BillingTimesOut_FallsBackToUnsignedUrl()
    {
        ProjectsAre((ProjectId, AccountId));
        _mock.When(HttpMethod.Post, $"{Issuer}/v1/accounts/{AccountId}/shared-tenants/{ProjectId}/transfer-token").Throw(new TaskCanceledException());
        await using var mcp = await Connect(SingleProjectToken());

        var opened = Opened(await mcp.CallToolAsync("ui_open", OpenArgs("data_list", new { entity = "posts" })));

        opened.GetProperty("url").GetString().Should().Be("https://uk01-lon.anythink.cloud/org/42/entities/posts");
        opened.GetProperty("signedIn").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task UiOpen_MalformedProjectListEntry_DoesNotStopSigning()
    {
        _mock.When(HttpMethod.Get, $"{Issuer}/oauth/v1/projects").Respond("application/json",
            $$"""[{"name":"broken"},{"project_id":7,"account_id":"{{AccountId}}"},"odd",null,{"project_id":"{{ProjectId}}"},{"project_id":"{{ProjectId}}","account_id":"{{AccountId}}"}]""");
        TransferTokenIs(ProjectId, TransferToken);
        await using var mcp = await Connect(SingleProjectToken());

        var opened = Opened(await mcp.CallToolAsync("ui_open", OpenArgs("data_list", new { entity = "posts" })));

        opened.GetProperty("url").GetString().Should().Be(SignedUrl("/org/42/entities/posts"));
        opened.GetProperty("signedIn").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task UiOpen_Path_FromBreeze_IsSigned()
    {
        ProjectsAre((ProjectId, AccountId));
        TransferTokenIs(ProjectId, TransferToken);
        await using var mcp = await Connect(SingleProjectToken());

        var opened = Opened(await mcp.CallToolAsync("ui_open", OpenArgs("workflows_get", path: "/org/42/workflows/9")));

        opened.GetProperty("url").GetString().Should().Be(SignedUrl("/org/42/workflows/9"));
        opened.GetProperty("signedIn").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData("/org/43/workflows/9")]
    [InlineData("/org/42")]
    [InlineData("/org/421/workflows/9")]
    [InlineData("/workflows/9")]
    public async Task UiOpen_Path_ForAnotherOrg_IsRefused(string path)
    {
        var billing = AnyBillingRequest();
        await using var mcp = await Connect(SingleProjectToken());

        var result = await mcp.CallToolAsync("ui_open", OpenArgs("workflows_get", path: path));

        result.IsError.Should().BeTrue();
        _mock.GetMatchCount(billing).Should().Be(0);
    }

    [Fact]
    public async Task UiOpen_AccountTool_ReturnsMyAnythinkUnsigned()
    {
        var billing = AnyBillingRequest();
        await using var mcp = await Connect(SingleProjectToken());

        var opened = Opened(await mcp.CallToolAsync("ui_open", OpenArgs("accounts_list")));

        opened.GetProperty("url").GetString().Should().Be("https://my.anythink.cloud/dashboard");
        opened.GetProperty("origin").GetString().Should().Be(MyAnythinkOrigin);
        opened.GetProperty("target").GetString().Should().Be("account");
        opened.GetProperty("signedIn").GetBoolean().Should().BeFalse();
        _mock.GetMatchCount(billing).Should().Be(0);
    }

    [Fact]
    public async Task UiOpen_UnmappedTool_IsError()
    {
        await using var mcp = await Connect(SingleProjectToken());

        var result = await mcp.CallToolAsync("ui_open", OpenArgs("project_details"));

        result.IsError.Should().BeTrue();
        Text(result).Should().Contain("no view for this tool");
    }

    [Fact]
    public async Task ToolsList_UiOpen_IsAppOnly()
    {
        await using var mcp = await Connect(SingleProjectToken());

        var tool = (await mcp.ListToolsAsync()).Single(t => t.Name == "ui_open");

        tool.ProtocolTool.Meta!["ui"]!["visibility"]!.AsArray().Select(n => n!.GetValue<string>()).Should().Equal("app");
        tool.ProtocolTool.Annotations!.ReadOnlyHint.Should().BeTrue();
    }

    [Fact]
    public async Task TransferToken_AppearsOnlyInUiOpensResult()
    {
        ProjectsAre((ProjectId, AccountId));
        TransferTokenIs(ProjectId, TransferToken);
        _mock.When(HttpMethod.Get, $"{Issuer}/v1/accounts").Respond("application/json", "[]");
        await using var mcp = await Connect(SingleProjectToken());

        var opened = await mcp.CallToolAsync("ui_open", OpenArgs("data_list", new { entity = "posts" }));
        var others = new[]
        {
            await mcp.CallToolAsync("projects_list"),
            await mcp.CallToolAsync("accounts_list"),
            await mcp.CallToolAsync("ui_open", OpenArgs("project_details")),
        };
        var listed = await mcp.ListToolsAsync();

        Text(opened).Should().Contain(TransferToken);
        others.Select(Text).Should().OnlyContain(text => !text.Contains(TransferToken));
        JsonSerializer.Serialize(listed.Select(t => t.ProtocolTool)).Should().NotContain(TransferToken);
    }
}
