using System.Text.Json;
using AnythinkCli.Config;
using AnythinkMcp.Tools;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkMcp.Tests;

[Collection("SequentialConfig")]
public class ProjectToolsTests : McpTestBase
{
    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");
    private static readonly Guid ProjectId1 = Guid.Parse("11111111-aaaa-bbbb-cccc-dddddddddddd");
    private static readonly Guid ProjectId2 = Guid.Parse("22222222-aaaa-bbbb-cccc-dddddddddddd");
    private static readonly Guid PlanId = Guid.Parse("99999999-0000-1111-2222-333333333333");

    private static string ProjectsJson => $$"""
        [
            {
                "id": "{{ProjectId1}}",
                "name": "My App",
                "description": "Main application",
                "plan_id": "{{PlanId}}",
                "region": "lon1",
                "status": 2,
                "api_url": "https://50001.api.anythink.cloud",
                "frontend_url": "https://my-app.anythink.cloud",
                "external_tenant_id": 50001,
                "created_at": "2025-01-15T10:00:00Z"
            },
            {
                "id": "{{ProjectId2}}",
                "name": "Staging",
                "description": null,
                "plan_id": "{{PlanId}}",
                "region": "lon1",
                "status": 0,
                "api_url": null,
                "frontend_url": null,
                "external_tenant_id": null,
                "created_at": "2025-03-20T14:00:00Z"
            }
        ]
    """;

