using AnythinkCli.Client;
using AnythinkCli.Config;
using CliProfile = AnythinkCli.Config.Profile;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AnythinkCli.Commands;

/// <summary>
/// Base for all project-level commands.
/// </summary>
public abstract class BaseCommand<TSettings> : AsyncCommand<TSettings>
    where TSettings : CommandSettings
{
    protected AnythinkClient GetClient() => GetClient(refreshHttp: null);

    /// <summary>
    /// Full credential-resolution logic.
    /// <paramref name="refreshHttp"/> is null in production (a real HttpClient is created inside
    /// RefreshTokenAsync) and injected in unit tests so the refresh call can be mocked.
    /// </summary>
    internal AnythinkClient GetClient(HttpClient? refreshHttp)
    {
        var profile = ConfigService.GetActiveProfile()
            ?? throw new CliException(
                "No credentials. Run [bold #F97316]anythink login[/]");

        // API-key profiles never expire — use directly.
        if (!string.IsNullOrEmpty(profile.ApiKey))
            return new AnythinkClient(profile);

        // JWT expired — attempt a silent token refresh before giving up.
        if (profile.IsTokenExpired)
        {
            if (string.IsNullOrEmpty(profile.RefreshToken))
                throw new CliException("Session expired. Run [bold #F97316]anythink login[/] to sign in again.");

            var refreshed = TryRefreshSync(profile, refreshHttp);
            if (refreshed is null)
                throw new CliException("Session expired and refresh failed. Run [bold #F97316]anythink login[/] to sign in again.");

            profile = refreshed;
        }

        return new AnythinkClient(profile);
    }

    /// <summary>
    /// Calls the refresh endpoint synchronously (safe in a CLI — no synchronisation context).
    /// On success, persists the new tokens to disk and returns the updated Profile.
    /// Returns null when the server rejects the refresh token or on any failure.
    /// <paramref name="profileKey"/> is the config dictionary key to save the refreshed tokens under;
    /// defaults to <c>config.DefaultProfile</c> when null (the normal single-profile case).
    /// The optional <paramref name="http"/> parameter is for unit testing only.
    /// </summary>
    internal static CliProfile? TryRefreshSync(CliProfile profile, HttpClient? http = null,
        string? profileKey = null)
    {
        try
        {
            var response = (http is null
                ? AnythinkClient.RefreshTokenAsync(profile.InstanceApiUrl, profile.OrgId, profile.RefreshToken!)
                : AnythinkClient.RefreshTokenAsync(profile.InstanceApiUrl, profile.OrgId, profile.RefreshToken!, http))
                .GetAwaiter().GetResult();

            if (response is null) return null;

            profile.AccessToken    = response.AccessToken;
            profile.RefreshToken   = response.RefreshToken ?? profile.RefreshToken;
            profile.TokenExpiresAt = response.ExpiresIn.HasValue
                ? DateTime.UtcNow.AddSeconds(response.ExpiresIn.Value)
                : DateTime.UtcNow.AddHours(1);

            // Persist so the next command doesn't need to refresh again.
            // Use the supplied key (named profile) or fall back to the default slot.
            var config = ConfigService.Load();
            var key = profileKey ?? config.DefaultProfile;
            config.Profiles[key] = profile;
            ConfigService.Save(config);

            return profile;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves a named profile by key, refreshing its token if expired.
    /// Use this instead of <see cref="GetClient()"/> when a specific profile key is needed
    /// (e.g. the migrate command's --from / --to profiles).
    /// </summary>
    internal AnythinkClient GetClientForProfile(string profileKey, HttpClient? refreshHttp = null)
    {
        var profile = ConfigService.GetProfile(profileKey)
            ?? throw new CliException($"Profile '[bold #F97316]{Markup.Escape(profileKey)}[/]' not found. " +
                                      "Run [bold #F97316]anythink config show[/] to list saved profiles.");

        if (!string.IsNullOrEmpty(profile.ApiKey))
            return new AnythinkClient(profile);

        if (profile.IsTokenExpired)
        {
            if (string.IsNullOrEmpty(profile.RefreshToken))
                throw new CliException(
                    $"Session expired for profile '[bold #F97316]{Markup.Escape(profileKey)}[/]'. " +
                    "Run [bold #F97316]anythink login[/] and [bold #F97316]anythink projects use[/] to sign in again.");

            var refreshed = TryRefreshSync(profile, refreshHttp, profileKey: profileKey);
            if (refreshed is null)
                throw new CliException(
                    $"Token refresh failed for profile '[bold #F97316]{Markup.Escape(profileKey)}[/]'. " +
                    "Run [bold #F97316]anythink login[/] and [bold #F97316]anythink projects use[/] to sign in again.");

            profile = refreshed;
        }

        return new AnythinkClient(profile);
    }
    
    protected static string? Env(string name) => Environment.GetEnvironmentVariable(name);

    /// <summary>
    /// Unified error display. CliException carries markup and is printed as-is.
    /// AnythinkException shows status code. Everything else shows the message.
    /// </summary>
    protected static void HandleError(Exception ex)
    {
        switch (ex)
        {
            case AnythinkException ae:
                Renderer.Error($"API error ({ae.StatusCode}): {ae.Message}");
                break;
            case CliException:
                AnsiConsole.MarkupLine($"[red]✗[/] {ex.Message}");
                break;
            default:
                Renderer.Error(ex.Message);
                break;
        }
    }
}
