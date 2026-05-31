using AnythinkCli.Client;
using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace AnythinkCli.Commands;

// ── pay status ────────────────────────────────────────────────────────────────

public class PayStatusCommand : BaseCommand<EmptySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EmptySettings settings)
    {
        try
        {
            var client = GetClient();
            StripeConnectStatus? status = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching Stripe Connect status...", async _ =>
                {
                    status = await client.GetStripeConnectAsync();
                });

            if (status == null)
            {
                Renderer.Info("No Stripe Connect account found.");
                AnsiConsole.MarkupLine("Run [bold #F97316]anythink pay connect[/] to set up Stripe Connect.");
                return 0;
            }

            Renderer.Header("Stripe Connect Status");

            if (string.IsNullOrEmpty(status.StripeAccountId))
                AnsiConsole.MarkupLine($"  [dim]Account ID:[/] [dim]—[/]");
            else
                Renderer.KeyValue("Account ID", status.StripeAccountId);

            Renderer.KeyValue("Onboarding complete", status.OnboardingCompleted ? "yes" : "no");
            Renderer.KeyValue("Charges enabled", status.ChargesEnabled ? "yes" : "no");
            Renderer.KeyValue("Payouts enabled", status.PayoutsEnabled ? "yes" : "no");
            Renderer.KeyValue("Details submitted", status.DetailsSubmitted ? "yes" : "no");

            if (!status.OnboardingCompleted)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("Run [bold #F97316]anythink pay connect[/] to set up Stripe Connect.");
            }

            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── pay connect ───────────────────────────────────────────────────────────────

