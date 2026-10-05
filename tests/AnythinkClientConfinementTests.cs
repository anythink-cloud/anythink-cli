using AnythinkCli.Client;
using AnythinkCli.Models;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkCli.Tests;

public class AnythinkClientConfinementTests
{
    private const string BaseUrl = "https://api.example.com";
    private const string Root = $"{BaseUrl}/org/1";

    private readonly MockHttpMessageHandler _mock = new();
    private readonly List<Uri> _sent = [];

    private AnythinkClient Client()
    {
        _mock.When("*").Respond(req =>
        {
            _sent.Add(req.RequestUri!);
            return new HttpResponseMessage { Content = new StringContent(req.RequestUri!.AbsolutePath.EndsWith("/fields") ? "[]" : "{}") };
        });
        return new AnythinkClient("1", BaseUrl, new HttpClient(_mock));
    }

    // ── Rule: nothing is ever sent outside {BaseUrl}/org/{OrgId} ──────────────

    [Theory]
    [InlineData($"{Root}/../2/users")]
    [InlineData($"{Root}/entities/../../2/users")]
    [InlineData($"{Root}/%2e%2e/2/users")]
    [InlineData($"{Root}/%2E%2E/%2E%2E/users")]
    [InlineData($"{Root}\\..\\2\\users")]
    [InlineData($"{BaseUrl}/org/10/users")]
    [InlineData($"{BaseUrl}/org/1x")]
    [InlineData($"{BaseUrl}/users")]
    [InlineData("https://other.example/org/1/users")]
    [InlineData("http://api.example.com/org/1/users")]
    [InlineData("https://api.example.com:8443/org/1/users")]
    [InlineData("https://user@api.example.com/org/1/users")]
    public async Task FetchRaw_AUrlThatLeavesTheProjectRoot_IsRefused_AndNothingIsSent(string url)
    {
        var client = Client();

        var act = () => client.FetchRawAsync(url);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _sent.Should().BeEmpty();
    }

    [Theory]
    [InlineData(Root)]
    [InlineData($"{Root}/")]
    [InlineData($"{Root}/entities")]
    [InlineData($"{Root}/entities?filter=../../2")]
    [InlineData($"{Root}/a/../entities")]
    public async Task FetchRaw_AUrlUnderTheProjectRoot_IsSent(string url)
    {
        var client = Client();

        await client.FetchRawAsync(url);

        _sent.Should().ContainSingle().Which.AbsolutePath.Should().StartWith("/org/1");
    }

    [Fact]
    public async Task FetchRaw_EveryMethod_IsConfined()
    {
        var client = Client();
        var outside = $"{Root}/../2/users";

        foreach (var method in new[] { "GET", "POST", "PUT", "DELETE", "PATCH" })
        {
            var act = () => client.FetchRawAsync(outside, method, "{}");
            await act.Should().ThrowAsync<InvalidOperationException>(method);
        }

        _sent.Should().BeEmpty();
    }

    // ── Rule: values that become path segments are escaped, never parsed as paths ──

    [Theory]
    [InlineData("a/b", "a%2Fb")]
    [InlineData("../2/entities/posts", "..%2F2%2Fentities%2Fposts")]
    [InlineData("a?b=c", "a%3Fb%3Dc")]
    [InlineData("a#b", "a%23b")]
    [InlineData("a b", "a%20b")]
    [InlineData("blog_posts", "blog_posts")]
    public async Task APathValue_IsSentAsOneEscapedSegment(string value, string escaped)
    {
        var client = Client();

        await client.GetEntityAsync(value);
        await client.GetFieldsAsync(value);
        await client.DeleteSecretAsync(value);
        await client.GetIntegrationDefinitionAsync(value);
        await client.GetItemRlsUsersAsync(value, 5);
        await client.SetItemRlsUserAsync(value, 5, 7, true);

        _sent.Select(uri => uri.AbsolutePath).Should().Equal(
            $"/org/1/entities/{escaped}",
            $"/org/1/entities/{escaped}/fields",
            $"/org/1/secrets/{escaped}",
            $"/org/1/integrations/definitions/{escaped}",
            $"/org/1/entities/{escaped}/items/5/rls-users",
            $"/org/1/entities/{escaped}/items/5/rls-users");
        _sent.Should().OnlyContain(uri => uri.Query == "");
    }

    [Fact]
    public async Task APathValue_ReachesWriteAndSearchCallsEscapedToo()
    {
        var client = Client();

        await client.UpdateEntityAsync("a/b", new UpdateEntityRequest(false, false, false));
        await client.CreateItemAsync("a/b", new());
        await client.DeleteEntityAsync("a/b");
        await client.PurgeSearchIndexAsync("a/b");
        await client.RehydrateSearchIndexAsync("a/b");

        _sent.Select(uri => uri.AbsolutePath).Should().Equal(
            "/org/1/entities/a%2Fb", "/org/1/entities/a%2Fb/items", "/org/1/entities/a%2Fb",
            "/org/1/search/purge/a%2Fb", "/org/1/search/rehydrate/a%2Fb");
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    public async Task ADotSegment_IsRefused_AndNothingIsSent(string value)
    {
        var client = Client();

        var act = () => client.GetItemAsync(value, 1);

        await act.Should().ThrowAsync<ArgumentException>();
        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task TheProjectRootItself_CanBeFetched()
    {
        var client = Client();

        await client.GetTenantAsync();

        _sent.Should().ContainSingle().Which.AbsolutePath.Should().Be("/org/1");
    }
}
