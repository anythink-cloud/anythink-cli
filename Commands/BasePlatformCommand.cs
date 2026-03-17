using AnythinkCli.Client;
using AnythinkCli.Config;
using Spectre.Console.Cli;

namespace AnythinkCli.Commands;

/// <summary>
/// Base for billing/platform commands. Extends BaseCommand so both GetClient()
/// and GetBillingClient() are available, with a single shared HandleError.
///
/// Additional env vars:
///   ANYTHINK_PLATFORM_ORG_ID   — central platform org ID
///   ANYTHINK_PLATFORM_TOKEN    — JWT from platform login
///   ANYTHINK_PLATFORM_API_URL  — WebAPI base URL (overrides ANYTHINK_ENV for platform)
///   ANYTHINK_BILLING_URL       — BillingAPI base URL
///   ANYTHINK_ACCOUNT_ID        — active billing account ID
///   ANYTHINK_ENV               — preset: local | dev | prod
/// </summary>
public abstract class BasePlatformCommand<TSettings> : BaseCommand<TSettings>
    where TSettings : CommandSettings
{
    protected BillingClient GetBillingClient()
    {
        var platform = ResolvePlatform();
        if (string.IsNullOrEmpty(platform.Token))
            throw new CliException(
                "Not logged in. Run [bold #F97316]anythink login[/] first.");
        if (platform.IsTokenExpired)
            throw new CliException(
                "Session expired. Run [bold #F97316]anythink login[/] to refresh.");
        return new BillingClient(platform);
    }

    protected BillingClient GetUnauthenticatedBillingClient() => new(ResolvePlatform());

    protected PlatformConfig ResolvePlatform()
    {
        var orgId      = Env("ANYTHINK_PLATFORM_ORG_ID");
        var token      = Env("ANYTHINK_PLATFORM_TOKEN");
        var apiUrl     = Env("ANYTHINK_PLATFORM_API_URL");
        var billingUrl = Env("ANYTHINK_BILLING_URL");
        var envName    = Env("ANYTHINK_ENV");

        // Env-var-only path (CI / automation)
        if (orgId != null)
        {
            var (defApi, defBilling, _) = envName != null
                ? ApiDefaults.ForEnv(envName)
                : (ApiDefaults.ProdApi, ApiDefaults.BillingCloud, null);
            return new PlatformConfig
            {
                OrgId      = orgId,
                Token      = token,
                ApiUrl     = apiUrl     ?? defApi,
                BillingUrl = billingUrl ?? defBilling,
                AccountId  = Env("ANYTHINK_ACCOUNT_ID")
            };
        }

        // Fall back to env-specific saved config, then optionally override URLs from ANYTHINK_ENV
        var saved = ConfigService.GetPlatform(envName) ?? new PlatformConfig();
        if (envName != null)
        {
            var (envApi, envBilling, envOrgId) = ApiDefaults.ForEnv(envName);
            saved.ApiUrl     = apiUrl     ?? envApi;
            saved.BillingUrl = billingUrl ?? envBilling;
            // Always apply env's platform org — each env has its own platform tenant
            if (envOrgId != null)
                saved.OrgId = envOrgId;
        }
        return saved;
    }

    /// <summary>Saves platform config to the correct env slot (dev/local/prod).</summary>
    protected void SavePlatform(PlatformConfig platform)
        => ConfigService.SavePlatform(platform, Env("ANYTHINK_ENV"));

    protected Guid GetAccountId(string? flagValue = null)
    {
        var raw = flagValue
               ?? Env("ANYTHINK_ACCOUNT_ID")
               ?? ConfigService.GetPlatform()?.AccountId;

        if (!string.IsNullOrEmpty(raw) && Guid.TryParse(raw, out var id))
            return id;

        throw new CliException(
            "No billing account selected. Run [bold #F97316]anythink accounts use <id>[/] " +
            "or set [#F97316]ANYTHINK_ACCOUNT_ID[/].");
    }
}