public class PayConnectCommand : BaseCommand<EmptySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EmptySettings settings)
    {
        try
        {
            var client = GetClient();

            var businessType = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select [#F97316]business type[/]:")
                    .AddChoices("individual", "company")
                    .HighlightStyle(new Style(foreground: new Color(196, 69, 54)))
            );

            var country = AnsiConsole.Ask<string>("Country code (e.g. [dim]GB[/]):", "GB");
            var email   = AnsiConsole.Ask<string>("Email address:");

            StripeConnectStatus? connectStatus = null;
            OnboardingLinkResponse? link = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Setting up Stripe Connect...", async _ =>
                {
                    connectStatus = await client.CreateStripeConnectAsync(new CreateStripeConnectRequest(
                        businessType,
                        country,
                        email
                    ));
                    link = await client.CreateOnboardingLinkAsync(
                        "https://anythink.cloud/connect/refresh",
                        "https://anythink.cloud/connect/return"
                    );
                });

            Renderer.Success("Stripe Connect account created.");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("Opening onboarding in your browser...");
            AnsiConsole.MarkupLine($"  [dim]URL:[/] [#F97316]{Markup.Escape(link!.Url)}[/]");

            try
            {
                Process.Start(new ProcessStartInfo { FileName = link.Url, UseShellExecute = true });
            }
            catch
            {
                Renderer.Warn("Could not open browser automatically. Copy the URL above to continue.");
            }

            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── pay payments ──────────────────────────────────────────────────────────────

public class PayPaymentsSettings : CommandSettings
{
    [CommandOption("--page <PAGE>")]
    [Description("Page number (default: 1)")]
    public int Page { get; set; } = 1;

    [CommandOption("--limit <LIMIT>")]
    [Description("Items per page (default: 25)")]
    public int Limit { get; set; } = 25;
}

public class PayPaymentsCommand : BaseCommand<PayPaymentsSettings>
{
    private static string FormatAmount(decimal amount, string currency)
    {
        return currency.ToLower() switch
        {
            "gbp" => $"£{amount:0.00}",
            "usd" => $"${amount:0.00}",
            "eur" => $"€{amount:0.00}",
            _     => $"{amount:0.00} {currency.ToUpper()}"
        };
    }

    public override async Task<int> ExecuteAsync(CommandContext context, PayPaymentsSettings settings)
    {
        try
        {
            var client = GetClient();
            List<PaymentResponse> payments = [];

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching payments...", async _ =>
                {
                    payments = await client.GetPaymentsAsync(settings.Page, settings.Limit);
                });

            if (payments.Count == 0)
            {
                Renderer.Info("No payments found.");
                return 0;
            }

            Renderer.Header($"Payments (page {settings.Page})");

            var table = Renderer.BuildTable("ID", "Amount", "Status", "Description", "Date");
            foreach (var p in payments)
            {
                var shortId = p.Id.Length > 8 ? p.Id[..8] + "…" : p.Id;
                Renderer.AddRow(table,
                    shortId,
                    FormatAmount(p.Amount, p.Currency),
                    p.Status,
                    p.Description,
                    p.CreatedAt.ToString("yyyy-MM-dd HH:mm")
                );
            }

            AnsiConsole.Write(table);
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── pay methods ───────────────────────────────────────────────────────────────

public class PayMethodsCommand : BaseCommand<EmptySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EmptySettings settings)
    {
        try
        {
            var client = GetClient();
            List<PaymentMethodResponse> methods = [];

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching payment methods...", async _ =>
                {
                    methods = await client.GetPaymentMethodsAsync();
                });

            if (methods.Count == 0)
            {
                Renderer.Info("No payment methods found.");
                return 0;
            }

            Renderer.Header($"Payment Methods ({methods.Count})");

            var table = Renderer.BuildTable("ID", "Type", "Brand", "Last4", "Expires");
            foreach (var m in methods)
            {
                var shortId = m.Id.Length > 8 ? m.Id[..8] + "…" : m.Id;
                var expires = (m.ExpMonth.HasValue && m.ExpYear.HasValue)
                    ? $"{m.ExpMonth:00}/{m.ExpYear}"
                    : null;
                Renderer.AddRow(table,
                    shortId,
                    m.Type,
                    m.Brand,
                    m.Last4,
                    expires
                );
            }

            AnsiConsole.Write(table);
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── pay › money formatting helper ─────────────────────────────────────────────

internal static class PayFormat
{
    public static string Money(decimal amount, string currency) => currency.ToLower() switch
    {
        "gbp" => $"£{amount:0.00}",
        "usd" => $"${amount:0.00}",
        "eur" => $"€{amount:0.00}",
        _     => $"{amount:0.00} {currency.ToUpper()}"
    };

    public static string Interval(string interval, int count) =>
        count == 1 ? $"/{interval}" : $"/{count} {interval}s";
}

// ── pay plans ────────────────────────────────────────────────────────────────

public class PayPlansListCommand : BaseCommand<EmptySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EmptySettings settings)
    {
        try
        {
            var client = GetClient();
            List<SubscriptionPlanResponse> plans = [];
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching subscription plans...", async _ =>
                    plans = await client.GetSubscriptionPlansAsync());

            if (plans.Count == 0)
            {
                Renderer.Info("No subscription plans found. Create one with [bold #F97316]anythink pay plans create[/].");
                return 0;
            }

            Renderer.Header($"Subscription Plans ({plans.Count})");
            var table = Renderer.BuildTable("ID", "Plan name", "Name", "Price", "Trial", "Apple", "Active");
            foreach (var p in plans)
            {
                Renderer.AddRow(table,
                    p.Id.ToString(),
                    p.PlanName,
                    p.Name,
                    PayFormat.Money(p.Amount, p.Currency) + PayFormat.Interval(p.BillingInterval, p.IntervalCount),
                    p.TrialPeriodDays.HasValue ? $"{p.TrialPeriodDays}d" : "—",
                    p.AppleProductId ?? "—",
                    p.IsActive ? "[green]yes[/]" : "[dim]no[/]"
                );
            }
            AnsiConsole.Write(table);
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

public class PayPlansGetSettings : CommandSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("Plan id (integer)")]
    public int Id { get; set; }
}

public class PayPlansGetCommand : BaseCommand<PayPlansGetSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PayPlansGetSettings settings)
    {
        try
        {
            var client = GetClient();
            SubscriptionPlanResponse? plan = null;
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync($"Fetching plan {settings.Id}...", async _ =>
                    plan = await client.GetSubscriptionPlanAsync(settings.Id));

            if (plan is null) { Renderer.Warn($"Plan {settings.Id} not found."); return 1; }

            Renderer.Header($"{plan.Name} (#{plan.Id})");
            Renderer.KeyValue("Plan name", plan.PlanName);
            Renderer.KeyValue("Description", plan.Description);
            Renderer.KeyValue("Type", plan.Type);
            Renderer.KeyValue("Price", PayFormat.Money(plan.Amount, plan.Currency) + PayFormat.Interval(plan.BillingInterval, plan.IntervalCount));
            Renderer.KeyValue("Trial", plan.TrialPeriodDays.HasValue ? $"{plan.TrialPeriodDays} days" : "none");
            Renderer.KeyValue("Product name", plan.ProductName);
            Renderer.KeyValue("Product description", plan.ProductDescription);
            Renderer.KeyValue("Reference", plan.Reference);
            Renderer.KeyValue("Apple product id", plan.AppleProductId);
            Renderer.KeyValue("Apple subscription group", plan.AppleSubscriptionGroupId);
            Renderer.KeyValue("Active", plan.IsActive ? "yes" : "no");
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

public class PayPlansCreateSettings : CommandSettings
{
    [CommandOption("--plan-name <PLAN_NAME>")]
    [Description("Internal plan identifier (e.g. \"trial\", \"monthly\", \"annual\")")]
    public string? PlanName { get; set; }

    [CommandOption("--name <NAME>")]
    [Description("Display name shown to subscribers")]
    public string? Name { get; set; }

    [CommandOption("--description <DESC>")]
    [Description("Description")]
    public string? Description { get; set; }

    [CommandOption("--amount <AMOUNT>")]
    [Description("Recurring amount (decimal, in the plan's currency)")]
    public decimal? Amount { get; set; }

    [CommandOption("--currency <CURRENCY>")]
    [Description("Currency code (default: gbp)")]
    public string Currency { get; set; } = "gbp";

    [CommandOption("--interval <INTERVAL>")]
    [Description("Billing interval: day, week, month, year (default: month)")]
    public string BillingInterval { get; set; } = "month";

    [CommandOption("--interval-count <COUNT>")]
    [Description("Number of intervals per billing cycle (default: 1)")]
    public int IntervalCount { get; set; } = 1;

    [CommandOption("--trial-days <DAYS>")]
    [Description("Trial period length in days (optional)")]
    public int? TrialPeriodDays { get; set; }

    [CommandOption("--type <TYPE>")]
    [Description("Plan type (default: web)")]
    public string Type { get; set; } = "web";

    [CommandOption("--product-name <NAME>")]
    [Description("Stripe product display name (optional, defaults to Name)")]
    public string? ProductName { get; set; }

    [CommandOption("--product-description <DESC>")]
    [Description("Stripe product description (optional)")]
    public string? ProductDescription { get; set; }

    [CommandOption("--reference <REF>")]
    [Description("Internal reference / external id (optional)")]
    public string? Reference { get; set; }

    [CommandOption("--apple-product-id <ID>")]
    [Description("Apple App Store product id for this plan (optional)")]
    public string? AppleProductId { get; set; }

    [CommandOption("--apple-subscription-group-id <ID>")]
    [Description("Apple subscription group id (optional; auto-filled on first verify)")]
    public string? AppleSubscriptionGroupId { get; set; }

    [CommandOption("--inactive")]
    [Description("Create the plan as inactive (hidden from picker)")]
    public bool Inactive { get; set; }
}

public class PayPlansCreateCommand : BaseCommand<PayPlansCreateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PayPlansCreateSettings settings)
    {
        try
        {
            var planName = settings.PlanName    ?? AnsiConsole.Ask<string>("Plan name (internal id):");
            var name     = settings.Name        ?? AnsiConsole.Ask<string>("Display name:");
            var desc     = settings.Description  ?? AnsiConsole.Ask<string>("Description:");
            var amount   = settings.Amount       ?? AnsiConsole.Ask<decimal>($"Amount ({settings.Currency}):");

            var req = new CreateSubscriptionPlanRequest(
                PlanName: planName,
                Name: name,
                Description: desc,
                Type: settings.Type,
                Amount: amount,
                Currency: settings.Currency,
                BillingInterval: settings.BillingInterval,
                IntervalCount: settings.IntervalCount,
                TrialPeriodDays: settings.TrialPeriodDays,
                ProductName: settings.ProductName,
                ProductDescription: settings.ProductDescription,
                Reference: settings.Reference,
                IsActive: !settings.Inactive,
                AppleProductId: settings.AppleProductId,
                AppleSubscriptionGroupId: settings.AppleSubscriptionGroupId
            );

            SubscriptionPlanResponse? plan = null;
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync("Creating plan...", async _ =>
                    plan = await GetClient().CreateSubscriptionPlanAsync(req));

            if (plan is null) { Renderer.Warn("Create returned empty payload."); return 1; }
            Renderer.Success($"Plan #{plan.Id} created.");
            Renderer.KeyValue("Plan name", plan.PlanName);
            Renderer.KeyValue("Price", PayFormat.Money(plan.Amount, plan.Currency) + PayFormat.Interval(plan.BillingInterval, plan.IntervalCount));
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

public class PayPlansUpdateSettings : PayPlansCreateSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("Plan id to update")]
    public int Id { get; set; }
}

public class PayPlansUpdateCommand : BaseCommand<PayPlansUpdateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PayPlansUpdateSettings settings)
    {
        try
        {
            var client = GetClient();
            var existing = await client.GetSubscriptionPlanAsync(settings.Id);
            if (existing is null) { Renderer.Warn($"Plan {settings.Id} not found."); return 1; }

            var req = new UpdateSubscriptionPlanRequest(
                PlanName: settings.PlanName      ?? existing.PlanName,
                Name: settings.Name              ?? existing.Name,
                Description: settings.Description ?? existing.Description,
                Type: settings.Type              ,
                Amount: settings.Amount          ?? existing.Amount,
                Currency: settings.Currency      ,
                BillingInterval: settings.BillingInterval,
                IntervalCount: settings.IntervalCount,
                TrialPeriodDays: settings.TrialPeriodDays ?? existing.TrialPeriodDays,
                ProductName: settings.ProductName ?? existing.ProductName,
                ProductDescription: settings.ProductDescription ?? existing.ProductDescription,
                Reference: settings.Reference     ?? existing.Reference,
                IsActive: !settings.Inactive,
                AppleProductId: settings.AppleProductId ?? existing.AppleProductId,
                AppleSubscriptionGroupId: settings.AppleSubscriptionGroupId ?? existing.AppleSubscriptionGroupId
            );

            SubscriptionPlanResponse? plan = null;
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync($"Updating plan {settings.Id}...", async _ =>
                    plan = await client.UpdateSubscriptionPlanAsync(settings.Id, req));

            if (plan is null) { Renderer.Warn("Update returned empty payload."); return 1; }
            Renderer.Success($"Plan #{plan.Id} updated.");
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

public class PayPlansDeleteSettings : CommandSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("Plan id to delete")]
    public int Id { get; set; }

    [CommandOption("--yes")]
    [Description("Skip confirmation prompt")]
    public bool SkipConfirm { get; set; }
}

