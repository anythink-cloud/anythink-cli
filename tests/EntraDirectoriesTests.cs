using AnythinkCli.Client;
using AnythinkCli.Commands;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkCli.Tests;

[Collection("ConsoleOutput")]
public class EntraDirectoriesTests
{
    private const string Billing = "https://billing.example";
    private static readonly Guid Account = Guid.NewGuid();
    private static readonly Guid Tenant = Guid.NewGuid();

    private static BillingClient Client(MockHttpMessageHandler mock) =>
        new(Billing, new HttpClient(mock), new HttpClient(mock));

    [Fact]
    public async Task ListingDirectories_ReadsTheAccountsDirectories()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, $"{Billing}/v1/accounts/{Account}/entra-directories")
            .Respond("application/json", $$"""[{"tenant_id":"{{Tenant}}","default_access_level":1,"created_at":"2026-10-11T00:00:00Z"}]""");

        var directories = await Client(mock).GetEntraDirectoriesAsync(Account);

        directories.Should().ContainSingle(d => d.TenantId == Tenant && d.DefaultAccessLevel == 1);
    }

    private const string Api = "https://api.my.example";

    [Fact]
    public async Task StartingConsent_PostsTheAccountLevelAndPortalReturnAddress()
    {
        var mock = new MockHttpMessageHandler();
        var bodies = new List<string>();
        mock.ReplyWith(HttpMethod.Post, $"{Api}/org/42/auth/v1/microsoft/admin-consent", """{"consent_url":"https://login.microsoftonline.com/organizations/v2.0/adminconsent?state=s"}""", bodies);
        var client = new BillingClient(new AnythinkCli.Config.PlatformConfig { BillingUrl = Billing, MyAnythinkUrl = Api, MyAnythinkOrgId = "42" }, new HttpClient(mock));

        var url = await client.StartMicrosoftAdminConsentAsync(Account, 1, "https://my.example/microsoft-admin-consent");

        url.Should().Contain("adminconsent");
        bodies.Single().Should().Be($$"""{"account_id":"{{Account}}","default_access_level":1,"redirect_uri":"https://my.example/microsoft-admin-consent"}""");
    }

    [Fact]
    public async Task StartingConsent_OnAClientWithoutThePlatformApi_SaysWhereToDoIt()
    {
        var act = () => Client(new MockHttpMessageHandler()).StartMicrosoftAdminConsentAsync(Account, 0, "https://x");

        await act.Should().ThrowAsync<CliException>().WithMessage("*Team page*");
    }

    [Theory]
    [InlineData("https://api.my.anythink.cloud", "https://my.anythink.cloud")]
    [InlineData("https://api.my.anythink.dev", "https://my.anythink.dev")]
    [InlineData("https://localhost:7136", "http://localhost:3001")]
    [InlineData("https://platform.example.com", "https://platform.example.com")]
    public void PortalUrl_IsTheApiHostWithoutApi(string api, string portal) =>
        AnythinkCli.Config.ApiDefaults.PortalUrl(api).Should().Be(portal);

    [Fact]
    public async Task DisconnectingADirectory_DeletesIt()
    {
        var mock = new MockHttpMessageHandler();
        var delete = mock.When(HttpMethod.Delete, $"{Billing}/v1/accounts/{Account}/entra-directories/{Tenant}")
            .Respond(System.Net.HttpStatusCode.NoContent);

        await Client(mock).RemoveEntraDirectoryAsync(Account, Tenant);

        mock.GetMatchCount(delete).Should().Be(1);
    }

    [Theory]
    [InlineData("viewer", 0)]
    [InlineData("Admin", 1)]
    [InlineData("OWNER", 2)]
    [InlineData("superuser", null)]
    public void AccessLevelNames_MapToTheBillingLevels(string name, int? expected) =>
        AccountStatusText.AccessLevelValue(name).Should().Be(expected);

    [Fact]
    public async Task Add_WithAnUnknownLevel_FailsBeforeCallingTheApi()
    {
        var mock = new MockHttpMessageHandler();
        var (code, output) = await CommandRunner.RunAsync(mock, () =>
            new AccountsDirectoriesAddCommand().ExecuteAsync(null!, new AccountsDirectoriesAddSettings { DefaultLevel = "god" }));

        code.Should().Be(1);
        output.Should().Contain("viewer, admin or owner");
    }

    [Fact]
    public async Task Remove_WithATenantIdThatIsNotAGuid_FailsBeforeCallingTheApi()
    {
        var mock = new MockHttpMessageHandler();
        var (code, _) = await CommandRunner.RunAsync(mock, () =>
            new AccountsDirectoriesRemoveCommand().ExecuteAsync(null!, new AccountsDirectoriesRemoveSettings { TenantId = "x" }));

        code.Should().Be(1);
    }

    [Theory]
    [InlineData("google", "Google")]
    [InlineData("microsoft", "Microsoft")]
    public void SocialSignIn_NamesTheProvider(string provider, string name) =>
        AnythinkCli.Auth.SocialAuthFlow.DisplayName(provider).Should().Be(name);

    private static async Task<(int Code, string Output)> RunWithBilling(MockHttpMessageHandler mock, Func<Task<int>> command, BillingClient? billing = null)
    {
        ClientContext.Billing = billing ?? Client(mock);
        try { return await CommandRunner.RunAsync(mock, command); }
        finally { ClientContext.Billing = null; }
    }

    [Fact]
    public async Task ListCommand_ShowsEachDirectoryWithItsJoinLevel()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, $"{Billing}/v1/accounts/{Account}/entra-directories")
            .Respond("application/json", $$"""[{"tenant_id":"{{Tenant}}","default_access_level":1,"created_at":"2026-10-11T00:00:00Z"}]""");

        var (code, output) = await RunWithBilling(mock, () =>
            new AccountsDirectoriesListCommand().ExecuteAsync(null!, new AccountsDirectoriesListSettings { AccountId = Account.ToString() }));

        code.Should().Be(0);
        output.Should().Contain(Tenant.ToString()).And.Contain("admin");
    }

    [Fact]
    public async Task ListCommand_Json_ReturnsTenantIdsAndLevelNames()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, $"{Billing}/v1/accounts/{Account}/entra-directories")
            .Respond("application/json", $$"""[{"tenant_id":"{{Tenant}}","default_access_level":0,"created_at":"2026-10-11T00:00:00Z"}]""");

        var (_, output) = await RunWithBilling(mock, () =>
            new AccountsDirectoriesListCommand().ExecuteAsync(null!, new AccountsDirectoriesListSettings { AccountId = Account.ToString(), Json = true }));

        output.Should().Contain("\"default_access_level\": \"viewer\"");
    }

    [Fact]
    public async Task ListCommand_WithNoDirectories_SaysHowToAddOne()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, $"{Billing}/v1/accounts/{Account}/entra-directories").Respond("application/json", "[]");

        var (code, output) = await RunWithBilling(mock, () =>
            new AccountsDirectoriesListCommand().ExecuteAsync(null!, new AccountsDirectoriesListSettings { AccountId = Account.ToString() }));

        code.Should().Be(0);
        output.Should().Contain("No Microsoft directories connected");
    }

    [Fact]
    public async Task AddCommand_AsAMachine_ReturnsTheConsentLinkInsteadOfOpeningABrowser()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, "*/auth/v1/microsoft/admin-consent")
            .Respond("application/json", """{"consent_url":"https://login.microsoftonline.com/organizations/v2.0/adminconsent?state=s"}""");
        ClientContext.MachineOutput = true;
        try
        {
            var (code, output) = await RunWithBilling(mock, () => new AccountsDirectoriesAddCommand().ExecuteAsync(null!,
                new AccountsDirectoriesAddSettings { AccountId = Account.ToString() }), new BillingClient(new AnythinkCli.Config.PlatformConfig { BillingUrl = Billing }, new HttpClient(mock)));

            code.Should().Be(0);
            output.Should().Contain("\"consent_url\"").And.Contain("adminconsent");
        }
        finally { ClientContext.MachineOutput = false; }
    }

    [Fact]
    public async Task RemoveCommand_WithYes_DisconnectsTheDirectory()
    {
        var mock = new MockHttpMessageHandler();
        var delete = mock.When(HttpMethod.Delete, $"{Billing}/v1/accounts/{Account}/entra-directories/{Tenant}")
            .Respond(System.Net.HttpStatusCode.NoContent);

        var (code, _) = await RunWithBilling(mock, () => new AccountsDirectoriesRemoveCommand().ExecuteAsync(null!,
            new AccountsDirectoriesRemoveSettings { TenantId = Tenant.ToString(), AccountId = Account.ToString(), Yes = true }));

        code.Should().Be(0);
        mock.GetMatchCount(delete).Should().Be(1);
    }

    [Fact]
    public async Task DirectoryCommands_AreRegisteredUnderAccounts()
    {
        static Task<int> Run(params string[] args)
        {
            var app = new Spectre.Console.Cli.CommandApp();
            app.Configure(CliApp.Configure);
            return app.RunAsync(args);
        }

        var mock = new MockHttpMessageHandler();
        var (code, output) = await CommandRunner.RunAsync(mock, () => Run("accounts", "directories", "add", "--help"));

        code.Should().Be(0);
        output.Should().Contain("--default-level").And.NotContain("TENANT_ID");
    }
}
