using System.Net;
using AnythinkCli.Client;
using AnythinkCli.Models;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkCli.Tests;

/// <summary>
/// Tests for the integrations CLI client — covers the catalog (definitions), connections,
/// OAuth setup, and execution endpoints. Body shapes are verified for the request paths
/// that send credentials or operation inputs, since that's where we hit real bugs (snake_case
/// vs camelCase, JsonValue.Create on JsonNode, etc.) during development.
/// </summary>
public class IntegrationsTests
{
    private const string BaseUrl = "https://api.example.com";
    private const string OrgId   = "99999";
    private const string OrgPath = $"{BaseUrl}/org/{OrgId}";

    private static AnythinkClient BuildClient(MockHttpMessageHandler handler)
        => new(OrgId, BaseUrl, new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) });

    // ── Definitions (catalog) ────────────────────────────────────────────────

    [Fact]
    public async Task GetIntegrationDefinitionsAsync_ReturnsList()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{OrgPath}/integrations/definitions").Respond("application/json",
            """
            [
              {"id":"claude","provider":"claude","parent_provider":null,
               "display_name":"Claude AI","description":"AI","icon":"brain","category":"AI",
               "operations":[{"key":"generate-text","display_name":"Generate","description":"d"}],
               "auth_type":"ApiKey","is_enabled":true,"can_social_sign_in":false}
            ]
            """);

        var defs = await BuildClient(handler).GetIntegrationDefinitionsAsync();

        defs.Should().HaveCount(1);
        defs[0].Provider.Should().Be("claude");
        defs[0].DisplayName.Should().Be("Claude AI");
        defs[0].AuthType.Should().Be("ApiKey");
        defs[0].Operations.Should().HaveCount(1);
        defs[0].Operations[0].Key.Should().Be("generate-text");
    }

    [Fact]
    public async Task GetIntegrationDefinitionAsync_ReturnsSingle()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{OrgPath}/integrations/definitions/slack").Respond("application/json",
            """
            {"id":"slack","provider":"slack","parent_provider":null,
             "display_name":"Slack","description":"Chat","icon":"slack","category":"Communication",
             "operations":[],"auth_type":"OAuth2","is_enabled":true,"can_social_sign_in":false}
            """);

        var def = await BuildClient(handler).GetIntegrationDefinitionAsync("slack");

        def.Should().NotBeNull();
        def!.Provider.Should().Be("slack");
        def.AuthType.Should().Be("OAuth2");
    }

    [Fact]
    public async Task GetIntegrationDefinitionAsync_NotFound_Throws()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{OrgPath}/integrations/definitions/missing")
            .Respond(HttpStatusCode.NotFound, "application/json", """{"error":"not found"}""");

        var act = async () => await BuildClient(handler).GetIntegrationDefinitionAsync("missing");
        await act.Should().ThrowAsync<AnythinkException>().Where(ex => ex.StatusCode == 404);
    }

    // ── Connections ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetIntegrationConnectionsAsync_ReturnsList()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{OrgPath}/integrations/connections").Respond("application/json",
            """
            [
              {"id":"abc-123","tenant_id":99999,"user_id":null,
               "integration_definition_id":"slack","provider":"slack","name":"main","display_name":"main",
               "is_enabled":true,"connected_at":"2026-05-05T20:00:00Z","last_used_at":null}
            ]
            """);

        var conns = await BuildClient(handler).GetIntegrationConnectionsAsync();

        conns.Should().HaveCount(1);
        conns[0].Id.Should().Be("abc-123");
        conns[0].Provider.Should().Be("slack");
        conns[0].UserId.Should().BeNull();
        conns[0].IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task GetIntegrationConnectionsForProviderAsync_HitsCorrectUrl()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{OrgPath}/integrations/definitions/slack/connections")
            .Respond("application/json", "[]");

        var conns = await BuildClient(handler).GetIntegrationConnectionsForProviderAsync("slack");
        conns.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateApiKeyConnectionAsync_PostsSnakeCaseBody()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Post, $"{OrgPath}/integrations/connections/api-key")
            .WithContent("""{"integration_definition_id":"claude","name":"main","api_key":"sk-test","is_user_connection":false}""")
            .Respond("application/json",
                """
                {"id":"new-id","tenant_id":99999,"user_id":null,
                 "integration_definition_id":"claude","provider":"claude","name":"main","display_name":"main",
                 "is_enabled":true,"connected_at":"2026-05-05T20:00:00Z","last_used_at":null}
                """);

        var req = new CreateApiKeyConnectionRequest("claude", "main", "sk-test", IsUserConnection: false);
        var result = await BuildClient(handler).CreateApiKeyConnectionAsync(req);

        result.Id.Should().Be("new-id");
        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task UpdateIntegrationConnectionAsync_PutsSnakeCaseBody()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Put, $"{OrgPath}/integrations/connections/abc")
            .WithContent("""{"is_enabled":false}""")
            .Respond("application/json",
                """
                {"id":"abc","tenant_id":99999,"user_id":null,
                 "integration_definition_id":"slack","provider":"slack","name":"main","display_name":"main",
                 "is_enabled":false,"connected_at":"2026-05-05T20:00:00Z","last_used_at":null}
                """);

        var result = await BuildClient(handler).UpdateIntegrationConnectionAsync("abc", new UpdateConnectionRequest(IsEnabled: false));

        result.Should().NotBeNull();
        result!.IsEnabled.Should().BeFalse();
        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task DeleteIntegrationConnectionAsync_HitsDeleteUrl()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Delete, $"{OrgPath}/integrations/connections/abc")
            .Respond(HttpStatusCode.NoContent);

        await BuildClient(handler).DeleteIntegrationConnectionAsync("abc");

        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task TestIntegrationConnectionAsync_ParsesResult()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"{OrgPath}/integrations/connections/abc/test")
            .Respond("application/json", """{"success":true,"message":"Connection successful"}""");

        var result = await BuildClient(handler).TestIntegrationConnectionAsync("abc");

        result.Success.Should().BeTrue();
        result.Message.Should().Be("Connection successful");
    }

    // ── OAuth setup ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetIntegrationOAuthSettingsAsync_ReturnsSettings()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{OrgPath}/integrations/definitions/slack/oauth").Respond("application/json",
            """{"has_client_id":true,"use_social_sign_in":false,"is_enabled":true}""");

        var settings = await BuildClient(handler).GetIntegrationOAuthSettingsAsync("slack");

        settings.Should().NotBeNull();
        settings!.HasClientId.Should().BeTrue();
        settings.UseSocialSignIn.Should().BeFalse();
        settings.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task SetIntegrationOAuthSettingsAsync_PutsSnakeCaseBody()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Put, $"{OrgPath}/integrations/definitions/google/oauth")
            .WithContent("""{"client_id":"cid","client_secret":"csec","is_enabled":true,"use_social_sign_in":true}""")
            .Respond(HttpStatusCode.NoContent);

        await BuildClient(handler).SetIntegrationOAuthSettingsAsync("google",
            new SetOAuthSettingsRequest("cid", "csec", true, UseSocialSignIn: true));

        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task GetIntegrationOAuthUrlAsync_PassesEncodedRedirectUri()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect($"{OrgPath}/integrations/definitions/slack/oauth-url")
            .WithQueryString("redirectUri", "http://localhost:8745/callback")
            .Respond("application/json", """{"url":"https://slack.com/oauth/v2/authorize?client_id=x"}""");

        var result = await BuildClient(handler).GetIntegrationOAuthUrlAsync("slack", "http://localhost:8745/callback");

        result.Url.Should().StartWith("https://slack.com/");
        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task CreateOAuthConnectionAsync_PostsSnakeCaseBody()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Post, $"{OrgPath}/integrations/connections")
            .WithContent("""{"integration_definition_id":"slack","name":"main","auth_code":"code123","redirect_uri":"http://localhost:8745/callback","state":"abc","is_user_connection":false}""")
            .Respond("application/json",
                """
                {"id":"new-id","tenant_id":99999,"user_id":null,
                 "integration_definition_id":"slack","provider":"slack","name":"main","display_name":"main",
                 "is_enabled":true,"connected_at":"2026-05-05T20:00:00Z","last_used_at":null}
                """);

        var req = new CreateConnectionRequest(
            IntegrationDefinitionId: "slack",
            Name: "main",
            AuthCode: "code123",
            RedirectUri: "http://localhost:8745/callback",
            State: "abc",
            IsUserConnection: false);

        var result = await BuildClient(handler).CreateOAuthConnectionAsync(req);

        result.Id.Should().Be("new-id");
        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task ExecuteIntegrationAsync_PostsOperationAndInputs()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Post, $"{OrgPath}/integrations/definitions/claude/execute")
            .WithContent("""{"operation":"generate-text","inputs":{"prompt":"hi"}}""")
            .Respond("application/json", """{"success":true,"content":"hello"}""");

        var inputs = new Dictionary<string, object> { ["prompt"] = "hi" };
        var req = new ExecuteIntegrationRequest("generate-text", inputs);

        var result = await BuildClient(handler).ExecuteIntegrationAsync("claude", req);

        result["content"]!.ToString().Should().Be("hello");
        handler.VerifyNoOutstandingExpectation();
    }

    // ── Dashboard callback URL derivation ───────────────────────────────────

    [Fact]
    public void IntegrationsCallbackUrl_DerivesDashboardUrl_FromCloudApiUrl()
    {
        var client = new AnythinkClient("37523255", "https://api.uk01-lon.anythink.cloud");

        client.IntegrationsCallbackUrl.Should().Be(
            "https://uk01-lon.anythink.cloud/org/37523255/settings/integrations/callback");
    }

    [Fact]
    public void IntegrationsCallbackUrl_LeavesNonApiHostUnchanged()
    {
        // For non-standard hosts (no "api." prefix), the URL is left as-is — user can
        // override later via flag/env if a deploy uses a different layout.
        var client = new AnythinkClient("42", "https://localhost:7136");

        client.IntegrationsCallbackUrl.Should().Be(
            "https://localhost:7136/org/42/settings/integrations/callback");
    }

    [Fact]
    public void DashboardUrl_StripsApiPrefixOnly()
    {
        var client = new AnythinkClient("1", "https://api.staging.example.com");
        client.DashboardUrl.Should().Be("https://staging.example.com");
    }
}
