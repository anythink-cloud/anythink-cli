using System.Net;
using System.Text.Json.Nodes;
using AnythinkCli.Client;
using AnythinkCli.Commands;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkCli.Tests;

/// <summary>
/// Tests for `settings get/set` and `settings cors list/add/remove`.
/// The update endpoint replaces the whole settings object, so these also check that
/// fields the CLI doesn't model survive a round trip instead of being reset.
/// </summary>
public class SettingsTests
{
    private const string BaseUrl = "https://api.example.com";
    private const string OrgId   = "99999";
    private const string OrgPath = $"{BaseUrl}/org/{OrgId}";

    private const string TenantJson =
        """
        {
          "id": 99999,
          "name": "Demo",
          "description": "A demo project",
          "google_maps_key": null,
          "require_email_confirmation": true,
          "logo_square": null,
          "logo_standard": null,
          "tenant_settings": {
            "allow_registrations": false,
            "default_role_id": 3,
            "allowed_application_urls": ["https://app.example.com", "*.example.org"],
            "payment_success_url": null,
            "payment_cancel_url": null,
            "app_engagement_trial_enabled": false,
            "app_engagement_trial_days": 7,
            "enable_group_rls": true,
            "ai_mode": "platform",
            "ai_byok_provider": null,
            "ai_default_model": null,
            "some_future_setting": "keep-me"
          },
          "theme_settings": {
            "primary_color": "#F97316",
            "gray_color": "slate",
            "radius": "0.5rem",
            "email_wrapper_html": "<div>{{content}}</div>"
          }
        }
        """;

    private sealed class Captured
    {
        public JsonNode? PutBody;
        public int PutCount;
        public int ClearCacheCount;
    }

