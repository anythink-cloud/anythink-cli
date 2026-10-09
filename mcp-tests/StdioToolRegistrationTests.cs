using System.Reflection;
using AnythinkMcp.Cli;
using FluentAssertions;
using ModelContextProtocol.Server;

namespace AnythinkMcp.Tests;

public class StdioToolRegistrationTests
{
    private static readonly string[] HandWrittenTools =
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

    internal static IEnumerable<string> ExpectedStdioTools =>
        HandWrittenTools.Concat(CliCommandTool.All(CliToolScope.Local).Select(t => t.ProtocolTool.Name));

    [Fact]
    public void HandWrittenTools_AreUnchanged()
    {
        var names = typeof(McpClientFactory).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
            .SelectMany(t => t.GetMethods())
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .OfType<string>();

        names.Should().BeEquivalentTo(HandWrittenTools);
    }

    [Fact]
    public void GeneratedTools_DoNotCollideWithHandWrittenOnes() =>
        CliCommandTool.All(CliToolScope.Local).Select(t => t.ProtocolTool.Name).Should().NotIntersectWith(HandWrittenTools);
}
