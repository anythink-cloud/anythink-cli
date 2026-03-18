using AnythinkCli.Client;
using AnythinkCli.Config;
using CliProfile = AnythinkCli.Config.Profile;
using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AnythinkCli.Commands;

/// <summary>
/// Base for all project-level commands.
///
/// Env vars (override saved profile — useful for CI / AI-driven workflows):
///   ANYTHINK_ORG_ID    — org/tenant ID
///   ANYTHINK_API_KEY   — API key (ak_...)
///   ANYTHINK_TOKEN     — JWT access token
///   ANYTHINK_BASE_URL  — override base URL
///   ANYTHINK_ENV       — preset: local | dev | prod (auto-sets ANYTHINK_BASE_URL)
/// </summary>
public abstract class BaseCommand<TSettings> : AsyncCommand<TSettings>
    where TSettings : CommandSettings
{
    protected AnythinkClient GetClient()
    {
        var orgId  = Env("ANYTHINK_ORG_ID");
        var apiKey = Env("ANYTHINK_API_KEY");
        var token  = Env("ANYTHINK_TOKEN");
        var url    = Env("ANYTHINK_BASE_URL") ?? EnvPresetUrl();

        // Env-var path: no refresh logic needed — caller controls the token lifecycle.
        if (orgId != null && (apiKey != null || token != null))
            return new AnythinkClient(orgId, url ?? ApiDefaults.ProdApi, token, apiKey);

        var profile = ConfigService.GetActiveProfile()
            ?? throw new CliException(
                "No credentials. Run [bold #F97316]anythink login[/] or set " +
                "[#F97316]ANYTHINK_ORG_ID[/] + [#F97316]ANYTHINK_API_KEY[/].");

        // API-key profiles never expire — use directly.
        if (!string.IsNullOrEmpty(profile.ApiKey))
            return new AnythinkClient(profile);

        // JWT expired — attempt a silent token refresh before giving up.
        if (profile.IsTokenExpired)
        {
            if (string.IsNullOrEmpty(profile.RefreshToken))
                throw new CliException("Session expired. Run [bold #F97316]anythink login[/] to sign in again.");

            var refreshed = TryRefreshSync(profile, http: null);
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
    /// The optional <paramref name="http"/> parameter is for unit testing only.
    /// </summary>
    internal static CliProfile? TryRefreshSync(CliProfile profile, HttpClient? http = null)
    {
        try
        {
            var response = (http is null
                ? AnythinkClient.RefreshTokenAsync(profile.BaseUrl, profile.OrgId, profile.RefreshToken!)
                : AnythinkClient.RefreshTokenAsync(profile.BaseUrl, profile.OrgId, profile.RefreshToken!, http))
                .GetAwaiter().GetResult();

            if (response is null) return null;

            profile.AccessToken    = response.AccessToken;
            profile.RefreshToken   = response.RefreshToken ?? profile.RefreshToken;
            profile.TokenExpiresAt = response.ExpiresIn.HasValue
                ? DateTime.UtcNow.AddSeconds(response.ExpiresIn.Value)
                : DateTime.UtcNow.AddHours(1);

            // Persist so the next command doesn't need to refresh again.
            var config = ConfigService.Load();
            config.Profiles[config.DefaultProfile] = profile;
            ConfigService.Save(config);

            return profile;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Returns the API URL implied by ANYTHINK_ENV, if set.</summary>
    private static string? EnvPresetUrl()
    {
        var e = Env("ANYTHINK_ENV");
        return e is null ? null : ApiDefaults.ForEnv(e).ApiUrl;
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
