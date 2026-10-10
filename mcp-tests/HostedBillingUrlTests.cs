using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.Client;
using RichardSzalay.MockHttp;

namespace AnythinkMcp.Tests;

[Collection("SequentialConfig")]
public class HostedBillingUrlTests : McpTestBase, IAsyncLifetime
{
    private const string Issuer = "https://issuer.test";
    private const string Billing = "https://billing.test";
    private const string PublicUrl = "https://mcp.test/mcp";

    private RSA _rsa = null!;
    private RsaSecurityKey _key = null!;
    private MockHttpMessageHandler _mock = null!;
    private WebApplication? _app;

    public Task InitializeAsync()
    {
        _rsa = RSA.Create(2048);
        _key = new RsaSecurityKey(_rsa) { KeyId = "test-key-1" };
        _mock = new MockHttpMessageHandler();
        HostedTestSupport.ConfigureIssuer(_mock, Issuer, _key);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_app is not null) await _app.StopAsync();
        _rsa.Dispose();
    }

    private async Task<HttpClient> Start(string? billingUrl)
    {
        var options = new HostedMode.Options
        {
            PublicUrl = PublicUrl,
            Issuer = Issuer,
            BillingUrl = billingUrl,
            Audience = PublicUrl,
            Exchange = new TokenExchangeOptions { ClientId = "mcp", ClientSecret = "s3cret", Audience = "anythink-oauth" },
        };
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        _app = HostedMode.BuildPublicApp(builder, options, new McpClientFactory(null, _mock),
            jwt => jwt.BackchannelHttpHandler = _mock, exchangeHandler: _mock);
        await _app.StartAsync();
        var client = _app.GetTestClient();
        client.BaseAddress = new Uri("https://mcp.test/");
        return client;
    }

    private async Task<McpClient> Connect(HttpClient http)
    {
        var token = HostedTestSupport.CreateToken(_key, Issuer, PublicUrl, tid: null, instanceUrl: null,
            extraClaims: new Dictionary<string, object> { ["projects"] = "all", ["scope"] = "offline_access account" });
        return await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(PublicUrl),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" }
        }, http, ownsHttpClient: false));
    }

    // ── Rule: account tools call the billing URL when one is set, and the issuer only when it isn't ──

    [Fact]
    public async Task WithABillingUrl_AccountToolsCallItInsteadOfTheIssuer()
    {
        var atBilling = _mock.When(HttpMethod.Get, $"{Billing}/v1/accounts").Respond("application/json", "[]");
        var atIssuer = _mock.When(HttpMethod.Get, $"{Issuer}/v1/accounts").Respond("application/json", "[]");
        using var http = await Start(Billing);

        await using var mcp = await Connect(http);
        (await mcp.CallToolAsync("accounts_list")).IsError.Should().NotBe(true);

        _mock.GetMatchCount(atBilling).Should().Be(1);
        _mock.GetMatchCount(atIssuer).Should().Be(0);
    }

    [Fact]
    public async Task WithoutABillingUrl_AccountToolsCallTheIssuer()
    {
        var atBilling = _mock.When(HttpMethod.Get, $"{Billing}/v1/accounts").Respond("application/json", "[]");
        var atIssuer = _mock.When(HttpMethod.Get, $"{Issuer}/v1/accounts").Respond("application/json", "[]");
        using var http = await Start(null);

        await using var mcp = await Connect(http);
        (await mcp.CallToolAsync("accounts_list")).IsError.Should().NotBe(true);

        _mock.GetMatchCount(atIssuer).Should().Be(1);
        _mock.GetMatchCount(atBilling).Should().Be(0);
    }
}
