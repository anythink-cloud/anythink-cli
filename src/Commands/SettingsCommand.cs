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

    /// <summary>Sets one key and saves. Returns false (and saves nothing) when the key is unknown.</summary>
    public static async Task<bool> SetAsync(AnythinkClient client, string rawKey, string value)
    {
        var tenant = Require(await client.GetTenantAsync());
        var ts = tenant.TenantSettings ?? DefaultTenantSettings();

        var name = tenant.Name;
        var description = tenant.Description;
        var googleMapsKey = tenant.GoogleMapsKey;
        var requireEmail = tenant.RequireEmailConfirmation;

        var key = rawKey.ToLowerInvariant();
        switch (key)
        {
            case "name":                         name = value; break;
            case "description":                  description = value; break;
            case "google_maps_key":              googleMapsKey = value; break;
            case "require_email_confirmation":   requireEmail = ParseBool(value, key); break;

            case "allow_registrations":          ts = ts with { AllowRegistrations = ParseBool(value, key) }; break;
            case "default_role_id":              ts = ts with { DefaultRoleId = ParseInt(value, key) }; break;
            case "enable_group_rls":             ts = ts with { EnableGroupRls = ParseBool(value, key) }; break;
            case "payment_success_url":          ts = ts with { PaymentSuccessUrl = value }; break;
            case "payment_cancel_url":           ts = ts with { PaymentCancelUrl = value }; break;
            case "ai_mode":                      ts = ts with { AiMode = value }; break;
            case "ai_byok_provider":             ts = ts with { AiByokProvider = value }; break;
            case "ai_default_model":             ts = ts with { AiDefaultModel = value }; break;
            case "app_engagement_trial_enabled": ts = ts with { AppEngagementTrialEnabled = ParseBool(value, key) }; break;
            case "app_engagement_trial_days":    ts = ts with { AppEngagementTrialDays = ParseInt(value, key) }; break;

            default: return false;
        }

        await client.UpdateTenantAsync(BuildUpdate(tenant, name, description, googleMapsKey, requireEmail, ts));
        return true;
    }

    /// <summary>Adds an allowed origin and clears the CORS cache. Returns false when it is already present.</summary>
    public static Task<bool> AddCorsUrlAsync(AnythinkClient client, string url) =>
        UpdateCorsUrlsAsync(client, urls =>
        {
            if (urls.Contains(url, StringComparer.OrdinalIgnoreCase)) return false;
            urls.Add(url);
            return true;
        });

    /// <summary>Removes an allowed origin and clears the CORS cache. Returns false when it isn't present.</summary>
    public static Task<bool> RemoveCorsUrlAsync(AnythinkClient client, string url) =>
        UpdateCorsUrlsAsync(client, urls =>
            urls.RemoveAll(u => string.Equals(u, url, StringComparison.OrdinalIgnoreCase)) > 0);

    private static async Task<bool> UpdateCorsUrlsAsync(AnythinkClient client, Func<List<string>, bool> change)
    {
        var tenant = Require(await client.GetTenantAsync());
        var ts = tenant.TenantSettings ?? DefaultTenantSettings();
        var urls = new List<string>(ts.AllowedApplicationUrls ?? []);
        if (!change(urls)) return false;

        await client.UpdateTenantAsync(BuildUpdate(
            tenant, tenant.Name, tenant.Description, tenant.GoogleMapsKey,
            tenant.RequireEmailConfirmation, ts with { AllowedApplicationUrls = urls }));
        await client.ClearCorsCacheAsync();
        return true;
    }
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
            Renderer.KeyValue("enable_group_rls", (ts?.EnableGroupRls ?? false).ToString().ToLowerInvariant());
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
            AnsiConsole.MarkupLine("[dim](none)[/]");
        }
        else
        {
            foreach (var u in urls) AnsiConsole.MarkupLine($"  • {Markup.Escape(u)}");
        }
        AnsiConsole.MarkupLine("[dim]Anythink domains and localhost are always allowed.[/]");
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
            var known = false;
            await AnsiConsole.Status().StartAsync("Saving…", async _ =>
                known = await SettingsHelpers.SetAsync(client, s.Key, s.Value));

            if (!known)
            {
                Renderer.Error($"Unknown setting key '{Markup.Escape(s.Key)}'.");
                AnsiConsole.MarkupLine("[dim]Run [bold]anythink settings get[/] to see available keys. For CORS URLs use [bold]anythink settings cors add/remove[/].[/]");
                return 1;
            }

            var key = s.Key.ToLowerInvariant();
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
            var changed = false;
            await AnsiConsole.Status().StartAsync("Saving…", async _ =>
                changed = await SettingsHelpers.AddCorsUrlAsync(client, s.Url));

            if (!changed)
            {
                Renderer.Warn($"'{Markup.Escape(s.Url)}' is already in the allow-list.");
                return 0;
            }

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
            var changed = false;
            await AnsiConsole.Status().StartAsync("Saving…", async _ =>
                changed = await SettingsHelpers.RemoveCorsUrlAsync(client, s.Url));

            if (!changed)
            {
                Renderer.Warn($"'{Markup.Escape(s.Url)}' is not in the allow-list.");
                return 0;
            }

            Renderer.Success($"Removed [bold]{Markup.Escape(s.Url)}[/] from allowed origins (CORS cache cleared).");
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}
