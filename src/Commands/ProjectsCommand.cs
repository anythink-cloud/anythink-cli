using AnythinkCli.Client;
using AnythinkCli.Config;
using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using CliProfile = AnythinkCli.Config.Profile;

namespace AnythinkCli.Commands;

static class ProjectStatusMarkup
{
    public static string Render(int status) => status switch
    {
        0 => "[dim]initializing[/]",
        1 => "[yellow]provisioning[/]",
        2 => "[green]active[/]",
        3 => "[yellow]suspended[/]",
        4 => "[red]terminated[/]",
        5 => "[red]error[/]",
        _ => status.ToString()
    };
}

// ── projects list ─────────────────────────────────────────────────────────────

public class ProjectsListSettings : CommandSettings
{
    [CommandOption("--account <ID>")]
    [Description("Billing account ID (uses active account if omitted)")]
    public string? AccountId { get; set; }
}

public class ProjectsListCommand : BasePlatformCommand<ProjectsListSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ProjectsListSettings settings)
    {
        try
        {
            var accountId = GetAccountId(settings.AccountId);
            var client = GetBillingClient();
            List<SharedTenant> projects = [];

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching projects...", async _ =>
                {
                    projects = await client.GetProjectsAsync(accountId);
                });

            Renderer.Header($"Projects ({projects.Count})");

            if (projects.Count == 0)
            {
                Renderer.Info("No projects yet.");
                AnsiConsole.MarkupLine("Run [bold #F97316]anythink projects create \"My Project\"[/]");
                return 0;
            }

            var activeOrgId = ConfigService.GetActiveProfile()?.OrgId;
            var table = Renderer.BuildTable("ID", "Name", "Status", "Region", "Org ID", "API URL", "Active");

            foreach (var p in projects.OrderBy(x => x.CreatedAt))
            {
                var isActive = p.TenantId?.ToString() == activeOrgId;
                table.AddRow(
                    Markup.Escape(p.Id.ToString()[..8] + "…"),
                    $"[bold]{Markup.Escape(p.Name)}[/]",
                    ProjectStatusMarkup.Render(p.Status),
                    Markup.Escape(p.Region ?? "—"),
                    Markup.Escape(p.TenantId?.ToString() ?? "—"),
                    Markup.Escape(p.ApiUrl ?? "—"),
                    isActive ? "[green]●[/]" : ""
                );
            }
            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine("Run [bold #F97316]anythink projects use <id>[/] to connect to a project.");
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }

}

// ── projects create ───────────────────────────────────────────────────────────

public class ProjectsCreateSettings : CommandSettings
{
    [CommandArgument(0, "[NAME]")]
    [Description("Project name")]
    public string? Name { get; set; }

    [CommandOption("--plan <ID>")]
    [Description("Plan ID (UUID). Run 'anythink plans' to see options.")]
    public string? PlanId { get; set; }

    [CommandOption("--region <REGION>")]
    [Description("Deployment region (e.g. lon1)")]
    public string? Region { get; set; }

    [CommandOption("--description <DESC>")]
    public string? Description { get; set; }

    [CommandOption("--account <ID>")]
    [Description("Billing account ID (uses active account if omitted)")]
    public string? AccountId { get; set; }
}

