using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Text.Json;

namespace AnythinkCli.Commands;

public class PlansListSettings : CommandSettings
{
    [System.ComponentModel.Description("Output raw JSON")]
    [CommandOption("--json")]
    public bool Json { get; set; }
}

public class PlansListCommand : BasePlatformCommand<PlansListSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PlansListSettings settings)
    {
        try
        {
            var client = GetUnauthenticatedBillingClient(); // plans are public
            List<BillingPlan> plans = [];

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching plans...", async _ =>
                {
                    plans = await client.GetPlansAsync();
                });

            plans = plans.Where(p => p.IsActive).OrderBy(p => p.DisplayOrder).ToList();

            if (settings.Json)
            {
                Renderer.PrintJson(JsonSerializer.Serialize(plans, Renderer.PrettyJson));
                return 0;
            }

            Renderer.Header($"Available Plans ({plans.Count})");

            var table = Renderer.BuildTable("ID", "Name", "Resource", "Monthly", "Annual", "Storage", "Users", "Custom Domain", "Backups");
            foreach (var p in plans)
            {
                var monthly = p.MonthlyPriceCents == 0 ? "Free"
                    : $"{p.Currency.ToUpper()} {p.MonthlyPriceCents / 100m:0.00}/mo";
                var annual = p.AnnualPriceCents == 0 ? "Free"
                    : $"{p.Currency.ToUpper()} {p.AnnualPriceCents / 100m:0.00}/yr";

                table.AddRow(
                    Markup.Escape(p.Id.ToString()[..8] + "…"),
                    $"[bold]{Markup.Escape(p.Name)}[/]",
                    Markup.Escape(p.Resource.ToString()),
                    Markup.Escape(monthly),
                    Markup.Escape(annual),
                    Markup.Escape($"{p.StorageQuotaGb}GB"),
                    Markup.Escape(p.UserQuota.ToString()),
                    p.CustomDomainEnabled ? "[green]✓[/]" : "[dim]—[/]",
                    p.BackupsEnabled ? "[green]✓[/]" : "[dim]—[/]"
                );
            }

            AnsiConsole.Write(table);
            Renderer.Info("Use the full plan ID with [bold]anythink projects create --plan <id>[/].");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}
