using AnythinkCli.Client;
using AnythinkCli.Commands;
using AnythinkCli.Config;
using FluentAssertions;
using RichardSzalay.MockHttp;
using System.Net;
using System.Text.Json;

namespace AnythinkCli.Tests;

// ── AnythinkClient.RefreshTokenAsync ─────────────────────────────────────────

public class RefreshTokenAsyncTests
{
    private const string BaseUrl = "https://api.example.com";
    private const string OrgId   = "42";
    private const string RefTok  = "refresh-abc-123";

    private static string RefreshUrl => $"{BaseUrl}/org/{OrgId}/auth/v1/refresh";

    private static string ValidResponse(string access = "new-access", string? refresh = "new-refresh", int? expiresIn = 3600) =>
        JsonSerializer.Serialize(new
        {
            access_token  = access,
            refresh_token = refresh,
            expires_in    = expiresIn
        });

    [Fact]
    public async Task Returns_LoginResponse_On_200()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, RefreshUrl).Respond("application/json", ValidResponse());

        var result = await AnythinkClient.RefreshTokenAsync(BaseUrl, OrgId, RefTok, mock.ToHttpClient());

        result.Should().NotBeNull();
        result!.AccessToken.Should().Be("new-access");
        result.RefreshToken.Should().Be("new-refresh");
        result.ExpiresIn.Should().Be(3600);
    }

    [Fact]
    public async Task Returns_Null_On_401()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, RefreshUrl).Respond(HttpStatusCode.Unauthorized);

        var result = await AnythinkClient.RefreshTokenAsync(BaseUrl, OrgId, RefTok, mock.ToHttpClient());

        result.Should().BeNull();
    }

    [Fact]
    public async Task Returns_Null_On_500()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, RefreshUrl).Respond(HttpStatusCode.InternalServerError);

        var result = await AnythinkClient.RefreshTokenAsync(BaseUrl, OrgId, RefTok, mock.ToHttpClient());

        result.Should().BeNull();
    }

    [Fact]
    public async Task Returns_Null_On_Empty_Body()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, RefreshUrl).Respond("application/json", "");

        var result = await AnythinkClient.RefreshTokenAsync(BaseUrl, OrgId, RefTok, mock.ToHttpClient());

        result.Should().BeNull();
    }

    [Fact]
    public async Task Returns_Null_On_Malformed_Json()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, RefreshUrl).Respond("application/json", "not-json{{{");

        var result = await AnythinkClient.RefreshTokenAsync(BaseUrl, OrgId, RefTok, mock.ToHttpClient());

        result.Should().BeNull();
    }

    [Fact]
    public async Task Posts_To_Correct_Url()
    {
        var mock    = new MockHttpMessageHandler();
        var handler = mock.When(HttpMethod.Post, RefreshUrl).Respond("application/json", ValidResponse());

        await AnythinkClient.RefreshTokenAsync(BaseUrl, OrgId, RefTok, mock.ToHttpClient());

        mock.GetMatchCount(handler).Should().Be(1);
    }

    [Fact]
    public async Task Posts_RefreshToken_In_Body()
    {
        string? capturedBody = null;
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, RefreshUrl)
            .With(req =>
            {
                capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return true;
            })
            .Respond("application/json", ValidResponse());

        await AnythinkClient.RefreshTokenAsync(BaseUrl, OrgId, RefTok, mock.ToHttpClient());

        capturedBody.Should().Contain(RefTok);
        capturedBody.Should().Contain("\"token\"");
    }

    [Fact]
    public async Task Handles_Null_Refresh_Token_In_Response()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, RefreshUrl)
            .Respond("application/json", ValidResponse(refresh: null));

        var result = await AnythinkClient.RefreshTokenAsync(BaseUrl, OrgId, RefTok, mock.ToHttpClient());

        result.Should().NotBeNull();
        result!.AccessToken.Should().Be("new-access");
        result.RefreshToken.Should().BeNull();
    }

    [Fact]
    public async Task Handles_Null_ExpiresIn_In_Response()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, RefreshUrl)
            .Respond("application/json", ValidResponse(expiresIn: null));

        var result = await AnythinkClient.RefreshTokenAsync(BaseUrl, OrgId, RefTok, mock.ToHttpClient());

        result.Should().NotBeNull();
        result!.ExpiresIn.Should().BeNull();
    }

    [Fact]
    public async Task Trims_Trailing_Slash_From_BaseUrl()
    {
        var mock    = new MockHttpMessageHandler();
        var handler = mock.When(HttpMethod.Post, RefreshUrl).Respond("application/json", ValidResponse());

        await AnythinkClient.RefreshTokenAsync(BaseUrl + "/", OrgId, RefTok, mock.ToHttpClient());

        mock.GetMatchCount(handler).Should().Be(1);
    }
}

// ── BaseCommand.TryRefreshSync ────────────────────────────────────────────────

[Collection("SequentialConfig")]
public class TryRefreshSyncTests : IDisposable
{
    private readonly string _tempDir;

    public TryRefreshSyncTests()
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

    private static string RefreshUrl(string baseUrl, string orgId) =>
        $"{baseUrl}/org/{orgId}/auth/v1/refresh";

    private static Profile ExpiredProfile(string baseUrl = "https://api.example.com", string orgId = "42") =>
        new()
        {
            OrgId          = orgId,
            BaseUrl        = baseUrl,
            AccessToken    = "old-access",
            RefreshToken   = "old-refresh",
            TokenExpiresAt = DateTime.UtcNow.AddHours(-1)   // already expired
        };

