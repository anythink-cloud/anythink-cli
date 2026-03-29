using AnythinkCli.Client;
using AnythinkCli.Config;

namespace AnythinkMcp;

/// <summary>
/// Resolves credentials into an authenticated <see cref="AnythinkClient"/>.
///
/// In stdio mode: uses CLI config files and saved profiles (same as the CLI).
/// In HTTP mode: uses per-request credentials passed via <see cref="SetRequestCredentials"/>.
/// </summary>
public class McpClientFactory
{
    private readonly string? _profileName;
    private readonly HttpMessageHandler? _httpHandler;

    // Per-request credentials for HTTP mode — AsyncLocal flows correctly across async/await
    private static readonly AsyncLocal<(string OrgId, string BaseUrl, string Token)?> _requestCredentials = new();

    public string? ProfileName => _profileName;

    public McpClientFactory(string? profileName = null)
    {
        _profileName = profileName;
    }

    /// <summary>
    /// Sets per-request credentials for HTTP mode. Must be called before tool execution.
    /// Thread-static so concurrent requests don't interfere.
    /// </summary>
    public static void SetRequestCredentials(string orgId, string baseUrl, string token)
    {
        _requestCredentials.Value = (orgId, baseUrl, token);
    }

    /// <summary>Clears per-request credentials after the request completes.</summary>
    public static void ClearRequestCredentials()
    {
        _requestCredentials.Value = null;
    }

    /// <summary>Returns true if running in HTTP mode with per-request credentials.</summary>
    public static bool IsHttpMode => _requestCredentials.Value.HasValue;

    /// <summary>Test-only constructor — injects a mock HTTP handler for all clients.</summary>
    internal McpClientFactory(string? profileName, HttpMessageHandler httpHandler)
    {
        _profileName = profileName;
        _httpHandler = httpHandler;
    }

    /// <summary>
    /// Returns an authenticated BillingClient using the saved platform config.
    /// </summary>
    public BillingClient GetBillingClient()
    {
        var platform = ConfigService.ResolvePlatform();
        if (platform.IsTokenExpired)
            throw new InvalidOperationException(
                "Platform session expired. Run 'anythink login' to sign in again.");
        return CreateBillingClient(platform);
    }

    /// <summary>
    /// Returns a BillingClient that does not require an auth token (for signup/login).
    /// </summary>
    public BillingClient GetUnauthenticatedBillingClient()
    {
        var platform = ConfigService.ResolvePlatform();
        return CreateBillingClient(platform);
    }

    /// <summary>
    /// Returns an authenticated client. In HTTP mode, uses per-request credentials.
    /// In stdio mode, uses CLI config files and refreshes expired tokens.
    /// </summary>
    public AnythinkClient GetClient()
    {
        // HTTP mode: use per-request credentials (no config files)
        if (_requestCredentials.Value.HasValue)
        {
            var creds = _requestCredentials.Value.Value;
            return new AnythinkClient(creds.OrgId, creds.BaseUrl, creds.Token);
        }

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

    private BillingClient CreateBillingClient(PlatformConfig platform)
        => _httpHandler is not null
            ? new BillingClient(platform, new HttpClient(_httpHandler))
            : new BillingClient(platform);

    /// <summary>
    /// Creates an AnythinkClient for a given profile. Uses mock HTTP handler in tests.
    /// </summary>
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

            profile.AccessToken    = response.AccessToken;
            profile.RefreshToken   = response.RefreshToken ?? profile.RefreshToken;
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
