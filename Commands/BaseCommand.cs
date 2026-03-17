using AnythinkCli.Client;
using AnythinkCli.Config;
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

        if (orgId != null && (apiKey != null || token != null))
            return new AnythinkClient(orgId, url ?? ApiDefaults.ProdApi, token, apiKey);

        return new AnythinkClient(
            ConfigService.GetActiveProfile()
            ?? throw new CliException(
                "No credentials. Run [bold #F97316]anythink login[/] or set " +
                "[#F97316]ANYTHINK_ORG_ID[/] + [#F97316]ANYTHINK_API_KEY[/].")
        );
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
