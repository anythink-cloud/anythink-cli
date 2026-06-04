using System.Net;
using System.Text.Json;
using AnythinkCli.Client;
using AnythinkCli.Models;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkCli.Tests;

/// <summary>
/// URL/body coverage for the expanded AnythinkPay client surface (Apple IAP,
/// entitlement, payment options, verify, subscription history, admin recovery)
/// plus serialization round-trips for the new snake_case shapes.
/// </summary>
public class PayClientTests
{
    private const string BaseUrl = "https://api.example.com";
    private const string OrgId   = "99999";
    private const string PayPath = $"{BaseUrl}/org/{OrgId}/integrations/anythinkpay";

    private static AnythinkClient BuildClient(MockHttpMessageHandler handler)
        => new(OrgId, BaseUrl, new HttpClient(handler));

    [Fact]
    public async Task GetAppleIapCredentialsAsync_TargetsCredentialsUrl()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, $"{PayPath}/apple-iap/credentials")
               .Respond("application/json",
                   """{"bundle_id":"com.example.app","is_configured":true,"has_private_key":true,"notification_url":"https://api.example.com/v1/apple/notifications"}""");

        var creds = await BuildClient(handler).GetAppleIapCredentialsAsync();

        creds!.BundleId.Should().Be("com.example.app");
        creds.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAppleIapCredentialsAsync_PutsPemInBody()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Put, $"{PayPath}/apple-iap/credentials")
               .WithPartialContent("asc_private_key_pem")
               .WithPartialContent("PEMDATA")
               .Respond("application/json", """{"bundle_id":"com.example.app","is_configured":true,"has_private_key":true}""");

        var act = async () => await BuildClient(handler).UpdateAppleIapCredentialsAsync(new UpdateAppleIapCredentialsRequest(
            BundleId: "com.example.app", AscIssuerId: "iss", AscKeyId: "key", AscPrivateKeyPem: "PEMDATA"));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetEntitlementAsync_TargetsMeEntitlement()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, $"{PayPath}/subscriptions/me/entitlement")
               .Respond("application/json", """{"has_access":true,"is_trial":true,"trial_days_remaining":5,"status":"trialing"}""");

        var e = await BuildClient(handler).GetEntitlementAsync();

        e!.HasAccess.Should().BeTrue();
        e.IsTrial.Should().BeTrue();
        e.TrialDaysRemaining.Should().Be(5);
    }

    [Fact]
    public async Task GetPaymentOptionsAsync_PassesPlatformAndStorefront()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, $"{PayPath}/payment-options*")
               .WithQueryString("platform", "ios")
               .WithQueryString("storefront", "GBR")
               .Respond("application/json", """{"primary_provider":"apple","providers":["apple"],"web_checkout_allowed":false,"storefront":"GBR"}""");

        var options = await BuildClient(handler).GetPaymentOptionsAsync("ios", "GBR");

        options["primary_provider"]!.ToString().Should().Be("apple");
    }

    [Fact]
    public async Task VerifyAppleTransactionAsync_PostsSignedTransaction()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"{PayPath}/subscriptions/apple/verify")
               .WithPartialContent("signed_transaction")
               .WithPartialContent("JWS")
               .Respond("application/json", """{"subscription_id":"11111111-1111-1111-1111-111111111111","bound_user_id":7,"has_access":true,"matched_known_plan":true}""");

        var result = await BuildClient(handler).VerifyAppleTransactionAsync(
            new AppleVerifyRequest(SignedTransaction: "JWS"));

        result.BoundUserId.Should().Be(7);
        result.HasAccess.Should().BeTrue();
    }

    [Fact]
    public async Task GetSubscriptionEventsAsync_TargetsEventsUrl()
    {
        var id = Guid.NewGuid();
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, $"{PayPath}/subscriptions/{id}/events")
               .Respond("application/json",
                   """[{"id":"22222222-2222-2222-2222-222222222222","event_type":"created","source":"verify","occurred_at":"2026-05-30T10:00:00Z"}]""");

        var events = await BuildClient(handler).GetSubscriptionEventsAsync(id);

        events.Should().HaveCount(1);
        events[0].EventType.Should().Be("created");
    }

    [Fact]
    public async Task AdminDeleteSubscriptionAsync_IssuesDeleteOnAdmin()
    {
        var id = Guid.NewGuid();
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Delete, $"{PayPath}/subscriptions/{id}/admin")
               .Respond(HttpStatusCode.NoContent);

        var act = async () => await BuildClient(handler).AdminDeleteSubscriptionAsync(id);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ForceExpireAsync_PostsExpiredStatusWithClearPeriod()
    {
        var id = Guid.NewGuid();
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"{PayPath}/subscriptions/{id}/admin/status")
               .WithPartialContent("\"status\":\"expired\"")
               .WithPartialContent("clear_period")
               .Respond(HttpStatusCode.OK, "application/json", "{}");

        var act = async () => await BuildClient(handler).AdminForceExpireSubscriptionAsync(id);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RelinkAsync_PostsUserId()
    {
        var id = Guid.NewGuid();
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"{PayPath}/subscriptions/{id}/admin/relink")
               .WithPartialContent("\"user_id\":99")
               .Respond(HttpStatusCode.OK, "application/json", "{}");

        var act = async () => await BuildClient(handler).AdminRelinkSubscriptionAsync(id, 99);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ResyncAsync_PostsToAdminResync()
    {
        var id = Guid.NewGuid();
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"{PayPath}/subscriptions/{id}/admin/resync")
               .Respond("application/json", "{}");

        var act = async () => await BuildClient(handler).AdminResyncSubscriptionAsync(id);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CreatePlanAsync_SendsPlanNameAndAppleFields_NotTemplateName()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"{PayPath}/subscription-plans")
               .WithPartialContent("\"plan_name\":\"monthly\"")
               .WithPartialContent("apple_product_id")
               .Respond("application/json", """{"id":1,"plan_name":"monthly","name":"Monthly","description":"d","type":"web","amount":9.99,"currency":"gbp","billing_interval":"month","interval_count":1,"is_active":true}""");

        var plan = await BuildClient(handler).CreateSubscriptionPlanAsync(new CreateSubscriptionPlanRequest(
            PlanName: "monthly", Name: "Monthly", Description: "d", Type: "web",
            Amount: 9.99m, Currency: "gbp", BillingInterval: "month", IntervalCount: 1,
            TrialPeriodDays: 7, ProductName: null, ProductDescription: null, Reference: null,
            IsActive: true, AppleProductId: "monthly_002", AppleSubscriptionGroupId: "21445497"));

        plan.PlanName.Should().Be("monthly");
    }

    // ── Serialization round-trips ─────────────────────────────────────────────

    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void SubscriptionPlanResponse_ReadsSnakeCaseAppleFields()
    {
        const string json = """
        {"id":1,"plan_name":"monthly","name":"Monthly","description":"d","type":"web",
         "amount":9.99,"currency":"gbp","billing_interval":"month","interval_count":1,
         "trial_period_days":7,"is_active":true,
         "apple_product_id":"monthly_002","apple_subscription_group_id":"21445497"}
        """;
        var plan = JsonSerializer.Deserialize<SubscriptionPlanResponse>(json, Opts)!;

        plan.PlanName.Should().Be("monthly");
        plan.AppleProductId.Should().Be("monthly_002");
        plan.AppleSubscriptionGroupId.Should().Be("21445497");
    }

    [Fact]
    public void TenantSettings_RoundTripsEngagementTrialFlag()
    {
        var settings = new TenantSettingsDto(true, null, [], null, null, AppEngagementTrialEnabled: true);
        var json = JsonSerializer.Serialize(settings);
        json.Should().Contain("app_engagement_trial_enabled");

        var back = JsonSerializer.Deserialize<TenantSettingsDto>(json, Opts)!;
        back.AppEngagementTrialEnabled.Should().BeTrue();
    }
}