public class PayPlansDeleteCommand : BaseCommand<PayPlansDeleteSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PayPlansDeleteSettings settings)
    {
        try
        {
            if (!settings.SkipConfirm &&
                !AnsiConsole.Confirm($"Delete plan #{settings.Id}? This cannot be undone."))
            {
                Renderer.Info("Cancelled.");
                return 0;
            }
            await GetClient().DeleteSubscriptionPlanAsync(settings.Id);
            Renderer.Success($"Plan #{settings.Id} deleted.");
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

// ── pay subscriptions ─────────────────────────────────────────────────────────

public class PaySubscriptionsListSettings : CommandSettings
{
    [CommandOption("--page <PAGE>")]
    [Description("Page number (default: 1)")]
    public int Page { get; set; } = 1;

    [CommandOption("--limit <LIMIT>")]
    [Description("Items per page (default: 25)")]
    public int Limit { get; set; } = 25;

    [CommandOption("--status <STATUS>")]
    [Description("Filter by status (e.g. active, trialing, cancelled)")]
    public string? Status { get; set; }
}

public class PaySubscriptionsListCommand : BaseCommand<PaySubscriptionsListSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PaySubscriptionsListSettings settings)
    {
        try
        {
            var client = GetClient();
            List<SubscriptionResponse> subs = [];
            int? total = null;
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching subscriptions...", async _ =>
                {
                    if (settings.Status is not null)
                    {
                        var r = await client.GetSubscriptionsByStatusAsync(settings.Status);
                        subs = r.Items; total = r.TotalCount;
                    }
                    else
                    {
                        var r = await client.GetSubscriptionsAsync(settings.Page, settings.Limit);
                        subs = r.Items; total = r.TotalCount;
                    }
                });

            if (subs.Count == 0) { Renderer.Info("No subscriptions found."); return 0; }

            Renderer.Header($"Subscriptions (page {settings.Page}{(total.HasValue ? $" of {total}" : "")})");
            var table = Renderer.BuildTable("ID", "Name", "Status", "Amount", "Customer", "Created");
            foreach (var s in subs)
            {
                Renderer.AddRow(table,
                    s.Id.ToString("N")[..8] + "…",
                    s.Name,
                    s.Status,
                    PayFormat.Money(s.Amount, s.Currency) + PayFormat.Interval(s.BillingInterval, s.IntervalCount),
                    s.CustomerEmail ?? "—",
                    s.CreatedAt.ToString("yyyy-MM-dd")
                );
            }
            AnsiConsole.Write(table);
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

public class PaySubscriptionsIdSettings : CommandSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("Subscription id (guid)")]
    public string Id { get; set; } = string.Empty;
}

