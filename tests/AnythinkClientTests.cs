using System.Net;
using AnythinkCli.Client;
using AnythinkCli.Models;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkCli.Tests;

/// <summary>
/// Tests for AnythinkClient — uses MockHttpMessageHandler to intercept HTTP calls.
/// No real network traffic is made.
/// </summary>
public class AnythinkClientTests
{
    private const string BaseUrl = "https://api.example.com";
    private const string OrgId   = "99999";
    private const string OrgPath = $"{BaseUrl}/org/{OrgId}";

    private static AnythinkClient BuildClient(MockHttpMessageHandler handler)
        => new(OrgId, BaseUrl, new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) });

    // ── Entities ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetEntitiesAsync_Returns_EntityList()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{OrgPath}/entities")
               .Respond("application/json",
                   """[{"name":"customers","table_name":"customers","enable_rls":true,"is_system":false,"is_junction":false,"is_public":false,"lock_new_records":false}]""");

        var client   = BuildClient(handler);
        var entities = await client.GetEntitiesAsync();

        entities.Should().HaveCount(1);
        entities[0].Name.Should().Be("customers");
        entities[0].EnableRls.Should().BeTrue();
    }

    [Fact]
    public async Task GetEntitiesAsync_EmptyArray_ReturnsEmptyList()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{OrgPath}/entities").Respond("application/json", "[]");

        var entities = await BuildClient(handler).GetEntitiesAsync();
        entities.Should().BeEmpty();
    }

    [Fact]
    public async Task GetEntitiesAsync_Unauthorized_ThrowsAnythinkException()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{OrgPath}/entities")
               .Respond(HttpStatusCode.Unauthorized, "application/json", """{"error":"Unauthorized"}""");

        var act = async () => await BuildClient(handler).GetEntitiesAsync();
        await act.Should().ThrowAsync<AnythinkException>()
                 .Where(ex => ex.StatusCode == 401);
    }

    [Fact]
    public async Task CreateEntityAsync_ReturnsCreatedEntity()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"{OrgPath}/entities")
               .Respond("application/json",
                   """{"name":"orders","table_name":"orders","enable_rls":false,"is_system":false,"is_junction":false,"is_public":false,"lock_new_records":false}""");

        var req    = new CreateEntityRequest("orders");
        var result = await BuildClient(handler).CreateEntityAsync(req);

        result.Name.Should().Be("orders");
    }

    [Fact]
    public async Task DeleteEntityAsync_Success_DoesNotThrow()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Delete, $"{OrgPath}/entities/orders")
               .Respond(HttpStatusCode.NoContent);

        var act = async () => await BuildClient(handler).DeleteEntityAsync("orders");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteEntityAsync_NotFound_ThrowsAnythinkException()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Delete, $"{OrgPath}/entities/ghost")
               .Respond(HttpStatusCode.NotFound, "application/json", """{"error":"Not found"}""");

        var act = async () => await BuildClient(handler).DeleteEntityAsync("ghost");
        await act.Should().ThrowAsync<AnythinkException>()
                 .Where(ex => ex.StatusCode == 404);
    }

    // ── Workflows ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetWorkflowsAsync_ReturnsWorkflowList()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{OrgPath}/workflows")
               .Respond("application/json",
                   """[{"id":76,"name":"HN Research Pull","trigger":"Timed","enabled":true,"description":null}]""");

        var workflows = await BuildClient(handler).GetWorkflowsAsync();

        workflows.Should().HaveCount(1);
        workflows[0].Id.Should().Be(76);
        workflows[0].Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task GetWorkflowAsync_NotFound_ThrowsAnythinkException()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{OrgPath}/workflows/999")
               .Respond(HttpStatusCode.NotFound, "application/json", "{}");

        var act = async () => await BuildClient(handler).GetWorkflowAsync(999);
        await act.Should().ThrowAsync<AnythinkException>();
    }

    // ── Users ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetUsersAsync_ReturnsUserList()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{OrgPath}/users")
               .Respond("application/json",
                   """[{"id":1,"first_name":"Alice","last_name":"Smith","email":"alice@example.com","is_confirmed":true,"created_at":"2024-01-01T00:00:00Z"}]""");

        var users = await BuildClient(handler).GetUsersAsync();

        users.Should().HaveCount(1);
        users[0].Email.Should().Be("alice@example.com");
        users[0].IsConfirmed.Should().BeTrue();
    }

    // ── Google OAuth ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetGoogleOAuthAsync_ReturnsSettings()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{OrgPath}/integrations/oauth/google")
               .Respond("application/json",
                   """{"enabled":true,"client_id":"123-abc.apps.googleusercontent.com","client_secret":"***masked***"}""");

        var settings = await BuildClient(handler).GetGoogleOAuthAsync();

        settings.Should().NotBeNull();
        settings!.Enabled.Should().BeTrue();
        settings.ClientId.Should().Be("123-abc.apps.googleusercontent.com");
    }

    [Fact]
    public async Task PutGoogleOAuthAsync_Success_DoesNotThrow()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Put, $"{OrgPath}/integrations/oauth/google")
               .Respond(HttpStatusCode.NoContent);

        var req = new UpdateGoogleOAuthRequest(true, "client-id", "client-secret");
        var act = async () => await BuildClient(handler).PutGoogleOAuthAsync(req);
        await act.Should().NotThrowAsync();
    }

    // ── Error handling ────────────────────────────────────────────────────────

    [Fact]
    public async Task AnythinkException_IncludesStatusCode()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{OrgPath}/entities")
               .Respond(HttpStatusCode.Forbidden, "application/json", """{"message":"Access denied"}""");

        var ex = await Assert.ThrowsAsync<AnythinkException>(
            () => BuildClient(handler).GetEntitiesAsync());

        ex.StatusCode.Should().Be(403);
        ex.Message.Should().Contain("Access denied");
    }

    [Fact]
    public async Task AnythinkException_BadJson_IncludesParseError()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{OrgPath}/entities")
               .Respond(HttpStatusCode.OK, "application/json", "not valid json {{{");

        var act = async () => await BuildClient(handler).GetEntitiesAsync();
        await act.Should().ThrowAsync<AnythinkException>()
                 .WithMessage("*Parse error*");
    }
}
