using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Diagnostics;

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
