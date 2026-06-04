using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Text.Json;

namespace AnythinkCli.Commands;

// ── pay offers › shared helpers ────────────────────────────────────────────────

internal static class OfferFormat
{
    private static readonly JsonSerializerOptions Read = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions Write = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(OfferRewardDto reward) => JsonSerializer.Serialize(reward, Write);

    /// <summary>Validates a raw JSON string parses as an object; returns it untouched so unknown
    /// reward shapes pass straight through to PayApi.</summary>
    public static string ValidateJson(string json)
    {
        using var _ = JsonDocument.Parse(json); // throws JsonException on malformed input
        return json;
    }

    /// <summary>Best-effort one-line summary of a reward JSON string for tables/detail views.</summary>
    public static string Reward(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "—";
        try
        {
            var r = JsonSerializer.Deserialize<OfferRewardDto>(json, Read);
            if (r is null || string.IsNullOrEmpty(r.Type)) return json;
            return r.Type switch
            {
                "trial_extension"        => $"trial +{r.Days}d",
                "subscription_extension" => $"subscription +{r.Days}d",
                "discount"               => $"{r.PercentOff}% off{(r.Duration is null ? "" : $" ({r.Duration})")}",
                "account_credit"         => $"credit {r.Amount} {r.Currency?.ToUpper()}",
                "cash_payout"            => $"cash {r.Amount} {r.Currency?.ToUpper()}",
                "tiered"                 => $"tiered ({r.Tiers?.Count ?? 0} tiers)",
                _                        => r.Type
            };
        }
        catch (JsonException) { return json; }
    }

    public static string Status(string status) => status switch
    {
        "active"  => "[green]active[/]",
        "paused"  => "[yellow]paused[/]",
        "expired" => "[dim]expired[/]",
        _         => status
    };
}

// ── pay offers write settings (shared create/update flags) ─────────────────────

public class PayOffersWriteSettings : CommandSettings
{
    [CommandOption("--name <NAME>")]
    [Description("Offer display name")]
    public string? Name { get; set; }

    [CommandOption("--description <DESC>")]
    [Description("Offer description")]
    public string? Description { get; set; }

    [CommandOption("--redeemer-reward <JSON>")]
    [Description("Redeemer reward as JSON, e.g. {\"type\":\"discount\",\"percent_off\":50,\"duration\":\"once\"}")]
    public string? RedeemerRewardJson { get; set; }

    [CommandOption("--referrer-reward <JSON>")]
    [Description("Referrer reward as JSON (referral offers only)")]
    public string? ReferrerRewardJson { get; set; }

    [CommandOption("--eligibility <JSON>")]
    [Description("Eligibility rules as JSON, e.g. {\"match\":\"all\",\"rules\":[{\"type\":\"subscribed_to_plan\",\"plan_ids\":[1]}]}")]
    public string? EligibilityJson { get; set; }

    // Convenience shortcuts — ignored when the matching raw-JSON flag is supplied.
    [CommandOption("--redeemer-trial-days <DAYS>")]
    [Description("Shortcut: redeemer reward = trial extension of N days")]
    public int? RedeemerTrialDays { get; set; }

    [CommandOption("--discount-percent <PERCENT>")]
    [Description("Shortcut: redeemer reward = N% discount (use --discount-duration for recurrence)")]
    public decimal? DiscountPercent { get; set; }

    [CommandOption("--discount-duration <DURATION>")]
    [Description("Discount duration for --discount-percent: once, forever, repeating (default once)")]
    public string? DiscountDuration { get; set; }

    [CommandOption("--referrer-trial-days <DAYS>")]
    [Description("Shortcut: referrer reward = trial extension of N days")]
    public int? ReferrerTrialDays { get; set; }

    [CommandOption("--referrer-subscription-days <DAYS>")]
    [Description("Shortcut: referrer reward = subscription extension of N days")]
    public int? ReferrerSubscriptionDays { get; set; }

    [CommandOption("--valid-from <DATE>")]
    [Description("Offer valid-from (ISO date/time)")]
    public DateTime? ValidFrom { get; set; }

    [CommandOption("--valid-until <DATE>")]
    [Description("Offer valid-until (ISO date/time)")]
    public DateTime? ValidUntil { get; set; }

    [CommandOption("--total-cap <N>")]
    [Description("Total redemption cap (omit for unlimited)")]
    public int? TotalRedemptionCap { get; set; }