public class ProjectsCreateCommand : BasePlatformCommand<ProjectsCreateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ProjectsCreateSettings settings)
    {
        var name = settings.Name ?? AnsiConsole.Ask<string>("[#F97316]Project name:[/]");

        // Fetch and display plans if no --plan given
        Guid planId;
        if (!string.IsNullOrEmpty(settings.PlanId) && Guid.TryParse(settings.PlanId, out var parsedId))
        {
            planId = parsedId;
        }
        else
        {
            planId = await PickPlanInteractively();
            if (planId == Guid.Empty) return 1;
        }

        var region = settings.Region ?? AnsiConsole.Prompt(
            Renderer.Prompt<string>()
                .Title("[#F97316]Region:[/]")
                .AddChoices("lon1"));

        try
        {
            var accountId = GetAccountId(settings.AccountId);
            var client = GetBillingClient();
            SharedTenant? project = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Creating project '{name}'...", async _ =>
                {
                    project = await client.CreateProjectAsync(accountId,
                        new CreateSharedTenantRequest(name, planId, region, settings.Description));
                });

            Renderer.Success($"Project [#F97316]{Markup.Escape(project!.Name)}[/] created!");
            Renderer.KeyValue("ID", project.Id.ToString());
            Renderer.KeyValue("Status", ProjectStatusMarkup.Render(project.Status));
            Renderer.KeyValue("Region", project.Region ?? "—");

            if (project.Status is 0 or 1) // 0=Initializing, 1=Provisioning
            {
                AnsiConsole.MarkupLine("\n[yellow]Your project is being provisioned.[/]");
                AnsiConsole.MarkupLine("Run [bold #F97316]anythink projects list[/] to check status.");
                AnsiConsole.MarkupLine("Once [green]active[/], run [bold #F97316]anythink projects use {0}[/] to connect.", project.Id.ToString()[..8]);
            }

            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }

    private async Task<Guid> PickPlanInteractively()
    {
        List<BillingPlan> plans = [];
        try
        {
            var client = GetUnauthenticatedBillingClient();
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Loading plans...", async _ =>
                {
                    plans = await client.GetPlansAsync();
                });
        }
        catch
        {
            Renderer.Error("Could not fetch plans. Use --plan <id> to specify one directly.");
            return Guid.Empty;
        }

        plans = plans.Where(p => p.IsActive).OrderBy(p => p.DisplayOrder).ToList();
        if (plans.Count == 0)
        {
            Renderer.Error("No active plans found.");
            return Guid.Empty;
        }

        var choices = plans.Select(p =>
        {
            var price = p.MonthlyPriceCents == 0 ? "Free"
                : $"{p.Currency.ToUpper()} {p.MonthlyPriceCents / 100m:0.00}/mo";
            return $"{p.Name} — {price} | {p.StorageQuotaGb}GB | {p.UserQuota} users";
        }).ToList();

        var selected = AnsiConsole.Prompt(
            Renderer.Prompt<string>()
                .Title("[#F97316]Choose a plan:[/]")
                .AddChoices(choices));

        var idx = choices.IndexOf(selected);
        return plans[idx].Id;
    }
}

// ── projects use ──────────────────────────────────────────────────────────────

public class ProjectsUseSettings : CommandSettings
{
    [CommandArgument(0, "[ID]")]
    [Description("Project name, org ID, or UUID prefix. Omit to pick interactively.")]
    public string? Id { get; set; }

    [CommandOption("--account <ID>")]
    [Description("Billing account ID (uses active account if omitted)")]
    public string? AccountId { get; set; }

    [CommandOption("--api-key <KEY>")]
    [Description("API key for the project (optional; generates one if omitted)")]
    public string? ApiKey { get; set; }
}

