using AnythinkCli.Client;
using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace AnythinkCli.Commands;

// ── helpers ──────────────────────────────────────────────────────────────────

internal static class SettingsHelpers
{
    public static TenantSettingsDto DefaultTenantSettings() =>
        new(false, null, [], null, null, false, 7, "platform", null, null);

    public static TenantResponse Require(TenantResponse? t) =>
        t ?? throw new CliException("Project not found, or you don't have access. Run [bold #F97316]anythink projects use <id>[/].");

    public static bool ParseBool(string v, string key) =>
        v.ToLowerInvariant() switch
        {
            "true" or "1" or "yes" or "on"  => true,
            "false" or "0" or "no" or "off" => false,
            _ => throw new CliException($"'{Markup.Escape(v)}' is not a valid boolean for [bold]{Markup.Escape(key)}[/] (use true/false)."),
        };

    public static int ParseInt(string v, string key) =>
        int.TryParse(v, out var n)
            ? n
            : throw new CliException($"'{Markup.Escape(v)}' is not a valid integer for [bold]{Markup.Escape(key)}[/].");

    // Rebuilds the full update request from the current tenant, preserving everything not being changed.
    public static UpdateTenantRequest BuildUpdate(
        TenantResponse t, string name, string? description, string? googleMapsKey,
        bool? requireEmailConfirmation, TenantSettingsDto settings) =>
        new(
            Name: name,
            Description: description,
            GoogleMapsKey: googleMapsKey,
            LogoSquareId: t.LogoSquare?.Id,
            LogoStandardId: t.LogoStandard?.Id,
            TenantSettings: settings,
            ThemeSettings: t.ThemeSettings,
            RequireEmailConfirmation: requireEmailConfirmation);
}

// ── settings get ─────────────────────────────────────────────────────────────

public class SettingsGetSettings : CommandSettings
{
    [CommandOption("--json")]
    [Description("Output raw JSON")]
    public bool Json { get; set; }
}

public class SettingsGetCommand : BaseCommand<SettingsGetSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext ctx, SettingsGetSettings opts)
    {
        try
        {
            var tenant = SettingsHelpers.Require(await GetClient().GetTenantAsync());

            if (opts.Json)
            {
                Renderer.PrintJson(System.Text.Json.JsonSerializer.Serialize(tenant, Renderer.PrettyJson));
                return 0;
            }

            var ts = tenant.TenantSettings;

            Renderer.Header($"Project — {tenant.Name}");
            Renderer.KeyValue("name", tenant.Name);
            Renderer.KeyValue("description", string.IsNullOrEmpty(tenant.Description) ? "—" : tenant.Description);
            Renderer.KeyValue("require_email_confirmation", (tenant.RequireEmailConfirmation ?? false).ToString().ToLowerInvariant());
            Renderer.KeyValue("google_maps_key", string.IsNullOrEmpty(tenant.GoogleMapsKey) ? "—" : "✓ set");

            AnsiConsole.WriteLine();
            Renderer.Header("Tenant settings");
            Renderer.KeyValue("allow_registrations", (ts?.AllowRegistrations ?? false).ToString().ToLowerInvariant());
            Renderer.KeyValue("default_role_id", ts?.DefaultRoleId?.ToString() ?? "—");
            Renderer.KeyValue("ai_mode", ts?.AiMode ?? "—");
            Renderer.KeyValue("ai_default_model", ts?.AiDefaultModel ?? "—");
            Renderer.KeyValue("ai_byok_provider", ts?.AiByokProvider ?? "—");
            Renderer.KeyValue("payment_success_url", ts?.PaymentSuccessUrl ?? "—");
            Renderer.KeyValue("payment_cancel_url", ts?.PaymentCancelUrl ?? "—");
            Renderer.KeyValue("app_engagement_trial_enabled", (ts?.AppEngagementTrialEnabled ?? false).ToString().ToLowerInvariant());
            Renderer.KeyValue("app_engagement_trial_days", ts?.AppEngagementTrialDays?.ToString() ?? "—");

            AnsiConsole.WriteLine();
            Renderer.Header("Allowed application URLs (CORS)");
            PrintUrls(ts?.AllowedApplicationUrls);
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }

    internal static void PrintUrls(List<string>? urls)
    {
        if (urls is null || urls.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim](none — defaults allow *.anythink.cloud / .uk / .dev and localhost)[/]");
            return;
        }
        foreach (var u in urls) AnsiConsole.MarkupLine($"  • {Markup.Escape(u)}");
    }
}

// ── settings set ─────────────────────────────────────────────────────────────

public class SettingsSetSettings : CommandSettings
{
    [CommandArgument(0, "<KEY>")]
    [Description("Setting key (run 'anythink settings get' to see them). For CORS URLs use 'settings cors'.")]
    public string Key { get; set; } = "";

    [CommandArgument(1, "<VALUE>")]
    [Description("New value")]
    public string Value { get; set; } = "";
}

