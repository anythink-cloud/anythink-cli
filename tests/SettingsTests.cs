using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using AnythinkCli.Client;
using AnythinkCli.Commands;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkCli.Tests;

public class SettingsTests
{
    private const string BaseUrl = "https://api.example.com";
    private const string OrgId = "99999";
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
        handler.When(HttpMethod.Get, $"{OrgPath}/roles/3").Respond("application/json", """{"id":3,"name":"Member","is_active":true}""");
        handler.When(HttpMethod.Get, $"{OrgPath}/roles/1").Respond("application/json", """{"id":1,"name":"Administrator","is_active":true}""");
        handler.When(HttpMethod.Get, $"{OrgPath}/roles/2").Respond("application/json", """{"id":2,"name":"Ops","is_active":true,"is_administrator":true}""");
        handler.When(HttpMethod.Get, $"{OrgPath}/roles/4").Respond("application/json", """{"id":4,"name":"Support","is_active":true,"permissions":[{"id":9,"name":"anythink_users:update","entity_id":null,"is_active":true}]}""");
        handler.When(HttpMethod.Get, $"{OrgPath}/roles/5").Respond("application/json", """{"id":5,"name":"Standard User","is_active":true,"permissions":[{"id":10,"name":"blog_posts:create","entity_id":3,"is_active":true},{"id":11,"name":"anythink_files:read","entity_id":null,"is_active":true}]}""");
        handler.When(HttpMethod.Get, $"{OrgPath}/roles/77").Respond("application/json", "null");
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