public class PaySubscriptionsGetCommand : BaseCommand<PaySubscriptionsIdSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PaySubscriptionsIdSettings settings)
    {
        try
        {
            if (!Guid.TryParse(settings.Id, out var id))
            { Renderer.Warn("Subscription id must be a guid."); return 1; }

            SubscriptionResponse? s = null;
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync($"Fetching subscription {id}...", async _ =>
                    s = await GetClient().GetSubscriptionAsync(id));

            if (s is null) { Renderer.Warn("Not found."); return 1; }

            Renderer.Header(s.Name);
            Renderer.KeyValue("Id", s.Id.ToString());
            Renderer.KeyValue("Status", s.Status);
            Renderer.KeyValue("Amount", PayFormat.Money(s.Amount, s.Currency) + PayFormat.Interval(s.BillingInterval, s.IntervalCount));
            Renderer.KeyValue("Customer", s.CustomerEmail);
            Renderer.KeyValue("Customer name", s.CustomerName);
            Renderer.KeyValue("Current period", $"{s.CurrentPeriodStartsAt:yyyy-MM-dd} → {s.CurrentPeriodEndsAt:yyyy-MM-dd}");
            if (s.TrialEndsAt.HasValue) Renderer.KeyValue("Trial ends", s.TrialEndsAt.Value.ToString("yyyy-MM-dd"));
            if (s.CancelledAt.HasValue) Renderer.KeyValue("Cancelled", s.CancelledAt.Value.ToString("yyyy-MM-dd HH:mm"));
            if (s.AutoCancelAt.HasValue) Renderer.KeyValue("Auto-cancel", s.AutoCancelAt.Value.ToString("yyyy-MM-dd"));
            Renderer.KeyValue("Reference", s.Reference);
            if (!string.IsNullOrEmpty(s.CheckoutUrl)) Renderer.KeyValue("Checkout URL", s.CheckoutUrl);
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

public class PaySubscriptionsCancelCommand : BaseCommand<PaySubscriptionsIdSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PaySubscriptionsIdSettings settings)
    {
        try
        {
            if (!Guid.TryParse(settings.Id, out var id))
            { Renderer.Warn("Subscription id must be a guid."); return 1; }

            await GetClient().CancelSubscriptionAsync(id);
            Renderer.Success($"Subscription {id} cancelled.");
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

public class PaySubscriptionsResumeCommand : BaseCommand<PaySubscriptionsIdSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PaySubscriptionsIdSettings settings)
    {
        try
        {
            if (!Guid.TryParse(settings.Id, out var id))
            { Renderer.Warn("Subscription id must be a guid."); return 1; }

            await GetClient().ResumeSubscriptionAsync(id);
            Renderer.Success($"Subscription {id} resumed.");
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

public class PaySubscriptionsByUserSettings : CommandSettings
{
    [CommandArgument(0, "<USER_ID>")]
    [Description("Numeric user id")]
    public int UserId { get; set; }
}

public class PaySubscriptionsByUserCommand : BaseCommand<PaySubscriptionsByUserSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PaySubscriptionsByUserSettings settings)
    {
        try
        {
            List<SubscriptionResponse> subs = [];
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync($"Fetching subscriptions for user {settings.UserId}...", async _ =>
                    subs = await GetClient().GetSubscriptionsByUserAsync(settings.UserId));

            if (subs.Count == 0) { Renderer.Info("No subscriptions for that user."); return 0; }

            Renderer.Header($"Subscriptions for user {settings.UserId}");
            var table = Renderer.BuildTable("ID", "Name", "Status", "Amount", "Created");
            foreach (var s in subs)
            {
                Renderer.AddRow(table,
                    s.Id.ToString("N")[..8] + "…",
                    s.Name,
                    s.Status,
                    PayFormat.Money(s.Amount, s.Currency) + PayFormat.Interval(s.BillingInterval, s.IntervalCount),
                    s.CreatedAt.ToString("yyyy-MM-dd")
                );
            }
            AnsiConsole.Write(table);
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

public class PaySubscriptionsCheckAccessSettings : CommandSettings
{
    [CommandArgument(0, "<NAME>")]
    [Description("Subscription name to check")]
    public string Name { get; set; } = string.Empty;

    [CommandOption("--status <STATUS>")]
    [Description("Require a specific status (optional)")]
    public string? Status { get; set; }
}

public class PaySubscriptionsCheckAccessCommand : BaseCommand<PaySubscriptionsCheckAccessSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PaySubscriptionsCheckAccessSettings settings)
    {
        try
        {
            CheckSubscriptionAccessResponse? r = null;
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync("Checking access...", async _ =>
                    r = await GetClient().CheckSubscriptionAccessAsync(settings.Name, settings.Status));

            if (r is null || !r.HasAccess)
            { Renderer.Info("No access."); return 0; }

            Renderer.Success("Has access.");
            if (r.Subscription is not null)
            {
                Renderer.KeyValue("Id", r.Subscription.Id.ToString());
                Renderer.KeyValue("Name", r.Subscription.Name);
                Renderer.KeyValue("Status", r.Subscription.Status);
            }
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

// ── pay subscriptions users ───────────────────────────────────────────────────

public class PaySubscriptionsUsersListCommand : BaseCommand<PaySubscriptionsIdSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PaySubscriptionsIdSettings settings)
    {
        try
        {
            if (!Guid.TryParse(settings.Id, out var id))
            { Renderer.Warn("Subscription id must be a guid."); return 1; }

            List<SubscriptionUserResponse> users = [];
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching subscription users...", async _ =>
                    users = await GetClient().GetSubscriptionUsersAsync(id));

            if (users.Count == 0) { Renderer.Info("No users on this subscription."); return 0; }

            Renderer.Header($"Users on subscription {id.ToString("N")[..8]}…");
            var table = Renderer.BuildTable("User id", "Read-only");
            foreach (var u in users)
                Renderer.AddRow(table, u.UserId.ToString(), u.Readonly ? "[yellow]yes[/]" : "no");
            AnsiConsole.Write(table);
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

public class PaySubscriptionsUsersSetSettings : CommandSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("Subscription id (guid)")]
    public string Id { get; set; } = string.Empty;

    [CommandArgument(1, "<USER_ID>")]
    [Description("User id to grant access to")]
    public int UserId { get; set; }

    [CommandOption("--readonly")]
    [Description("Grant read-only access (default: full)")]
    public bool ReadOnly { get; set; }
}

public class PaySubscriptionsUsersSetCommand : BaseCommand<PaySubscriptionsUsersSetSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PaySubscriptionsUsersSetSettings settings)
    {
        try
        {
            if (!Guid.TryParse(settings.Id, out var id))
            { Renderer.Warn("Subscription id must be a guid."); return 1; }

            await GetClient().SetSubscriptionUserAsync(id, settings.UserId, settings.ReadOnly);
            Renderer.Success($"User {settings.UserId} granted {(settings.ReadOnly ? "read-only" : "full")} access to subscription {id.ToString("N")[..8]}….");
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

public class PaySubscriptionsUsersRemoveSettings : CommandSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("Subscription id (guid)")]
    public string Id { get; set; } = string.Empty;

    [CommandArgument(1, "<USER_ID>")]
    [Description("User id to remove access from")]
    public int UserId { get; set; }
}

public class PaySubscriptionsUsersRemoveCommand : BaseCommand<PaySubscriptionsUsersRemoveSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PaySubscriptionsUsersRemoveSettings settings)
    {
        try
        {
            if (!Guid.TryParse(settings.Id, out var id))
            { Renderer.Warn("Subscription id must be a guid."); return 1; }

            await GetClient().DeleteSubscriptionUserAsync(id, settings.UserId);
            Renderer.Success($"User {settings.UserId} removed from subscription {id.ToString("N")[..8]}….");
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

// ── pay › shared helpers ───────────────────────────────────────────────────────

internal static class PayHelpers
{
    /// <summary>
    /// Some pay operations are tenant-administrator only. Turn the API 403 into a
    /// plain instruction instead of a raw status dump. Returns true if it handled it.
    /// </summary>
    public static bool HandleAdminError(Exception ex)
    {
        if (ex is AnythinkException { StatusCode: 403 })
        {
            Renderer.Error("This action requires tenant administrator access.");
            return true;
        }
        return false;
    }

    /// <summary>Masks all but the first and last two characters of an identifier.</summary>
    public static string? Mask(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (value.Length <= 6) return new string('•', value.Length);
        return $"{value[..2]}{new string('•', value.Length - 4)}{value[^2..]}";
    }
}

// ── pay trial ──────────────────────────────────────────────────────────────────
// The "give new users a free trial of app access without a subscription" toggle.

public class PayTrialStatusCommand : BaseCommand<EmptySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EmptySettings settings)
    {
        try
        {
            var client = GetClient();
            TenantResponse? tenant = null;
            List<SubscriptionPlanResponse> plans = [];
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching app trial status...", async _ =>
                {
                    tenant = await client.GetTenantAsync();
                    plans  = await client.GetSubscriptionPlansAsync();
                });

            var enabled = tenant?.TenantSettings?.AppEngagementTrialEnabled ?? false;
            var source  = plans
                .Where(p => p.IsActive && p.TrialPeriodDays is > 0)
                .OrderByDescending(p => p.TrialPeriodDays)
                .FirstOrDefault();

            Renderer.Header("App Engagement Trial");
            Renderer.KeyValue("Enabled", enabled ? "yes" : "no");
            if (source is not null)
            {
                Renderer.KeyValue("Trial length", $"{source.TrialPeriodDays} days");
                Renderer.KeyValue("Derived from plan", $"{source.Name} (#{source.Id})");
            }
            else
            {
                Renderer.KeyValue("Trial length", "none (no active plan offers a trial)");
            }
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

public abstract class PayTrialToggleCommand(bool enable) : BaseCommand<EmptySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EmptySettings settings)
    {
        try
        {
            var client = GetClient();
            var tenant = await client.GetTenantAsync();
            if (tenant is null) { Renderer.Warn("Could not load organisation settings."); return 1; }

            var current = tenant.TenantSettings;
            var updated = current is null
                ? new TenantSettingsDto(false, null, [], null, null, enable)
                : current with { AppEngagementTrialEnabled = enable };

            await client.UpdateTenantAsync(new UpdateTenantRequest(
                Name: tenant.Name,
                Description: tenant.Description,
                GoogleMapsKey: tenant.GoogleMapsKey,
                LogoSquareId: tenant.LogoSquare?.Id,
                LogoStandardId: tenant.LogoStandard?.Id,
                TenantSettings: updated,
                ThemeSettings: tenant.ThemeSettings
            ));

            Renderer.Success($"App engagement trial {(enable ? "enabled" : "disabled")}.");
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

public class PayTrialEnableCommand()  : PayTrialToggleCommand(true);
public class PayTrialDisableCommand() : PayTrialToggleCommand(false);

// ── pay apple credentials ───────────────────────────────────────────────────────

public class PayAppleCredentialsSetSettings : CommandSettings
{
    [CommandOption("--issuer-id <ISSUER_ID>")]
    [Description("App Store Connect API issuer id (UUID)")]
    public string? IssuerId { get; set; }

    [CommandOption("--key-id <KEY_ID>")]
    [Description("App Store Connect API key id")]
    public string? KeyId { get; set; }

    [CommandOption("--bundle-id <BUNDLE_ID>")]
    [Description("App bundle id (e.g. com.example.app)")]
    public string? BundleId { get; set; }

    [CommandOption("--private-key-file <PATH>")]
    [Description("Path to the App Store Connect API private key (.p8)")]
    public string? PrivateKeyFile { get; set; }

    [CommandOption("--environment <ENV>")]
    [Description("Apple environment: sandbox or production (optional)")]
    public string? Environment { get; set; }
}

public class PayAppleCredentialsSetCommand : BaseCommand<PayAppleCredentialsSetSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PayAppleCredentialsSetSettings settings)
    {
        try
        {
            var issuerId = settings.IssuerId ?? AnsiConsole.Ask<string>("Issuer id:");
            var keyId    = settings.KeyId    ?? AnsiConsole.Ask<string>("Key id:");
            var bundleId = settings.BundleId ?? AnsiConsole.Ask<string>("Bundle id:");
            var keyFile  = settings.PrivateKeyFile ?? AnsiConsole.Ask<string>("Private key (.p8) file path:");

            if (!File.Exists(keyFile))
            { Renderer.Warn($"Private key file not found: {keyFile}"); return 1; }

            if (settings.Environment is not (null or "sandbox" or "production"))
            { Renderer.Warn("Environment must be 'sandbox' or 'production'."); return 1; }

            var pem = await File.ReadAllTextAsync(keyFile);

            var req = new UpdateAppleIapCredentialsRequest(
                BundleId: bundleId,
                Environment: settings.Environment,
                AscIssuerId: issuerId,
                AscKeyId: keyId,
                AscPrivateKeyPem: pem
            );

            AppleIapCredentialsResponse? result = null;
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync("Saving Apple IAP credentials...", async _ =>
                    result = await GetClient().UpdateAppleIapCredentialsAsync(req));

            Renderer.Success("Apple IAP credentials saved.");
            if (result is not null)
            {
                Renderer.KeyValue("Bundle id", result.BundleId);
                Renderer.KeyValue("Configured", result.IsConfigured ? "yes" : "no");
                if (!string.IsNullOrEmpty(result.NotificationUrl))
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine("Register this URL in App Store Connect (App Store Server Notifications V2):");
                    AnsiConsole.MarkupLine($"  [#F97316]{Markup.Escape(result.NotificationUrl)}[/]");
                }
            }
            return 0;
        }
        catch (Exception ex)
        {
            if (!PayHelpers.HandleAdminError(ex)) HandleError(ex);
            return 1;
        }
    }
}

public class PayAppleCredentialsShowCommand : BaseCommand<EmptySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EmptySettings settings)
    {
        try
        {
            AppleIapCredentialsResponse? creds = null;
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching Apple IAP credentials...", async _ =>
                    creds = await GetClient().GetAppleIapCredentialsAsync());

            if (creds is null) { Renderer.Info("Apple IAP is not configured."); return 0; }

            Renderer.Header("Apple IAP Credentials");
            Renderer.KeyValue("Bundle id", creds.BundleId);
            Renderer.KeyValue("Environment", creds.Environment);
            Renderer.KeyValue("Issuer id", PayHelpers.Mask(creds.AscIssuerId));
            Renderer.KeyValue("Key id", PayHelpers.Mask(creds.AscKeyId));
            Renderer.KeyValue("Private key stored", creds.HasPrivateKey ? "yes" : "no");
            Renderer.KeyValue("Configured", creds.IsConfigured ? "yes" : "no");
            Renderer.KeyValue("Notification URL", creds.NotificationUrl);
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

public class PayAppleCredentialsNotificationUrlCommand : BaseCommand<EmptySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EmptySettings settings)
    {
        try
        {
            AppleIapCredentialsResponse? creds = null;
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching notification URL...", async _ =>
                    creds = await GetClient().GetAppleIapCredentialsAsync());

            if (creds?.NotificationUrl is null or "")
            { Renderer.Info("No notification URL available."); return 0; }

            AnsiConsole.MarkupLine("Register this URL in App Store Connect (App Store Server Notifications V2,");
            AnsiConsole.MarkupLine("for both production and sandbox):");
            AnsiConsole.MarkupLine($"  [#F97316]{Markup.Escape(creds.NotificationUrl)}[/]");
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

// ── pay apple verify ────────────────────────────────────────────────────────────

public class PayAppleVerifySettings : CommandSettings
{
    [CommandOption("--signed-transaction <JWS>")]
    [Description("Signed transaction JWS (StoreKit 2)")]
    public string? SignedTransaction { get; set; }

    [CommandOption("--original-transaction-id <ID>")]
    [Description("Original transaction id (restore flow)")]
    public string? OriginalTransactionId { get; set; }
}

public class PayAppleVerifyCommand : BaseCommand<PayAppleVerifySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PayAppleVerifySettings settings)
    {
        try
        {
            if (string.IsNullOrEmpty(settings.SignedTransaction) &&
                string.IsNullOrEmpty(settings.OriginalTransactionId))
            {
                Renderer.Warn("Provide --signed-transaction or --original-transaction-id.");
                return 1;
            }

            AppleVerifyResponse? result = null;
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync("Verifying Apple transaction...", async _ =>
                    result = await GetClient().VerifyAppleTransactionAsync(
                        new AppleVerifyRequest(settings.SignedTransaction, settings.OriginalTransactionId)));

            if (result is null) { Renderer.Warn("Verify returned empty payload."); return 1; }

            Renderer.Success("Transaction verified.");
            Renderer.KeyValue("Subscription id", result.SubscriptionId.ToString());
            Renderer.KeyValue("Status", result.Status);
            Renderer.KeyValue("Product id", result.ProductId);
            Renderer.KeyValue("Plan", result.PlanName);
            Renderer.KeyValue("Has access", result.HasAccess ? "yes" : "no");
            Renderer.KeyValue("Matched known plan", result.MatchedKnownPlan ? "yes" : "no");
            if (result.ExpiresAt.HasValue)
                Renderer.KeyValue("Expires", result.ExpiresAt.Value.ToString("yyyy-MM-dd HH:mm"));
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

// ── pay subscriptions events ────────────────────────────────────────────────────

public class PaySubscriptionsEventsCommand : BaseCommand<PaySubscriptionsIdSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PaySubscriptionsIdSettings settings)
    {
        try
        {
            if (!Guid.TryParse(settings.Id, out var id))
            { Renderer.Warn("Subscription id must be a guid."); return 1; }

            List<SubscriptionEventResponse> events = [];
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching subscription history...", async _ =>
                    events = await GetClient().GetSubscriptionEventsAsync(id));

            if (events.Count == 0) { Renderer.Info("No events for this subscription."); return 0; }

            Renderer.Header($"History for subscription {id.ToString("N")[..8]}…");
            var table = Renderer.BuildTable("When", "Event", "Source", "Status", "Summary");
            foreach (var e in events)
            {
                var status = string.IsNullOrEmpty(e.PriorStatus) && string.IsNullOrEmpty(e.NewStatus)
                    ? "—"
                    : $"{e.PriorStatus ?? "—"} → {e.NewStatus ?? "—"}";
                Renderer.AddRow(table,
                    e.OccurredAt.ToString("yyyy-MM-dd HH:mm"),
                    e.EventType,
                    e.Source,
                    status,
                    e.Summary
                );
            }
            AnsiConsole.Write(table);
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

// ── pay subscriptions admin recovery (tenant administrators) ─────────────────────

public class PaySubscriptionsAdminIdSettings : CommandSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("Subscription id (guid)")]
    public string Id { get; set; } = string.Empty;

    [CommandOption("--yes")]
    [Description("Skip confirmation prompt")]
    public bool SkipConfirm { get; set; }
}

public class PaySubscriptionsDeleteCommand : BaseCommand<PaySubscriptionsAdminIdSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PaySubscriptionsAdminIdSettings settings)
    {
        try
        {
            if (!Guid.TryParse(settings.Id, out var id))
            { Renderer.Warn("Subscription id must be a guid."); return 1; }

            if (!settings.SkipConfirm &&
                !AnsiConsole.Confirm($"Hard-delete subscription {id.ToString("N")[..8]}…? This cannot be undone."))
            { Renderer.Info("Cancelled."); return 0; }

            await GetClient().AdminDeleteSubscriptionAsync(id);
            Renderer.Success($"Subscription {id.ToString("N")[..8]}… deleted.");
            return 0;
        }
        catch (Exception ex)
        {
            if (!PayHelpers.HandleAdminError(ex)) HandleError(ex);
            return 1;
        }
    }
}