    [CommandOption("--per-user-cap <N>")]
    [Description("Per-user redemption cap (default 1)")]
    public int? PerUserRedemptionCap { get; set; }

    /// <summary>Resolves the redeemer reward to a JSON string (raw flag wins over shortcuts).</summary>
    public string? RedeemerRewardOrNull()
    {
        if (!string.IsNullOrWhiteSpace(RedeemerRewardJson)) return OfferFormat.ValidateJson(RedeemerRewardJson);
        if (RedeemerTrialDays.HasValue) return OfferFormat.Serialize(new OfferRewardDto("trial_extension", Days: RedeemerTrialDays));
        if (DiscountPercent.HasValue)
            return OfferFormat.Serialize(new OfferRewardDto("discount", PercentOff: DiscountPercent, Duration: DiscountDuration ?? "once"));
        return null;
    }

    public string? ReferrerRewardOrNull()
    {
        if (!string.IsNullOrWhiteSpace(ReferrerRewardJson)) return OfferFormat.ValidateJson(ReferrerRewardJson);
        if (ReferrerTrialDays.HasValue) return OfferFormat.Serialize(new OfferRewardDto("trial_extension", Days: ReferrerTrialDays));
        if (ReferrerSubscriptionDays.HasValue) return OfferFormat.Serialize(new OfferRewardDto("subscription_extension", Days: ReferrerSubscriptionDays));
        return null;
    }

    public string? EligibilityOrNull()
        => string.IsNullOrWhiteSpace(EligibilityJson) ? null : OfferFormat.ValidateJson(EligibilityJson);
}

// ── pay offers list / get ──────────────────────────────────────────────────────

public class PayOffersListCommand : BaseCommand<EmptySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EmptySettings settings)
    {
        try
        {
            List<OfferResponse> offers = [];
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching offers...", async _ => offers = await GetClient().GetOffersAsync());

            if (offers.Count == 0)
            {
                Renderer.Info("No offers found. Create one with [bold #F97316]anythink pay offers create[/].");
                return 0;
            }

            Renderer.Header($"Offers ({offers.Count})");
            var table = Renderer.BuildTable("ID", "Name", "Kind", "Code", "Redeemer", "Referrer", "Status");
            foreach (var o in offers)
            {
                Renderer.AddRow(table,
                    o.Id.ToString("N")[..8] + "…",
                    o.Name,
                    o.Kind,
                    o.PrimaryCode?.Slug,
                    OfferFormat.Reward(o.RedeemerRewardJson),
                    OfferFormat.Reward(o.ReferrerRewardJson),
                    OfferFormat.Status(o.Status)
                );
            }
            AnsiConsole.Write(table);
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

public class PayOffersIdSettings : CommandSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("Offer id (guid)")]
    public string Id { get; set; } = string.Empty;
}

