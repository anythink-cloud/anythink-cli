using AnythinkCli.Commands;
using AnythinkCli.Models;
using FluentAssertions;

namespace AnythinkCli.Tests;

// ── Rule: plan update changes only the flags that were passed ────────────────

public class PayPlansUpdateTests
{
    private static readonly SubscriptionPlanResponse UsdAnnualInactive = new(
        Id: 12, PlanName: "annual", Name: "Annual", Description: "Yearly", Type: "ios",
        Amount: 99m, Currency: "usd", BillingInterval: "year", IntervalCount: 1,
        TrialPeriodDays: 7, ProductName: "Pro", ProductDescription: null, Reference: "ref-1",
        IsActive: false, AppleProductId: "annual_01", AppleSubscriptionGroupId: "grp", TierRank: 3);

    [Fact]
    public void PlansUpdate_OnlyName_PreservesCurrencyIntervalAndActive()
    {
        var req = PayPlansUpdateCommand.BuildRequest(new PayPlansUpdateSettings { Name = "Annual Pro" }, UsdAnnualInactive);

        req.Name.Should().Be("Annual Pro");
        req.Currency.Should().Be("usd");
        req.BillingInterval.Should().Be("year");
        req.IntervalCount.Should().Be(1);
        req.Type.Should().Be("ios");
        req.Amount.Should().Be(99m);
        req.IsActive.Should().BeFalse();
        req.TierRank.Should().Be(3);
    }

    [Fact]
    public void PlansUpdate_TierRankFlag_OverridesExistingRank()
    {
        var req = PayPlansUpdateCommand.BuildRequest(new PayPlansUpdateSettings { TierRank = 5 }, UsdAnnualInactive);

        req.TierRank.Should().Be(5);
    }

    [Fact]
    public void PlansUpdate_TierRank_IsSentOnTheWire()
    {
        var req = PayPlansUpdateCommand.BuildRequest(new PayPlansUpdateSettings(), UsdAnnualInactive);

        System.Text.Json.JsonSerializer.Serialize(req).Should().Contain("\"tier_rank\":3");
    }

    [Fact]
    public void PlansUpdate_ActiveFlag_ReactivatesPlan()
    {
        var req = PayPlansUpdateCommand.BuildRequest(new PayPlansUpdateSettings { Active = true }, UsdAnnualInactive);

        req.IsActive.Should().BeTrue();
    }

    [Fact]
    public void PlansUpdate_InactiveFlag_DeactivatesPlan()
    {
        var active = UsdAnnualInactive with { IsActive = true };

        var req = PayPlansUpdateCommand.BuildRequest(new PayPlansUpdateSettings { Inactive = true }, active);

        req.IsActive.Should().BeFalse();
    }

    [Fact]
    public void PlansUpdate_ExplicitCurrency_IsNormalisedToLowerCase()
    {
        var req = PayPlansUpdateCommand.BuildRequest(new PayPlansUpdateSettings { Currency = "EUR" }, UsdAnnualInactive);

        req.Currency.Should().Be("eur");
    }
}

// ── Rule: amounts honour each currency's minor units and ignore the machine culture ──

public class PayFormatTests
{
    [Theory]
    [InlineData(1500, "jpy", "1500 JPY")]
    [InlineData(1500, "KRW", "1500 KRW")]
    [InlineData(9.99, "gbp", "£9.99")]
    [InlineData(9.5, "usd", "$9.50")]
    [InlineData(12, "eur", "€12.00")]
    [InlineData(1.5, "kwd", "1.500 KWD")]
    [InlineData(4, "cad", "4.00 CAD")]
    public void Money_UsesCurrencyMinorUnits(double amount, string currency, string expected)
        => PayFormat.Money((decimal)amount, currency).Should().Be(expected);

    [Fact]
    public void Money_NullCurrencyFromServer_DoesNotThrow()
        => PayFormat.Money(5m, null!).Should().Be("5.00");

    [Fact]
    public void Money_UsesInvariantCulture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try { PayFormat.Money(9.99m, "gbp").Should().Be("£9.99"); }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }
}

// ── Rule: money and currency input is validated before it is sent ──

public class PayValidationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Amount_ZeroOrNegative_IsRejected(double amount)
        => PayValidation.Amount((decimal)amount).Should().NotBeNull();

    [Fact]
    public void Amount_Positive_IsAccepted() => PayValidation.Amount(0.01m).Should().BeNull();

    [Theory]
    [InlineData("gbp")]
    [InlineData("USD")]
    public void Currency_ThreeLetterCode_IsAccepted(string code) => PayValidation.Currency(code).Should().BeNull();

    [Theory]
    [InlineData("")]
    [InlineData("us")]
    [InlineData("dollar")]
    [InlineData("u5d")]
    public void Currency_NotThreeLetters_IsRejected(string code) => PayValidation.Currency(code).Should().NotBeNull();

    [Fact]
    public void PlanSettings_BadAmountOrCurrency_FailValidation()
    {
        new PayPlansCreateSettings { Amount = 0m }.ValidateSupplied().Should().NotBeNull();
        new PayPlansCreateSettings { Currency = "pounds" }.ValidateSupplied().Should().NotBeNull();
        new PayPlansCreateSettings { Amount = 5m, Currency = "gbp" }.ValidateSupplied().Should().BeNull();
    }
}

