using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AnythinkMcp.Cli;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RichardSzalay.MockHttp;

namespace AnythinkMcp.Tests;

[Collection("SequentialConfig")]
public class HostedAccountToolsTests : McpTestBase, IAsyncLifetime
{
    private const string Issuer = "https://issuer.test";
    private const string PublicUrl = "https://mcp.test/mcp";
    private const string SavedPlatformToken = "test-platform-token";
    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");
    private static readonly Guid OtherAccountId = Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444");
    private static readonly Guid PlanId = Guid.Parse("cccccccc-1111-2222-3333-444444444444");
    private static readonly Guid ProjectId = Guid.Parse("dddddddd-1111-2222-3333-444444444444");
    private static readonly string[] AccountToolNames = ["accounts_list", "accounts_create", "plans", "projects_create", "projects_delete"];

    private RSA _rsa = null!;
    private RsaSecurityKey _key = null!;
    private MockHttpMessageHandler _mock = null!;
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private string? _createdBody;

    public async Task InitializeAsync()
    {
        _rsa = RSA.Create(2048);
        _key = new RsaSecurityKey(_rsa) { KeyId = "test-key-1" };
        _mock = new MockHttpMessageHandler();
        HostedTestSupport.ConfigureIssuer(_mock, Issuer, _key);

        var options = new HostedMode.Options
        {
            PublicUrl = PublicUrl,
            Issuer = Issuer,
            Audience = PublicUrl,
            Exchange = new TokenExchangeOptions { ClientId = "mcp", ClientSecret = "s3cret", Audience = "anythink-oauth" },
        };
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        _app = HostedMode.BuildPublicApp(builder, options, new McpClientFactory(null, _mock),
            jwt => jwt.BackchannelHttpHandler = _mock, exchangeHandler: _mock);
        await _app.StartAsync();
        _client = _app.GetTestClient();
        _client.BaseAddress = new Uri("https://mcp.test/");
    }

