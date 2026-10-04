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

    public static async Task<SaveResult> SetAsync(
        AnythinkClient client, string rawKey, string value, bool yes = false, Func<string, bool>? confirm = null)
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
            case "require_email_confirmation":
                requireEmail = ParseBool(value, key);
                if (requireEmail == false &&
                    !Authorise("Turning off email confirmation lets anyone register with an address they don't own.", false, yes, confirm))
                    return SaveResult.Cancelled;
                break;

            case "allow_registrations":
                var allow = ParseBool(value, key);
                if (allow &&
                    !Authorise("Enabling registrations lets anyone sign up to this project without an invitation.", false, yes, confirm))
                    return SaveResult.Cancelled;
                ts = ts with { AllowRegistrations = allow };
                break;
            case "default_role_id":
                var roleId = ParseInt(value, key);
                var adminWarning = await AdminRoleWarningAsync(client, roleId);
                if (adminWarning != null && !Authorise(adminWarning, true, yes, confirm))
                    return SaveResult.Cancelled;
                ts = ts with { DefaultRoleId = roleId };
                break;
            case "enable_group_rls":             ts = ts with { EnableGroupRls = ParseBool(value, key) }; break;
            case "payment_success_url":          ts = ts with { PaymentSuccessUrl = value }; break;
            case "payment_cancel_url":           ts = ts with { PaymentCancelUrl = value }; break;
            case "ai_mode":                      ts = ts with { AiMode = value }; break;
            case "ai_byok_provider":             ts = ts with { AiByokProvider = value }; break;
            case "ai_default_model":             ts = ts with { AiDefaultModel = value }; break;
            case "app_engagement_trial_enabled": ts = ts with { AppEngagementTrialEnabled = ParseBool(value, key) }; break;
            case "app_engagement_trial_days":    ts = ts with { AppEngagementTrialDays = ParseInt(value, key) }; break;

            default: return SaveResult.UnknownKey;
        }

        await client.UpdateTenantAsync(BuildUpdate(tenant, name, description, googleMapsKey, requireEmail, ts));
        return SaveResult.Saved;
    }

    public static Func<string, bool>? InteractiveConfirm() =>
        AnsiConsole.Profile.Capabilities.Interactive
            ? warning => AnsiConsole.Confirm($"[yellow]{Markup.Escape(warning)}[/] Continue?", defaultValue: false)
            : null;

    // Hard warnings never prompt: only an explicit --yes lets them through.
    internal static bool Authorise(string warning, bool hard, bool yes, Func<string, bool>? confirm)
    {
        if (yes) return true;
        if (hard || confirm is null)
            throw new CliException($"{Markup.Escape(warning)} Re-run with [bold]--yes[/] to confirm.");
        return confirm(warning);
    }

    internal static async Task<string?> AdminRoleWarningAsync(AnythinkClient client, int roleId)
    {
        var role = await client.GetRoleAsync(roleId)
            ?? throw new CliException($"Role {roleId} doesn't exist in this project.");

        // Ordinary seeded roles already have API access, so that says nothing about privilege.
        var adminLike = role.IsAdministrator ||
            role.Name.Trim().ToLowerInvariant() is "admin" or "administrator" ||
            (role.Permissions ?? []).Any(p => p.Name.StartsWith("anythink_", StringComparison.OrdinalIgnoreCase)
                                              && !p.Name.EndsWith(":read", StringComparison.OrdinalIgnoreCase));
        return adminLike
            ? $"Role '{role.Name}' has administrative access, and every new sign-up would be given it."
            : null;
    }

    public static async Task<SaveResult> AddCorsUrlAsync(
        AnythinkClient client, string url, bool yes = false, Func<string, bool>? confirm = null)
    {
        var origin = CorsOrigin.Parse(url);
        if (origin.Warning != null && !Authorise(origin.Warning, true, yes, confirm))
            return SaveResult.Cancelled;

        return await UpdateCorsUrlsAsync(client, urls =>
        {
            if (urls.Contains(origin.Value, StringComparer.OrdinalIgnoreCase)) return false;
            urls.Add(origin.Value);
            return true;
        });
    }

    public static Task<SaveResult> RemoveCorsUrlAsync(AnythinkClient client, string url)
    {
        var raw = url.Trim();
        var normalised = CorsOrigin.TryParse(raw)?.Value ?? raw;
        return UpdateCorsUrlsAsync(client, urls =>
            urls.RemoveAll(u => string.Equals(u, normalised, StringComparison.OrdinalIgnoreCase)
                             || string.Equals(u, raw, StringComparison.OrdinalIgnoreCase)) > 0);
    }

    private static async Task<SaveResult> UpdateCorsUrlsAsync(AnythinkClient client, Func<List<string>, bool> change)
    {
        var tenant = Require(await client.GetTenantAsync());
        var ts = tenant.TenantSettings ?? DefaultTenantSettings();
        var urls = new List<string>(ts.AllowedApplicationUrls ?? []);
        if (!change(urls)) return SaveResult.Unchanged;

        await client.UpdateTenantAsync(BuildUpdate(
            tenant, tenant.Name, tenant.Description, tenant.GoogleMapsKey,
            tenant.RequireEmailConfirmation, ts with { AllowedApplicationUrls = urls }));
        await client.ClearCorsCacheAsync();
        return SaveResult.Saved;
    }

    private static readonly System.Text.RegularExpressions.Regex SecretName =
        new("secret|token|password|api_?key|private_?key|_key$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public static System.Text.Json.Nodes.JsonNode? MaskSecrets(System.Text.Json.Nodes.JsonNode? node)
    {
        switch (node)
        {
            case System.Text.Json.Nodes.JsonObject obj:
                foreach (var (k, v) in obj.ToList())
                {
                    if (SecretName.IsMatch(k))
                        obj[k] = v is null || (v is System.Text.Json.Nodes.JsonValue jv &&
                                  jv.TryGetValue<string>(out var str) && string.IsNullOrEmpty(str))
                            ? null : "set";
                    else
                        MaskSecrets(v);
                }
                break;
            case System.Text.Json.Nodes.JsonArray arr:
                foreach (var item in arr) MaskSecrets(item);
                break;
        }
        return node;
    }
}

