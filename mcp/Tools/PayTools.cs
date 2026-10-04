using System.ComponentModel;
using System.Text.Json;
using AnythinkCli.Commands;
using AnythinkCli.Models;
using ModelContextProtocol.Server;

namespace AnythinkMcp.Tools;

[McpServerToolType]
public class PayTools
{
    private readonly McpClientFactory _factory;
    public PayTools(McpClientFactory factory) => _factory = factory;

    private static string Json(object? value) =>
        JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });

    private static string? OfferStatusPreview(string? status, string offerId, bool confirm) =>
        status is "paused" or "expired" && !confirm
            ? $"Would set offer {offerId} to {status}, so its codes stop being redeemable. Re-run with confirm: true to proceed."
            : null;

    private static bool TryId(string value, string label, out Guid id, out string error)
    {
        error = Guid.TryParse(value, out id) ? "" : $"Invalid {label} '{value}': expected a guid such as 3f2504e0-4f89-41d3-9a0c-0305e82c3301.";
        return error.Length == 0;
    }

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
        var request = PayTrialToggle.BuildRequest(await client.GetTenantAsync(), enable);
        await client.UpdateTenantAsync(request);
        return Json(new { app_engagement_trial_enabled = enable });
    }

    [McpServerTool(Name = "anythinkpay_get_engagement_trial_status", ReadOnly = true),
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

    // ── Apple IAP status ──────────────────────────────────────────────────────

    [McpServerTool(Name = "anythinkpay_get_apple_credentials_status", ReadOnly = true),
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

    // ── Subscription history ──────────────────────────────────────────────────

    [McpServerTool(Name = "anythinkpay_get_subscription_events", ReadOnly = true),
     Description("Get the lifecycle history of a subscription (newest first)")]
    public async Task<string> GetSubscriptionEvents(
        [Description("Subscription id (guid)")] string subscriptionId)
    {
        if (!TryId(subscriptionId, "subscription id", out var id, out var error)) return error;
        return Json(await _factory.GetClient().GetSubscriptionEventsAsync(id));
    }

    // ── Admin recovery (project administrators) ────────────────────────────────

    [McpServerTool(Name = "anythinkpay_admin_delete_subscription", Destructive = true),
     Description("Hard-delete a subscription (project administrator). Irreversible — requires confirm: true.")]
    public async Task<string> AdminDeleteSubscription(
        [Description("Subscription id (guid)")] string subscriptionId,
        [Description("Must be true to actually delete. Omit or pass false to preview what this would do.")] bool confirm = false)
    {
        if (!TryId(subscriptionId, "subscription id", out var id, out var error)) return error;
        if (!confirm)
            return $"Would permanently hard-delete subscription {subscriptionId}. This cannot be undone. " +
                   "Re-run with confirm: true to proceed.";

        await _factory.GetClient().AdminDeleteSubscriptionAsync(id);
        return $"Subscription {subscriptionId} deleted.";
    }

    [McpServerTool(Name = "anythinkpay_admin_force_expire_subscription", Destructive = true),
     Description("Force-expire a subscription immediately (project administrator). Changes billing state — requires confirm: true.")]
    public async Task<string> AdminForceExpireSubscription(
        [Description("Subscription id (guid)")] string subscriptionId,
        [Description("Must be true to actually force-expire. Omit or pass false to preview what this would do.")] bool confirm = false)
    {
        if (!TryId(subscriptionId, "subscription id", out var id, out var error)) return error;
        if (!confirm)
            return $"Would force-expire subscription {subscriptionId} immediately, ending its access now. " +
                   "Re-run with confirm: true to proceed.";

        await _factory.GetClient().AdminForceExpireSubscriptionAsync(id);
        return $"Subscription {subscriptionId} force-expired.";
    }

    [McpServerTool(Name = "anythinkpay_admin_relink_subscription", Destructive = true),
     Description("Move a subscription to a different user (project administrator). Changes billing state — requires confirm: true.")]
    public async Task<string> AdminRelinkSubscription(
        [Description("Subscription id (guid)")] string subscriptionId,
        [Description("User id to move the subscription to")] int toUserId,
        [Description("Must be true to actually relink. Omit or pass false to preview what this would do.")] bool confirm = false)
    {
        if (!TryId(subscriptionId, "subscription id", out var id, out var error)) return error;
        if (!confirm)
            return $"Would relink subscription {subscriptionId} to user {toUserId}. " +
                   "Re-run with confirm: true to proceed.";

        await _factory.GetClient().AdminRelinkSubscriptionAsync(id, toUserId);
        return $"Subscription {subscriptionId} relinked to user {toUserId}.";
    }

    [McpServerTool(Name = "anythinkpay_admin_resync_subscription", Destructive = true),
     Description("Re-sync a subscription from the provider and overwrite its stored state (project administrator). Requires confirm: true.")]
    public async Task<string> AdminResyncSubscription(
        [Description("Subscription id (guid)")] string subscriptionId,
        [Description("Must be true to actually re-sync. Omit or pass false to preview what this would do.")] bool confirm = false)
    {
        if (!TryId(subscriptionId, "subscription id", out var id, out var error)) return error;
        if (!confirm)
            return $"Would re-sync subscription {subscriptionId} from the payment provider, overwriting its stored state. " +
                   "Re-run with confirm: true to proceed.";

        return Json(await _factory.GetClient().AdminResyncSubscriptionAsync(id));
    }

    // ── Read-only lookups ─────────────────────────────────────────────────────

    [McpServerTool(Name = "anythinkpay_get_entitlement", ReadOnly = true),
     Description("Show the current user's access / trial entitlement")]
    public async Task<string> GetEntitlement()
        => Json(await _factory.GetClient().GetEntitlementAsync());

    [McpServerTool(Name = "anythinkpay_get_payment_options", ReadOnly = true),
     Description("Show available payment providers for a platform/storefront")]
    public async Task<string> GetPaymentOptions(
        [Description("Client platform: ios, android, or web")] string platform,
        [Description("Storefront code (ISO 3166-1 alpha-3, e.g. GBR) — optional")] string? storefront = null)
        => Json(await _factory.GetClient().GetPaymentOptionsAsync(platform, storefront));

    [McpServerTool(Name = "anythinkpay_list_subscription_plans", ReadOnly = true),
     Description("List subscription plans")]
    public async Task<string> ListSubscriptionPlans()
        => Json(await _factory.GetClient().GetSubscriptionPlansAsync());

    [McpServerTool(Name = "anythinkpay_get_subscription_plan", ReadOnly = true),
     Description("Show one subscription plan by id")]
    public async Task<string> GetSubscriptionPlan(
        [Description("Plan id (integer)")] int id)
        => Json(await _factory.GetClient().GetSubscriptionPlanAsync(id));

    [McpServerTool(Name = "anythinkpay_list_subscriptions", ReadOnly = true),
     Description("List subscriptions (paginated, optionally filtered by status)")]
    public async Task<string> ListSubscriptions(
        [Description("Page number (default 1)")] int page = 1,
        [Description("Items per page (default 25)")] int pageSize = 25,
        [Description("Filter by status (e.g. active, trialing, cancelled) — optional")] string? status = null)
    {
        var client = _factory.GetClient();
        var result = status is null
            ? await client.GetSubscriptionsAsync(page, pageSize)
            : await client.GetSubscriptionsByStatusAsync(status, page, pageSize);
        return Json(result);
    }

    [McpServerTool(Name = "anythinkpay_get_subscription", ReadOnly = true),
     Description("Show one subscription by id (guid)")]
    public async Task<string> GetSubscription(
        [Description("Subscription id (guid)")] string subscriptionId)
    {
        if (!TryId(subscriptionId, "subscription id", out var id, out var error)) return error;
        return Json(await _factory.GetClient().GetSubscriptionAsync(id));
    }

    // ── Offers (admin) ────────────────────────────────────────────────────────

    [McpServerTool(Name = "anythinkpay_list_offers", ReadOnly = true),
     Description("List offers and their primary promo/referral code")]
    public async Task<string> ListOffers()
        => Json(await _factory.GetClient().GetOffersAsync());

    [McpServerTool(Name = "anythinkpay_get_offer", ReadOnly = true),
     Description("Show one offer by id (guid)")]
    public async Task<string> GetOffer(
        [Description("Offer id (guid)")] string offerId)
    {
        if (!TryId(offerId, "offer id", out var id, out var error)) return error;
        return Json(await _factory.GetClient().GetOfferAsync(id));
    }

    [McpServerTool(Name = "anythinkpay_create_offer"),
     Description("Create an offer. Rewards and eligibility are JSON strings in the offer reward vocabulary, e.g. redeemerRewardJson {\"type\":\"trial_extension\",\"days\":14}.")]
    public async Task<string> CreateOffer(
        [Description("Offer display name")] string name,
        [Description("Offer kind: discount, trial_extension, or referral")] string kind,
        [Description("Redeemer reward as a JSON string (required)")] string redeemerRewardJson,
        [Description("Description (optional)")] string? description = null,
        [Description("Referrer reward as a JSON string, referral offers only (optional)")] string? referrerRewardJson = null,
        [Description("Eligibility rules as a JSON string (optional)")] string? eligibilityJson = null,
        [Description("Total redemption cap (optional, unlimited if omitted)")] int? totalRedemptionCap = null,
        [Description("Per-user redemption cap (default 1)")] int perUserRedemptionCap = 1,
        [Description("Initial status: active, paused, or expired (default active)")] string status = "active")
    {
        var offer = await _factory.GetClient().CreateOfferAsync(new CreateOfferRequest(
            Name: name, Kind: kind, RedeemerRewardJson: redeemerRewardJson, Description: description,
            ReferrerRewardJson: referrerRewardJson, EligibilityJson: eligibilityJson,
            TotalRedemptionCap: totalRedemptionCap, PerUserRedemptionCap: perUserRedemptionCap, Status: status));
        return Json(offer);
    }

    [McpServerTool(Name = "anythinkpay_update_offer"),
     Description("Update an offer (patch — only supplied fields change). kind is immutable.")]
    public async Task<string> UpdateOffer(
        [Description("Offer id (guid)")] string offerId,
        [Description("Name (optional)")] string? name = null,
        [Description("Description (optional)")] string? description = null,
        [Description("Redeemer reward as a JSON string (optional)")] string? redeemerRewardJson = null,
        [Description("Referrer reward as a JSON string (optional)")] string? referrerRewardJson = null,
        [Description("Eligibility rules as a JSON string (optional)")] string? eligibilityJson = null,
        [Description("Status: active, paused, or expired (optional). paused and expired require confirm: true.")] string? status = null,
        [Description("Must be true to pause or expire the offer. Omit or pass false to preview.")] bool confirm = false)
    {
        if (!TryId(offerId, "offer id", out var id, out var error)) return error;
        if (OfferStatusPreview(status, offerId, confirm) is { } preview) return preview;
        var offer = await _factory.GetClient().UpdateOfferAsync(id, new UpdateOfferRequest(
            Name: name, Description: description, RedeemerRewardJson: redeemerRewardJson,
            ReferrerRewardJson: referrerRewardJson, EligibilityJson: eligibilityJson, Status: status));
        return Json(offer);
    }

    [McpServerTool(Name = "anythinkpay_set_offer_status"),
     Description("Set an offer's status (active, paused, or expired)")]
    public async Task<string> SetOfferStatus(
        [Description("Offer id (guid)")] string offerId,
        [Description("Status: active, paused, or expired. paused and expired require confirm: true.")] string status,
        [Description("Must be true to pause or expire the offer. Omit or pass false to preview.")] bool confirm = false)
    {
        if (!TryId(offerId, "offer id", out var id, out var error)) return error;
        if (OfferStatusPreview(status, offerId, confirm) is { } preview) return preview;
        return Json(await _factory.GetClient().SetOfferStatusAsync(id, status));
    }

    [McpServerTool(Name = "anythinkpay_list_offer_codes", ReadOnly = true),
     Description("List the promo/referral codes attached to an offer")]
    public async Task<string> ListOfferCodes(
        [Description("Offer id (guid)")] string offerId)
    {
        if (!TryId(offerId, "offer id", out var id, out var error)) return error;
        return Json(await _factory.GetClient().GetOfferCodesAsync(id));
    }

    [McpServerTool(Name = "anythinkpay_create_offer_code"),
     Description("Add a promo/referral code to an offer. Omit ownerUserId for a shared promo code.")]
    public async Task<string> CreateOfferCode(
        [Description("Offer id (guid)")] string offerId,
        [Description("Code slug (e.g. LAUNCH50)")] string slug,
        [Description("Owner user id for a personal/referral code (optional)")] int? ownerUserId = null)
    {
        if (!TryId(offerId, "offer id", out var id, out var error)) return error;
        return Json(await _factory.GetClient().CreateOfferCodeAsync(id, new CreateOfferCodeRequest(slug, ownerUserId)));
    }

    [McpServerTool(Name = "anythinkpay_get_offer_redemptions", ReadOnly = true),
     Description("List redemptions for one offer")]
    public async Task<string> GetOfferRedemptions(
        [Description("Offer id (guid)")] string offerId,
        [Description("Page number (default 1)")] int page = 1,
        [Description("Items per page (default 50)")] int pageSize = 50)
    {
        if (!TryId(offerId, "offer id", out var id, out var error)) return error;
        return Json(await _factory.GetClient().GetOfferRedemptionsAsync(id, page, pageSize));
    }

    [McpServerTool(Name = "anythinkpay_get_user_code", ReadOnly = true),
     Description("Look up a user's personal referral code (admin)")]
    public async Task<string> GetUserCode(
        [Description("Numeric user id")] int userId)
        => Json(await _factory.GetClient().GetUserCodeAsync(userId));
}