    private MockHttpMessageHandler SetupMockWithProjects()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, $"{BillingUrl}/v1/accounts/{AccountId}/shared-tenants")
            .Respond("application/json", ProjectsJson);
        return mock;
    }

    // ── List ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProjectsList_Returns_All_Projects()
    {
        SetupPlatformLogin(AccountId.ToString());
        var mock = SetupMockWithProjects();

        var tools = new ProjectTools(CreateFactory(mock));
        var result = await tools.ProjectsList();

        var arr = JsonDocument.Parse(result).RootElement;
        arr.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task ProjectsList_Shows_Status_Labels()
    {
        SetupPlatformLogin(AccountId.ToString());
        var mock = SetupMockWithProjects();

        var tools = new ProjectTools(CreateFactory(mock));
        var result = await tools.ProjectsList();

        result.Should().Contain("Active");
        result.Should().Contain("Initializing");
    }

    [Fact]
    public async Task ProjectsList_With_Explicit_AccountId()
    {
        SetupPlatformLogin(); // no active account
        var mock = SetupMockWithProjects();

        var tools = new ProjectTools(CreateFactory(mock));
        var result = await tools.ProjectsList(AccountId.ToString());

        var arr = JsonDocument.Parse(result).RootElement;
        arr.GetArrayLength().Should().Be(2);
    }

    // ── Create ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProjectsCreate_Returns_New_Project()
    {
        SetupPlatformLogin(AccountId.ToString());
        var newId = Guid.NewGuid();
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, $"{BillingUrl}/v1/accounts/{AccountId}/shared-tenants")
            .Respond("application/json", $$"""
                {
                    "id": "{{newId}}",
                    "name": "New Project",
                    "description": null,
                    "plan_id": "{{PlanId}}",
                    "region": "lon1",
                    "status": 0,
                    "api_url": null,
                    "frontend_url": null,
                    "external_tenant_id": 60001,
                    "created_at": "2025-03-23T12:00:00Z"
                }
            """);

        var tools = new ProjectTools(CreateFactory(mock));
        var result = await tools.ProjectsCreate("New Project", PlanId.ToString(), "lon1");

        result.Should().Contain("New Project");
        result.Should().Contain("projects_use");
    }

    // ── Use ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProjectsUse_With_ApiKey_Saves_Profile()
    {
        SetupPlatformLogin(AccountId.ToString());
        var mock = SetupMockWithProjects();

        var tools = new ProjectTools(CreateFactory(mock));
        var result = await tools.ProjectsUse("My App", apiKey: "ak_test_key");

        result.Should().Contain("my-app");
        var profile = ConfigService.GetProfile("my-app")!;
        profile.OrgId.Should().Be("50001");
        profile.ApiKey.Should().Be("ak_test_key");
        profile.InstanceApiUrl.Should().Be("https://50001.api.anythink.cloud");
    }

    [Fact]
    public async Task ProjectsUse_With_TransferToken_Exchanges_For_JWT()
    {
        SetupPlatformLogin(AccountId.ToString());
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, $"{BillingUrl}/v1/accounts/{AccountId}/shared-tenants")
            .Respond("application/json", ProjectsJson);
        mock.When(HttpMethod.Post, $"{BillingUrl}/v1/accounts/{AccountId}/shared-tenants/{ProjectId1}/transfer-token")
            .Respond("application/json", """{"transfer_token":"xfer-tok-123"}""");
        mock.When(HttpMethod.Post, "https://50001.api.anythink.cloud/org/50001/auth/v1/exchange-transfer-token")
            .Respond("application/json", """
                {"access_token":"project-jwt","refresh_token":"proj-rt","expires_in":3600}
            """);

        var tools = new ProjectTools(CreateFactory(mock));
        var result = await tools.ProjectsUse("My App");

        result.Should().Contain("my-app");
        result.Should().Contain("token");
        var profile = ConfigService.GetProfile("my-app")!;
        profile.AccessToken.Should().Be("project-jwt");
        profile.RefreshToken.Should().Be("proj-rt");
    }

    [Fact]
    public async Task ProjectsUse_Matches_By_OrgId()
    {
        SetupPlatformLogin(AccountId.ToString());
        var mock = SetupMockWithProjects();

        var tools = new ProjectTools(CreateFactory(mock));
        var result = await tools.ProjectsUse("50001", apiKey: "ak_test");

        result.Should().Contain("My App");
    }

    [Fact]
    public async Task ProjectsUse_Matches_By_Uuid_Prefix()
    {
        SetupPlatformLogin(AccountId.ToString());
        var mock = SetupMockWithProjects();

        var tools = new ProjectTools(CreateFactory(mock));
        var result = await tools.ProjectsUse("11111111", apiKey: "ak_test");

        result.Should().Contain("My App");
    }

    [Fact]
    public async Task ProjectsUse_Not_Ready_Returns_Error()
    {
        SetupPlatformLogin(AccountId.ToString());
        var mock = SetupMockWithProjects();

        var tools = new ProjectTools(CreateFactory(mock));
        var result = await tools.ProjectsUse("Staging", apiKey: "ak_test");

        result.Should().Contain("not ready");
    }

    [Fact]
    public async Task ProjectsUse_Unknown_Returns_Not_Found()
    {
        SetupPlatformLogin(AccountId.ToString());
        var mock = SetupMockWithProjects();

        var tools = new ProjectTools(CreateFactory(mock));
        var result = await tools.ProjectsUse("nonexistent", apiKey: "ak_test");

        result.Should().Contain("No project found");
    }

    // ── Delete ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProjectsDelete_Deletes_By_Name()
    {
        SetupPlatformLogin(AccountId.ToString());
        var mock = SetupMockWithProjects();
        mock.When(HttpMethod.Delete, $"{BillingUrl}/v1/accounts/{AccountId}/shared-tenants/{ProjectId1}")
            .Respond(System.Net.HttpStatusCode.NoContent);

        var tools = new ProjectTools(CreateFactory(mock));
        var result = await tools.ProjectsDelete("My App");

        result.Should().Contain("deleted");
    }

    [Fact]
    public async Task ProjectsDelete_Unknown_Returns_Not_Found()
    {
        SetupPlatformLogin(AccountId.ToString());
        var mock = SetupMockWithProjects();

        var tools = new ProjectTools(CreateFactory(mock));
        var result = await tools.ProjectsDelete("ghost");

        result.Should().Contain("No project found");
    }

    // ── No account ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProjectsList_Without_Account_Throws()
    {
        SetupPlatformLogin(); // no account ID

        var mock = new MockHttpMessageHandler();
        var tools = new ProjectTools(CreateFactory(mock));

        var act = () => tools.ProjectsList();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*billing account*");
    }
}
