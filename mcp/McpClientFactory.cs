using AnythinkCli.Client;
using AnythinkCli.Config;

namespace AnythinkMcp;

/// <summary>
/// Resolves a profile name (or the active default) into an authenticated
/// <see cref="AnythinkClient"/>. Uses the same config files and token-refresh
/// logic as the CLI — credentials stored by <c>anythink login</c> work here too.
/// </summary>
public class McpClientFactory
{
    private readonly string? _profileName;
    private readonly HttpMessageHandler? _httpHandler;

    public string? ProfileName => _profileName;

    public McpClientFactory(string? profileName = null)
    {
        _profileName = profileName;
    }

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
    /// Returns an authenticated client for the configured profile.
    /// Refreshes expired JWT tokens automatically (same logic as the CLI).
    /// </summary>
    public AnythinkClient GetClient()
    {
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