    private static string TokenResponse(string access = "fresh-access", string? refresh = "fresh-refresh", int? expiresIn = 7200) =>
        JsonSerializer.Serialize(new
        {
            access_token  = access,
            refresh_token = refresh,
            expires_in    = expiresIn
        });

    private void SaveProfile(Profile profile, string key = "default")
    {
        ConfigService.SaveProfile(key, profile);
        ConfigService.SetDefault(key);
    }

    // ── Happy-path ────────────────────────────────────────────────────────────

    [Fact]
    public void Returns_Updated_Profile_On_Success()
    {
        var profile = ExpiredProfile();
        SaveProfile(profile);

        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, RefreshUrl(profile.BaseUrl, profile.OrgId))
            .Respond("application/json", TokenResponse());

        var result = BaseCommand<Spectre.Console.Cli.EmptyCommandSettings>
            .TryRefreshSync(profile, mock.ToHttpClient());

        result.Should().NotBeNull();
        result!.AccessToken.Should().Be("fresh-access");
    }

    [Fact]
    public void Updates_RefreshToken_When_Server_Returns_New_One()
    {
        var profile = ExpiredProfile();
        SaveProfile(profile);

        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, RefreshUrl(profile.BaseUrl, profile.OrgId))
            .Respond("application/json", TokenResponse(refresh: "rotated-refresh"));

        var result = BaseCommand<Spectre.Console.Cli.EmptyCommandSettings>
            .TryRefreshSync(profile, mock.ToHttpClient());

        result!.RefreshToken.Should().Be("rotated-refresh");
    }

    [Fact]
    public void Keeps_Existing_RefreshToken_When_Server_Returns_Null()
    {
        var profile = ExpiredProfile();
        SaveProfile(profile);

        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, RefreshUrl(profile.BaseUrl, profile.OrgId))
            .Respond("application/json", TokenResponse(refresh: null));

        var result = BaseCommand<Spectre.Console.Cli.EmptyCommandSettings>
            .TryRefreshSync(profile, mock.ToHttpClient());

        result!.RefreshToken.Should().Be("old-refresh");
    }

    [Fact]
    public void Sets_TokenExpiresAt_From_ExpiresIn()
    {
        var profile = ExpiredProfile();
        SaveProfile(profile);
        var before = DateTime.UtcNow;

        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, RefreshUrl(profile.BaseUrl, profile.OrgId))
            .Respond("application/json", TokenResponse(expiresIn: 3600));

        var result = BaseCommand<Spectre.Console.Cli.EmptyCommandSettings>
            .TryRefreshSync(profile, mock.ToHttpClient());

        result!.TokenExpiresAt.Should().BeCloseTo(before.AddSeconds(3600), precision: TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Defaults_TokenExpiresAt_To_One_Hour_When_ExpiresIn_Is_Null()
    {
        var profile = ExpiredProfile();
        SaveProfile(profile);
        var before = DateTime.UtcNow;

        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, RefreshUrl(profile.BaseUrl, profile.OrgId))
            .Respond("application/json", TokenResponse(expiresIn: null));

        var result = BaseCommand<Spectre.Console.Cli.EmptyCommandSettings>
            .TryRefreshSync(profile, mock.ToHttpClient());

        result!.TokenExpiresAt.Should().BeCloseTo(before.AddHours(1), precision: TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Persists_New_Tokens_To_Config()
    {
        var profile = ExpiredProfile();
        SaveProfile(profile);

        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, RefreshUrl(profile.BaseUrl, profile.OrgId))
            .Respond("application/json", TokenResponse());

        BaseCommand<Spectre.Console.Cli.EmptyCommandSettings>
            .TryRefreshSync(profile, mock.ToHttpClient());

        // Reload from disk and verify the new token was persisted.
        var persisted = ConfigService.GetActiveProfile();
        persisted.Should().NotBeNull();
        persisted!.AccessToken.Should().Be("fresh-access");
    }

    // ── Failure paths ─────────────────────────────────────────────────────────

    [Fact]
    public void Returns_Null_When_Server_Returns_401()
    {
        var profile = ExpiredProfile();
        SaveProfile(profile);

        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, RefreshUrl(profile.BaseUrl, profile.OrgId))
            .Respond(HttpStatusCode.Unauthorized);

        var result = BaseCommand<Spectre.Console.Cli.EmptyCommandSettings>
            .TryRefreshSync(profile, mock.ToHttpClient());

        result.Should().BeNull();
    }

    [Fact]
    public void Returns_Null_On_Network_Failure()
    {
        var profile = ExpiredProfile();
        SaveProfile(profile);

        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, RefreshUrl(profile.BaseUrl, profile.OrgId))
            .Throw(new HttpRequestException("Network unreachable"));

        var result = BaseCommand<Spectre.Console.Cli.EmptyCommandSettings>
            .TryRefreshSync(profile, mock.ToHttpClient());

        result.Should().BeNull();
    }

    [Fact]
    public void Does_Not_Throw_On_Any_Failure()
    {
        var profile = ExpiredProfile();
        SaveProfile(profile);

        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, RefreshUrl(profile.BaseUrl, profile.OrgId))
            .Respond(HttpStatusCode.InternalServerError);

        var act = () => BaseCommand<Spectre.Console.Cli.EmptyCommandSettings>
            .TryRefreshSync(profile, mock.ToHttpClient());

        act.Should().NotThrow();
    }
}
