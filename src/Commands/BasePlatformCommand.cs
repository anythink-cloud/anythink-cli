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
        var platform = EffectivePlatform();
        if (string.IsNullOrEmpty(platform.Token))
            throw new CliException(
                "Not logged in. Run [bold #F97316]anythink login[/] first.");
        if (platform.IsTokenExpired)
            throw new CliException(
                "Session expired. Run [bold #F97316]anythink login[/] to refresh.");
        return new BillingClient(platform);
    }

    protected BillingClient GetUnauthenticatedBillingClient() => new(EffectivePlatform());

    protected PlatformContext ResolvePlatformContext(string? myanythinkUrlFlag = null, string? billingUrlFlag = null)
        => ConfigService.ResolvePlatformContext(myanythinkUrlFlag, billingUrlFlag);

    protected PlatformConfig ResolvePlatform() => ConfigService.ResolvePlatform();

    protected PlatformConfig EffectivePlatform()
        => ConfigService.ApplyRuntimeOverrides(ConfigService.ResolvePlatform());

    protected void SavePlatformAt(string key, PlatformConfig platform)
        => ConfigService.SavePlatformAt(key, platform);

    protected void SaveAndActivatePlatform(string key, PlatformConfig platform)
        => ConfigService.SaveAndActivatePlatform(key, platform);

    protected void SavePlatform(PlatformConfig platform)
        => ConfigService.SavePlatform(platform);

    protected Guid GetAccountId(string? flagValue = null)
    {
        var raw = flagValue ?? EffectivePlatform().AccountId;

        if (!string.IsNullOrEmpty(raw) && Guid.TryParse(raw, out var id))
            return id;

        throw new CliException(
            "No billing account selected. Run [bold #F97316]anythink accounts use <id>[/]");
    }
}