public class SettingsSetCommand : BaseCommand<SettingsSetSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext ctx, SettingsSetSettings s)
    {
        try
        {
            var client = GetClient();
            var tenant = SettingsHelpers.Require(await client.GetTenantAsync());
            var ts = tenant.TenantSettings ?? SettingsHelpers.DefaultTenantSettings();

            var name = tenant.Name;
            var description = tenant.Description;
            var googleMapsKey = tenant.GoogleMapsKey;
            var requireEmail = tenant.RequireEmailConfirmation;

            var key = s.Key.ToLowerInvariant();
            switch (key)
            {
                case "name":                         name = s.Value; break;
                case "description":                  description = s.Value; break;
                case "google_maps_key":              googleMapsKey = s.Value; break;
                case "require_email_confirmation":   requireEmail = SettingsHelpers.ParseBool(s.Value, key); break;

                case "allow_registrations":          ts = ts with { AllowRegistrations = SettingsHelpers.ParseBool(s.Value, key) }; break;
                case "default_role_id":              ts = ts with { DefaultRoleId = SettingsHelpers.ParseInt(s.Value, key) }; break;
                case "payment_success_url":          ts = ts with { PaymentSuccessUrl = s.Value }; break;
                case "payment_cancel_url":           ts = ts with { PaymentCancelUrl = s.Value }; break;
                case "ai_mode":                      ts = ts with { AiMode = s.Value }; break;
                case "ai_byok_provider":             ts = ts with { AiByokProvider = s.Value }; break;
                case "ai_default_model":             ts = ts with { AiDefaultModel = s.Value }; break;
                case "app_engagement_trial_enabled": ts = ts with { AppEngagementTrialEnabled = SettingsHelpers.ParseBool(s.Value, key) }; break;
                case "app_engagement_trial_days":    ts = ts with { AppEngagementTrialDays = SettingsHelpers.ParseInt(s.Value, key) }; break;

                default:
                    Renderer.Error($"Unknown setting key '{Markup.Escape(s.Key)}'.");
                    AnsiConsole.MarkupLine("[dim]Run [bold]anythink settings get[/] to see available keys. For CORS URLs use [bold]anythink settings cors add/remove[/].[/]");
                    return 1;
            }

            var req = SettingsHelpers.BuildUpdate(tenant, name, description, googleMapsKey, requireEmail, ts);
            await AnsiConsole.Status().StartAsync("Saving…", async _ => await client.UpdateTenantAsync(req));
            Renderer.Success($"Set [bold]{Markup.Escape(key)}[/] = [bold]{Markup.Escape(s.Value)}[/].");
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

// ── settings cors ────────────────────────────────────────────────────────────

public class SettingsCorsListCommand : BaseCommand<EmptySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext ctx, EmptySettings _)
    {
        try
        {
            var tenant = SettingsHelpers.Require(await GetClient().GetTenantAsync());
            Renderer.Header("Allowed application URLs (CORS)");
            SettingsGetCommand.PrintUrls(tenant.TenantSettings?.AllowedApplicationUrls);
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

public class SettingsCorsUrlSettings : CommandSettings
{
    [CommandArgument(0, "<URL>")]
    [Description("Application origin, e.g. https://app.example.com or a wildcard like *.example.com")]
    public string Url { get; set; } = "";
}

public class SettingsCorsAddCommand : BaseCommand<SettingsCorsUrlSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext ctx, SettingsCorsUrlSettings s)
    {
        try
        {
            var client = GetClient();
            var tenant = SettingsHelpers.Require(await client.GetTenantAsync());
            var ts = tenant.TenantSettings ?? SettingsHelpers.DefaultTenantSettings();
            var urls = new List<string>(ts.AllowedApplicationUrls ?? []);

            if (urls.Contains(s.Url, StringComparer.OrdinalIgnoreCase))
            {
                Renderer.Warn($"'{Markup.Escape(s.Url)}' is already in the allow-list.");
                return 0;
            }

            urls.Add(s.Url);
            var req = SettingsHelpers.BuildUpdate(
                tenant, tenant.Name, tenant.Description, tenant.GoogleMapsKey,
                tenant.RequireEmailConfirmation, ts with { AllowedApplicationUrls = urls });

            await AnsiConsole.Status().StartAsync("Saving…", async _ =>
            {
                await client.UpdateTenantAsync(req);
                await client.ClearCorsCacheAsync();
            });
            Renderer.Success($"Added [bold]{Markup.Escape(s.Url)}[/] to allowed origins (CORS cache cleared).");
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

public class SettingsCorsRemoveCommand : BaseCommand<SettingsCorsUrlSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext ctx, SettingsCorsUrlSettings s)
    {
        try
        {
            var client = GetClient();
            var tenant = SettingsHelpers.Require(await client.GetTenantAsync());
            var ts = tenant.TenantSettings ?? SettingsHelpers.DefaultTenantSettings();
            var urls = new List<string>(ts.AllowedApplicationUrls ?? []);

            var removed = urls.RemoveAll(u => string.Equals(u, s.Url, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
            {
                Renderer.Warn($"'{Markup.Escape(s.Url)}' is not in the allow-list.");
                return 0;
            }

            var req = SettingsHelpers.BuildUpdate(
                tenant, tenant.Name, tenant.Description, tenant.GoogleMapsKey,
                tenant.RequireEmailConfirmation, ts with { AllowedApplicationUrls = urls });

            await AnsiConsole.Status().StartAsync("Saving…", async _ =>
            {
                await client.UpdateTenantAsync(req);
                await client.ClearCorsCacheAsync();
            });
            Renderer.Success($"Removed [bold]{Markup.Escape(s.Url)}[/] from allowed origins (CORS cache cleared).");
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}
