using AnythinkCli.Client;
using AnythinkCli.Config;
using System.Net.Http.Headers;

namespace AnythinkMcp;

public class McpClientFactory
{
    private readonly string? _profileName;
    private readonly HttpMessageHandler? _httpHandler;

    private static readonly AsyncLocal<(string OrgId, string BaseUrl, string Token)?> _requestCredentials = new();

    private static readonly SocketsHttpHandler UpstreamHandler = new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    };

    private static readonly TimeSpan UpstreamTimeout = TimeSpan.FromSeconds(30);

    public string? ProfileName => _profileName;

    public McpClientFactory(string? profileName = null)
    {
        _profileName = profileName;
    }

    public static void SetRequestCredentials(string orgId, string baseUrl, string token)
    {
        _requestCredentials.Value = (orgId, baseUrl, token);
    }

    public static void ClearRequestCredentials()
    {
        _requestCredentials.Value = null;
    }

    /// <summary>Test-only constructor — injects a mock HTTP handler for all clients.</summary>
    internal McpClientFactory(string? profileName, HttpMessageHandler httpHandler)
    {
        _profileName = profileName;
        _httpHandler = httpHandler;
    }

    public BillingClient GetBillingClient()
    {
        var platform = ConfigService.ResolvePlatform();
        if (platform.IsTokenExpired)
            throw new InvalidOperationException(
                "Platform session expired. Run 'anythink login' to sign in again.");
        return CreateBillingClient(platform);
    }

    public BillingClient GetUnauthenticatedBillingClient()
    {
        var platform = ConfigService.ResolvePlatform();
        return CreateBillingClient(platform);
    }

    public AnythinkClient GetClient(HostedCredentials credentials)
    {
        if (string.IsNullOrEmpty(credentials.OrgId) || string.IsNullOrEmpty(credentials.InstanceUrl)
            || string.IsNullOrEmpty(credentials.Token))
            throw new InvalidOperationException("Hosted request has no resolved Anythink credentials.");

        return CreateRequestClient(credentials.OrgId, credentials.InstanceUrl, credentials.Token);
    }

    private AnythinkClient CreateRequestClient(string orgId, string baseUrl, string token)
    {
        var handler = _httpHandler ?? UpstreamHandler;
        var http = new HttpClient(handler, disposeHandler: false) { Timeout = UpstreamTimeout };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var anonymous = new HttpClient(handler, disposeHandler: false) { Timeout = UpstreamTimeout };
        return new AnythinkClient(orgId, baseUrl, http, anonymous);
    }

    public AnythinkClient GetRequestClient()
    {
        var creds = _requestCredentials.Value
            ?? throw new InvalidOperationException("The request carries no credentials.");
        return CreateRequestClient(creds.OrgId, creds.BaseUrl, creds.Token);
    }

    public AnythinkClient GetClient()
    {
        // HTTP mode: use per-request credentials (no config files)
        if (_requestCredentials.Value.HasValue)
            return GetRequestClient();

        // Stdio mode: resolve from CLI config
        var profile = !string.IsNullOrEmpty(_profileName)
            ? ConfigService.GetProfile(_profileName)
              ?? throw new InvalidOperationException(
                  $"Profile '{_profileName}' not found. Run 'anythink config show' to list saved profiles.")
            : ConfigService.GetActiveProfile()
              ?? throw new InvalidOperationException(
                  "No credentials. Run 'anythink login' first.");

        // API-key profiles never expire.
        if (!string.IsNullOrEmpty(profile.ApiKey))
            return CreateAnythinkClient(profile);

        // Refresh expired JWTs silently.
        if (profile.IsTokenExpired)
        {
            if (string.IsNullOrEmpty(profile.RefreshToken))
                throw new InvalidOperationException(
                    "Session expired. Run 'anythink login' to sign in again.");

            var refreshed = TryRefresh(profile);
            if (refreshed is null)
                throw new InvalidOperationException(
                    "Token refresh failed. Run 'anythink login' to sign in again.");

            profile = refreshed;
        }

        return CreateAnythinkClient(profile);
    }

    public AnythinkClient? GetClientOrNull()
    {
        try
        {
            return GetClient();
        }
        catch (InvalidOperationException) when (string.IsNullOrEmpty(_profileName))
        {
            return null;
        }
    }

    private BillingClient CreateBillingClient(PlatformConfig platform)
        => _httpHandler is not null
            ? new BillingClient(platform, new HttpClient(_httpHandler))
            : new BillingClient(platform);

    public AnythinkClient CreateAnythinkClient(Profile profile)
        => _httpHandler is not null
            ? new AnythinkClient(profile.OrgId, profile.InstanceApiUrl, new HttpClient(_httpHandler))
            : new AnythinkClient(profile);

    private Profile? TryRefresh(Profile profile)
    {
        try
        {
            var response = AnythinkClient
                .RefreshTokenAsync(profile.InstanceApiUrl, profile.OrgId, profile.RefreshToken!)
                .GetAwaiter().GetResult();

            if (response is null) return null;

            profile.AccessToken = response.AccessToken;
            profile.RefreshToken = response.RefreshToken ?? profile.RefreshToken;
            profile.TokenExpiresAt = response.ExpiresIn.HasValue
                ? DateTime.UtcNow.AddSeconds(response.ExpiresIn.Value)
                : DateTime.UtcNow.AddHours(1);

            // Persist so the next invocation doesn't need to refresh again.
            var config = ConfigService.Load();
            var key = _profileName ?? config.DefaultProfile;
            config.Profiles[key] = profile;
            ConfigService.Save(config);

            return profile;
        }
        catch
        {
            return null;
        }
    }
}
