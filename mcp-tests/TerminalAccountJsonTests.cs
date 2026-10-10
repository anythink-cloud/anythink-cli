using System.Net;
using AnythinkCli.Client;
using AnythinkCli.Config;
using AnythinkMcp.Cli;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkMcp.Tests;

[Collection("SequentialConfig")]
public class TerminalAccountJsonTests : McpTestBase
{
    private const string Billing = "https://billing.example";
    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");
    private static readonly Guid PlanId = Guid.Parse("cccccccc-1111-2222-3333-444444444444");
    private static readonly Guid ProjectId = Guid.Parse("dddddddd-1111-2222-3333-444444444444");

    private readonly MockHttpMessageHandler _mock = new();

    private async Task<CliRunResult> Terminal(params string[] args)
    {
        var signedIn = new HttpClient(_mock);
        signedIn.DefaultRequestHeaders.Authorization = new("Bearer", "a-token");
        return await CliRunner.RunAsync(args, client: null, CliToolScope.Local, billing: new BillingClient(Billing, signedIn, new HttpClient(_mock)));
    }

    // ── Rule: --json in a terminal run behaves like the table output, minus the decoration ──

    [Fact]
    public async Task AccountsCreate_Json_InATerminalRun_SetsTheActiveAccount_LikeTheTableOutput()
    {
        SetupPlatformLogin();
        _mock.When(HttpMethod.Post, $"{Billing}/v1/accounts").Respond("application/json",
            $$"""{"account_id":"{{AccountId}}","organization_name":"Acme","billing_email":"b@acme.test","currency":"gbp","status":0,"access_level":2,"current_balance_cents":0}""");

        var result = await Terminal("accounts", "create", "--name", "Acme", "--email", "b@acme.test", "--currency", "gbp", "--json");

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain(AccountId.ToString()).And.Contain("active account");
        ConfigService.ResolvePlatform().AccountId.Should().Be(AccountId.ToString());
    }

    [Fact]
    public async Task TerminalJson_DoesNotNameMcpTools()
    {
        SetupPlatformLogin();
        _mock.When(HttpMethod.Post, $"{Billing}/v1/accounts").Respond("application/json",
            $$"""{"account_id":"{{AccountId}}","organization_name":"Acme","billing_email":"b@acme.test","currency":"gbp","status":0,"access_level":2,"current_balance_cents":0}""");
        _mock.When(HttpMethod.Post, $"{Billing}/v1/accounts/{AccountId}/shared-tenants").Respond(HttpStatusCode.Created, "application/json",
            $$"""{"id":"{{ProjectId}}","name":"Shop","plan_id":"{{PlanId}}","region":"lon1","status":1,"created_at":"2026-10-06T10:00:00Z"}""");

        var account = await Terminal("accounts", "create", "--name", "Acme", "--email", "b@acme.test", "--currency", "gbp", "--json");
        var project = await Terminal("projects", "create", "Shop", "--plan", PlanId.ToString(), "--region", "lon1", "--account", AccountId.ToString(), "--json");

        foreach (var output in new[] { account.Output, project.Output })
            output.Should().NotContain("projects_list").And.NotContain("projects_create").And.NotContain("account_id to");
        project.Output.Should().Contain(ProjectId.ToString()).And.Contain("being set up");
    }
}