// ── Rule: the trial toggle changes the trial flag and nothing else ───────────

public class PayTrialToggleTests
{
    private static TenantResponse Tenant(TenantSettingsDto? settings) => new(
        Id: 1, Name: "Acme", Description: "desc", TenantSettings: settings,
        ThemeSettings: new ThemeSettingsDto("orange", "slate"), LogoSquare: null, LogoStandard: null, GoogleMapsKey: "gm-key");

    [Fact]
    public void TrialToggle_Enable_PreservesRegistrationRoleAndUrls()
    {
        var settings = new TenantSettingsDto(true, 7, ["https://app.example.com"], "https://ok", "https://no");

        var req = PayTrialToggle.BuildRequest(Tenant(settings), enable: true);

        req.TenantSettings.Should().Be(settings with { AppEngagementTrialEnabled = true });
        req.TenantSettings!.AllowRegistrations.Should().BeTrue();
        req.TenantSettings.DefaultRoleId.Should().Be(7);
        req.TenantSettings.AllowedApplicationUrls.Should().Equal("https://app.example.com");
        req.ThemeSettings!.PrimaryColor.Should().Be("orange");
        req.GoogleMapsKey.Should().Be("gm-key");
    }

    [Fact]
    public void TrialToggle_Disable_ClearsOnlyTheTrialFlag()
    {
        var settings = new TenantSettingsDto(true, 7, [], null, null, AppEngagementTrialEnabled: true);

        var req = PayTrialToggle.BuildRequest(Tenant(settings), enable: false);

        req.TenantSettings.Should().Be(settings with { AppEngagementTrialEnabled = false });
    }

    [Fact]
    public void TrialToggle_UnmodelledSettingsAndThemeKeys_SurviveTheRoundTrip()
    {
        const string get = """
        {"id":1,"name":"Acme","require_email_confirmation":true,
         "tenant_settings":{"allow_registrations":true,"allowed_application_urls":[],"enable_group_rls":true,"ai_mode":"byok","some_future_setting":{"a":1}},
         "theme_settings":{"primary_color":"red","email_wrapper_html":"<b>w</b>","radius":"large"}}
        """;
        var tenant = System.Text.Json.JsonSerializer.Deserialize<TenantResponse>(get)!;

        var sent = System.Text.Json.JsonSerializer.Serialize(PayTrialToggle.BuildRequest(tenant, enable: true));

        sent.Should().Contain("\"enable_group_rls\":true").And.Contain("\"ai_mode\":\"byok\"")
            .And.Contain("some_future_setting").And.Contain("email_wrapper_html").And.Contain("\"radius\":\"large\"")
            .And.Contain("\"require_email_confirmation\":true").And.Contain("\"app_engagement_trial_enabled\":true");
    }

    [Fact]
    public void TrialToggle_SettingsUnreadable_RefusesInsteadOfWritingDefaults()
    {
        var noSettings = () => PayTrialToggle.BuildRequest(Tenant(null), enable: true);
        var noTenant = () => PayTrialToggle.BuildRequest(null, enable: true);

        noSettings.Should().Throw<InvalidOperationException>();
        noTenant.Should().Throw<InvalidOperationException>();
    }
}

// ── Rule: migrating settings copies everything, remapping only the default role ──

public class MigrateSettingsTests
{
    [Fact]
    public void MigrateSettings_CopiesTrialFlagAndRemapsOnlyDefaultRole()
    {
        var source = new TenantSettingsDto(true, 3, ["https://a"], "https://ok", "https://no", AppEngagementTrialEnabled: true);

        var result = MigrateCommand.BuildDestinationSettings(source, remappedDefaultRoleId: 9);

        result.Should().Be(source with { DefaultRoleId = 9 });
        result!.AppEngagementTrialEnabled.Should().BeTrue();
    }

    [Fact]
    public void MigrateSettings_UnmodelledKeys_SurviveTheRoundTrip()
    {
        const string json = """{"allow_registrations":true,"allowed_application_urls":[],"enable_group_rls":true,"ai_mode":"byok","email_wrapper_html":"<b>w</b>"}""";
        var source = System.Text.Json.JsonSerializer.Deserialize<TenantSettingsDto>(json)!;

        var sent = System.Text.Json.JsonSerializer.Serialize(MigrateCommand.BuildDestinationSettings(source, 9));

        sent.Should().Contain("\"enable_group_rls\":true").And.Contain("\"ai_mode\":\"byok\"").And.Contain("email_wrapper_html");
    }

    [Fact]
    public void MigrateSettings_NoSourceSettings_YieldsNull()
        => MigrateCommand.BuildDestinationSettings(null, null).Should().BeNull();
}