public class PayOffersGetCommand : BaseCommand<PayOffersIdSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PayOffersIdSettings settings)
    {
        try
        {
            if (!Guid.TryParse(settings.Id, out var id))
            { Renderer.Warn("Offer id must be a guid."); return 1; }

            OfferResponse? o = null;
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync($"Fetching offer {id}...", async _ => o = await GetClient().GetOfferAsync(id));

            if (o is null) { Renderer.Warn("Not found."); return 1; }

            Renderer.Header($"{o.Name} (#{o.Id.ToString("N")[..8]}…)");
            Renderer.KeyValue("Id", o.Id.ToString());
            Renderer.KeyValue("Kind", o.Kind);
            Renderer.KeyValue("Description", o.Description);
            Renderer.KeyValue("Status", o.Status);
            Renderer.KeyValue("Code", o.PrimaryCode?.Slug);
            Renderer.KeyValue("Redeemer reward", OfferFormat.Reward(o.RedeemerRewardJson));
            Renderer.KeyValue("Referrer reward", OfferFormat.Reward(o.ReferrerRewardJson));
            if (!string.IsNullOrEmpty(o.EligibilityJson)) Renderer.KeyValue("Eligibility", o.EligibilityJson);
            Renderer.KeyValue("Per-user cap", o.PerUserRedemptionCap.ToString());
            if (o.TotalRedemptionCap.HasValue) Renderer.KeyValue("Total cap", o.TotalRedemptionCap.ToString());
            if (o.ValidFrom.HasValue)  Renderer.KeyValue("Valid from", o.ValidFrom.Value.ToString("yyyy-MM-dd"));
            if (o.ValidUntil.HasValue) Renderer.KeyValue("Valid until", o.ValidUntil.Value.ToString("yyyy-MM-dd"));
            if (!string.IsNullOrEmpty(o.StripeCouponId)) Renderer.KeyValue("Stripe coupon", o.StripeCouponId);
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

// ── pay offers create / update ──────────────────────────────────────────────────

public class PayOffersCreateSettings : PayOffersWriteSettings
{
    [CommandOption("--kind <KIND>")]
    [Description("Offer kind: discount, trial_extension, or referral")]
    public string? Kind { get; set; }

    [CommandOption("--status <STATUS>")]
    [Description("Initial status: active (default), paused, or expired")]
    public string Status { get; set; } = "active";
}

public class PayOffersCreateCommand : BaseCommand<PayOffersCreateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PayOffersCreateSettings settings)
    {
        try
        {
            var name = settings.Name ?? AnsiConsole.Ask<string>("Offer name:");
            var kind = settings.Kind ?? AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Offer [#F97316]kind[/]:")
                    .AddChoices("discount", "trial_extension", "referral")
                    .HighlightStyle(new Style(foreground: new Color(196, 69, 54))));

            var redeemer = settings.RedeemerRewardOrNull();
            if (redeemer is null)
            {
                Renderer.Warn("A redeemer reward is required. Provide --redeemer-reward <JSON> " +
                              "or a shortcut (--redeemer-trial-days / --discount-percent).");
                return 1;
            }

            var req = new CreateOfferRequest(
                Name: name,
                Kind: kind,
                RedeemerRewardJson: redeemer,
                Description: settings.Description,
                ReferrerRewardJson: settings.ReferrerRewardOrNull(),
                EligibilityJson: settings.EligibilityOrNull(),
                ValidFrom: settings.ValidFrom,
                ValidUntil: settings.ValidUntil,
                TotalRedemptionCap: settings.TotalRedemptionCap,
                PerUserRedemptionCap: settings.PerUserRedemptionCap ?? 1,
                Status: settings.Status
            );

            OfferResponse? o = null;
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync("Creating offer...", async _ => o = await GetClient().CreateOfferAsync(req));

            if (o is null) { Renderer.Warn("Create returned empty payload."); return 1; }
            Renderer.Success($"Offer '{o.Name}' created (#{o.Id.ToString("N")[..8]}…).");
            Renderer.KeyValue("Kind", o.Kind);
            Renderer.KeyValue("Redeemer reward", OfferFormat.Reward(o.RedeemerRewardJson));
            Renderer.KeyValue("Referrer reward", OfferFormat.Reward(o.ReferrerRewardJson));
            AnsiConsole.MarkupLine("Add a shareable code with [bold #F97316]anythink pay offers add-code " +
                                   $"{o.Id.ToString("N")[..8]}… --slug <SLUG>[/].");
            return 0;
        }
        catch (JsonException jx) { Renderer.Error($"Invalid reward/eligibility JSON: {jx.Message}"); return 1; }
        catch (Exception ex) { if (!PayHelpers.HandleAdminError(ex)) HandleError(ex); return 1; }
    }
}

public class PayOffersUpdateSettings : PayOffersWriteSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("Offer id to update")]
    public string Id { get; set; } = string.Empty;

    [CommandOption("--status <STATUS>")]
    [Description("Set status: active, paused, or expired")]
    public string? Status { get; set; }
}

public class PayOffersUpdateCommand : BaseCommand<PayOffersUpdateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PayOffersUpdateSettings settings)
    {
        try
        {
            if (!Guid.TryParse(settings.Id, out var id))
            { Renderer.Warn("Offer id must be a guid."); return 1; }

            // Patch — only supplied fields are sent (nulls are omitted on the wire).
            var req = new UpdateOfferRequest(
                Name: settings.Name,
                Description: settings.Description,
                RedeemerRewardJson: settings.RedeemerRewardOrNull(),
                ReferrerRewardJson: settings.ReferrerRewardOrNull(),
                EligibilityJson: settings.EligibilityOrNull(),
                ValidFrom: settings.ValidFrom,
                ValidUntil: settings.ValidUntil,
                TotalRedemptionCap: settings.TotalRedemptionCap,
                PerUserRedemptionCap: settings.PerUserRedemptionCap,
                Status: settings.Status
            );

            OfferResponse? o = null;
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync($"Updating offer {id}...", async _ => o = await GetClient().UpdateOfferAsync(id, req));

            if (o is null) { Renderer.Warn("Update returned empty payload."); return 1; }
            Renderer.Success($"Offer '{o.Name}' updated.");
            return 0;
        }
        catch (JsonException jx) { Renderer.Error($"Invalid reward/eligibility JSON: {jx.Message}"); return 1; }
        catch (Exception ex) { if (!PayHelpers.HandleAdminError(ex)) HandleError(ex); return 1; }
    }
}

