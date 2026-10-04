using AnythinkMcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using System.Text.Json;
using System.Reflection;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkMcp.Tests;

// Rule: AnythinkPay MCP tools that delete or change billing state need confirm: true, and bad input returns a helpful error.
[Collection("SequentialConfig")]
public class PayToolsTests : McpTestBase
{
    private readonly Guid _subscriptionId = Guid.NewGuid();

    private PayTools BuildTools(MockHttpMessageHandler handler)
    {
        SetupProjectProfile();
        return new PayTools(CreateFactory(handler));
    }

    [Fact]
    public async Task AdminDeleteSubscription_WithoutConfirm_DoesNotCallApi()
    {
        // No .When(...) registered — MockHttpMessageHandler throws if any request is sent.
        var handler = new MockHttpMessageHandler();
        var tools = BuildTools(handler);

        var result = await tools.AdminDeleteSubscription(_subscriptionId.ToString());

        result.Should().ContainEquivalentOf("confirm");
        result.Should().Contain(_subscriptionId.ToString());
    }

    [Fact]
    public async Task AdminDeleteSubscription_WithConfirmFalse_DoesNotCallApi()
    {
        var handler = new MockHttpMessageHandler();
        var tools = BuildTools(handler);

        var result = await tools.AdminDeleteSubscription(_subscriptionId.ToString(), confirm: false);

        result.Should().ContainEquivalentOf("confirm");
    }