public class PaySubscriptionsForceExpireCommand : BaseCommand<PaySubscriptionsAdminIdSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PaySubscriptionsAdminIdSettings settings)
    {
        try
        {
            if (!Guid.TryParse(settings.Id, out var id))
            { Renderer.Warn("Subscription id must be a guid."); return 1; }

            if (!settings.SkipConfirm &&
                !AnsiConsole.Confirm($"Force-expire subscription {id.ToString("N")[..8]}… now?"))
            { Renderer.Info("Cancelled."); return 0; }

            await GetClient().AdminForceExpireSubscriptionAsync(id);
            Renderer.Success($"Subscription {id.ToString("N")[..8]}… force-expired.");
            return 0;
        }
        catch (Exception ex)
        {
            if (!PayHelpers.HandleAdminError(ex)) HandleError(ex);
            return 1;
        }
    }
}

public class PaySubscriptionsRelinkSettings : CommandSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("Subscription id (guid)")]
    public string Id { get; set; } = string.Empty;

    [CommandOption("--to-user-id <USER_ID>")]
    [Description("User id to move the subscription to")]
    public int ToUserId { get; set; }
}

public class PaySubscriptionsRelinkCommand : BaseCommand<PaySubscriptionsRelinkSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PaySubscriptionsRelinkSettings settings)
    {
        try
        {
            if (!Guid.TryParse(settings.Id, out var id))
            { Renderer.Warn("Subscription id must be a guid."); return 1; }
            if (settings.ToUserId <= 0)
            { Renderer.Warn("--to-user-id is required."); return 1; }

            await GetClient().AdminRelinkSubscriptionAsync(id, settings.ToUserId);
            Renderer.Success($"Subscription {id.ToString("N")[..8]}… relinked to user {settings.ToUserId}.");
            return 0;
        }
        catch (Exception ex)
        {
            if (!PayHelpers.HandleAdminError(ex)) HandleError(ex);
            return 1;
        }
    }
}

