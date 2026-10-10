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
        if (ClientContext.Billing is { } caller)
            return caller;
        ClientContext.RequireLocal();

        var platform = EffectivePlatform();
        if (string.IsNullOrEmpty(platform.Token))
            throw new CliException(
                "Not logged in. Run [bold #F97316]anythink login[/] first.");
        if (platform.IsTokenExpired)
            throw new CliException(
                "Session expired. Run [bold #F97316]anythink login[/] to refresh.");
        return new BillingClient(platform);
    }

    protected BillingClient GetUnauthenticatedBillingClient()
    {
        if (ClientContext.Billing is { } caller)
            return caller.Unauthenticated();
        ClientContext.RequireLocal();

        return new(EffectivePlatform());
    }

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

    protected async Task<Guid> ResolveAccountIdAsync(string? flagValue)
    {
        if (!ClientContext.Remote)
            return GetAccountId(flagValue);

        if (!string.IsNullOrEmpty(flagValue))
            return Guid.TryParse(flagValue, out var id)
                ? id
                : throw new CliException("'account_id' must be an account id from accounts_list.");

        var accounts = await GetBillingClient().GetAccountsAsync();
        return accounts.Count switch
        {
            1 => accounts[0].Id,
            0 => throw new CliException("You have no billing account yet. Create one with accounts_create."),
            _ => throw new CliException($"You have {accounts.Count} billing accounts. Pass 'account_id', one of the ids from accounts_list.")
        };
    }
}