public enum SaveResult { Saved, Unchanged, UnknownKey, Cancelled }

internal sealed record CorsOrigin(string Value, string? Warning)
{
    private static readonly string[] SharedSuffixes =
    [
        "vercel.app", "netlify.app", "herokuapp.com", "github.io", "pages.dev", "web.app", "firebaseapp.com",
        "azurewebsites.net", "cloudfront.net", "onrender.com", "fly.dev", "co.uk", "com.au",
    ];

    public static CorsOrigin? TryParse(string input)
    {
        try { return Parse(input); }
        catch (CliException) { return null; }
    }

    public static CorsOrigin Parse(string input)
    {
        var s = input.Trim();
        if (s.EndsWith('/')) s = s[..^1];
        if (s.Length == 0 || s == "*" || s.Equals("null", StringComparison.OrdinalIgnoreCase))
            throw Invalid(input, "an origin is required, not a bare wildcard or 'null'");

        string? scheme = null;
        var rest = s;
        var sep = s.IndexOf("://", StringComparison.Ordinal);
        if (sep >= 0)
        {
            scheme = s[..sep].ToLowerInvariant();
            rest = s[(sep + 3)..];
            if (scheme is not ("http" or "https"))
                throw Invalid(input, "only http and https origins are allowed");
        }
        else if (!s.StartsWith("*.", StringComparison.Ordinal))
        {
            throw Invalid(input, "include the scheme, e.g. https://app.example.com");
        }

        if (rest.IndexOfAny(['/', '?', '#', '@', '\\', ' ']) >= 0)
            throw Invalid(input, "an origin has no path, query, fragment or credentials");

        var host = rest;
        string? port = null;
        var colon = rest.LastIndexOf(':');
        if (colon >= 0)
        {
            host = rest[..colon];
            port = rest[(colon + 1)..];
            if (!int.TryParse(port, out var p) || p is < 1 or > 65535 || port.Any(c => !char.IsAsciiDigit(c)))
                throw Invalid(input, "invalid port");
        }

        host = host.ToLowerInvariant();
        var wildcard = host.StartsWith("*.", StringComparison.Ordinal);
        var baseHost = wildcard ? host[2..] : host;
        if (baseHost.Length == 0 || baseHost.Split('.').Any(l => l.Length == 0 || l.StartsWith('-') || l.EndsWith('-'))
            || baseHost.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-')))
            throw Invalid(input, "invalid host name");

        var value = (scheme is null ? "" : scheme + "://") + host + (port is null ? "" : ":" + port);

        string? warning = null;
        if (wildcard && (!baseHost.Contains('.') ||
                         SharedSuffixes.Any(x => baseHost == x || baseHost.EndsWith("." + x))))
            warning = $"'{value}' would trust every site hosted under '{baseHost}', including ones other people control.";

        return new CorsOrigin(value, warning);
    }

    private static CliException Invalid(string input, string why) =>
        new($"'{Markup.Escape(input)}' is not a valid origin: {why}.");
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
                var node = System.Text.Json.JsonSerializer.SerializeToNode(tenant, Renderer.PrettyJson);
                Renderer.PrintJson(SettingsHelpers.MaskSecrets(node)!.ToJsonString(Renderer.PrettyJson));
                return 0;
            }

