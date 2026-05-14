using System.Net;
using System.Text.Json.Nodes;
using AnythinkCli.Client;
using AnythinkCli.Models;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkCli.Tests;

/// <summary>
/// Tests for the search CLI client — query, similar, rehydrate, purge.
/// Includes wire-format verification for the snake_case wrapper response,
/// because the explore agent had it as camelCase originally and we caught
/// the bug by hitting the live API. Locking it in here so a regression
/// would fail tests rather than silently break "0 matches" output.
/// </summary>
public class SearchTests
{
    private const string BaseUrl = "https://api.example.com";
    private const string OrgId   = "99999";
    private const string OrgPath = $"{BaseUrl}/org/{OrgId}";

    private static AnythinkClient BuildClient(MockHttpMessageHandler handler)
        => new(OrgId, BaseUrl, new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) });

    [Fact]
    public async Task SearchAsync_ParsesSnakeCaseWrapper()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{OrgPath}/search").Respond("application/json",
            """
            {
              "items": [{"id":"1","__name":"Test","__entity":"posts"}],
              "page": 2,
              "page_size": 10,
              "total_items": 42,
              "total_pages": 5,
              "has_next_page": true,
              "has_previous_page": true,
              "retrieval_time": 12,
              "facet_distribution": null
            }
            """);

        var result = await BuildClient(handler).SearchAsync("");

        result.Items.Should().HaveCount(1);
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(10);
        result.TotalItems.Should().Be(42);
        result.TotalPages.Should().Be(5);
        result.HasNextPage.Should().BeTrue();
        result.HasPreviousPage.Should().BeTrue();
        result.RetrievalTime.Should().Be(12);
    }

    [Fact]
    public async Task SearchAsync_PassesQueryStringThrough()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect($"{OrgPath}/search?q=hello&pageSize=5")
            .Respond("application/json",
                """{"items":[],"page":1,"page_size":5,"total_items":0,"total_pages":0,"has_next_page":false,"has_previous_page":false,"retrieval_time":0,"facet_distribution":null}""");

        await BuildClient(handler).SearchAsync("q=hello&pageSize=5");

        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task SearchAsync_PublicMode_HitsPublicEndpoint()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect($"{OrgPath}/search/public?q=x")
            .Respond("application/json",
                """{"items":[],"page":1,"page_size":20,"total_items":0,"total_pages":0,"has_next_page":false,"has_previous_page":false,"retrieval_time":0,"facet_distribution":null}""");

        await BuildClient(handler).SearchAsync("q=x", isPublic: true);

        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task SearchAsync_EmptyQueryString_HitsBareUrl()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect($"{OrgPath}/search")
            .Respond("application/json",
                """{"items":[],"page":1,"page_size":20,"total_items":0,"total_pages":0,"has_next_page":false,"has_previous_page":false,"retrieval_time":0,"facet_distribution":null}""");

        await BuildClient(handler).SearchAsync("");

        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task SearchAsync_FacetsParseAsJsonElement()
    {
        var handler = new MockHttpMessageHandler();
        handler.When($"{OrgPath}/search").Respond("application/json",
            """
            {
              "items": [],
              "page": 1, "page_size": 20, "total_items": 0, "total_pages": 0,
              "has_next_page": false, "has_previous_page": false, "retrieval_time": 0,
              "facet_distribution": {"status": {"published": 10, "draft": 3}}
            }
            """);

        var result = await BuildClient(handler).SearchAsync("");

        result.FacetDistribution.Should().NotBeNull();
        result.FacetDistribution!.Value.GetProperty("status").GetProperty("published").GetInt32()
            .Should().Be(10);
    }

    [Fact]
    public async Task SearchSimilarAsync_BuildsCorrectQueryString()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect($"{OrgPath}/search/similar?e=posts&id=42&limit=5")
            .Respond("application/json",
                """[{"id":"43","__name":"Similar 1","__entity":"posts"}]""");

        var items = await BuildClient(handler).SearchSimilarAsync("posts", 42, 5);

        items.Should().HaveCount(1);
        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task SearchSimilarAsync_PublicMode_HitsPublicEndpoint()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect($"{OrgPath}/search/public/similar?e=posts&id=42&limit=10")
            .Respond("application/json", "[]");

        await BuildClient(handler).SearchSimilarAsync("posts", 42, 10, isPublic: true);

        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task SearchSimilarAsync_EncodesEntityName()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect($"{OrgPath}/search/similar?e=blog%20posts&id=1&limit=10")
            .Respond("application/json", "[]");

        await BuildClient(handler).SearchSimilarAsync("blog posts", 1);

        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task RehydrateSearchIndexAsync_NoEntity_HitsGlobalRoute()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Post, $"{OrgPath}/search/rehydrate")
            .Respond(HttpStatusCode.NoContent);

        await BuildClient(handler).RehydrateSearchIndexAsync();

        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task RehydrateSearchIndexAsync_WithEntity_HitsScopedRoute()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Post, $"{OrgPath}/search/rehydrate/posts")
            .Respond(HttpStatusCode.NoContent);

        await BuildClient(handler).RehydrateSearchIndexAsync("posts");

        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task PurgeSearchIndexAsync_NoEntity_HitsGlobalRoute()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Delete, $"{OrgPath}/search/purge")
            .Respond(HttpStatusCode.NoContent);

        await BuildClient(handler).PurgeSearchIndexAsync();

        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task PurgeSearchIndexAsync_WithEntity_HitsScopedRoute()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Delete, $"{OrgPath}/search/purge/posts")
            .Respond(HttpStatusCode.NoContent);

        await BuildClient(handler).PurgeSearchIndexAsync("posts");

        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task PurgeSearchIndexAsync_NotAdmin_PropagatesAs403()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Delete, $"{OrgPath}/search/purge")
            .Respond(HttpStatusCode.Forbidden, "application/json", """{"error":"Admin access required"}""");

        var act = async () => await BuildClient(handler).PurgeSearchIndexAsync();
        await act.Should().ThrowAsync<AnythinkException>().Where(ex => ex.StatusCode == 403);
    }

    // ── Anonymous request guarantee for --public ────────────────────────────
    //
    // The --public mode must NOT send the user's bearer token, otherwise the
    // audit's "what does an unauthenticated visitor see?" call is reflecting
    // an authenticated request and the comparison is meaningless. We verify
    // here by constructing a real production AnythinkClient (which sets the
    // Authorization default header on the inner HttpClient) and asserting the
    // anonymous HttpClient it uses has no auth headers.

    [Fact]
    public void AnonymousHttpClient_HasNoAuthHeaders_EvenWhenTokenSet()
    {
        // Production constructor — sets Authorization on the auth HttpClient
        var client = new AnythinkClient(OrgId, BaseUrl, token: "fake-bearer-token");

        // Reach in via reflection to verify the anonymous client is genuinely empty
        var anonField = typeof(AnythinkClient)
            .GetField("_anonymousHttp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        anonField.Should().NotBeNull("the anonymous HttpClient field should exist");

        var anonHttp = (HttpClient)anonField!.GetValue(client)!;
        anonHttp.DefaultRequestHeaders.Authorization.Should().BeNull(
            "the anonymous client must not carry a bearer token");
        anonHttp.DefaultRequestHeaders.Contains("X-API-Key").Should().BeFalse(
            "the anonymous client must not carry an API key");
    }

    [Fact]
    public void AnonymousHttpClient_HasNoApiKeyHeader_EvenWhenApiKeySet()
    {
        var client = new AnythinkClient(OrgId, BaseUrl, apiKey: "ak_fake");

        var anonField = typeof(AnythinkClient)
            .GetField("_anonymousHttp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var anonHttp = (HttpClient)anonField!.GetValue(client)!;

        anonHttp.DefaultRequestHeaders.Contains("X-API-Key").Should().BeFalse();
        anonHttp.DefaultRequestHeaders.Authorization.Should().BeNull();
    }
}
