using AnythinkMcp.Cli;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Web;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
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

    public Task InitializeAsync() => StartAppAsync(HostedMode.DefaultMaxConcurrentRequestsPerProject);

    private async Task RestartAppAsync(int maxConcurrentRequestsPerProject)
    {
        await DisposeAsync();
        await StartAppAsync(maxConcurrentRequestsPerProject);
    }

    private async Task StartAppAsync(int maxConcurrentRequestsPerProject)
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
            MaxConcurrentRequestsPerProject = maxConcurrentRequestsPerProject,
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
        string? tid = "42", string? instanceUrl = "https://api.my.anythink.cloud",
        DateTime? expires = null, string? issuer = null, string? audience = null, RsaSecurityKey? signWith = null,
        DateTime? notBefore = null) =>
        HostedTestSupport.CreateToken(signWith ?? _key, issuer ?? Issuer, audience ?? PublicUrl, tid, instanceUrl, expires, notBefore);

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

    private static string UpstreamOrgUrl(string tid, string host = "api.my.anythink.cloud") => $"https://{host}/org/{tid}";

    private MockedRequest MockProject(string tid, string name, string bearer, string host = "api.my.anythink.cloud") =>
        _mock.When(HttpMethod.Get, UpstreamOrgUrl(tid, host))
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
        var token = ValidToken(expires: DateTime.UtcNow.AddHours(-1), notBefore: DateTime.UtcNow.AddHours(-2));
        var response = await _client.SendAsync(McpPost(token));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(null, "https://api.my.anythink.cloud")]
    [InlineData("42", null)]
    [InlineData("not-a-number", "https://api.my.anythink.cloud")]
    public async Task TokenWithoutAValidTidOrInstanceUrl_Returns401(string? tid, string? instanceUrl)
    {
        var token = ValidToken(tid: tid, instanceUrl: instanceUrl);
        var response = await _client.SendAsync(McpPost(token));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ValidToken_SetsCredentialsFromClaims_IgnoringHeaders()
    {
        const string tid = "42";
        const string instanceUrl = "https://api.my.anythink.cloud";
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
    public async Task ToolsList_HostedMode_IsProjectDetailsAndTheRemoteCliTools_AllAnnotated()
    {
        await using var mcpClient = await McpClient.CreateAsync(Transport(ValidToken()));
        var tools = await mcpClient.ListToolsAsync();

        tools.Select(t => t.Name).Should().BeEquivalentTo(
            CliCommandTool.All(CliToolScope.Remote).Select(t => t.ProtocolTool.Name).Append("project_details"));

        foreach (var tool in tools)
        {
            tool.Title.Should().NotBeNullOrWhiteSpace();
            var annotations = tool.ProtocolTool.Annotations!;
            (annotations.ReadOnlyHint == true || annotations.DestructiveHint.HasValue).Should().BeTrue();
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
            Claims = new Dictionary<string, object> { ["tid"] = "42", ["instance_url"] = "https://api.my.anythink.cloud" },
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
            Claims = new Dictionary<string, object> { ["tid"] = "42", ["instance_url"] = "https://api.my.anythink.cloud" },
            SigningCredentials = new SigningCredentials(hmacKey, SecurityAlgorithms.HmacSha256),
        });

        var response = await _client.SendAsync(McpPost(token));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _exchangeCount.Should().Be(0);
    }

    [Theory]
    [InlineData("https://evilanythink.cloud")]
    [InlineData("https://api.my.evilanythink.cloud")]
    [InlineData("https://attacker.example.com")]
    [InlineData("http://api.my.anythink.cloud")]
    public async Task InstanceUrlOutsideAllowlist_Returns401_AndNothingIsExchanged(string instanceUrl)
    {
        var response = await _client.SendAsync(McpPost(ValidToken(instanceUrl: instanceUrl)));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _exchangeCount.Should().Be(0);
    }

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
        var tokenA = ValidToken(tid: "1", instanceUrl: "https://api.one.anythink.cloud");
        var tokenB = ValidToken(tid: "2", instanceUrl: "https://api.two.anythink.cloud");
        MockProject("1", "Project One", ExchangedFor(tokenA), "api.one.anythink.cloud");
        MockProject("2", "Project Two", ExchangedFor(tokenB), "api.two.anythink.cloud");

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

    [Fact]
    public async Task ConcurrentRequestsBeyondTheProjectLimit_AreRejected_WithoutAffectingOtherProjects()
    {
        await RestartAppAsync(maxConcurrentRequestsPerProject: 1);
        var token = ValidToken();
        var otherToken = ValidToken(tid: "7", instanceUrl: "https://api.other.anythink.cloud");
        MockProject("7", "Other", ExchangedFor(otherToken), "api.other.anythink.cloud");
        var upstreamEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseUpstream = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _mock.When(HttpMethod.Get, UpstreamOrgUrl("42")).Respond(async _ =>
        {
            upstreamEntered.TrySetResult();
            await releaseUpstream.Task;
            return new HttpResponseMessage { Content = new StringContent("""{"id":42,"name":"P"}""") };
        });
        await using var holding = await McpClient.CreateAsync(Transport(token));
        await using var secondCaller = await McpClient.CreateAsync(Transport(token));
        await using var thirdCaller = await McpClient.CreateAsync(Transport(token));
        await using var otherProject = await McpClient.CreateAsync(Transport(otherToken));

        var holdingCall = holding.CallToolAsync("project_details").AsTask();
        await upstreamEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var waiting = new[] { secondCaller.CallToolAsync("project_details").AsTask(), thirdCaller.CallToolAsync("project_details").AsTask() };
        var refusedCall = await Task.WhenAny(waiting).WaitAsync(TimeSpan.FromSeconds(10));
        var otherCall = await otherProject.CallToolAsync("project_details");
        releaseUpstream.SetResult();

        refusedCall.Exception!.InnerException.Should().BeOfType<HttpRequestException>()
            .Which.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        ((TextContentBlock)otherCall.Content[0]).Text.Should().Contain("Other");
        await holdingCall;
        await waiting.Single(call => call != refusedCall);
    }

    [Theory]
    [InlineData("POST", "/mcp/other")]
    [InlineData("PUT", "/mcp")]
    public async Task AuthenticatedRequestToAnUnmappedRoute_NeverTriggersAnExchange(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", ValidToken()) }
        };

        var response = await _client.SendAsync(request);

        response.IsSuccessStatusCode.Should().BeFalse();
        _exchangeCount.Should().Be(0);
    }

    [Fact]
    public async Task DataList_ThroughTheHostedPipeline_ReturnsTheProjectsRecords()
    {
        var token = ValidToken();
        _mock.When(HttpMethod.Get, $"{UpstreamOrgUrl("42")}/entities/blog_posts/items*")
            .WithHeaders("Authorization", $"Bearer {ExchangedFor(token)}")
            .Respond("application/json", """{"items":[{"id":1,"title":"Hello"}],"total_items":1,"total_pages":1,"has_next_page":false,"page":1,"page_size":20}""");

        await using var mcpClient = await McpClient.CreateAsync(Transport(token));
        var result = await mcpClient.CallToolAsync("data_list", new Dictionary<string, object?> { ["entity"] = "blog_posts" });

        ((TextContentBlock)result.Content[0]).Text.Should().Contain("Hello");
    }
}
