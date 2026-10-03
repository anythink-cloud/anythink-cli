using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Web;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.TestHost;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RichardSzalay.MockHttp;

namespace AnythinkMcp.Tests;

public class HostedModeTests : IAsyncLifetime
{
    private const string Issuer = "https://issuer.test";
    private const string PublicUrl = "https://mcp.test/mcp";

    private RSA _rsa = null!;
    private RsaSecurityKey _key = null!;
    private MockHttpMessageHandler _mock = null!;
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private int _exchangeCount;
    private Dictionary<string, string> _exchangeForm = new();
    private Func<HttpResponseMessage>? _exchangeOverride;

    private const string Origin = "https://allowed.test";

    private static string ExchangedFor(string inbound) => "ex-" + inbound[^16..];

    public async Task InitializeAsync()
    {
        _rsa = RSA.Create(2048);
        _key = new RsaSecurityKey(_rsa) { KeyId = "test-key-1" };
        _mock = new MockHttpMessageHandler();
        HostedTestSupport.ConfigureIssuer(_mock, Issuer, _key);

        _mock.When(HttpMethod.Post, $"{Issuer}/oauth/v1/token").Respond(async req =>
        {
            Interlocked.Increment(ref _exchangeCount);
            var parsed = HttpUtility.ParseQueryString(await req.Content!.ReadAsStringAsync());
            _exchangeForm = parsed.AllKeys.ToDictionary(k => k!, k => parsed[k]!);
            if (_exchangeOverride is not null) return _exchangeOverride();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"access_token":"{{ExchangedFor(parsed["subject_token"]!)}}","expires_in":3600,"token_type":"Bearer"}""",
                    Encoding.UTF8, "application/json")
            };
        });

        var options = new HostedMode.Options
        {
            PublicUrl = PublicUrl,
            Issuer = Issuer,
            Audience = PublicUrl,
            Exchange = new TokenExchangeOptions { ClientId = "mcp", ClientSecret = "s3cret", Audience = "anythink-oauth" },
            AllowedOrigins = [Origin],
        };
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        _app = HostedMode.BuildPublicApp(builder, options, new McpClientFactory(null, _mock),
            jwt => jwt.BackchannelHttpHandler = _mock, exchangeHandler: _mock);

        await _app.StartAsync();
        _client = _app.GetTestClient();
        _client.BaseAddress = new Uri("https://mcp.test/");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        _rsa.Dispose();
    }

    private string ValidToken(
        string? tid = "42", string? instanceUrl = "https://42.api.anythink.cloud",
        DateTime? expires = null, string? issuer = null, string? audience = null, RsaSecurityKey? signWith = null) =>
        HostedTestSupport.CreateToken(signWith ?? _key, issuer ?? Issuer, audience ?? PublicUrl, tid, instanceUrl, expires);

    private static HttpRequestMessage McpPost(string? bearerToken) =>
        new(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            Headers = { Authorization = bearerToken is null ? null : new AuthenticationHeaderValue("Bearer", bearerToken) }
        };

    private HttpClientTransport Transport(string token) => new HttpClientTransport(new HttpClientTransportOptions
    {
        Endpoint = new Uri(PublicUrl),
        TransportMode = HttpTransportMode.StreamableHttp,
        AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" }
    }, _client, ownsHttpClient: false);

    private static string UpstreamOrgUrl(string tid) => $"https://{tid}.api.anythink.cloud/org/{tid}";

    private MockedRequest MockProject(string tid, string name, string bearer) =>
        _mock.When(HttpMethod.Get, UpstreamOrgUrl(tid))
            .WithHeaders("Authorization", $"Bearer {bearer}")
            .Respond("application/json", $$"""{"id":{{tid}},"name":"{{name}}"}""");

    [Fact]
    public async Task NoToken_Returns401_WithResourceMetadataChallenge()
    {
        var response = await _client.SendAsync(McpPost(null));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Should().ContainSingle();
        var challenge = response.Headers.WwwAuthenticate.Single();
        challenge.Scheme.Should().Be("Bearer");
        challenge.Parameter.Should().Be("resource_metadata=\"https://mcp.test/.well-known/oauth-protected-resource/mcp\"");
    }

    [Fact]
    public async Task NoToken_BehindTlsTerminatingProxy_StillAdvertisesHttpsMetadata()
    {
        var request = McpPost(null);
        request.RequestUri = new Uri("http://mcp.test/mcp");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Single().Parameter
            .Should().Be("resource_metadata=\"https://mcp.test/.well-known/oauth-protected-resource/mcp\"");
    }

    [Theory]
    [InlineData("/.well-known/oauth-protected-resource")]
    [InlineData("/.well-known/oauth-protected-resource/mcp")]
    public async Task ProtectedResourceMetadata_ServedAtBothPaths(string path)
    {
        var response = await _client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("resource").GetString().Should().Be(PublicUrl);

        var servers = doc.RootElement.GetProperty("authorization_servers").EnumerateArray()
            .Select(e => e.GetString()).ToList();
        servers.Should().NotBeEmpty();
        servers[0].Should().Be(Issuer);

        doc.RootElement.GetProperty("bearer_methods_supported").EnumerateArray()
            .Select(e => e.GetString()).Should().Contain("header");
    }

    [Fact]
    public async Task WrongIssuer_Returns401()
    {
        var token = ValidToken(issuer: "https://not-the-issuer.test");
        var response = await _client.SendAsync(McpPost(token));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WrongAudience_Returns401()
    {
        var token = ValidToken(audience: "https://someone-elses-server.test/mcp");
        var response = await _client.SendAsync(McpPost(token));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WrongSignature_Returns401()
    {
        using var otherRsa = RSA.Create(2048);
        var otherKey = new RsaSecurityKey(otherRsa) { KeyId = "not-in-jwks" };

        var token = ValidToken(signWith: otherKey);
        var response = await _client.SendAsync(McpPost(token));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ExpiredToken_Returns401()
    {
        var token = ValidToken(expires: DateTime.UtcNow.AddHours(-1));
        var response = await _client.SendAsync(McpPost(token));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TokenMissingTidOrInstanceUrl_Returns401()
    {
        var token = ValidToken(tid: null);
        var response = await _client.SendAsync(McpPost(token));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task HttpInstanceUrl_Refused()
    {
        var token = ValidToken(instanceUrl: "http://evil.example.com");
        var response = await _client.SendAsync(McpPost(token));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ValidToken_SetsCredentialsFromClaims_IgnoringHeaders()
    {
        const string tid = "42";
        const string instanceUrl = "https://42.api.anythink.cloud";
        var token = ValidToken(tid: tid, instanceUrl: instanceUrl);

        MockProject(tid, "Real Project", ExchangedFor(token));

        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(PublicUrl),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {token}",
                ["X-Org-Id"] = "999999",
                ["X-Instance-Url"] = "https://attacker.example.com",
            }
        }, _client, ownsHttpClient: false);

        await using var mcpClient = await McpClient.CreateAsync(transport);
        var result = await mcpClient.CallToolAsync("project_details");

        var text = ((TextContentBlock)result.Content[0]).Text;
        text.Should().Contain("Real Project");
        text.Should().Contain(tid);
        text.Should().NotContain("999999");
        text.Should().NotContain("attacker.example.com");
    }

    [Fact]
    public async Task ToolsList_HostedMode_ReturnsOnlyProvingTools_WithTitleAndReadOnlyHint()
    {
        var token = ValidToken();
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(PublicUrl),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" }
        }, _client, ownsHttpClient: false);

        await using var mcpClient = await McpClient.CreateAsync(transport);
        var tools = await mcpClient.ListToolsAsync();

        tools.Select(t => t.Name).Should().BeEquivalentTo(
            ["project_details", "entities_list", "records_query"]);

        foreach (var tool in tools)
        {
            tool.Title.Should().NotBeNullOrWhiteSpace();
            tool.ProtocolTool.Annotations?.ReadOnlyHint.Should().BeTrue();
            tool.Name.Length.Should().BeLessOrEqualTo(64);
        }
    }

    [Fact]
    public async Task UpstreamRequest_CarriesExchangedToken_NeverTheInboundOne()
    {
        var token = ValidToken();
        string? upstreamBearer = null;
        _mock.When(HttpMethod.Get, UpstreamOrgUrl("42")).Respond(req =>
        {
            upstreamBearer = req.Headers.Authorization?.Parameter;
            return new HttpResponseMessage { Content = new StringContent("""{"id":42,"name":"P"}""") };
        });

        await using var mcpClient = await McpClient.CreateAsync(Transport(token));
        await mcpClient.CallToolAsync("project_details");

        upstreamBearer.Should().Be(ExchangedFor(token)).And.NotBe(token);
    }

    [Fact]
    public async Task ExchangeRequest_CarriesInboundTokenAndConfiguredAudienceAndClientCredentials()
    {
        var token = ValidToken();
        MockProject("42", "P", ExchangedFor(token));

        await using var mcpClient = await McpClient.CreateAsync(Transport(token));
        await mcpClient.CallToolAsync("project_details");

        _exchangeForm["grant_type"].Should().Be("urn:ietf:params:oauth:grant-type:token-exchange");
        _exchangeForm["subject_token"].Should().Be(token);
        _exchangeForm["subject_token_type"].Should().Be("urn:ietf:params:oauth:token-type:access_token");
        _exchangeForm["audience"].Should().Be("anythink-oauth");
        _exchangeForm["client_id"].Should().Be("mcp");
        _exchangeForm["client_secret"].Should().Be("s3cret");
    }

    [Fact]
    public async Task RepeatedCallsWithOneToken_ExchangeOnlyOnce()
    {
        var token = ValidToken();
        MockProject("42", "P", ExchangedFor(token));

        await using var mcpClient = await McpClient.CreateAsync(Transport(token));
        await mcpClient.CallToolAsync("project_details");
        await mcpClient.CallToolAsync("project_details");

        _exchangeCount.Should().Be(1);
    }

    [Fact]
    public async Task ExchangeRejected_Returns401_WithGenericBody_AndNoUpstreamCall()
    {
        _exchangeOverride = () => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":"invalid_grant","detail":"upstream-secret-detail"}""")
        };
        var token = ValidToken();
        var upstream = MockProject("42", "P", ExchangedFor(token));

