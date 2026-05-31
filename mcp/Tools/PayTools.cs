using System.ComponentModel;
using System.Text.Json;
using AnythinkCli.Models;
using ModelContextProtocol.Server;

namespace AnythinkMcp.Tools;

/// <summary>
/// MCP tools for AnythinkPay — the app engagement trial, Apple IAP configuration,
/// receipt verification, subscription history, admin recovery, and read-only lookups.
/// Operates on the active project profile (same credentials as the CLI).
/// </summary>
[McpServerToolType]
public class PayTools
{
    private readonly McpClientFactory _factory;
    public PayTools(McpClientFactory factory) => _factory = factory;

    private static string Json(object? value) =>
        JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });

    // ── App engagement trial ──────────────────────────────────────────────────

    [McpServerTool(Name = "anythinkpay_enable_engagement_trial"),
     Description("Enable the app engagement trial (free app access without a subscription)")]
    public Task<string> EnableEngagementTrial() => SetEngagementTrial(true);

    [McpServerTool(Name = "anythinkpay_disable_engagement_trial"),
     Description("Disable the app engagement trial")]
    public Task<string> DisableEngagementTrial() => SetEngagementTrial(false);

    private async Task<string> SetEngagementTrial(bool enable)
    {
        var client = _factory.GetClient();
        var tenant = await client.GetTenantAsync()
            ?? throw new InvalidOperationException("Could not load organisation settings.");

        var updated = tenant.TenantSettings is null
            ? new TenantSettingsDto(false, null, [], null, null, enable)
            : tenant.TenantSettings with { AppEngagementTrialEnabled = enable };

        await client.UpdateTenantAsync(new UpdateTenantRequest(
            Name: tenant.Name,
            Description: tenant.Description,
            GoogleMapsKey: tenant.GoogleMapsKey,
            LogoSquareId: tenant.LogoSquare?.Id,
            LogoStandardId: tenant.LogoStandard?.Id,
            TenantSettings: updated,
            ThemeSettings: tenant.ThemeSettings));

        return Json(new { app_engagement_trial_enabled = enable });
    }

    [McpServerTool(Name = "anythinkpay_get_engagement_trial_status"),
     Description("Show whether the app engagement trial is enabled and its derived length")]
    public async Task<string> GetEngagementTrialStatus()
    {
        var client = _factory.GetClient();
        var tenant = await client.GetTenantAsync();
        var plans  = await client.GetSubscriptionPlansAsync();

        var source = plans
            .Where(p => p.IsActive && p.TrialPeriodDays is > 0)
            .OrderByDescending(p => p.TrialPeriodDays)
            .FirstOrDefault();

        return Json(new
        {
            enabled = tenant?.TenantSettings?.AppEngagementTrialEnabled ?? false,
            trial_days = source?.TrialPeriodDays,
            source_plan = source is null ? null : new { source.Id, source.Name }
        });
    }

    // ── Apple IAP credentials ─────────────────────────────────────────────────

    [McpServerTool(Name = "anythinkpay_set_apple_credentials"),
     Description("Set Apple App Store Connect API credentials (tenant administrator). The private key is stored encrypted.")]
    public async Task<string> SetAppleCredentials(
        [Description("App Store Connect issuer id (UUID)")] string issuerId,
        [Description("App Store Connect key id")] string keyId,
        [Description("App bundle id (e.g. com.example.app)")] string bundleId,
        [Description("The .p8 private key contents (PEM)")] string privateKeyPem,
        [Description("Apple environment: sandbox or production (optional)")] string? environment = null)
    {
        var client = _factory.GetClient();
        var result = await client.UpdateAppleIapCredentialsAsync(new UpdateAppleIapCredentialsRequest(
            BundleId: bundleId,
            Environment: environment,
            AscIssuerId: issuerId,
            AscKeyId: keyId,
            AscPrivateKeyPem: privateKeyPem));

        // Never echo the private key back.
        return Json(new
        {
            result?.BundleId,
            result?.Environment,
            result?.IsConfigured,
            result?.NotificationUrl
        });
    }

    [McpServerTool(Name = "anythinkpay_get_apple_credentials_status"),
     Description("Show Apple IAP configuration status (bundle id, configured?, notification URL)")]
    public async Task<string> GetAppleCredentialsStatus()
    {
        var creds = await _factory.GetClient().GetAppleIapCredentialsAsync();
        if (creds is null) return Json(new { is_configured = false });
        return Json(new
        {
            is_configured = creds.IsConfigured,
            bundle_id = creds.BundleId,
            environment = creds.Environment,
            has_private_key = creds.HasPrivateKey,
            notification_url = creds.NotificationUrl
        });
    }

    // ── Apple receipt verify ──────────────────────────────────────────────────

    [McpServerTool(Name = "anythinkpay_verify_apple_transaction"),
     Description("Verify an Apple transaction (testing) and bind it to the current user. Provide a signed transaction JWS or an original transaction id.")]
    public async Task<string> VerifyAppleTransaction(
        [Description("Signed transaction JWS (StoreKit 2)")] string? signedTransaction = null,
        [Description("Original transaction id (restore flow)")] string? originalTransactionId = null)
    {
        if (string.IsNullOrEmpty(signedTransaction) && string.IsNullOrEmpty(originalTransactionId))
            throw new ArgumentException("Provide signedTransaction or originalTransactionId.");

        var result = await _factory.GetClient().VerifyAppleTransactionAsync(
            new AppleVerifyRequest(signedTransaction, originalTransactionId));
        return Json(result);
    }

    // ── Subscription history ──────────────────────────────────────────────────

    [McpServerTool(Name = "anythinkpay_get_subscription_events"),
     Description("Get the lifecycle history of a subscription (newest first)")]
    public async Task<string> GetSubscriptionEvents(
        [Description("Subscription id (guid)")] string subscriptionId)
    {
        var events = await _factory.GetClient().GetSubscriptionEventsAsync(Guid.Parse(subscriptionId));
        return Json(events);
    }

    // ── Admin recovery (tenant administrators) ────────────────────────────────

    [McpServerTool(Name = "anythinkpay_admin_delete_subscription"),
     Description("Hard-delete a subscription (tenant administrator)")]
    public async Task<string> AdminDeleteSubscription(
        [Description("Subscription id (guid)")] string subscriptionId)
    {
        await _factory.GetClient().AdminDeleteSubscriptionAsync(Guid.Parse(subscriptionId));
        return $"Subscription {subscriptionId} deleted.";
    }

    [McpServerTool(Name = "anythinkpay_admin_force_expire_subscription"),
     Description("Force-expire a subscription immediately (tenant administrator)")]
    public async Task<string> AdminForceExpireSubscription(
        [Description("Subscription id (guid)")] string subscriptionId)
    {
        await _factory.GetClient().AdminForceExpireSubscriptionAsync(Guid.Parse(subscriptionId));
        return $"Subscription {subscriptionId} force-expired.";
    }

    [McpServerTool(Name = "anythinkpay_admin_relink_subscription"),
     Description("Move a subscription to a different user (tenant administrator)")]
    public async Task<string> AdminRelinkSubscription(
        [Description("Subscription id (guid)")] string subscriptionId,
        [Description("User id to move the subscription to")] int toUserId)
    {
        await _factory.GetClient().AdminRelinkSubscriptionAsync(Guid.Parse(subscriptionId), toUserId);
        return $"Subscription {subscriptionId} relinked to user {toUserId}.";
    }

    [McpServerTool(Name = "anythinkpay_admin_resync_subscription"),
     Description("Re-sync a subscription from the provider (tenant administrator)")]
    public async Task<string> AdminResyncSubscription(
        [Description("Subscription id (guid)")] string subscriptionId)
    {
        var result = await _factory.GetClient().AdminResyncSubscriptionAsync(Guid.Parse(subscriptionId));
        return Json(result);
    }

    // ── Read-only lookups ─────────────────────────────────────────────────────

    [McpServerTool(Name = "anythinkpay_get_entitlement"),
     Description("Show the current user's access / trial entitlement")]
    public async Task<string> GetEntitlement()
        => Json(await _factory.GetClient().GetEntitlementAsync());

    [McpServerTool(Name = "anythinkpay_get_payment_options"),
     Description("Show available payment providers for a platform/storefront")]
    public async Task<string> GetPaymentOptions(
        [Description("Client platform: ios, android, or web")] string platform,
        [Description("Storefront code (ISO 3166-1 alpha-3, e.g. GBR) — optional")] string? storefront = null)
        => Json(await _factory.GetClient().GetPaymentOptionsAsync(platform, storefront));

    [McpServerTool(Name = "anythinkpay_list_subscription_plans"),
     Description("List subscription plans")]
    public async Task<string> ListSubscriptionPlans()
        => Json(await _factory.GetClient().GetSubscriptionPlansAsync());

    [McpServerTool(Name = "anythinkpay_get_subscription_plan"),
     Description("Show one subscription plan by id")]
    public async Task<string> GetSubscriptionPlan(
        [Description("Plan id (integer)")] int id)
        => Json(await _factory.GetClient().GetSubscriptionPlanAsync(id));

    [McpServerTool(Name = "anythinkpay_list_subscriptions"),
     Description("List subscriptions (paginated, optionally filtered by status)")]
    public async Task<string> ListSubscriptions(
        [Description("Page number (default 1)")] int page = 1,
        [Description("Items per page (default 25)")] int pageSize = 25,
        [Description("Filter by status (e.g. active, trialing, cancelled) — optional")] string? status = null)
    {
        var client = _factory.GetClient();
        var result = status is null
            ? await client.GetSubscriptionsAsync(page, pageSize)
            : await client.GetSubscriptionsByStatusAsync(status);
        return Json(result);
    }

    [McpServerTool(Name = "anythinkpay_get_subscription"),
     Description("Show one subscription by id (guid)")]
    public async Task<string> GetSubscription(
        [Description("Subscription id (guid)")] string subscriptionId)
        => Json(await _factory.GetClient().GetSubscriptionAsync(Guid.Parse(subscriptionId)));
}
