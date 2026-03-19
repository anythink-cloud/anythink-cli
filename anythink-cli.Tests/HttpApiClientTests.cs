using AnythinkCli.Client;
using AnythinkCli.Models;
using FluentAssertions;
using RichardSzalay.MockHttp;
using System.Net;
using Xunit;

namespace AnythinkCli.Tests;

/// <summary>
/// Tests for the HttpApiClient base class behaviours, exercised via AnythinkClient:
///   - PutAsync&lt;T&gt; 204 No Content → returns default(T) without throwing
///   - PutAsync&lt;T&gt; 200 with body  → deserialises normally
///   - PutAsync&lt;T&gt; error status   → throws AnythinkException
///   - PutVoidAsync 204             → does not throw
///   - PutVoidAsync error           → throws AnythinkException
///   - DeleteAsync error            → throws AnythinkException
/// </summary>
public class HttpApiClientTests
{
    private const string BaseUrl = "https://api.example.com";
    private const string OrgId   = "1";

    private static AnythinkClient Build(MockHttpMessageHandler mock) =>
        new(OrgId, BaseUrl, mock.ToHttpClient());

    // ── PutAsync<T> — 204 No Content ─────────────────────────────────────────

    [Fact]
    public async Task PutAsync_204_Returns_Default_Without_Throwing()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Put, $"{BaseUrl}/org/{OrgId}/entities/orders")
            .Respond(HttpStatusCode.NoContent);

        var client = Build(mock);
        // UpdateEntityAsync calls PutAsync<Entity>; on 204 it should return null silently.
        var result = await client.UpdateEntityAsync("orders",
            new UpdateEntityRequest(EnableRls: false, IsPublic: false, LockNewRecords: false));

        result.Should().BeNull();
    }

    [Fact]
    public async Task PutAsync_200_With_Body_Deserialises_Correctly()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Put, $"{BaseUrl}/org/{OrgId}/entities/orders")
            .Respond("application/json",
                """{"name":"orders","table_name":"orders","enable_rls":true,"is_system":false,"is_junction":false,"is_public":false,"lock_new_records":false}""");

        var entity = await Build(mock).UpdateEntityAsync("orders",
            new UpdateEntityRequest(EnableRls: true, IsPublic: false, LockNewRecords: false));

        entity.Should().NotBeNull();
        entity!.Name.Should().Be("orders");
        entity.EnableRls.Should().BeTrue();
    }

    [Fact]
    public async Task PutAsync_400_Throws_AnythinkException()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Put, $"{BaseUrl}/org/{OrgId}/entities/orders")
            .Respond(HttpStatusCode.BadRequest, "application/json", "\"Validation failed\"");

        var act = async () => await Build(mock).UpdateEntityAsync("orders",
            new UpdateEntityRequest(EnableRls: false, IsPublic: false, LockNewRecords: false));

        await act.Should().ThrowAsync<AnythinkException>()
            .Where(e => e.StatusCode == 400);
    }

    // ── PutVoidAsync — OAuth configure (returns 204) ──────────────────────────

    [Fact]
    public async Task PutVoidAsync_204_Does_Not_Throw()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Put, $"{BaseUrl}/org/{OrgId}/integrations/oauth/google")
            .Respond(HttpStatusCode.NoContent);

        var act = async () => await Build(mock).PutGoogleOAuthAsync(
            new UpdateGoogleOAuthRequest(true, "client-id", "client-secret"));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PutVoidAsync_400_Throws_AnythinkException()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Put, $"{BaseUrl}/org/{OrgId}/integrations/oauth/google")
            .Respond(HttpStatusCode.BadRequest, "application/json", "\"Bad request\"");

        var act = async () => await Build(mock).PutGoogleOAuthAsync(
            new UpdateGoogleOAuthRequest(true, "x", "y"));

        await act.Should().ThrowAsync<AnythinkException>()
            .Where(e => e.StatusCode == 400);
    }

    // ── DeleteAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_204_Does_Not_Throw()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Delete, $"{BaseUrl}/org/{OrgId}/entities/temp/fields/5")
            .Respond(HttpStatusCode.NoContent);

        var act = async () => await Build(mock).DeleteFieldAsync("temp", 5);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteAsync_404_Throws_AnythinkException()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Delete, $"{BaseUrl}/org/{OrgId}/entities/ghost/fields/99")
            .Respond(HttpStatusCode.NotFound, "application/json", "\"Not found\"");

        var act = async () => await Build(mock).DeleteFieldAsync("ghost", 99);

        await act.Should().ThrowAsync<AnythinkException>()
            .Where(e => e.StatusCode == 404);
    }

    // ── GetAsync — malformed JSON throws ─────────────────────────────────────

    [Fact]
    public async Task GetAsync_MalformedJson_Throws_AnythinkException_With_ParseError()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, $"{BaseUrl}/org/{OrgId}/entities")
            .Respond("application/json", "not-valid-json{{{{");

        var act = async () => await Build(mock).GetEntitiesAsync();

        await act.Should().ThrowAsync<AnythinkException>()
            .WithMessage("*Parse error*");
    }

    // ── PostAsync — empty response throws ────────────────────────────────────

    [Fact]
    public async Task PostAsync_EmptyResponse_Throws_AnythinkException()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, $"{BaseUrl}/org/{OrgId}/entities")
            .Respond(HttpStatusCode.OK, "application/json", "");

        var act = async () => await Build(mock).CreateEntityAsync(
            new CreateEntityRequest("test_entity"));

        await act.Should().ThrowAsync<AnythinkException>()
            .WithMessage("*Empty response*");
    }
}
