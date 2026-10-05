using System.Net;
using System.Web;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkMcp.Tests;

public class HostedTokenExchangerTests
{
    private const string Issuer = "https://issuer.test";
    private const string TokenEndpoint = "https://issuer.test/oauth/v1/token";

    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static readonly TokenExchangeOptions Options = new()
    {
        ClientId = "mcp-client", ClientSecret = "mcp-secret", Audience = "anythink-oauth"
    };

    private readonly MockHttpMessageHandler _mock = new();
    private readonly FakeClock _clock = new();
    private int _exchanges;
    private Dictionary<string, string> _form = new();
    private string? _requestUri;

    public HostedTokenExchangerTests()
    {
        _mock.When(HttpMethod.Get, $"{Issuer}/.well-known/oauth-authorization-server")
            .Respond("application/json", $$"""{"issuer":"{{Issuer}}","token_endpoint":"{{TokenEndpoint}}"}""");

        _mock.When(HttpMethod.Post, TokenEndpoint).Respond(async req =>
        {
            _exchanges++;
            _requestUri = req.RequestUri!.ToString();
            var parsed = HttpUtility.ParseQueryString(await req.Content!.ReadAsStringAsync());
            _form = parsed.AllKeys.ToDictionary(k => k!, k => parsed[k]!);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"access_token":"exchanged-{{_exchanges}}","expires_in":3600,"token_type":"Bearer","issued_token_type":"urn:ietf:params:oauth:token-type:access_token"}""",
                    System.Text.Encoding.UTF8, "application/json")
            };
        });
    }

    private HostedTokenExchanger Exchanger(MockHttpMessageHandler? mock = null, TokenExchangeOptions? options = null, int cacheLimit = 100) =>
        new(new HttpClient(mock ?? _mock), Issuer, options ?? Options, _clock, cacheLimit: cacheLimit);

    private static void Discovery(MockHttpMessageHandler mock, string issuer = Issuer, string endpoint = TokenEndpoint) =>
        mock.When(HttpMethod.Get, $"{Issuer}/.well-known/oauth-authorization-server")
            .Respond("application/json", $$"""{"issuer":"{{issuer}}","token_endpoint":"{{endpoint}}"}""");

    [Fact]
    public async Task Exchange_SendsRfc8693FormFields_AndKeepsSecretsOutOfTheUrl()
    {
        var token = await Exchanger().ExchangeAsync("inbound-token", null, default);

        token.Should().Be("exchanged-1");
        _form["grant_type"].Should().Be("urn:ietf:params:oauth:grant-type:token-exchange");
        _form["subject_token"].Should().Be("inbound-token");
        _form["subject_token_type"].Should().Be("urn:ietf:params:oauth:token-type:access_token");
        _form["audience"].Should().Be("anythink-oauth");
        _form["client_id"].Should().Be("mcp-client");
        _form["client_secret"].Should().Be("mcp-secret");
        _requestUri.Should().NotContain("inbound-token").And.NotContain("mcp-secret");
    }

    [Fact]
    public async Task Exchange_FallsBackToOpenIdConfiguration_WhenOAuthMetadataIsMissing()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, $"{Issuer}/.well-known/openid-configuration")
            .Respond("application/json", $$"""{"issuer":"{{Issuer}}","token_endpoint":"{{TokenEndpoint}}"}""");
        mock.When(HttpMethod.Post, TokenEndpoint)
            .Respond("application/json", """{"access_token":"via-openid","expires_in":60}""");

        var token = await Exchanger(mock)
            .ExchangeAsync("inbound", null, default);

        token.Should().Be("via-openid");
    }

    [Fact]
    public async Task Exchange_TokenEndpointOverPlainHttp_Refused()
    {
        var mock = new MockHttpMessageHandler();
        Discovery(mock, endpoint: "http://issuer.test/token");

        var act = () => Exchanger(mock)
            .ExchangeAsync("inbound", null, default);

        await act.Should().ThrowAsync<TokenExchangeException>();
    }

    [Fact]
    public async Task Exchange_SameInboundToken_IsServedFromCache()
    {
        var exchanger = Exchanger();

        var first = await exchanger.ExchangeAsync("inbound", null, default);
        var second = await exchanger.ExchangeAsync("inbound", null, default);

        second.Should().Be(first);
        _exchanges.Should().Be(1);
    }

    [Fact]
    public async Task Exchange_ConcurrentRequestsForOneToken_ShareASingleExchange()
    {
        var exchanger = Exchanger();

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => exchanger.ExchangeAsync("inbound", null, default)));

        results.Distinct().Should().ContainSingle();
        _exchanges.Should().Be(1);
    }

    [Fact]
    public async Task Exchange_DifferentInboundTokens_AreExchangedSeparately()
    {
        var exchanger = Exchanger();

        var a = await exchanger.ExchangeAsync("inbound-a", null, default);
        var b = await exchanger.ExchangeAsync("inbound-b", null, default);

        a.Should().NotBe(b);
        _exchanges.Should().Be(2);
    }

    [Fact]
    public async Task Exchange_AfterExpiry_ExchangesAgain()
    {
        var exchanger = Exchanger();
        await exchanger.ExchangeAsync("inbound", null, default);

        _clock.Now += TimeSpan.FromSeconds(3600);
        var refreshed = await exchanger.ExchangeAsync("inbound", null, default);

        refreshed.Should().Be("exchanged-2");
    }

    [Fact]
    public async Task Exchange_JustBeforeExpiryWindow_StillServedFromCache()
    {
        var exchanger = Exchanger();
        await exchanger.ExchangeAsync("inbound", null, default);

        _clock.Now += TimeSpan.FromSeconds(3500);
        await exchanger.ExchangeAsync("inbound", null, default);

        _exchanges.Should().Be(1);
    }

    [Fact]
    public async Task Exchange_Rejected4xx_ThrowsRejected_WithoutEchoingTheBody()
    {
        var mock = new MockHttpMessageHandler();
        Discovery(mock);
        mock.When(HttpMethod.Post, TokenEndpoint)
            .Respond(HttpStatusCode.BadRequest, "application/json", """{"error":"invalid_grant","error_description":"upstream-secret"}""");

        var act = () => Exchanger(mock)
            .ExchangeAsync("inbound", null, default);

        var ex = (await act.Should().ThrowAsync<TokenExchangeException>()).Which;
        ex.Rejected.Should().BeTrue();
        ex.Message.Should().NotContain("upstream-secret");
    }

    [Fact]
    public async Task Exchange_ServerError_ThrowsNotRejected_AndIsRetriedOnTheNextCall()
    {
        var calls = 0;
        var mock = new MockHttpMessageHandler();
        Discovery(mock);
        mock.When(HttpMethod.Post, TokenEndpoint).Respond(_ => new HttpResponseMessage(
            ++calls == 1 ? HttpStatusCode.InternalServerError : HttpStatusCode.OK)
        {
            Content = new StringContent("""{"access_token":"recovered","expires_in":60}""")
        });
        var exchanger = Exchanger(mock);

        var act = () => exchanger.ExchangeAsync("inbound", null, default);
        (await act.Should().ThrowAsync<TokenExchangeException>()).Which.Rejected.Should().BeFalse();

        (await exchanger.ExchangeAsync("inbound", null, default)).Should().Be("recovered");
    }

    [Fact]
    public async Task Exchange_ResponseWithoutAccessToken_Fails()
    {
        var mock = new MockHttpMessageHandler();
        Discovery(mock);
        mock.When(HttpMethod.Post, TokenEndpoint).Respond("application/json", """{"expires_in":60}""");

        var act = () => Exchanger(mock)
            .ExchangeAsync("inbound", null, default);

        await act.Should().ThrowAsync<TokenExchangeException>();
    }

    [Fact]
    public async Task Exchange_LifetimeWithinSkew_ReturnsTokenWithoutCachingIt()
    {
        var mock = new MockHttpMessageHandler();
        Discovery(mock);
        var post = mock.When(HttpMethod.Post, TokenEndpoint)
            .Respond("application/json", """{"access_token":"short-lived","expires_in":10}""");
        var exchanger = Exchanger(mock);

        (await exchanger.ExchangeAsync("inbound", null, default)).Should().Be("short-lived");

        mock.GetMatchCount(post).Should().Be(1);
        exchanger.CachedCount.Should().Be(0);
    }

    [Theory]
    [InlineData(400, "invalid_grant", true)]
    [InlineData(400, "invalid_client", false)]
    [InlineData(401, "invalid_client", false)]
    [InlineData(400, "unauthorized_client", false)]
    [InlineData(400, "invalid_target", false)]
    [InlineData(400, "invalid_request", false)]
    [InlineData(500, "server_error", false)]
    [InlineData(400, "not an oauth code!", false)]
    public async Task Exchange_ErrorCode_DecidesWhetherTheTokenWasRejected(int status, string error, bool rejected)
    {
        var mock = new MockHttpMessageHandler();
        Discovery(mock);
        mock.When(HttpMethod.Post, TokenEndpoint)
            .Respond((HttpStatusCode)status, "application/json", $$"""{"error":"{{error}}"}""");

        var act = () => Exchanger(mock).ExchangeAsync("inbound", null, default);

        (await act.Should().ThrowAsync<TokenExchangeException>()).Which.Rejected.Should().Be(rejected);
    }

    [Fact]
    public async Task Cache_NeverGrowsPastItsSizeLimit()
    {
        var exchanger = Exchanger(cacheLimit: 3);

        for (var i = 0; i < 20; i++) await exchanger.ExchangeAsync($"inbound-{i}", null, default);

        exchanger.CachedCount.Should().BeLessOrEqualTo(3);
    }

    [Fact]
    public async Task Cache_ExpiredEntryIsReplacedNotAccumulated()
    {
        var exchanger = Exchanger();
        await exchanger.ExchangeAsync("inbound", null, default);

        _clock.Now += TimeSpan.FromSeconds(3600);
        await exchanger.ExchangeAsync("inbound", null, default);

        exchanger.CachedCount.Should().Be(1);
    }

    [Fact]
    public async Task Discovery_IssuerMismatch_Refused()
    {
        var mock = new MockHttpMessageHandler();
        Discovery(mock, issuer: "https://someone-else.test");
        var post = mock.When(HttpMethod.Post, TokenEndpoint).Respond("application/json", """{"access_token":"x","expires_in":3600}""");

        var act = () => Exchanger(mock).ExchangeAsync("inbound", null, default);

        await act.Should().ThrowAsync<TokenExchangeException>();
        mock.GetMatchCount(post).Should().Be(0);
    }

    [Fact]
    public async Task Discovery_CrossOriginTokenEndpoint_Refused_AndNoSecretIsSentThere()
    {
        var mock = new MockHttpMessageHandler();
        Discovery(mock, endpoint: "https://attacker.test/token");
        var post = mock.When(HttpMethod.Post, "https://attacker.test/token")
            .Respond("application/json", """{"access_token":"x","expires_in":3600}""");

        var act = () => Exchanger(mock).ExchangeAsync("inbound", null, default);

        await act.Should().ThrowAsync<TokenExchangeException>();
        mock.GetMatchCount(post).Should().Be(0);
    }

    [Fact]
    public async Task ExplicitTokenEndpoint_SkipsDiscovery_AndMayBeCrossOrigin()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, "https://sts.test/token")
            .Respond("application/json", """{"access_token":"via-override","expires_in":3600}""");
        var options = new TokenExchangeOptions
        {
            ClientId = "c", ClientSecret = "s", Audience = "a", TokenEndpoint = "https://sts.test/token"
        };

        (await Exchanger(mock, options).ExchangeAsync("inbound", null, default)).Should().Be("via-override");
    }

    [Fact]
    public void ExchangeHandler_DoesNotFollowRedirects() =>
        HostedTokenExchanger.CreateHandler().AllowAutoRedirect.Should().BeFalse();
}