    private static (AnythinkClient Client, Captured Captured) BuildClient(string tenantJson = TenantJson)
    {
        var captured = new Captured();
        var handler = new MockHttpMessageHandler();

        handler.When(HttpMethod.Get, OrgPath).Respond("application/json", tenantJson);
        handler.When(HttpMethod.Put, OrgPath).Respond(async req =>
        {
            captured.PutCount++;
            captured.PutBody = JsonNode.Parse(await req.Content!.ReadAsStringAsync());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(tenantJson, System.Text.Encoding.UTF8, "application/json"),
            };
        });
        handler.When(HttpMethod.Post, $"{OrgPath}/cors/clear-cache").Respond(_ =>
        {
            captured.ClearCacheCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"message":"ok"}""", System.Text.Encoding.UTF8, "application/json"),
            };
        });

        var client = new AnythinkClient(OrgId, BaseUrl, new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) });
        return (client, captured);
    }

    private static List<string> SentUrls(Captured c) =>
        c.PutBody!["tenant_settings"]!["allowed_application_urls"]!.AsArray()
            .Select(n => n!.GetValue<string>()).ToList();

    // ── settings cors list ───────────────────────────────────────────────────

    [Fact]
    public async Task CorsList_ReadsAllowedApplicationUrls()
    {
        var (client, _) = BuildClient();

        var tenant = await client.GetTenantAsync();

        tenant!.TenantSettings!.AllowedApplicationUrls
            .Should().Equal("https://app.example.com", "*.example.org");
    }

    // ── settings cors add ────────────────────────────────────────────────────

    [Fact]
    public async Task CorsAdd_AppendsUrl_AndClearsCorsCache()
    {
        var (client, captured) = BuildClient();

        var changed = await SettingsHelpers.AddCorsUrlAsync(client, "http://localhost:3999");

        changed.Should().BeTrue();
        captured.PutCount.Should().Be(1);
        SentUrls(captured).Should().Equal("https://app.example.com", "*.example.org", "http://localhost:3999");
        captured.ClearCacheCount.Should().Be(1);
    }

    [Fact]
    public async Task CorsAdd_ExistingUrl_IsCaseInsensitiveNoOp()
    {
        var (client, captured) = BuildClient();

        var changed = await SettingsHelpers.AddCorsUrlAsync(client, "HTTPS://APP.EXAMPLE.COM");

        changed.Should().BeFalse();
        captured.PutCount.Should().Be(0);
        captured.ClearCacheCount.Should().Be(0);
    }

    [Fact]
    public async Task CorsAdd_NoExistingSettings_StartsFromEmptyList()
    {
        var json = JsonNode.Parse(TenantJson)!;
        json["tenant_settings"] = null;
        var (client, captured) = BuildClient(json.ToJsonString());

        await SettingsHelpers.AddCorsUrlAsync(client, "https://new.example.com");

        SentUrls(captured).Should().Equal("https://new.example.com");
        captured.ClearCacheCount.Should().Be(1);
    }

    [Fact]
    public async Task CorsAdd_PreservesOtherSettings_IncludingUnmodelledFields()
    {
        var (client, captured) = BuildClient();

        await SettingsHelpers.AddCorsUrlAsync(client, "http://localhost:3999");

        var body = captured.PutBody!;
        body["name"]!.GetValue<string>().Should().Be("Demo");
        body["description"]!.GetValue<string>().Should().Be("A demo project");
        body["require_email_confirmation"]!.GetValue<bool>().Should().BeTrue();

        var ts = body["tenant_settings"]!;
        ts["default_role_id"]!.GetValue<int>().Should().Be(3);
        ts["enable_group_rls"]!.GetValue<bool>().Should().BeTrue();
        ts["ai_mode"]!.GetValue<string>().Should().Be("platform");
        ts["some_future_setting"]!.GetValue<string>().Should().Be("keep-me");

        var theme = body["theme_settings"]!;
        theme["primary_color"]!.GetValue<string>().Should().Be("#F97316");
        theme["radius"]!.GetValue<string>().Should().Be("0.5rem");
        theme["email_wrapper_html"]!.GetValue<string>().Should().Be("<div>{{content}}</div>");
    }

    // ── settings cors remove ─────────────────────────────────────────────────

    [Fact]
    public async Task CorsRemove_DropsUrl_AndClearsCorsCache()
    {
        var (client, captured) = BuildClient();

        var changed = await SettingsHelpers.RemoveCorsUrlAsync(client, "https://app.example.com");

        changed.Should().BeTrue();
        captured.PutCount.Should().Be(1);
        SentUrls(captured).Should().Equal("*.example.org");
        captured.ClearCacheCount.Should().Be(1);
    }

    [Fact]
    public async Task CorsRemove_MissingUrl_IsNoOp()
    {
        var (client, captured) = BuildClient();

        var changed = await SettingsHelpers.RemoveCorsUrlAsync(client, "https://not-there.example.com");

        changed.Should().BeFalse();
        captured.PutCount.Should().Be(0);
        captured.ClearCacheCount.Should().Be(0);
    }

    // ── settings get ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_ParsesProjectAndTenantSettings()
    {
        var (client, _) = BuildClient();

        var tenant = (await client.GetTenantAsync())!;

        tenant.Name.Should().Be("Demo");
        tenant.RequireEmailConfirmation.Should().BeTrue();
        tenant.TenantSettings!.DefaultRoleId.Should().Be(3);
        tenant.TenantSettings.EnableGroupRls.Should().BeTrue();
        tenant.TenantSettings.AppEngagementTrialDays.Should().Be(7);
        tenant.TenantSettings.AiMode.Should().Be("platform");
    }

    // ── settings set ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Set_TenantSettingKey_UpdatesOnlyThatField()
    {
        var (client, captured) = BuildClient();

        var known = await SettingsHelpers.SetAsync(client, "allow_registrations", "true");

        known.Should().BeTrue();
        var ts = captured.PutBody!["tenant_settings"]!;
        ts["allow_registrations"]!.GetValue<bool>().Should().BeTrue();
        ts["default_role_id"]!.GetValue<int>().Should().Be(3);
        SentUrls(captured).Should().Equal("https://app.example.com", "*.example.org");
        captured.ClearCacheCount.Should().Be(0);
    }

    [Fact]
    public async Task Set_TopLevelKey_IsCaseInsensitive()
    {
        var (client, captured) = BuildClient();

        await SettingsHelpers.SetAsync(client, "REQUIRE_EMAIL_CONFIRMATION", "off");

        captured.PutBody!["require_email_confirmation"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task Set_IntegerKey_ParsesValue()
    {
        var (client, captured) = BuildClient();

        await SettingsHelpers.SetAsync(client, "app_engagement_trial_days", "14");

        captured.PutBody!["tenant_settings"]!["app_engagement_trial_days"]!.GetValue<int>().Should().Be(14);
    }

    [Fact]
    public async Task Set_UnknownKey_ReturnsFalse_AndSavesNothing()
    {
        var (client, captured) = BuildClient();

        var known = await SettingsHelpers.SetAsync(client, "allowed_application_urls", "x");

        known.Should().BeFalse();
        captured.PutCount.Should().Be(0);
    }

    [Theory]
    [InlineData("allow_registrations", "maybe")]
    [InlineData("default_role_id", "abc")]
    public async Task Set_InvalidValue_Throws_AndSavesNothing(string key, string value)
    {
        var (client, captured) = BuildClient();

        var act = () => SettingsHelpers.SetAsync(client, key, value);

        await act.Should().ThrowAsync<CliException>();
        captured.PutCount.Should().Be(0);
    }
}