        var response = await _client.SendAsync(McpPost(token));
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Single().Parameter.Should().Contain("resource_metadata=");
        body.Should().NotContain("upstream-secret-detail").And.NotContain(token);
        _mock.GetMatchCount(upstream).Should().Be(0);
    }

    [Fact]
    public async Task ExchangeRefusedForAnyOtherOAuthError_Returns502_NotAChallenge()
    {
        _exchangeOverride = () => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""{"error":"invalid_client"}""")
        };

        var response = await _client.SendAsync(McpPost(ValidToken()));

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        response.Headers.WwwAuthenticate.Should().BeEmpty();
    }

    [Fact]
    public async Task SubjectTokenNearExpiry_StillReachesUpstream_WithoutCachingTheExchange()
    {
        var token = ValidToken();
        _exchangeOverride = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"access_token":"short-lived","expires_in":10,"token_type":"Bearer"}""")
        };
        var upstream = MockProject("42", "P", "short-lived");

        await using var mcpClient = await McpClient.CreateAsync(Transport(token));
        await mcpClient.CallToolAsync("project_details");

        _mock.GetMatchCount(upstream).Should().Be(1);
    }

    [Fact]
    public async Task ExchangeUnavailable_Returns502_WithGenericBody_AndNoUpstreamCall()
    {
        _exchangeOverride = () => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("upstream-secret-detail")
        };
        var token = ValidToken();
        var upstream = MockProject("42", "P", ExchangedFor(token));

        var response = await _client.SendAsync(McpPost(token));
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        body.Should().NotContain("upstream-secret-detail").And.NotContain(token);
        _mock.GetMatchCount(upstream).Should().Be(0);
    }

    [Fact]
    public async Task UnsignedToken_AlgNone_Returns401()
    {
        var unsigned = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = PublicUrl,
            Claims = new Dictionary<string, object> { ["tid"] = "42", ["instance_url"] = "https://42.api.anythink.cloud" },
        });

        var response = await _client.SendAsync(McpPost(unsigned));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _exchangeCount.Should().Be(0);
    }

    [Fact]
    public async Task Hs256SignedToken_Returns401()
    {
        var hmacKey = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)) { KeyId = "test-key-1" };
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = PublicUrl,
            Claims = new Dictionary<string, object> { ["tid"] = "42", ["instance_url"] = "https://42.api.anythink.cloud" },
            SigningCredentials = new SigningCredentials(hmacKey, SecurityAlgorithms.HmacSha256),
        });

        var response = await _client.SendAsync(McpPost(token));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _exchangeCount.Should().Be(0);
    }

    [Fact]
    public void JwtValidation_AllowsOnlyRs256()
    {
        var jwt = _app.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
        jwt.TokenValidationParameters.ValidAlgorithms.Should().Equal("RS256");
    }

    [Theory]
    [InlineData("https://evilanythink.cloud")]
    [InlineData("https://42.api.evilanythink.cloud")]
    [InlineData("https://attacker.example.com")]
    public async Task InstanceUrlOutsideAllowlist_Returns401_AndNothingIsExchanged(string instanceUrl)
    {
        var response = await _client.SendAsync(McpPost(ValidToken(instanceUrl: instanceUrl)));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _exchangeCount.Should().Be(0);
    }

    [Fact]
    public void PublicApp_RequestBodyLimitIs1Mb() =>
        _app.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value.Limits.MaxRequestBodySize
            .Should().Be(1_048_576);

    [Fact]
    public async Task UnlistedOrigin_Returns403()
    {
        var request = McpPost(ValidToken());
        request.Headers.Add("Origin", "https://evil.example");

        (await _client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListedOrigin_IsNotBlocked()
    {
        var request = McpPost(null);
        request.Headers.Add("Origin", Origin);

        (await _client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AbsentOrigin_IsNotBlocked() =>
        (await _client.SendAsync(McpPost(null))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task UnexpectedHost_Returns403()
    {
        var request = McpPost(ValidToken());
        request.Headers.Host = "rebind.example";

        (await _client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task HealthCheck_IgnoresHost()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Host = "10.0.0.7:5300";

        (await _client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ConcurrentRequestsForDifferentProjects_GetIsolatedCredentials()
    {
        var tokenA = ValidToken(tid: "1", instanceUrl: "https://1.api.anythink.cloud");
        var tokenB = ValidToken(tid: "2", instanceUrl: "https://2.api.anythink.cloud");
        MockProject("1", "Project One", ExchangedFor(tokenA));
        MockProject("2", "Project Two", ExchangedFor(tokenB));

        async Task<string> Call(string token)
        {
            await using var mcpClient = await McpClient.CreateAsync(Transport(token));
            var result = await mcpClient.CallToolAsync("project_details");
            return ((TextContentBlock)result.Content[0]).Text;
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Call(i % 2 == 0 ? tokenA : tokenB)));

        for (var i = 0; i < results.Length; i++)
            results[i].Should().Contain(i % 2 == 0 ? "Project One" : "Project Two")
                .And.NotContain(i % 2 == 0 ? "Project Two" : "Project One");
    }
}
