using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RichardSzalay.MockHttp;

namespace AnythinkMcp.Tests;

/// <summary>
/// Exercises hosted mode's OAuth handshake end to end against an in-memory TestServer: a real RSA
/// key, a mocked issuer (no network), and the real ASP.NET Core JwtBearer + MCP auth pipeline.
/// </summary>
public class HostedModeTests : IAsyncLifetime
{
    private const string Issuer = "https://issuer.test";
    private const string PublicUrl = "https://mcp.test/mcp";

    private RSA _rsa = null!;
    private RsaSecurityKey _key = null!;
    private MockHttpMessageHandler _mock = null!;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _rsa = RSA.Create(2048);
        _key = new RsaSecurityKey(_rsa) { KeyId = "test-key-1" };
        _mock = new MockHttpMessageHandler();
        HostedTestSupport.ConfigureIssuer(_mock, Issuer, _key);

        var options = new HostedMode.Options { PublicUrl = PublicUrl, Issuer = Issuer, Audience = PublicUrl };
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        _app = HostedMode.BuildPublicApp(builder, options, new McpClientFactory(null, _mock),
            jwt => jwt.BackchannelHttpHandler = _mock);

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

    // ── Rule: no token → 401 with the exact WWW-Authenticate shape ───────────

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

    // ── Rule: protected resource metadata is served at both paths, resource fixed, issuer first ──

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

    // ── Rule: wrong issuer, audience, signature, or expiry all give 401 ──────

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

    // ── Rule: credentials come from the token only ───────────────────────────

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

        _mock.When(HttpMethod.Get, $"{instanceUrl}/org/{tid}")
            .Respond("application/json", """{"id":42,"name":"Real Project","description":"from the token"}""");

        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(PublicUrl),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {token}",
                // A stray connector or a compromised client might still send these — hosted mode must ignore them.
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

    // ── Rule: hosted mode's tools/list is the small read-only proving set only ──

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
}
