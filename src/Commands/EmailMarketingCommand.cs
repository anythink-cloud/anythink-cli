using System.ComponentModel;
using AnythinkCli.Client;
using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AnythinkCli.Commands;

internal static class EmailMarketingOutput
{
    public static void Show(EmailMarketingSettings s)
    {
        Renderer.KeyValue("Double opt-in", s.DoubleOptIn ? "on" : "off");
        Renderer.KeyValue("Soft opt-in", s.SoftOptIn ? "on" : "off");
        Renderer.KeyValue("Postal address", s.PostalAddress);
        Renderer.KeyValue("Default consent wording", s.DefaultConsentWording);
        Renderer.KeyValue("Privacy policy URL", s.PrivacyPolicyUrl);
        Renderer.KeyValue("Signup opt-out wording", s.SignupOptOutWording);
    }
}

// ── email-marketing settings get ──────────────────────────────────────────────

public class EmailMarketingSettingsGetCommand : BaseCommand<JsonSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, JsonSettings settings)
    {
        try
        {
            var current = await GetClient().GetEmailMarketingSettingsAsync();
            if (settings.Json) { ContactsOutput.PrintJson(current); return 0; }

            Renderer.Header("Email marketing settings");
            EmailMarketingOutput.Show(current);
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

// ── email-marketing settings set ──────────────────────────────────────────────

public class EmailMarketingSettingsSetSettings : JsonSettings
{
    [CommandOption("--double-opt-in <BOOL>")]
    [Description("Ask new subscribers to confirm by email before they're subscribed (true/false)")]
    public bool? DoubleOptIn { get; set; }

    [CommandOption("--soft-opt-in <BOOL>")]
    [Description("Allow marketing to existing customers under the soft opt-in rule (true/false). Turning it on needs --soft-opt-in-declaration")]
    public bool? SoftOptIn { get; set; }

    [CommandOption("--soft-opt-in-declaration")]
    [Description("Confirm that you meet the legal conditions for the soft opt-in")]
    public bool SoftOptInDeclaration { get; set; }

    [CommandOption("--postal-address <TEXT>")]
    [Description("The postal address shown in the footer of marketing emails")]
    public string? PostalAddress { get; set; }

    [CommandOption("--default-consent-wording <TEXT>")]
    [Description("The consent wording recorded when none is given")]
    public string? DefaultConsentWording { get; set; }

    [CommandOption("--privacy-policy-url <URL>")]
    [Description("Link to your privacy policy")]
    public string? PrivacyPolicyUrl { get; set; }

    [CommandOption("--signup-opt-out-wording <TEXT>")]
    [Description("The wording for the marketing opt-out offered at account signup")]
    public string? SignupOptOutWording { get; set; }

    public override Spectre.Console.ValidationResult Validate() =>
        DoubleOptIn is null && SoftOptIn is null && PostalAddress is null && DefaultConsentWording is null
        && PrivacyPolicyUrl is null && SignupOptOutWording is null
            ? Spectre.Console.ValidationResult.Error("Pass at least one setting to change. See --help.")
            : Spectre.Console.ValidationResult.Success();
}

public class EmailMarketingSettingsSetCommand : BaseCommand<EmailMarketingSettingsSetSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EmailMarketingSettingsSetSettings settings)
    {
        try
        {
            var client = GetClient();
            var current = await client.GetEmailMarketingSettingsAsync();
            var softOptIn = settings.SoftOptIn ?? current.SoftOptIn;

            var request = new UpdateEmailMarketingSettingsRequest(
                settings.DoubleOptIn ?? current.DoubleOptIn,
                softOptIn,
                settings.PostalAddress ?? current.PostalAddress,
                settings.DefaultConsentWording ?? current.DefaultConsentWording,
                settings.PrivacyPolicyUrl ?? current.PrivacyPolicyUrl,
                settings.SignupOptOutWording ?? current.SignupOptOutWording,
                settings.SoftOptInDeclaration ? true : null);

            var saved = await client.UpdateEmailMarketingSettingsAsync(request) ?? current;

            if (settings.Json) { ContactsOutput.PrintJson(saved); return 0; }

            Renderer.Success("Email marketing settings saved.");
            EmailMarketingOutput.Show(saved);
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

// ── email-marketing options ───────────────────────────────────────────────────

public class EmailMarketingOptionsCommand : BaseCommand<JsonSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, JsonSettings settings)
    {
        try
        {
            var options = await GetClient().GetEmailMarketingOptionsAsync();
            if (settings.Json) { ContactsOutput.PrintJson(options); return 0; }

            Renderer.Header("Public signup options");
            Renderer.KeyValue("Double opt-in", options.DoubleOptIn ? "on" : "off");
            Renderer.KeyValue("Newsletter consent wording", options.Newsletter?.ConsentWording);
            Renderer.KeyValue("Privacy policy URL", options.Newsletter?.PrivacyPolicyUrl);
            Renderer.KeyValue("Signup offers marketing opt-out", options.Signup?.MarketingOptOutOffered == true ? "yes" : "no");
            Renderer.KeyValue("Signup opt-out wording", options.Signup?.OptOutWording);
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}