public class PaySubscriptionsResyncCommand : BaseCommand<PaySubscriptionsIdSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PaySubscriptionsIdSettings settings)
    {
        try
        {
            if (!Guid.TryParse(settings.Id, out var id))
            { Renderer.Warn("Subscription id must be a guid."); return 1; }

            JsonObject? result = null;
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync("Re-syncing subscription...", async _ =>
                    result = await GetClient().AdminResyncSubscriptionAsync(id));

            Renderer.Success($"Subscription {id.ToString("N")[..8]}… re-synced.");
            if (result is { Count: > 0 }) Renderer.PrintJsonObject(result);
            return 0;
        }
        catch (Exception ex)
        {
            if (!PayHelpers.HandleAdminError(ex)) HandleError(ex);
            return 1;
        }
    }
}

// ── pay payment-options ──────────────────────────────────────────────────────────

public class PayPaymentOptionsSettings : CommandSettings
{
    [CommandOption("--platform <PLATFORM>")]
    [Description("Client platform: ios, android, or web")]
    public string? Platform { get; set; }

    [CommandOption("--storefront <STOREFRONT>")]
    [Description("Storefront code (ISO 3166-1 alpha-3, e.g. GBR) — optional")]
    public string? Storefront { get; set; }
}

public class PayPaymentOptionsCommand : BaseCommand<PayPaymentOptionsSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PayPaymentOptionsSettings settings)
    {
        try
        {
            var platform = settings.Platform ?? AnsiConsole.Ask<string>("Platform (ios/android/web):");

            JsonObject options = new();
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching payment options...", async _ =>
                    options = await GetClient().GetPaymentOptionsAsync(platform, settings.Storefront));

            if (options.Count == 0) { Renderer.Info("No payment options returned."); return 0; }

            Renderer.Header("Payment Options");
            foreach (var kv in options)
            {
                var value = kv.Value switch
                {
                    null => null,
                    JsonArray arr => string.Join(", ", arr.Select(a => a?.ToString())),
                    _ => kv.Value.ToString()
                };
                Renderer.KeyValue(kv.Key, value);
            }
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

// ── pay entitlement ──────────────────────────────────────────────────────────────

public class PayEntitlementCommand : BaseCommand<EmptySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EmptySettings settings)
    {
        try
        {
            SubscriptionEntitlementResponse? e = null;
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync("Checking entitlement...", async _ =>
                    e = await GetClient().GetEntitlementAsync());

            if (e is null) { Renderer.Info("No entitlement information."); return 0; }

            Renderer.Header("Entitlement");
            Renderer.KeyValue("Has access", e.HasAccess ? "yes" : "no");
            Renderer.KeyValue("Status", e.Status);
            if (e.IsTrial)
            {
                Renderer.KeyValue("On trial", "yes");
                if (e.TrialEndsAt.HasValue) Renderer.KeyValue("Trial ends", e.TrialEndsAt.Value.ToString("yyyy-MM-dd"));
                if (e.TrialDaysRemaining.HasValue) Renderer.KeyValue("Days remaining", e.TrialDaysRemaining.Value.ToString());
            }
            if (e.SubscriptionId.HasValue) Renderer.KeyValue("Subscription id", e.SubscriptionId.Value.ToString());
            Renderer.KeyValue("Provider", e.Provider);
            Renderer.KeyValue("Product id", e.ProductId);
            if (e.ExpiresAt.HasValue)    Renderer.KeyValue("Expires", e.ExpiresAt.Value.ToString("yyyy-MM-dd"));
            if (e.AutoCancelAt.HasValue) Renderer.KeyValue("Auto-cancel", e.AutoCancelAt.Value.ToString("yyyy-MM-dd"));
            if (e.CancelledAt.HasValue)  Renderer.KeyValue("Cancelled", e.CancelledAt.Value.ToString("yyyy-MM-dd HH:mm"));
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

// ── pay setup (guided) ───────────────────────────────────────────────────────────

public class PaySetupCommand : BaseCommand<EmptySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EmptySettings settings)
    {
        try
        {
            var client = GetClient();

            Renderer.Header("AnythinkPay Setup");
            AnsiConsole.MarkupLine("This walks through Stripe Connect, Apple IAP, and a first plan.");
            AnsiConsole.MarkupLine("[dim]Requires permissions:[/] anythink_subscription_plans:read/create, anythink_payments:read.");
            AnsiConsole.MarkupLine("[dim]Apple credentials need tenant administrator access.[/]");
            AnsiConsole.WriteLine();

            // Step 1 — Stripe Connect.
            if (AnsiConsole.Confirm("Set up Stripe Connect now?", false))
            {
                var businessType = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Select [#F97316]business type[/]:")
                        .AddChoices("individual", "company")
                        .HighlightStyle(new Style(foreground: new Color(196, 69, 54))));
                var country = AnsiConsole.Ask<string>("Country code (e.g. [dim]GB[/]):", "GB");
                var email   = AnsiConsole.Ask<string>("Email address:");

                OnboardingLinkResponse? link = null;
                await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                    .StartAsync("Setting up Stripe Connect...", async _ =>
                    {
                        await client.CreateStripeConnectAsync(new CreateStripeConnectRequest(businessType, country, email));
                        link = await client.CreateOnboardingLinkAsync(
                            "https://anythink.cloud/connect/refresh",
                            "https://anythink.cloud/connect/return");
                    });

                Renderer.Success("Stripe Connect account created.");
                if (link is not null)
                {
                    AnsiConsole.MarkupLine($"  [dim]Finish onboarding:[/] [#F97316]{Markup.Escape(link.Url)}[/]");
                    try { Process.Start(new ProcessStartInfo { FileName = link.Url, UseShellExecute = true }); }
                    catch { Renderer.Warn("Could not open the browser automatically — copy the URL above."); }
                }
                AnsiConsole.WriteLine();
            }

            // Step 2 — Apple IAP credentials.
            if (AnsiConsole.Confirm("Configure Apple IAP credentials now?", false))
            {
                var issuerId = AnsiConsole.Ask<string>("Issuer id:");
                var keyId    = AnsiConsole.Ask<string>("Key id:");
                var bundleId = AnsiConsole.Ask<string>("Bundle id:");
                var keyFile  = AnsiConsole.Ask<string>("Private key (.p8) file path:");

                if (!File.Exists(keyFile))
                {
                    Renderer.Warn($"Private key file not found: {keyFile} — skipping Apple setup.");
                }
                else
                {
                    var pem = await File.ReadAllTextAsync(keyFile);
                    try
                    {
                        AppleIapCredentialsResponse? creds = null;
                        await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                            .StartAsync("Saving Apple IAP credentials...", async _ =>
                                creds = await client.UpdateAppleIapCredentialsAsync(new UpdateAppleIapCredentialsRequest(
                                    BundleId: bundleId, AscIssuerId: issuerId, AscKeyId: keyId, AscPrivateKeyPem: pem)));

                        Renderer.Success("Apple IAP credentials saved.");
                        if (!string.IsNullOrEmpty(creds?.NotificationUrl))
                            AnsiConsole.MarkupLine($"  [dim]Register in App Store Connect:[/] [#F97316]{Markup.Escape(creds!.NotificationUrl)}[/]");
                    }
                    catch (Exception ex)
                    {
                        if (!PayHelpers.HandleAdminError(ex)) HandleError(ex);
                    }
                }
                AnsiConsole.WriteLine();
            }

            // Step 3 — first plan.
            if (AnsiConsole.Confirm("Create a subscription plan now?", false))
            {
                var planName = AnsiConsole.Ask<string>("Plan name (internal id):");
                var name     = AnsiConsole.Ask<string>("Display name:");
                var desc     = AnsiConsole.Ask<string>("Description:");
                var currency = AnsiConsole.Ask<string>("Currency:", "gbp");
                var amount   = AnsiConsole.Ask<decimal>($"Amount ({currency}):");
                var interval = AnsiConsole.Ask<string>("Billing interval (day/week/month/year):", "month");

                var req = new CreateSubscriptionPlanRequest(
                    PlanName: planName, Name: name, Description: desc, Type: "web",
                    Amount: amount, Currency: currency, BillingInterval: interval, IntervalCount: 1,
                    TrialPeriodDays: null, ProductName: null, ProductDescription: null,
                    Reference: null, IsActive: true);

                SubscriptionPlanResponse? plan = null;
                await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                    .StartAsync("Creating plan...", async _ => plan = await client.CreateSubscriptionPlanAsync(req));

                if (plan is not null) Renderer.Success($"Plan #{plan.Id} created.");
            }

            AnsiConsole.WriteLine();
            Renderer.Success("Setup complete.");
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}
