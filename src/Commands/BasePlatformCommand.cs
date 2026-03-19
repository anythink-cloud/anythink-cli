using AnythinkCli.Client;
using AnythinkCli.Config;
using Spectre.Console.Cli;

namespace AnythinkCli.Commands;

/// <summary>
/// Base for billing/platform commands. Extends BaseCommand so both GetClient()
/// and GetBillingClient() are available, with a single shared HandleError.
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
        var orgId = Env("ANYTHINK_PLATFORM_ORG_ID");
        var token = Env("ANYTHINK_PLATFORM_TOKEN");
        var apiUrl = Env("ANYTHINK_PLATFORM_API_URL");
        var billingUrl = Env("ANYTHINK_BILLING_URL");
        
        if (orgId != null)
        {
            return new PlatformConfig
            {
                MyAnythinkOrgId = orgId,
                MyAnythinkUrl = apiUrl ?? ApiDefaults.MyAnythinkApiUrl,
                Token = token,
                BillingUrl = billingUrl ?? ApiDefaults.BillingApiUrl,
                AccountId = Env("ANYTHINK_ACCOUNT_ID")
            };
        }

        var saved = ConfigService.GetPlatform() ?? new PlatformConfig();
        return saved;
    }

    protected void SavePlatform(PlatformConfig platform)
        => ConfigService.SavePlatform(platform);

    protected Guid GetAccountId(string? flagValue = null)
    {
        var raw = flagValue
                  ?? ConfigService.GetPlatform()?.AccountId;

        if (!string.IsNullOrEmpty(raw) && Guid.TryParse(raw, out var id))
            return id;

        throw new CliException(
            "No billing account selected. Run [bold #F97316]anythink accounts use <id>[/]");
    }
}
