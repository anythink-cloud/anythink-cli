using System.Text.Json;
using AnythinkCli.Config;
using AnythinkMcp.Tools;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkMcp.Tests;

[Collection("SequentialConfig")]
public class AccountToolsTests : McpTestBase
{
    private static readonly Guid AccountId1 = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");
    private static readonly Guid AccountId2 = Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444");

    private static string AccountsJson => $$"""
        [
            {
                "account_id": "{{AccountId1}}",
                "organization_name": "Acme Corp",
                "billing_email": "billing@acme.com",
                "currency": "gbp",
                "status": 0,
                "access_level": 2,
                "current_balance_cents": 0
            },
            {
                "account_id": "{{AccountId2}}",
                "organization_name": "Beta Inc",
                "billing_email": "billing@beta.com",
                "currency": "usd",
                "status": 1,
                "access_level": 1,
                "current_balance_cents": 500
            }
        ]
    """;

    // ── List ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AccountsList_Returns_All_Accounts()
    {
        SetupPlatformLogin();
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, $"{BillingUrl}/v1/accounts")
            .Respond("application/json", AccountsJson);

        var tools = new AccountTools(CreateFactory(mock));
        var result = await tools.AccountsList();

        var arr = JsonDocument.Parse(result).RootElement;
        arr.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task AccountsList_Shows_Status_Labels()
    {
        SetupPlatformLogin();
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, $"{BillingUrl}/v1/accounts")
            .Respond("application/json", AccountsJson);

        var tools = new AccountTools(CreateFactory(mock));
        var result = await tools.AccountsList();

        result.Should().Contain("Active");
        result.Should().Contain("Suspended");
    }

    [Fact]
    public async Task AccountsList_Marks_Active_Account()
    {
        SetupPlatformLogin(accountId: AccountId1.ToString());
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, $"{BillingUrl}/v1/accounts")
            .Respond("application/json", AccountsJson);

        var tools = new AccountTools(CreateFactory(mock));
        var result = await tools.AccountsList();

        var arr = JsonDocument.Parse(result).RootElement;
        // First account should be marked active
        arr[0].GetProperty("IsActive").GetBoolean().Should().BeTrue();
        arr[1].GetProperty("IsActive").GetBoolean().Should().BeFalse();
    }

    // ── Create ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AccountsCreate_Returns_New_Account_And_Sets_Active()
    {
        SetupPlatformLogin();
        var newId = Guid.NewGuid();
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, $"{BillingUrl}/v1/accounts")
            .Respond("application/json", $$"""
                {
                    "account_id": "{{newId}}",
                    "organization_name": "New Co",
                    "billing_email": "new@co.com",
                    "currency": "usd",
                    "status": 0,
                    "access_level": 2,
                    "current_balance_cents": 0
                }
            """);

        var tools = new AccountTools(CreateFactory(mock));
        var result = await tools.AccountsCreate("New Co", "new@co.com", "usd");

        result.Should().Contain("New Co");
        var platform = ConfigService.ResolvePlatform();
        platform.AccountId.Should().Be(newId.ToString());
    }

    // ── Use ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AccountsUse_Sets_Active_Account_By_Full_Id()
    {
        SetupPlatformLogin();
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, $"{BillingUrl}/v1/accounts")
            .Respond("application/json", AccountsJson);

        var tools = new AccountTools(CreateFactory(mock));
        var result = await tools.AccountsUse(AccountId2.ToString());

        result.Should().Contain("Beta Inc");
        ConfigService.ResolvePlatform().AccountId.Should().Be(AccountId2.ToString());
    }

    [Fact]
    public async Task AccountsUse_Matches_By_Prefix()
    {
        SetupPlatformLogin();
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, $"{BillingUrl}/v1/accounts")
            .Respond("application/json", AccountsJson);

        var tools = new AccountTools(CreateFactory(mock));
        var result = await tools.AccountsUse("aaaaaaaa");

        result.Should().Contain("Acme Corp");
    }

    [Fact]
    public async Task AccountsUse_Unknown_Id_Returns_Not_Found()
    {
        SetupPlatformLogin();
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, $"{BillingUrl}/v1/accounts")
            .Respond("application/json", AccountsJson);

        var tools = new AccountTools(CreateFactory(mock));
        var result = await tools.AccountsUse("zzz-no-match");

        result.Should().Contain("No account found");
    }

    // ── Auth required ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AccountsList_Without_Login_Throws()
    {
        // No platform login → expired token
        var mock = new MockHttpMessageHandler();
        var tools = new AccountTools(CreateFactory(mock));

        var act = () => tools.AccountsList();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*expired*");
    }
}
