using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace AnythinkCli.Commands;

// ── roles list ────────────────────────────────────────────────────────────────

public class RolesListCommand : BaseCommand<EmptySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EmptySettings settings)
    {
        try
        {
            var client = GetClient();
            List<RoleResponse> roles = [];

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching roles...", async _ =>
                {
                    roles = await client.GetRolesAsync();
                });

            if (roles.Count == 0)
            {
                Renderer.Info("No roles found.");
                return 0;
            }

            Renderer.Header($"Roles ({roles.Count})");

            var table = Renderer.BuildTable("ID", "Name", "Description", "Active");
            foreach (var r in roles.OrderBy(x => x.Id))
            {
                Renderer.AddRow(table,
                    r.Id.ToString(),
                    r.Name,
                    r.Description,
                    r.IsActive ? "yes" : "no"
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

// ── roles create ──────────────────────────────────────────────────────────────

public class RoleCreateSettings : CommandSettings
{
    [CommandArgument(0, "<NAME>")]
    [Description("Role name")]
    public string Name { get; set; } = "";

    [CommandOption("--description <DESC>")]
    [Description("Optional description")]
    public string? Description { get; set; }
}

public class RolesCreateCommand : BaseCommand<RoleCreateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, RoleCreateSettings settings)
    {
        try
        {
            var client = GetClient();
            RoleResponse? role = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Creating role '{settings.Name}'...", async _ =>
                {
                    role = await client.CreateRoleAsync(new CreateRoleRequest(
                        settings.Name,
                        settings.Description
                    ));
                });

            Renderer.Success($"Role [#F97316]{Markup.Escape(role!.Name)}[/] created.");
            Renderer.KeyValue("ID", role.Id.ToString());
            Renderer.KeyValue("Active", role.IsActive ? "yes" : "no");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── roles delete ──────────────────────────────────────────────────────────────

public class RoleDeleteSettings : CommandSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("Role ID to delete")]
    public int Id { get; set; }

    [CommandOption("--yes")]
    [Description("Skip confirmation prompt")]
    public bool Yes { get; set; }
}

public class RolesDeleteCommand : BaseCommand<RoleDeleteSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, RoleDeleteSettings settings)
    {
        if (!settings.Yes)
        {
            var confirm = AnsiConsole.Confirm(
                $"[yellow]Delete role[/] [bold red]{settings.Id}[/][yellow]?[/]",
                defaultValue: false);
            if (!confirm)
            {
                Renderer.Info("Cancelled.");
                return 0;
            }
        }

        try
        {
            var client = GetClient();

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Deleting role {settings.Id}...", async _ =>
                {
                    await client.DeleteRoleAsync(settings.Id);
                });

            Renderer.Success($"Role [#F97316]{settings.Id}[/] deleted.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}