public abstract class PayOffersStatusCommand(string status) : BaseCommand<PayOffersIdSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PayOffersIdSettings settings)
    {
        try
        {
            if (!Guid.TryParse(settings.Id, out var id))
            { Renderer.Warn("Offer id must be a guid."); return 1; }
            await GetClient().SetOfferStatusAsync(id, status);
            Renderer.Success($"Offer {id.ToString("N")[..8]}… set to {status}.");
            return 0;
        }
        catch (Exception ex) { if (!PayHelpers.HandleAdminError(ex)) HandleError(ex); return 1; }
    }
}

public class PayOffersPauseCommand()    : PayOffersStatusCommand("paused");
public class PayOffersActivateCommand() : PayOffersStatusCommand("active");

// ── pay offers delete ────────────────────────────────────────────────────────

public class PayOffersDeleteSettings : PayOffersIdSettings
{
    [CommandOption("-y|--yes|--force")]
    [Description("Skip confirmation prompt")]
    public bool Yes { get; set; }
}

public class PayOffersDeleteCommand : BaseCommand<PayOffersDeleteSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PayOffersDeleteSettings settings)
    {
        try
        {
            if (!Guid.TryParse(settings.Id, out var id))
            { Renderer.Warn("Offer id must be a guid."); return 1; }

            var client = GetClient();

            var offer = await client.GetOfferAsync(id);
            if (offer is null) { Renderer.Warn("Offer not found."); return 1; }

            if (!settings.Yes)
            {
                AnsiConsole.MarkupLine($"[yellow]About to permanently delete[/] [bold]{Markup.Escape(offer.Name)}[/] [dim]({id})[/].");
                AnsiConsole.MarkupLine("[yellow]This will also delete every shareable code, personal/referral code, and redemption record tied to the offer.[/]");
                AnsiConsole.MarkupLine("[dim]User trial bonuses sourced from this offer are kept but disassociated.[/]");
                AnsiConsole.MarkupLine("[red]This cannot be undone.[/]");
                var confirm = AnsiConsole.Confirm("Continue?", defaultValue: false);
                if (!confirm) { Renderer.Info("Cancelled."); return 0; }
            }

            DeleteOfferResponse? outcome = null;
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync("Deleting offer...", async _ => outcome = await client.DeleteOfferAsync(id));

            if (outcome is null) { Renderer.Warn("Delete returned empty payload."); return 1; }

            Renderer.Success($"Offer '{Markup.Escape(offer.Name)}' deleted.");
            Renderer.KeyValue("Codes deleted", outcome.CodesDeleted.ToString());
            Renderer.KeyValue("Redemptions deleted", outcome.RedemptionsDeleted.ToString());
            Renderer.KeyValue("Trial bonuses preserved", outcome.TrialBonusesPreserved.ToString());
            return 0;
        }
        catch (Exception ex) { if (!PayHelpers.HandleAdminError(ex)) HandleError(ex); return 1; }
    }
}

// ── pay offers codes ────────────────────────────────────────────────────────────

public class PayOffersCodesCommand : BaseCommand<PayOffersIdSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PayOffersIdSettings settings)
    {
        try
        {
            if (!Guid.TryParse(settings.Id, out var id))
            { Renderer.Warn("Offer id must be a guid."); return 1; }

            List<OfferCodeResponse> codes = [];
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching offer codes...", async _ => codes = await GetClient().GetOfferCodesAsync(id));

            if (codes.Count == 0) { Renderer.Info("No codes for this offer. Add one with [bold #F97316]pay offers add-code[/]."); return 0; }

            Renderer.Header($"Codes for offer {id.ToString("N")[..8]}…");
            var table = Renderer.BuildTable("Slug", "Owner", "Redemptions", "Created");
            foreach (var c in codes)
            {
                var owner = c.Owner is not null
                    ? $"{c.Owner.FirstName} {c.Owner.LastName}".Trim()
                    : c.OwnerUserId?.ToString() ?? "shared";
                Renderer.AddRow(table, c.Slug, owner, c.RedemptionCount.ToString(), c.CreatedAt.ToString("yyyy-MM-dd"));
            }
            AnsiConsole.Write(table);
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

public class PayOffersAddCodeSettings : CommandSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("Offer id (guid)")]
    public string Id { get; set; } = string.Empty;

    [CommandOption("--slug <SLUG>")]
    [Description("Code slug (e.g. LAUNCH50). Generated server-side if omitted is not supported — required.")]
    public string? Slug { get; set; }

    [CommandOption("--owner-user-id <USER_ID>")]
    [Description("Attach the code to a user (personal/referral code). Omit for a shared promo code.")]
    public int? OwnerUserId { get; set; }
}