    [Fact]
    public async Task AdminDeleteSubscription_WithConfirmTrue_CallsApi()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Delete, $"*/subscriptions/{_subscriptionId}/admin")
               .Respond(System.Net.HttpStatusCode.NoContent);
        var tools = BuildTools(handler);

        var result = await tools.AdminDeleteSubscription(_subscriptionId.ToString(), confirm: true);

        result.Should().Contain("deleted");
    }

    [Fact]
    public async Task AdminForceExpireSubscription_WithoutConfirm_DoesNotCallApi()
    {
        var handler = new MockHttpMessageHandler();
        var tools = BuildTools(handler);

        var result = await tools.AdminForceExpireSubscription(_subscriptionId.ToString());

        result.Should().ContainEquivalentOf("confirm");
    }

    [Fact]
    public async Task AdminForceExpireSubscription_WithConfirmTrue_CallsApi()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"*/subscriptions/{_subscriptionId}/admin/status")
               .Respond("application/json", "{}");
        var tools = BuildTools(handler);

        var result = await tools.AdminForceExpireSubscription(_subscriptionId.ToString(), confirm: true);

        result.Should().Contain("force-expired");
    }

    [Fact]
    public async Task AdminRelinkSubscription_WithoutConfirm_DoesNotCallApi()
    {
        var handler = new MockHttpMessageHandler();
        var tools = BuildTools(handler);

        var result = await tools.AdminRelinkSubscription(_subscriptionId.ToString(), toUserId: 42);

        result.Should().ContainEquivalentOf("confirm");
        result.Should().Contain("42");
    }

    [Fact]
    public async Task AdminRelinkSubscription_WithConfirmTrue_CallsApi()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"*/subscriptions/{_subscriptionId}/admin/relink")
               .Respond("application/json", "{}");
        var tools = BuildTools(handler);

        var result = await tools.AdminRelinkSubscription(_subscriptionId.ToString(), toUserId: 42, confirm: true);

        result.Should().Contain("relinked");
    }

    [Fact]
    public async Task AdminResyncSubscription_WithoutConfirm_DoesNotCallApi()
    {
        var handler = new MockHttpMessageHandler();
        var tools = BuildTools(handler);

        var result = await tools.AdminResyncSubscription(_subscriptionId.ToString());

        result.Should().ContainEquivalentOf("confirm");
    }

    [Fact]
    public async Task AdminResyncSubscription_WithConfirmTrue_CallsApi()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"*/subscriptions/{_subscriptionId}/admin/resync")
               .Respond("application/json", "{}");
        var tools = BuildTools(handler);

        var act = async () => await tools.AdminResyncSubscription(_subscriptionId.ToString(), confirm: true);

        await act.Should().NotThrowAsync();
    }

    // ── Rule: admin tools are destructive-annotated and unavailable over hosted HTTP ──

    private static readonly string[] AdminTools =
    [
        "anythinkpay_admin_delete_subscription", "anythinkpay_admin_force_expire_subscription",
        "anythinkpay_admin_relink_subscription", "anythinkpay_admin_resync_subscription"
    ];

    private static IEnumerable<McpServerToolAttribute> AllPayToolAttributes() =>
        typeof(PayTools).GetMethods()
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>())
            .Where(a => a is not null)!;

    [Fact]
    public void ReadTools_AreMarkedReadOnlyAndAdminToolsAreNot()
    {
        var attrs = AllPayToolAttributes().ToList();

        attrs.Where(a => a.Name!.StartsWith("anythinkpay_get_") || a.Name.StartsWith("anythinkpay_list_"))
             .Should().OnlyContain(a => a.ReadOnly == true);
        attrs.Where(a => AdminTools.Contains(a.Name!)).Should().OnlyContain(a => a.ReadOnly != true);
    }

    [Theory]
    [InlineData("anythinkpay_admin_delete_subscription")]
    [InlineData("anythinkpay_admin_force_expire_subscription")]
    [InlineData("anythinkpay_admin_relink_subscription")]
    [InlineData("anythinkpay_admin_resync_subscription")]
    public async Task AdminTools_AreRefusedOnTheHttpCallPath(string toolName)
    {
        SetupProjectProfile();
        var services = new ServiceCollection()
            .AddSingleton(CreateFactory(new MockHttpMessageHandler()))
            .BuildServiceProvider();
        var args = JsonDocument.Parse($$"""{"subscriptionId":"{{_subscriptionId}}","confirm":true,"toUserId":1}""").RootElement;

        var act = async () => await McpToolRegistry.ExecuteToolAsync(toolName, args, services);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task NonBlockedTools_StillRunOnTheHttpCallPath()
    {
        SetupProjectProfile();
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, "*/subscription-plans").Respond("application/json", "[]");
        var services = new ServiceCollection().AddSingleton(CreateFactory(handler)).BuildServiceProvider();

        var result = await McpToolRegistry.ExecuteToolAsync("anythinkpay_list_subscription_plans",
            JsonDocument.Parse("{}").RootElement, services);

        result.Should().Contain("[]");
    }

    // ── Rule: pausing or expiring an offer needs confirm: true ──

    [Fact]
    public async Task SetOfferStatus_PausedWithoutConfirm_DoesNotCallApi()
    {
        var tools = BuildTools(new MockHttpMessageHandler());

        var result = await tools.SetOfferStatus(Guid.NewGuid().ToString(), "paused");

        result.Should().ContainEquivalentOf("confirm");
    }

    [Fact]
    public async Task UpdateOffer_ExpiredWithoutConfirm_DoesNotCallApi()
    {
        var tools = BuildTools(new MockHttpMessageHandler());

        var result = await tools.UpdateOffer(Guid.NewGuid().ToString(), status: "expired");

        result.Should().ContainEquivalentOf("confirm");
    }

    [Fact]
    public async Task SetOfferStatus_ActiveNeedsNoConfirm()
    {
        var id = Guid.NewGuid();
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Put, $"*/offers/{id}").Respond("application/json", "{}");
        var tools = BuildTools(handler);

        var act = async () => await tools.SetOfferStatus(id.ToString(), "active");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SetOfferStatus_PausedWithConfirm_CallsApi()
    {
        var id = Guid.NewGuid();
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Put, $"*/offers/{id}").Respond("application/json", "{}");
        var tools = BuildTools(handler);

        await tools.SetOfferStatus(id.ToString(), "paused", confirm: true);

        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public void CredentialTools_RemainBlockedInHttpMode()
    {
        McpToolRegistry.BlockedInHttpMode.Should().Contain(["login", "login_direct", "signup", "logout", "config_use", "config_remove", "config_show", "accounts_use"]);
    }

    // ── Rule: tools that bind a transaction to the caller or take the Apple private key are not exposed ──

    [Fact]
    public void AppleVerifyAndSetCredentials_AreNotMcpTools()
    {
        var names = AllPayToolAttributes().Select(a => a.Name).ToList();

        names.Should().NotContain("anythinkpay_verify_apple_transaction");
        names.Should().NotContain("anythinkpay_set_apple_credentials");
    }

    [Fact]
    public void PayTools_NoToolTakesAPrivateKeyParameter()
    {
        typeof(PayTools).GetMethods()
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .SelectMany(m => m.GetParameters())
            .Select(p => p.Name!.ToLowerInvariant())
            .Should().NotContain(n => n.Contains("privatekey") || n.Contains("pem"));
    }

    // ── Rule: a malformed guid yields a helpful message, never an exception ──

    [Fact]
    public async Task BadGuid_ReturnsHelpfulErrorForEverySubscriptionAndOfferTool()
    {
        var tools = BuildTools(new MockHttpMessageHandler());
        const string bad = "not-a-guid";

        var results = new[]
        {
            await tools.GetSubscription(bad),
            await tools.GetSubscriptionEvents(bad),
            await tools.AdminDeleteSubscription(bad, confirm: true),
            await tools.AdminForceExpireSubscription(bad),
            await tools.AdminRelinkSubscription(bad, 1),
            await tools.AdminResyncSubscription(bad),
            await tools.GetOffer(bad),
            await tools.UpdateOffer(bad),
            await tools.SetOfferStatus(bad, "paused"),
            await tools.ListOfferCodes(bad),
            await tools.CreateOfferCode(bad, "X"),
            await tools.GetOfferRedemptions(bad)
        };

        results.Should().OnlyContain(r => r.Contains("Invalid") && r.Contains(bad) && r.Contains("guid"));
    }

    // ── Rule: the trial toggle changes only the trial flag ──

    [Fact]
    public async Task EnableEngagementTrial_PreservesOtherProjectSettings()
    {
        string? putBody = null;
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, "*/org/12345").Respond("application/json",
            """{"id":12345,"name":"Acme","tenant_settings":{"allow_registrations":true,"default_role_id":4,"allowed_application_urls":["https://app.example.com"]}}""");
        handler.When(HttpMethod.Put, "*/org/12345")
               .With(req => { putBody = req.Content!.ReadAsStringAsync().Result; return true; })
               .Respond("application/json", "{}");
        var tools = BuildTools(handler);

        await tools.EnableEngagementTrial();

        putBody.Should().Contain("\"allow_registrations\":true");
        putBody.Should().Contain("\"default_role_id\":4");
        putBody.Should().Contain("https://app.example.com");
        putBody.Should().Contain("\"app_engagement_trial_enabled\":true");
    }

    [Fact]
    public async Task EnableEngagementTrial_PreservesUnmodelledSettingsAndTheme()
    {
        string? putBody = null;
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, "*/org/12345").Respond("application/json",
            """{"id":12345,"name":"Acme","require_email_confirmation":true,"tenant_settings":{"allow_registrations":true,"allowed_application_urls":[],"enable_group_rls":true,"ai_mode":"byok","future_flag":"x"},"theme_settings":{"primary_color":"red","email_wrapper_html":"<b>w</b>"}}""");
        handler.When(HttpMethod.Put, "*/org/12345")
               .With(req => { putBody = req.Content!.ReadAsStringAsync().Result; return true; })
               .Respond("application/json", "{}");
        var tools = BuildTools(handler);

        await tools.EnableEngagementTrial();

        putBody.Should().Contain("\"enable_group_rls\":true").And.Contain("\"ai_mode\":\"byok\"")
               .And.Contain("\"future_flag\":\"x\"").And.Contain("email_wrapper_html")
               .And.Contain("\"require_email_confirmation\":true");
    }

    [Fact]
    public async Task EnableEngagementTrial_WhenSettingsMissing_RefusesAndWritesNothing()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, "*/org/12345").Respond("application/json", """{"id":12345,"name":"Acme"}""");
        var tools = BuildTools(handler);

        var act = async () => await tools.EnableEngagementTrial();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── Rule: the status filter honours paging ──

    [Fact]
    public async Task ListSubscriptions_WithStatus_ForwardsPageAndPageSize()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, "*/subscriptions/status/active?page=2&pageSize=5")
               .Respond("application/json", """{"items":[],"page":2,"page_size":5}""");
        var tools = BuildTools(handler);

        var result = await tools.ListSubscriptions(page: 2, pageSize: 5, status: "active");

        result.Should().Contain("\"page\"");
        handler.VerifyNoOutstandingExpectation();
    }
}