public class ProjectsUseCommand : BasePlatformCommand<ProjectsUseSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ProjectsUseSettings settings)
    {
        try
        {
            var accountId = GetAccountId(settings.AccountId);
            var client = GetBillingClient();

            List<SharedTenant> projects = [];
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching projects...", async _ =>
                {
                    projects = await client.GetProjectsAsync(accountId);
                });

            SharedTenant? match;

            if (string.IsNullOrEmpty(settings.Id))
            {
                // Interactive picker
                var choices = projects.OrderBy(p => p.Name).Select(p =>
                    $"{Markup.Escape(p.Name)}  (org: {p.TenantId?.ToString() ?? "—"})  ({p.Id.ToString()[..8]}…)"
                ).ToList();

                var selected = AnsiConsole.Prompt(
                    Renderer.Prompt<string>()
                        .Title("[#F97316]Select project:[/]")
                        .AddChoices(choices));

                var idx = choices.IndexOf(selected);
                match = projects.OrderBy(p => p.Name).ToList()[idx];
            }
            else
            {
                match = projects.FirstOrDefault(p =>
                    p.Id.ToString().StartsWith(settings.Id, StringComparison.OrdinalIgnoreCase) ||
                    p.TenantId?.ToString() == settings.Id ||
                    p.Name.Equals(settings.Id, StringComparison.OrdinalIgnoreCase));
            }

            if (match == null)
            {
                Renderer.Error($"No project matching '{settings.Id}' found.");
                return 1;
            }

            if (match.Status != 2) // 2 = Active
            {
                Renderer.Warn($"Project status is {ProjectStatusMarkup.Render(match.Status)} — it may not be ready yet.");
            }

            if (match.TenantId == null)
            {
                Renderer.Error("Project has no tenant ID yet. Wait for provisioning to complete.");
                return 1;
            }

            var platform = ResolvePlatform();
            var baseUrl = match.ApiUrl?.TrimEnd('/') ?? platform.MyAnythinkUrl;
            var profileKey = match.Name.ToLower().Replace(" ", "-");

            // Save profile — no AccessToken yet if no API key provided
            var profile = new CliProfile
            {
                OrgId = match.TenantId.Value.ToString(),
                ApiKey = settings.ApiKey,
                InstanceApiUrl = baseUrl,
                Alias = match.Name
            };
            ConfigService.SaveProfile(profileKey, profile);
            ConfigService.SetDefault(profileKey);

            Renderer.Success($"Now using project [#F97316]{Markup.Escape(match.Name)}[/].");
            Renderer.Info($"Profile: {profileKey}  |  Org ID: {match.TenantId}  |  API: {baseUrl}");

            // If an API key was provided we're done — no JWT needed
            if (!string.IsNullOrEmpty(settings.ApiKey))
            {
                AnsiConsole.MarkupLine("\nRun [bold #F97316]anythink entities list[/] to explore this project.");
                return 0;
            }

            // Exchange platform session for a project-scoped JWT via transfer token
            try
            {
                LoginResponse? loginResp = null;
                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync("Getting project access...", async _ =>
                    {
                        var transferResp = await client.GetTransferTokenAsync(accountId, match.Id);
                        var projectClient = new AnythinkClient(profile);
                        loginResp = await projectClient.ExchangeTransferTokenAsync(transferResp.TransferToken);
                    });

                profile.AccessToken = loginResp!.AccessToken;
                profile.RefreshToken = loginResp.RefreshToken;
                profile.TokenExpiresAt = loginResp.ExpiresIn.HasValue
                    ? DateTime.UtcNow.AddSeconds(loginResp.ExpiresIn.Value)
                    : DateTime.UtcNow.AddHours(1);
                ConfigService.SaveProfile(profileKey, profile);
                Renderer.Success("Project access granted.");
            }
            catch (AnythinkException ae)
            {
                Renderer.Warn($"Could not get project token ({ae.StatusCode}): {ae.Message}");
                AnsiConsole.MarkupLine("Provide an API key with [#F97316]--api-key ak_...[/] to authenticate manually.");
            }

            AnsiConsole.MarkupLine("\nRun [bold #F97316]anythink entities list[/] to explore this project.");
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

// ── projects delete ───────────────────────────────────────────────────────────

public class ProjectsDeleteSettings : CommandSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("Project ID (full UUID or prefix)")]
    public string Id { get; set; } = "";

    [CommandOption("--account <ID>")]
    public string? AccountId { get; set; }

    [CommandOption("--yes")]
    public bool Yes { get; set; }
}

public class ProjectsDeleteCommand : BasePlatformCommand<ProjectsDeleteSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ProjectsDeleteSettings settings)
    {
        try
        {
            var accountId = GetAccountId(settings.AccountId);
            var client = GetBillingClient();
            var projects = await client.GetProjectsAsync(accountId);

            var match = projects.FirstOrDefault(p =>
                p.Id.ToString().StartsWith(settings.Id, StringComparison.OrdinalIgnoreCase) ||
                p.Name.Equals(settings.Id, StringComparison.OrdinalIgnoreCase));

            if (match == null) { Renderer.Error($"No project matching '{settings.Id}'."); return 1; }

            if (!settings.Yes)
            {
                var confirm = AnsiConsole.Confirm(
                    $"[yellow]Delete project[/] [bold red]{match.Name}[/][yellow]? All data will be destroyed.[/]",
                    defaultValue: false);
                if (!confirm) { Renderer.Info("Cancelled."); return 0; }
            }

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Deleting '{match.Name}'...", async _ =>
                {
                    await client.DeleteProjectAsync(accountId, match.Id);
                });

            Renderer.Success($"Project [#F97316]{Markup.Escape(match.Name)}[/] deleted.");
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}