        changed.Should().Be(SaveResult.Saved);
        captured.PutCount.Should().Be(1);
        SentUrls(captured).Should().Equal("https://app.example.com", "*.example.org", "http://localhost:3999");
        captured.ClearCacheCount.Should().Be(1);
    }

    [Fact]
    public async Task CorsAdd_ExistingUrl_IsCaseInsensitiveNoOp()
    {
        var (client, captured) = BuildClient();

        var changed = await SettingsHelpers.AddCorsUrlAsync(client, "HTTPS://APP.EXAMPLE.COM/");

        changed.Should().Be(SaveResult.Unchanged);
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

        changed.Should().Be(SaveResult.Saved);
        captured.PutCount.Should().Be(1);
        SentUrls(captured).Should().Equal("*.example.org");
        captured.ClearCacheCount.Should().Be(1);
    }

    [Fact]
    public async Task CorsRemove_MissingUrl_IsNoOp()
    {
        var (client, captured) = BuildClient();

        var changed = await SettingsHelpers.RemoveCorsUrlAsync(client, "https://not-there.example.com");

        changed.Should().Be(SaveResult.Unchanged);
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

        var known = await SettingsHelpers.SetAsync(client, "allow_registrations", "true", yes: true);

        known.Should().Be(SaveResult.Saved);
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

        await SettingsHelpers.SetAsync(client, "REQUIRE_EMAIL_CONFIRMATION", "off", yes: true);

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

        known.Should().Be(SaveResult.UnknownKey);
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

    // ── risky settings need confirmation ─────────────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task SetDefaultRole_AdminRole_RefusedWithoutYes(int roleId)
    {
        var (client, captured) = BuildClient();

        var act = () => SettingsHelpers.SetAsync(client, "default_role_id", roleId.ToString(), confirm: _ => true);

        await act.Should().ThrowAsync<CliException>().WithMessage("*--yes*");
        captured.PutCount.Should().Be(0);
    }

    [Fact]
    public async Task SetDefaultRole_AdminRole_AllowedWithYes()
    {
        var (client, captured) = BuildClient();

        var result = await SettingsHelpers.SetAsync(client, "default_role_id", "1", yes: true);

        result.Should().Be(SaveResult.Saved);
        captured.PutBody!["tenant_settings"]!["default_role_id"]!.GetValue<int>().Should().Be(1);
    }

    [Fact]
    public async Task SetDefaultRole_UnknownRole_RefusedEvenWithYes()
    {
        var (client, captured) = BuildClient();

        var act = () => SettingsHelpers.SetAsync(client, "default_role_id", "77", yes: true);

        await act.Should().ThrowAsync<CliException>().WithMessage("*doesn't exist*");
        captured.PutCount.Should().Be(0);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public async Task SetDefaultRole_OrdinaryRole_SavesWithoutConfirmation(int roleId)
    {
        var (client, captured) = BuildClient();

        var result = await SettingsHelpers.SetAsync(client, "default_role_id", roleId.ToString());

        result.Should().Be(SaveResult.Saved);
        captured.PutCount.Should().Be(1);
    }

    [Theory]
    [InlineData("allow_registrations", "true")]
    [InlineData("require_email_confirmation", "false")]
    public async Task SetRiskyKey_NonInteractiveWithoutYes_Refuses(string key, string value)
    {
        var (client, captured) = BuildClient();

        var act = () => SettingsHelpers.SetAsync(client, key, value);

        await act.Should().ThrowAsync<CliException>().WithMessage("*--yes*");
        captured.PutCount.Should().Be(0);
    }

    [Theory]
    [InlineData("allow_registrations", "true")]
    [InlineData("require_email_confirmation", "false")]
    public async Task SetRiskyKey_InteractiveDecline_SavesNothing(string key, string value)
    {
        var (client, captured) = BuildClient();

        var result = await SettingsHelpers.SetAsync(client, key, value, confirm: _ => false);

        result.Should().Be(SaveResult.Cancelled);
        captured.PutCount.Should().Be(0);
    }

    [Fact]
    public async Task SetRiskyKey_InteractiveAccept_Saves()
    {
        var (client, captured) = BuildClient();

        var result = await SettingsHelpers.SetAsync(client, "allow_registrations", "true", confirm: _ => true);

        result.Should().Be(SaveResult.Saved);
        captured.PutCount.Should().Be(1);
    }

    [Theory]
    [InlineData("allow_registrations", "false")]
    [InlineData("require_email_confirmation", "true")]
    public async Task SetSafeDirection_NeedsNoConfirmation(string key, string value)
    {
        var (client, captured) = BuildClient();

        var result = await SettingsHelpers.SetAsync(client, key, value);

        result.Should().Be(SaveResult.Saved);
        captured.PutCount.Should().Be(1);
    }

    // ── CORS origin validation ───────────────────────────────────────────────

    [Theory]
    [InlineData("https://App.Example.com/", "https://app.example.com")]
    [InlineData("http://localhost:3000", "http://localhost:3000")]
    [InlineData("https://*.example.com", "https://*.example.com")]
    [InlineData("*.example.com", "*.example.com")]
    public void CorsOrigin_ValidInput_IsNormalised(string input, string expected) =>
        CorsOrigin.Parse(input).Value.Should().Be(expected);

    [Theory]
    [InlineData("*")]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://files.example.com")]
    [InlineData("app.example.com")]
    [InlineData("https://")]
    [InlineData("https://app.example.com/path")]
    [InlineData("https://app.example.com//")]
    [InlineData("https://app.example.com?x=1")]
    [InlineData("https://app.example.com#frag")]
    [InlineData("https://user@app.example.com")]
    [InlineData("https://app.example.com:99999")]
    [InlineData("https://app.*.example.com")]
    public void CorsOrigin_NonOrigin_IsRejected(string input)
    {
        var act = () => CorsOrigin.Parse(input);

        act.Should().Throw<CliException>();
    }

    [Theory]
    [InlineData("https://*.vercel.app")]
    [InlineData("https://*.github.io")]
    [InlineData("https://*.foo.netlify.app")]
    [InlineData("https://*.com")]
    [InlineData("https://*.pages.dev")]
    public void CorsOrigin_SharedHostWildcard_CarriesWarning(string input) =>
        CorsOrigin.Parse(input).Warning.Should().NotBeNull();

    [Theory]
    [InlineData("https://*.example.com")]
    [InlineData("https://my-app.vercel.app")]
    public void CorsOrigin_OwnedWildcardOrExactHost_HasNoWarning(string input) =>
        CorsOrigin.Parse(input).Warning.Should().BeNull();

    [Fact]
    public async Task CorsAdd_SharedHostWildcardWithoutYes_RefusesNonInteractively()
    {
        var (client, captured) = BuildClient();

        var act = () => SettingsHelpers.AddCorsUrlAsync(client, "https://*.vercel.app");

        await act.Should().ThrowAsync<CliException>().WithMessage("*--yes*");
        captured.PutCount.Should().Be(0);
    }

    [Fact]
    public async Task CorsAdd_SharedHostWildcardWithYes_IsAdded()
    {
        var (client, captured) = BuildClient();

        await SettingsHelpers.AddCorsUrlAsync(client, "https://*.vercel.app", yes: true);

        SentUrls(captured).Should().Contain("https://*.vercel.app");
    }

    [Fact]
    public async Task CorsAdd_InvalidOrigin_SavesNothing()
    {
        var (client, captured) = BuildClient();

        var act = () => SettingsHelpers.AddCorsUrlAsync(client, "*");

        await act.Should().ThrowAsync<CliException>();
        captured.PutCount.Should().Be(0);
    }

    [Fact]
    public async Task CorsRemove_NormalisesInputSoItMatchesStoredOrigin()
    {
        var (client, captured) = BuildClient();

        var result = await SettingsHelpers.RemoveCorsUrlAsync(client, "HTTPS://app.example.com/");

        result.Should().Be(SaveResult.Saved);
        SentUrls(captured).Should().Equal("*.example.org");
    }

    [Fact]
    public async Task CorsRemove_LegacyInvalidEntry_CanStillBeRemovedVerbatim()
    {
        var json = JsonNode.Parse(TenantJson)!;
        json["tenant_settings"]!["allowed_application_urls"] = new JsonArray("*", "https://app.example.com");
        var (client, captured) = BuildClient(json.ToJsonString());

        var result = await SettingsHelpers.RemoveCorsUrlAsync(client, "*");

        result.Should().Be(SaveResult.Saved);
        SentUrls(captured).Should().Equal("https://app.example.com");
    }

    // ── settings get --json masks secrets ────────────────────────────────────

    [Fact]
    public async Task GetJson_MasksGoogleMapsKeyAndOtherSecretLookingFields()
    {
        var json = JsonNode.Parse(TenantJson)!;
        json["google_maps_key"] = "AIza-real-key";
        json["tenant_settings"]!["stripe_secret_key"] = "sk_live_123";
        json["tenant_settings"]!["webhook_token"] = "tok";
        var (client, _) = BuildClient(json.ToJsonString());

        var tenant = (await client.GetTenantAsync())!;
        var masked = SettingsHelpers.MaskSecrets(JsonSerializer.SerializeToNode(tenant))!.ToJsonString();

        masked.Should().NotContain("AIza-real-key").And.NotContain("sk_live_123").And.NotContain("\"tok\"");
        JsonNode.Parse(masked)!["google_maps_key"]!.GetValue<string>().Should().Be("set");
        masked.Should().Contain("\"default_role_id\":3");
    }

    [Fact]
    public void MaskSecrets_UnsetSecret_StaysNull()
    {
        var node = JsonNode.Parse("""{"google_maps_key":null}""");

        SettingsHelpers.MaskSecrets(node)!["google_maps_key"].Should().BeNull();
    }

    // ── migrate copies the whole settings object ─────────────────────────────

    [Fact]
    public async Task MigrateCopy_WithRemappedRole_PreservesModelledAndExtensionFields()
    {
        var (client, _) = BuildClient();
        var src = (await client.GetTenantAsync())!;

        var copy = src.TenantSettings! with { DefaultRoleId = 42 };

        copy.DefaultRoleId.Should().Be(42);
        copy.AiMode.Should().Be("platform");
        copy.EnableGroupRls.Should().BeTrue();
        copy.AppEngagementTrialDays.Should().Be(7);
        copy.Extra!["some_future_setting"].GetString().Should().Be("keep-me");
    }
}