            var ts = tenant.TenantSettings;

            Renderer.Header($"Project — {tenant.Name}");
            Renderer.KeyValue("name", tenant.Name);
            Renderer.KeyValue("description", string.IsNullOrEmpty(tenant.Description) ? "—" : tenant.Description);
            Renderer.KeyValue("require_email_confirmation", (tenant.RequireEmailConfirmation ?? false).ToString().ToLowerInvariant());
            Renderer.KeyValue("google_maps_key", string.IsNullOrEmpty(tenant.GoogleMapsKey) ? "—" : "✓ set");

            AnsiConsole.WriteLine();
            Renderer.Header("Project settings");
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

    [CommandOption("-y|--yes")]
    [Description("Confirm risky changes without prompting (required when not running interactively)")]
    public bool Yes { get; set; }
}

public class SettingsSetCommand : BaseCommand<SettingsSetSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext ctx, SettingsSetSettings s)
    {
        try
        {
            var client = GetClient();
            var result = await SettingsHelpers.SetAsync(client, s.Key, s.Value, s.Yes, SettingsHelpers.InteractiveConfirm());

            if (result == SaveResult.Cancelled)
            {
                Renderer.Info("Cancelled.");
                return 0;
            }

            if (result == SaveResult.UnknownKey)
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
    [Description("Application origin, e.g. https://app.example.com or a wildcard like https://*.example.com")]
    public string Url { get; set; } = "";

    [CommandOption("-y|--yes")]
    [Description("Confirm risky wildcard origins without prompting")]
    public bool Yes { get; set; }
}

public class SettingsCorsAddCommand : BaseCommand<SettingsCorsUrlSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext ctx, SettingsCorsUrlSettings s)
    {
        try
        {
            var client = GetClient();
            var result = await SettingsHelpers.AddCorsUrlAsync(client, s.Url, s.Yes, SettingsHelpers.InteractiveConfirm());

            if (result == SaveResult.Cancelled)
            {
                Renderer.Info("Cancelled.");
                return 0;
            }

            var shown = CorsOrigin.Parse(s.Url).Value;
            if (result == SaveResult.Unchanged)
            {
                Renderer.Warn($"'{Markup.Escape(shown)}' is already in the allow-list.");
                return 0;
            }

            Renderer.Success($"Added [bold]{Markup.Escape(shown)}[/] to allowed origins (CORS cache cleared).");
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
            var changed = await SettingsHelpers.RemoveCorsUrlAsync(client, s.Url) == SaveResult.Saved;

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