public class PayOffersAddCodeCommand : BaseCommand<PayOffersAddCodeSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PayOffersAddCodeSettings settings)
    {
        try
        {
            if (!Guid.TryParse(settings.Id, out var id))
            { Renderer.Warn("Offer id must be a guid."); return 1; }

            var slug = settings.Slug ?? AnsiConsole.Ask<string>("Code slug:");

            OfferCodeResponse? code = null;
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync("Creating code...", async _ =>
                    code = await GetClient().CreateOfferCodeAsync(id, new CreateOfferCodeRequest(slug, settings.OwnerUserId)));

            Renderer.Success($"Code '{code?.Slug}' created.");
            if (settings.OwnerUserId.HasValue) Renderer.KeyValue("Owner user", settings.OwnerUserId.ToString());
            return 0;
        }
        catch (Exception ex) { if (!PayHelpers.HandleAdminError(ex)) HandleError(ex); return 1; }
    }
}

// ── pay offers redemptions ──────────────────────────────────────────────────────

public class PayOffersRedemptionsSettings : CommandSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("Offer id (guid)")]
    public string Id { get; set; } = string.Empty;

    [CommandOption("--page <PAGE>")]
    [Description("Page number (default: 1)")]
    public int Page { get; set; } = 1;

    [CommandOption("--limit <LIMIT>")]
    [Description("Items per page (default: 50)")]
    public int Limit { get; set; } = 50;
}

public class PayOffersRedemptionsCommand : BaseCommand<PayOffersRedemptionsSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PayOffersRedemptionsSettings settings)
    {
        try
        {
            if (!Guid.TryParse(settings.Id, out var id))
            { Renderer.Warn("Offer id must be a guid."); return 1; }

            List<OfferRedemptionResponse> rows = [];
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching redemptions...", async _ =>
                    rows = await GetClient().GetOfferRedemptionsAsync(id, settings.Page, settings.Limit));

            if (rows.Count == 0) { Renderer.Info("No redemptions found."); return 0; }

            Renderer.Header($"Redemptions for offer {id.ToString("N")[..8]}… (page {settings.Page})");
            var table = Renderer.BuildTable("Redeemer", "Reward status", "Referrer", "Referrer status", "When");
            foreach (var r in rows)
            {
                var redeemer = r.Redeemer is not null
                    ? $"{r.Redeemer.FirstName} {r.Redeemer.LastName}".Trim()
                    : r.RedeemerUserId.ToString();
                var referrer = r.Referrer is not null
                    ? $"{r.Referrer.FirstName} {r.Referrer.LastName}".Trim()
                    : r.ReferrerUserId?.ToString();
                Renderer.AddRow(table,
                    redeemer,
                    r.RedeemerRewardStatus,
                    referrer,
                    r.ReferrerRewardStatus,
                    r.RedeemedAt.ToString("yyyy-MM-dd HH:mm")
                );
            }
            AnsiConsole.Write(table);
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

// ── pay offers user-code (admin lookup) ──────────────────────────────────────────

public class PayOffersUserCodeSettings : CommandSettings
{
    [CommandArgument(0, "<USER_ID>")]
    [Description("Numeric user id")]
    public int UserId { get; set; }
}

public class PayOffersUserCodeCommand : BaseCommand<PayOffersUserCodeSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PayOffersUserCodeSettings settings)
    {
        try
        {
            PersonalCodeResponse? code = null;
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync($"Fetching code for user {settings.UserId}...", async _ =>
                    code = await GetClient().GetUserCodeAsync(settings.UserId));

            if (code is null) { Renderer.Info("No personal code for that user."); return 0; }

            Renderer.Header($"Personal code for user {settings.UserId}");
            Renderer.KeyValue("Slug", code.Slug);
            Renderer.KeyValue("Offer", code.OfferName);
            Renderer.KeyValue("Redemptions", code.RedemptionCount.ToString());
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}