    public new async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        _rsa.Dispose();
    }

    private const string WithAccount = "offline_access account";

    private string AllProjectsToken() => AllProjectsToken(WithAccount);

    private string AllProjectsToken(object? scope) =>
        HostedTestSupport.CreateToken(_key, Issuer, PublicUrl, tid: null, instanceUrl: null,
            extraClaims: Claims(("projects", "all"), ("scope", scope)));

    private string SingleProjectToken() => SingleProjectToken(WithAccount);

    private string SingleProjectToken(object? scope)
    {
        _mock.When(HttpMethod.Post, $"{Issuer}/oauth/v1/token").Respond("application/json", """{"access_token":"exchanged","expires_in":3600}""");
        return HostedTestSupport.CreateToken(_key, Issuer, PublicUrl, extraClaims: Claims(("scope", scope)));
    }

    private static Dictionary<string, object> Claims(params (string Name, object? Value)[] claims) =>
        claims.Where(c => c.Value is not null).ToDictionary(c => c.Name, c => c.Value!);

    private Task<McpClient> Connect(string token) => McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
    {
        Endpoint = new Uri(PublicUrl),
        TransportMode = HttpTransportMode.StreamableHttp,
        AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" }
    }, _client, ownsHttpClient: false));

    private static Dictionary<string, object?> Args(params (string Name, object? Value)[] arguments) =>
        arguments.ToDictionary(a => a.Name, a => a.Value);

    private static string Text(CallToolResult result) => ((TextContentBlock)result.Content[0]).Text;

    private static string AccountJson(Guid id, string name = "Acme") => $$"""
        {"account_id":"{{id}}","organization_name":"{{name}}","billing_email":"billing@acme.test","currency":"gbp","status":0,"access_level":2,"current_balance_cents":0}
        """;

    private MockedRequest AccountsAre(string token, params Guid[] ids) =>
        _mock.When(HttpMethod.Get, $"{Issuer}/v1/accounts")
            .WithHeaders("Authorization", $"Bearer {token}")
            .Respond("application/json", $"[{string.Join(',', ids.Select(id => AccountJson(id)))}]");

    private MockedRequest ProjectCreatedAs(string token, Guid account, int status = 1) =>
        _mock.When(HttpMethod.Post, $"{Issuer}/v1/accounts/{account}/shared-tenants")
            .WithHeaders("Authorization", $"Bearer {token}")
            .Respond(async request =>
            {
                _createdBody = await request.Content!.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent($$"""
                        {"id":"{{ProjectId}}","name":"Shop","plan_id":"{{PlanId}}","region":"lon1","status":{{status}},"created_at":"2026-10-06T10:00:00Z"}
                        """, Encoding.UTF8, "application/json")
                };
            });

    private MockedRequest AnyBillingRequest() => _mock.When($"{Issuer}/v1/*").Respond("application/json", "[]");

    private void SaveAPlatformLoginTheServerOwns()
    {
        SetupPlatformLogin();
        File.WriteAllText(Path.Combine(TempDir, "config.json"), "{ this is not a config file");
    }

    // ── Rule: with `account` in the scope, project creation is billing's POST, as the caller ──

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WithAccountScope_ProjectsCreate_PostsToBillingWithTheCallersToken_AndNeverReadsTheSavedConfig(bool allProjects)
    {
        SaveAPlatformLoginTheServerOwns();
        var token = allProjects ? AllProjectsToken() : SingleProjectToken();
        var create = ProjectCreatedAs(token, AccountId);
        var elsewhere = _mock.When($"{BillingUrl}/*").Respond("application/json", "[]");

        await using var mcp = await Connect(token);
        var result = await mcp.CallToolAsync("projects_create",
            Args(("name", "Shop"), ("plan_id", PlanId.ToString()), ("account_id", AccountId.ToString())));

        result.IsError.Should().NotBe(true, Text(result));
        _mock.GetMatchCount(create).Should().Be(1);
        _mock.GetMatchCount(elsewhere).Should().Be(0);
        using var body = JsonDocument.Parse(_createdBody!);
        body.RootElement.GetProperty("name").GetString().Should().Be("Shop");
        body.RootElement.GetProperty("plan_id").GetString().Should().Be(PlanId.ToString());
        body.RootElement.GetProperty("region").GetString().Should().Be("lon1");
    }

    [Fact]
    public async Task EveryAccountToolRunsAgainstBilling_WithTheCallersToken()
    {
        var token = AllProjectsToken();
        var accounts = AccountsAre(token, AccountId);
        var plans = _mock.When(HttpMethod.Get, $"{Issuer}/v1/plans").Respond("application/json", "[]");
        var created = _mock.When(HttpMethod.Post, $"{Issuer}/v1/accounts").WithHeaders("Authorization", $"Bearer {token}")
            .Respond("application/json", AccountJson(OtherAccountId, "Beta"));

        await using var mcp = await Connect(token);
        (await mcp.CallToolAsync("accounts_list")).IsError.Should().NotBe(true);
        (await mcp.CallToolAsync("plans")).IsError.Should().NotBe(true);
        var createdAccount = await mcp.CallToolAsync("accounts_create", Args(("name", "Beta"), ("email", "billing@beta.test")));

        _mock.GetMatchCount(accounts).Should().Be(1);
        _mock.GetMatchCount(plans).Should().Be(1);
        _mock.GetMatchCount(created).Should().Be(1);
        Text(createdAccount).Should().Contain(OtherAccountId.ToString());
    }

    [Fact]
    public async Task Plans_AreFetchedWithoutTheCallersToken_BecauseTheyArePublic()
    {
        var token = AllProjectsToken();
        var plans = _mock.When(HttpMethod.Get, $"{Issuer}/v1/plans").With(request => request.Headers.Authorization is null)
            .Respond("application/json", $$"""[{"id":"{{PlanId}}","name":"Free","resource":0,"monthly_price_cents":0,"annual_price_cents":0,"currency":"gbp","storage_quota_gb":1,"file_quota_gb":1,"user_quota":5,"custom_domain_enabled":false,"backups_enabled":false,"priority_support":false,"is_active":true,"display_order":1}]""");

        await using var mcp = await Connect(token);
        var result = await mcp.CallToolAsync("plans");

        _mock.GetMatchCount(plans).Should().Be(1);
        Text(result).Should().Contain(PlanId.ToString()).And.Contain("Free");
    }

    // ── Rule: without `account`, the account tools are hidden, refused if called, and nothing is sent ──

    public static TheoryData<string?> ScopesWithoutAccount => new()
    {
        null, "offline_access", "accounts", "my-account", "offline_access accounts account_read"
    };

    [Theory]
    [MemberData(nameof(ScopesWithoutAccount))]
    public async Task WithoutAccountInTheScope_TheAccountToolsAreHidden(string? scope)
    {
        await using var mcp = await Connect(AllProjectsToken(scope));

        var names = (await mcp.ListToolsAsync()).Select(t => t.Name).ToList();

        names.Should().NotIntersectWith(AccountToolNames).And.Contain("entities_list").And.Contain("projects_list");
    }

    [Theory]
    [MemberData(nameof(ScopesWithoutAccount))]
    public async Task WithoutAccountInTheScope_CallingAnAccountToolAnywayIsRefused_AndNothingIsSent(string? scope)
    {
        SaveAPlatformLoginTheServerOwns();
        var anything = AnyBillingRequest();
        var elsewhere = _mock.When($"{BillingUrl}/*").Respond("application/json", "[]");

        await using var mcp = await Connect(AllProjectsToken(scope));
        foreach (var (name, arguments) in new (string, Dictionary<string, object?>?)[]
                 {
                     ("projects_create", Args(("name", "Shop"), ("plan_id", PlanId.ToString()))),
                     ("projects_delete", Args(("id", ProjectId.ToString()))),
                     ("accounts_create", Args(("name", "Beta"), ("email", "billing@beta.test"))),
                     ("accounts_list", null),
                     ("plans", null),
                 })
        {
            var result = await mcp.CallToolAsync(name, arguments);

            result.IsError.Should().BeTrue(name);
            Text(result).Should().Contain("doesn't include account access").And.Contain("Manage projects and billing");
        }

        _mock.GetMatchCount(anything).Should().Be(0);
        _mock.GetMatchCount(elsewhere).Should().Be(0);
    }

    [Theory]
    [InlineData("account")]
    [InlineData("offline_access account")]
    [InlineData("account offline_access")]
    public async Task WithAccountInTheScope_TheAccountToolsAreListed(string scope)
    {
        await using var mcp = await Connect(AllProjectsToken(scope));

        (await mcp.ListToolsAsync()).Select(t => t.Name).Should().Contain(AccountToolNames);
    }

    [Fact]
    public async Task WithAccountInAScopeList_TheAccountToolsAreListed()
    {
        await using var mcp = await Connect(AllProjectsToken(new[] { "offline_access", "account" }));

        (await mcp.ListToolsAsync()).Select(t => t.Name).Should().Contain(AccountToolNames);
    }

    [Fact]
    public async Task AccountTools_TakeNoProjectArgument_AndWorkOnAnAllProjectsConnection()
    {
        await using var mcp = await Connect(AllProjectsToken());

        var tools = (await mcp.ListToolsAsync()).Where(t => AccountToolNames.Contains(t.Name));

        tools.Should().HaveCount(5).And.AllSatisfy(tool =>
            tool.ProtocolTool.InputSchema.GetProperty("properties").TryGetProperty("project", out _).Should().BeFalse(tool.Name));
    }

    // ── Rule: with no active account remotely, omitted account_id means "the only one" ──

    [Fact]
    public async Task AccountIdOmitted_WithOneAccount_UsesIt()
    {
        var token = AllProjectsToken();
        AccountsAre(token, AccountId);
        var create = ProjectCreatedAs(token, AccountId);

        await using var mcp = await Connect(token);
        var result = await mcp.CallToolAsync("projects_create", Args(("name", "Shop"), ("plan_id", PlanId.ToString())));

        result.IsError.Should().NotBe(true, Text(result));
        _mock.GetMatchCount(create).Should().Be(1);
    }

    [Fact]
    public async Task AccountIdOmitted_WithTwoAccounts_IsAClearToolError_AndNothingIsCreated()
    {
        var token = AllProjectsToken();
        AccountsAre(token, AccountId, OtherAccountId);
        var create = _mock.When(HttpMethod.Post, $"{Issuer}/v1/accounts/*").Respond("application/json", "{}");

        await using var mcp = await Connect(token);
        var result = await mcp.CallToolAsync("projects_create", Args(("name", "Shop"), ("plan_id", PlanId.ToString())));

        result.IsError.Should().BeTrue();
        Text(result).Should().Contain("account_id").And.Contain("accounts_list").And.Contain("2 billing accounts");
        _mock.GetMatchCount(create).Should().Be(0);
    }

    [Fact]
    public async Task AccountIdOmitted_WithNoAccount_SaysToCreateOne()
    {
        var token = AllProjectsToken();
        AccountsAre(token);

        await using var mcp = await Connect(token);
        var result = await mcp.CallToolAsync("projects_delete", Args(("id", ProjectId.ToString())));

        result.IsError.Should().BeTrue();
        Text(result).Should().Contain("accounts_create");
    }

    [Fact]
    public async Task AnAccountIdThatIsntAnId_IsAToolError_AndNothingIsSent()
    {
        var anything = AnyBillingRequest();

        await using var mcp = await Connect(AllProjectsToken());
        var result = await mcp.CallToolAsync("projects_create",
            Args(("name", "Shop"), ("plan_id", PlanId.ToString()), ("account_id", "my-account")));

        result.IsError.Should().BeTrue();
        Text(result).Should().Contain("account_id");
        _mock.GetMatchCount(anything).Should().Be(0);
    }

    // ── Rule: projects_create never prompts, and tells the caller the project's id ──

    [Fact]
    public async Task ProjectsCreate_ReportsTheProject_AndSaysItsBeingSetUp()
    {
        var token = AllProjectsToken();
        ProjectCreatedAs(token, AccountId);

        await using var mcp = await Connect(token);
        var result = await mcp.CallToolAsync("projects_create",
            Args(("name", "Shop"), ("plan_id", PlanId.ToString()), ("account_id", AccountId.ToString())));

        var text = Text(result);
        text.Should().Contain(ProjectId.ToString()).And.Contain("Shop").And.Contain("provisioning").And.Contain(PlanId.ToString())
            .And.Contain("projects_list").And.Contain("about a minute");
        text.Should().NotContain("anythink ");
    }

    [Theory]
    [InlineData("name", "'name' is required")]
    [InlineData("plan_id", "'plan_id' is required")]
    public async Task ProjectsCreate_WithoutANameOrAPlan_IsAToolError_NotAPrompt(string missing, string expected)
    {
        var anything = AnyBillingRequest();
        var arguments = Args(("name", "Shop"), ("plan_id", PlanId.ToString()), ("account_id", AccountId.ToString()));
        arguments.Remove(missing);

        await using var mcp = await Connect(AllProjectsToken());
        var result = await mcp.CallToolAsync("projects_create", arguments).AsTask().WaitAsync(TimeSpan.FromSeconds(30));

        result.IsError.Should().BeTrue();
        Text(result).Should().Contain(expected);
        _mock.GetMatchCount(anything).Should().Be(0);
    }

    [Fact]
    public async Task ProjectsCreate_WithAPlanThatIsntAnId_IsAToolError_NotAPlanPicker()
    {
        var anything = AnyBillingRequest();

        await using var mcp = await Connect(AllProjectsToken());
        var result = await mcp.CallToolAsync("projects_create",
            Args(("name", "Shop"), ("plan_id", "free"), ("account_id", AccountId.ToString()))).AsTask().WaitAsync(TimeSpan.FromSeconds(30));

        result.IsError.Should().BeTrue();
        Text(result).Should().Contain("plan_id");
        _mock.GetMatchCount(anything).Should().Be(0);
    }

    [Fact]
    public async Task AccountsCreate_WithoutAnEmail_IsAToolError_NotAPrompt()
    {
        var anything = AnyBillingRequest();

        await using var mcp = await Connect(AllProjectsToken());
        var result = await mcp.CallToolAsync("accounts_create", Args(("name", "Beta"))).AsTask().WaitAsync(TimeSpan.FromSeconds(30));

        result.IsError.Should().BeTrue();
        Text(result).Should().Contain("'email' is required");
        _mock.GetMatchCount(anything).Should().Be(0);
    }

    [Fact]
    public async Task AccountsCreate_DoesNotWriteTheServersConfig_OrSwitchItsActiveAccount()
    {
        SaveAPlatformLoginTheServerOwns();
        var configBefore = File.ReadAllText(Path.Combine(TempDir, "config.json"));
        var token = AllProjectsToken();
        _mock.When(HttpMethod.Post, $"{Issuer}/v1/accounts").Respond("application/json", AccountJson(OtherAccountId, "Beta"));

        await using var mcp = await Connect(token);
        var result = await mcp.CallToolAsync("accounts_create", Args(("name", "Beta"), ("email", "billing@beta.test"), ("currency", "usd")));

        result.IsError.Should().NotBe(true, Text(result));
        File.ReadAllText(Path.Combine(TempDir, "config.json")).Should().Be(configBefore);
    }

    // ── Rule: projects_delete deletes exactly the project it was given ─────────

    [Fact]
    public async Task ProjectsDelete_DeletesTheProjectByItsFullId()
    {
        var token = AllProjectsToken();
        _mock.When(HttpMethod.Get, $"{Issuer}/v1/accounts/{AccountId}/shared-tenants").Respond("application/json",
            $$"""[{"id":"{{ProjectId}}","name":"Shop","plan_id":"{{PlanId}}","region":"lon1","status":2,"created_at":"2026-10-06T10:00:00Z"}]""");
        var delete = _mock.When(HttpMethod.Delete, $"{Issuer}/v1/accounts/{AccountId}/shared-tenants/{ProjectId}")
            .WithHeaders("Authorization", $"Bearer {token}").Respond(HttpStatusCode.NoContent);

        await using var mcp = await Connect(token);
        var result = await mcp.CallToolAsync("projects_delete", Args(("id", ProjectId.ToString()), ("account_id", AccountId.ToString())));

        result.IsError.Should().NotBe(true, Text(result));
        _mock.GetMatchCount(delete).Should().Be(1);
        Text(result).Should().Contain("Shop").And.Contain("deleted");
    }

    [Theory]
    [InlineData("dddddddd")]
    [InlineData("Shop")]
    public async Task ProjectsDelete_NeverMatchesAPrefixOrAName(string id)
    {
        var token = AllProjectsToken();
        _mock.When(HttpMethod.Get, $"{Issuer}/v1/accounts/{AccountId}/shared-tenants").Respond("application/json",
            $$"""[{"id":"{{ProjectId}}","name":"Shop","plan_id":"{{PlanId}}","region":"lon1","status":2,"created_at":"2026-10-06T10:00:00Z"}]""");
        var delete = _mock.When(HttpMethod.Delete, $"{Issuer}/v1/accounts/*").Respond(HttpStatusCode.NoContent);

        await using var mcp = await Connect(token);
        var result = await mcp.CallToolAsync("projects_delete", Args(("id", id), ("account_id", AccountId.ToString())));

        result.IsError.Should().BeTrue();
        _mock.GetMatchCount(delete).Should().Be(0);
    }

    // ── Rule: annotations and descriptions suit a remote caller ───────────────

    [Fact]
    public void Annotations_MarkDeleteDestructive_CreatesAdditive_AndListsReadOnly()
    {
        ToolNamed("projects_delete").Annotations.Should().BeEquivalentTo(new { ReadOnlyHint = false, DestructiveHint = true });
        foreach (var name in new[] { "projects_create", "accounts_create" })
            ToolNamed(name).Annotations.Should().BeEquivalentTo(new { ReadOnlyHint = false, DestructiveHint = false }, name);
        foreach (var name in new[] { "accounts_list", "plans" })
            ToolNamed(name).Annotations!.ReadOnlyHint.Should().BeTrue(name);
    }

    [Fact]
    public void Descriptions_AndParameters_DontMentionCliOnlyThings()
    {
        foreach (var name in AccountToolNames)
        {
            var tool = ToolNamed(name);
            var text = tool.Description + " " + tool.InputSchema.GetRawText();

            text.Should().NotContain("anythink ", name).And.NotContain("--", name).And.NotContain("active account", name)
                .And.NotContain("json", name).And.NotContain("yes", name);
        }
    }

    [Fact]
    public void TheRequiredParameters_AreRequiredInTheSchema()
    {
        Required("projects_create").Should().BeEquivalentTo("name", "plan_id");
        Required("projects_delete").Should().BeEquivalentTo("id");
        Required("accounts_create").Should().BeEquivalentTo("name", "email");

        static IEnumerable<string?> Required(string name) =>
            ToolNamed(name).InputSchema.GetProperty("required").EnumerateArray().Select(e => e.GetString());
    }

    private static Tool ToolNamed(string name) =>
        CliCommandTool.All(CliToolScope.Hosted).Single(t => t.ProtocolTool.Name == name).ProtocolTool;

    // ── Rule: billing's 400 message reaches the caller; other failures give the status only ──

    [Fact]
    public async Task ABillingBadRequest_ReachesTheCaller_WithBillingsMessage()
    {
        var token = AllProjectsToken();
        _mock.When(HttpMethod.Post, $"{Issuer}/v1/accounts/{AccountId}/shared-tenants")
            .Respond(HttpStatusCode.BadRequest, "application/json", """{"error":"A paid plan needs a payment method."}""");

        await using var mcp = await Connect(token);
        var result = await mcp.CallToolAsync("projects_create",
            Args(("name", "Shop"), ("plan_id", PlanId.ToString()), ("account_id", AccountId.ToString())));

        result.IsError.Should().BeTrue();
        Text(result).Should().Contain("A paid plan needs a payment method.");
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, 500)]
    [InlineData(HttpStatusCode.Unauthorized, 401)]
    [InlineData(HttpStatusCode.Forbidden, 403)]
    public async Task OtherBillingFailures_GiveTheStatus_NotTheBody(HttpStatusCode status, int code)
    {
        var token = AllProjectsToken();
        _mock.When(HttpMethod.Post, $"{Issuer}/v1/accounts/{AccountId}/shared-tenants")
            .Respond(status, "application/json", """{"error":"secret-detail at Billing.Internal.Service"}""");

        await using var mcp = await Connect(token);
        var result = await mcp.CallToolAsync("projects_create",
            Args(("name", "Shop"), ("plan_id", PlanId.ToString()), ("account_id", AccountId.ToString())));

        result.IsError.Should().BeTrue();
        Text(result).Should().Contain($"status {code}").And.NotContain("secret-detail");
    }

    // ── Rule: the scope claim is read as a space-separated list ───────────────

    [Theory]
    [InlineData("account", true)]
    [InlineData("offline_access account", true)]
    [InlineData("  account  offline_access ", true)]
    [InlineData("accounts", false)]
    [InlineData("Account", false)]
    [InlineData("", false)]
    public void HasAccountScope_MatchesWholeWordsOnly(string scope, bool expected)
    {
        var principal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim("scope", scope)]));

        HostedAuth.HasAccountScope(principal).Should().Be(expected);
    }
}
