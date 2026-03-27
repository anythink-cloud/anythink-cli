using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace AnythinkCli.Commands;

static class AccountStatusMarkup
{
    public static string Render(int status) => status switch
    {
        0 => "[green]active[/]",
        1 => "[yellow]suspended[/]",
        2 => "[red]canceled[/]",
        _ => status.ToString()
    };
}

// ── accounts list ─────────────────────────────────────────────────────────────

public class AccountsListCommand : BasePlatformCommand<EmptySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EmptySettings settings)
    {
        try
        {
            var client = GetBillingClient();
            List<BillingAccount> accounts = [];

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching accounts...", async _ =>
                {
                    accounts = await client.GetAccountsAsync();
                });

            var platform = ResolvePlatform();
            Renderer.Header($"Billing Accounts ({accounts.Count})");

            if (accounts.Count == 0)
            {
                Renderer.Info("No billing accounts found.");
                AnsiConsole.MarkupLine("Run [bold #F97316]anythink accounts create[/] to create one.");
                return 0;
            }

            var table = Renderer.BuildTable("ID", "Name", "Email", "Currency", "Status", "Active");
            foreach (var a in accounts)
            {
                var isActive = a.Id.ToString() == platform.AccountId;
                table.AddRow(
                    Markup.Escape(a.Id.ToString()[..8] + "…"),
                    Markup.Escape(a.OrganizationName),
                    Markup.Escape(a.BillingEmail),
                    Markup.Escape(a.Currency.ToUpper()),
                    AccountStatusMarkup.Render(a.Status),
                    isActive ? "[green]●[/]" : ""
                );
            }
            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine("Run [bold #F97316]anythink accounts use <id>[/] to set the active account.");
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }

}

// ── accounts create ───────────────────────────────────────────────────────────

public class AccountsCreateSettings : CommandSettings
{
    [CommandOption("--name <NAME>")]
    [Description("Organisation name")]
    public string? Name { get; set; }

    [CommandOption("--email <EMAIL>")]
    [Description("Billing email address")]
    public string? Email { get; set; }

    [CommandOption("--currency <CODE>")]
    [Description("Currency code: gbp, usd, eur (default: gbp)")]
    public string? Currency { get; set; }
}

public class AccountsCreateCommand : BasePlatformCommand<AccountsCreateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, AccountsCreateSettings settings)
    {
        var name  = settings.Name  ?? AnsiConsole.Ask<string>("[#F97316]Organisation name:[/]");
        var email = settings.Email ?? AnsiConsole.Ask<string>("[#F97316]Billing email:[/]");
        var currency = settings.Currency
            ?? AnsiConsole.Prompt(
                Renderer.Prompt<string>()
                    .Title("[#F97316]Currency:[/]")
                    .AddChoices("gbp", "usd", "eur"));

        try
        {
            var client = GetBillingClient();
            BillingAccount? account = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Creating billing account...", async _ =>
                {
                    account = await client.CreateAccountAsync(new CreateBillingAccountRequest(name, email, currency));
                });

            Renderer.Success($"Billing account [#F97316]{Markup.Escape(account!.OrganizationName)}[/] created.");
            Renderer.Info($"ID: {Markup.Escape(account.Id.ToString())}");

            // Auto-set as active account
            var platform = ResolvePlatform();
            platform.AccountId = account.Id.ToString();
            SavePlatform(platform);
            Renderer.Success("Set as active account.");
            AnsiConsole.MarkupLine("\nRun [bold #F97316]anythink projects create \"My Project\"[/] to create your first project.");
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

// ── accounts use ──────────────────────────────────────────────────────────────

public class AccountsUseSettings : CommandSettings
{
    [CommandArgument(0, "[ID]")]
    [Description("Billing account ID (full UUID or prefix). Omit to pick interactively.")]
    public string? Id { get; set; }
}

public class AccountsUseCommand : BasePlatformCommand<AccountsUseSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, AccountsUseSettings settings)
    {
        try
        {
            var client = GetBillingClient();
            List<BillingAccount> accounts = [];

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching accounts...", async _ =>
                {
                    accounts = await client.GetAccountsAsync();
                });

            if (accounts.Count == 0)
            {
                Renderer.Error("No billing accounts found.");
                return 1;
            }

            BillingAccount? match;

            if (string.IsNullOrEmpty(settings.Id))
            {
                // Interactive picker
                var choices = accounts
                    .Select(a => $"{Markup.Escape(a.OrganizationName)}  <{Markup.Escape(a.BillingEmail)}>  ({a.Id.ToString()[..8]}…)")
                    .ToList();

                var selected = AnsiConsole.Prompt(
                    Renderer.Prompt<string>()
                        .Title("[#F97316]Select billing account:[/]")
                        .AddChoices(choices));

                var idx = choices.IndexOf(selected);
                match = accounts[idx];
            }
            else
            {
                match = accounts.FirstOrDefault(a =>
                    a.Id.ToString().StartsWith(settings.Id, StringComparison.OrdinalIgnoreCase));

                if (match == null)
                {
                    Renderer.Error($"No account matching '{settings.Id}' found.");
                    return 1;
                }
            }

            var platform = ResolvePlatform();
            platform.AccountId = match.Id.ToString();
            SavePlatform(platform);

            AnsiConsole.MarkupLine($"[green]✓[/] Active account: [bold #F97316]{Markup.Escape(match.OrganizationName)}[/] [dim]({match.Id})[/]");
            AnsiConsole.MarkupLine("\nRun [bold #F97316]anythink projects list[/] to see your projects.");
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}
