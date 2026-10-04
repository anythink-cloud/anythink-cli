using FluentAssertions;

namespace AnythinkMcp.Tests;

public class StdioToolRegistrationTests
{
    internal static readonly string[] ExpectedStdioTools =
    [
        "signup", "login", "login_google", "login_direct", "logout",
        "config_show", "config_use", "config_remove",
        "accounts_list", "accounts_create", "accounts_use",
        "projects_list", "projects_create", "projects_use", "projects_delete",
        "cli",
        "anythinkpay_enable_engagement_trial", "anythinkpay_disable_engagement_trial", "anythinkpay_get_engagement_trial_status",
        "anythinkpay_get_apple_credentials_status", "anythinkpay_get_subscription_events",
        "anythinkpay_admin_delete_subscription", "anythinkpay_admin_force_expire_subscription",
        "anythinkpay_admin_relink_subscription", "anythinkpay_admin_resync_subscription",
        "anythinkpay_get_entitlement", "anythinkpay_get_payment_options",
        "anythinkpay_list_subscription_plans", "anythinkpay_get_subscription_plan",
        "anythinkpay_list_subscriptions", "anythinkpay_get_subscription",
        "anythinkpay_list_offers", "anythinkpay_get_offer", "anythinkpay_create_offer", "anythinkpay_update_offer",
        "anythinkpay_set_offer_status", "anythinkpay_list_offer_codes", "anythinkpay_create_offer_code",
        "anythinkpay_get_offer_redemptions", "anythinkpay_get_user_code",
    ];

    [Fact]
    public void StdioToolSet_IsUnchanged()
    {
        var names = McpToolRegistry.GetToolDefinitions()
            .Select(t => (string)t.GetType().GetProperty("name")!.GetValue(t)!);

        names.Should().BeEquivalentTo(ExpectedStdioTools);
    }
}
