using AnythinkCli.Client;
using AnythinkCli.Commands;
using AnythinkCli.Config;
using FluentAssertions;
using RichardSzalay.MockHttp;
using Spectre.Console.Cli;
using System.Net;
using System.Text.Json;

namespace AnythinkCli.Tests;

/// <summary>
/// Covers all branches of BaseCommand.GetClient():
///   1. No saved profile            → CliException
///   2. API-key profile             → client built immediately, no expiry check
///   3. Valid (unexpired) JWT       → client built immediately, no refresh
///   4. Expired JWT, no refresh tok → CliException (specific message)
///   5. Expired JWT, refresh fails  → CliException (specific message)
///   6. Expired JWT, refresh ok     → fresh client returned silently
///
/// Uses a thin TestCommand subclass to surface the internal GetClient(HttpClient?) overload.
/// Belongs to SequentialConfig to prevent parallel conflicts on ConfigDirOverride.
/// </summary>
[Collection("SequentialConfig")]
public class GetClientTests : IDisposable
{
    private readonly string _tempDir;

    // Minimal concrete subclass so we can call the internal overload in tests.
    private sealed class TestCommand : BaseCommand<EmptyCommandSettings>
    {
        public override Task<int> ExecuteAsync(CommandContext ctx, EmptyCommandSettings s) => Task.FromResult(0);
        public AnythinkClient CallGetClient(HttpClient? http = null) => GetClient(http);
    }

    public GetClientTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        ConfigService.ConfigDirOverride = _tempDir;
    }

    public void Dispose()
    {
        ConfigService.ConfigDirOverride = null;
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private static string RefreshEndpoint(string baseUrl, string orgId) =>
        $"{baseUrl}/org/{orgId}/auth/v1/refresh";

    private static string OkRefreshBody(string access = "new-access", string refresh = "rotated") =>
        JsonSerializer.Serialize(new { access_token = access, refresh_token = refresh, expires_in = 3600 });

    // ── Branch 1: no profile ───────────────────────────────────────────────────

    [Fact]
    public void No_Profile_Throws_CliException()
    {
        // Config dir is empty — no profile saved.
        var cmd = new TestCommand();
        var act = () => cmd.CallGetClient();

        act.Should().Throw<CliException>()
            .WithMessage("*No credentials*");
    }

    // ── Branch 2: API-key profile ─────────────────────────────────────────────

    [Fact]
    public void ApiKey_Profile_Returns_Client_Without_Expiry_Check()
    {
        var profile = new Profile
        {
            OrgId          = "10",
            BaseUrl        = "https://api.example.com",
            ApiKey         = "ak_test123",
            TokenExpiresAt = DateTime.UtcNow.AddDays(-99)  // would be "expired" if checked
        };
        ConfigService.SaveProfile("prod", profile);
        ConfigService.SetDefault("prod");

        var cmd    = new TestCommand();
        var client = cmd.CallGetClient();   // must NOT throw

        client.Should().NotBeNull();
        client.OrgId.Should().Be("10");
    }

    // ── Branch 3: valid (unexpired) JWT ───────────────────────────────────────

    [Fact]
    public void Valid_JWT_Returns_Client_Without_Refresh()
    {
        var profile = new Profile
        {
            OrgId          = "20",
            BaseUrl        = "https://api.example.com",
            AccessToken    = "valid-jwt",
            RefreshToken   = "refresh-tok",
            TokenExpiresAt = DateTime.UtcNow.AddHours(2)   // not yet expired
        };
        ConfigService.SaveProfile("prod", profile);
        ConfigService.SetDefault("prod");

        // No mock HTTP — a real call would fail; the test confirms no refresh is attempted.
        var cmd    = new TestCommand();
        var client = cmd.CallGetClient();

        client.Should().NotBeNull();
        client.OrgId.Should().Be("20");
    }

    // ── Branch 4: expired JWT, no refresh token ───────────────────────────────

    [Fact]
    public void Expired_Token_No_RefreshToken_Throws_CliException()
    {
        var profile = new Profile
        {
            OrgId          = "30",
            BaseUrl        = "https://api.example.com",
            AccessToken    = "expired-jwt",
            RefreshToken   = null,
            TokenExpiresAt = DateTime.UtcNow.AddHours(-1)
        };
        ConfigService.SaveProfile("prod", profile);
        ConfigService.SetDefault("prod");

        var cmd = new TestCommand();
        var act = () => cmd.CallGetClient();

        act.Should().Throw<CliException>()
            .WithMessage("*Session expired*login*");
    }

    // ── Branch 5: expired JWT, refresh rejected by server ─────────────────────

    [Fact]
    public void Expired_Token_Refresh_Rejected_Throws_CliException()
    {
        var profile = new Profile
        {
            OrgId          = "40",
            BaseUrl        = "https://api.example.com",
            AccessToken    = "expired-jwt",
            RefreshToken   = "stale-refresh",
            TokenExpiresAt = DateTime.UtcNow.AddHours(-1)
        };
        ConfigService.SaveProfile("prod", profile);
        ConfigService.SetDefault("prod");

        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, RefreshEndpoint(profile.BaseUrl, profile.OrgId))
            .Respond(HttpStatusCode.Unauthorized);

        var cmd = new TestCommand();
        var act = () => cmd.CallGetClient(mock.ToHttpClient());

        act.Should().Throw<CliException>()
            .WithMessage("*refresh failed*login*");
    }

    // ── Branch 6: expired JWT, refresh succeeds ───────────────────────────────

    [Fact]
    public void Expired_Token_Refresh_Succeeds_Returns_Client_Silently()
    {
        var profile = new Profile
        {
            OrgId          = "50",
            BaseUrl        = "https://api.example.com",
            AccessToken    = "expired-jwt",
            RefreshToken   = "valid-refresh",
            TokenExpiresAt = DateTime.UtcNow.AddHours(-1)
        };
        ConfigService.SaveProfile("prod", profile);
        ConfigService.SetDefault("prod");

        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, RefreshEndpoint(profile.BaseUrl, profile.OrgId))
            .Respond("application/json", OkRefreshBody());

        var cmd    = new TestCommand();
        var client = cmd.CallGetClient(mock.ToHttpClient());

        client.Should().NotBeNull();
        client.OrgId.Should().Be("50");
    }

    [Fact]
    public void Expired_Token_Refresh_Succeeds_Persists_New_Token()
    {
        var profile = new Profile
        {
            OrgId          = "60",
            BaseUrl        = "https://api.example.com",
            AccessToken    = "expired-jwt",
            RefreshToken   = "valid-refresh",
            TokenExpiresAt = DateTime.UtcNow.AddHours(-1)
        };
        ConfigService.SaveProfile("prod", profile);
        ConfigService.SetDefault("prod");

        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, RefreshEndpoint(profile.BaseUrl, profile.OrgId))
            .Respond("application/json", OkRefreshBody(access: "brand-new-access"));

        new TestCommand().CallGetClient(mock.ToHttpClient());

        var persisted = ConfigService.GetActiveProfile();
        persisted!.AccessToken.Should().Be("brand-new-access");
    }
}
