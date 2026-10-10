using AnythinkCli.Client;
using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AnythinkCli.Commands;

static class AccountStatusText
{
    public static string Name(int status) => status switch
    {
        0 => "active",
        1 => "past due",
        2 => "suspended",
        3 => "closed",
        _ => status.ToString()
    };

    public static Text Cell(int status) => new(Name(status), status switch
    {
        0 => new Style(Color.Green),
        1 => new Style(Color.Yellow),
        2 or 3 => new Style(Color.Red),
        _ => Style.Plain
    });

    public static string AccessLevel(int level) => level switch
    {
        0 => "viewer",
        1 => "admin",
        2 => "owner",
        _ => level.ToString()
    };
}

// ── accounts list ─────────────────────────────────────────────────────────────

public class AccountsListSettings : CommandSettings
{
    [Description("Output raw JSON")]
    [CommandOption("--json")]
    public bool Json { get; set; }
}

public class AccountsListCommand : BasePlatformCommand<AccountsListSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, AccountsListSettings settings)
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

            if (settings.Json || ClientContext.Remote)
            {
                Renderer.PrintJson(JsonSerializer.Serialize(accounts.Select(a => new
                {
                    account_id = a.Id,
                    name = a.OrganizationName,
                    email = a.BillingEmail,
                    currency = a.Currency,
                    status = AccountStatusText.Name(a.Status),
                    access_level = AccountStatusText.AccessLevel(a.AccessLevel)
                }), Renderer.PrettyJson));
                return 0;
            }

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
                    new Text(a.Id.ToString()[..8] + "…"),
                    new Text(a.OrganizationName),
                    new Text(a.BillingEmail),
                    new Text(a.Currency.ToUpper()),
                    AccountStatusText.Cell(a.Status),
                    new Text(isActive ? "●" : "", new Style(Color.Green))
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

    [CommandOption("--json")]
    [Description("Output raw JSON")]
    public bool Json { get; set; }
}

public class AccountsCreateCommand : BasePlatformCommand<AccountsCreateSettings>
{
    public const string DefaultCurrency = "gbp";

    public override async Task<int> ExecuteAsync(CommandContext context, AccountsCreateSettings settings)
    {
        string name, email, currency;
        if (ClientContext.Remote)
        {
            if (string.IsNullOrWhiteSpace(settings.Name) || string.IsNullOrWhiteSpace(settings.Email))
            {
                Renderer.Error("'name' and 'email' are required.");
                return 1;
            }
            name = settings.Name;
            email = settings.Email;
            currency = settings.Currency ?? DefaultCurrency;
        }
        else
        {
            name = settings.Name  ?? AnsiConsole.Ask<string>("[#F97316]Organisation name:[/]");
            email = settings.Email ?? AnsiConsole.Ask<string>("[#F97316]Billing email:[/]");
            currency = settings.Currency
                ?? AnsiConsole.Prompt(
                    Renderer.Prompt<string>()
                        .Title("[#F97316]Currency:[/]")
                        .AddChoices("gbp", "usd", "eur"));
        }

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

            if (settings.Json || ClientContext.Remote)
            {
                if (!ClientContext.Remote)
                    SetActive(account!);

                Renderer.PrintJsonObject(new JsonObject
                {
                    ["account_id"] = account!.Id.ToString(),
                    ["name"] = account.OrganizationName,
                    ["currency"] = account.Currency,
                    ["message"] = ClientContext.Remote
                        ? "Billing account created. Pass its account_id to projects_create."
                        : "Billing account created and set as the active account."
                });
                return 0;
            }

            Renderer.Success($"Billing account [#F97316]{Markup.Escape(account!.OrganizationName)}[/] created.");
            Renderer.Info($"ID: {Markup.Escape(account.Id.ToString())}");

            SetActive(account);
            Renderer.Success("Set as active account.");
            AnsiConsole.MarkupLine("\nRun [bold #F97316]anythink projects create \"My Project\"[/] to create your first project.");
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }

    private void SetActive(BillingAccount account)
    {
        var (platformKey, platform) = ResolvePlatformContext();
        platform.AccountId = account.Id.ToString();
        SaveAndActivatePlatform(platformKey, platform);
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
                    .Select(a => $"{a.OrganizationName}  <{a.BillingEmail}>  ({a.Id.ToString()[..8]}…)")
                    .ToList();

                var selected = AnsiConsole.Prompt(
                    Renderer.Prompt<string>()
                        .Title("[#F97316]Select billing account:[/]")
                        .UseConverter(Markup.Escape)
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

            var (platformKey, platform) = ResolvePlatformContext();
            platform.AccountId = match.Id.ToString();
            SaveAndActivatePlatform(platformKey, platform);

            AnsiConsole.MarkupLine($"[green]✓[/] Active account: [bold #F97316]{Markup.Escape(match.OrganizationName)}[/] [dim]({match.Id})[/]");
            AnsiConsole.MarkupLine("\nRun [bold #F97316]anythink projects list[/] to see your projects.");
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}
