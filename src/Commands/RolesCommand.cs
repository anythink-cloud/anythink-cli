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

    [CommandOption("-y|--yes")]
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

// ── roles permissions list ───────────────────────────────────────────────────

public class RolePermissionsListSettings : CommandSettings
{
    [CommandArgument(0, "<ROLE_ID>")]
    [Description("Role ID")]
    public int RoleId { get; set; }
}

public class RolesPermissionsListCommand : BaseCommand<RolePermissionsListSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, RolePermissionsListSettings settings)
    {
        try
        {
            var client = GetClient();
            RoleResponse? role = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching role permissions...", async _ =>
                {
                    role = await client.GetRoleAsync(settings.RoleId);
                });

            if (role is null)
            {
                Renderer.Error($"Role {settings.RoleId} not found.");
                return 1;
            }

            var perms = role.Permissions ?? [];
            var entityPerms = new Dictionary<string, List<string>>();
            foreach (var p in perms.Where(p => p.EntityId.HasValue))
            {
                var parts = p.Name.Split(':');
                if (parts.Length == 2)
                {
                    if (!entityPerms.ContainsKey(parts[0]))
                        entityPerms[parts[0]] = [];
                    entityPerms[parts[0]].Add(parts[1]);
                }
            }

            Renderer.Header($"Role {role.Name} — Entity Permissions ({entityPerms.Count} entities)");

            var table = Renderer.BuildTable("Entity", "Actions");
            foreach (var kv in entityPerms.OrderBy(x => x.Key))
                Renderer.AddRow(table, kv.Key, string.Join(", ", kv.Value));
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

// ── roles permissions add ────────────────────────────────────────────────────

public class RolePermissionsAddSettings : CommandSettings
{
    [CommandArgument(0, "<ROLE_ID>")]
    [Description("Role ID")]
    public int RoleId { get; set; }

    [CommandArgument(1, "<ENTITY>")]
    [Description("Entity name")]
    public string Entity { get; set; } = "";

    [CommandOption("--actions <ACTIONS>")]
    [Description("Comma-separated actions: read,create,update,delete (default: read)")]
    public string Actions { get; set; } = "read";
}

public class RolesPermissionsAddCommand : BaseCommand<RolePermissionsAddSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, RolePermissionsAddSettings settings)
    {
        try
        {
            var client = GetClient();

            var entity = await client.GetEntityAsync(settings.Entity);

            var role = await client.GetRoleAsync(settings.RoleId)
                ?? throw new AnythinkCli.Client.AnythinkException($"Role {settings.RoleId} not found.", 404);

            var permIds = (role.Permissions ?? []).Select(p => p.Id).ToList();

            var allPerms = await client.GetPermissionsAsync();
            var actions = settings.Actions.Split(',').Select(a => a.Trim().ToLower()).ToList();

            var added = new List<string>();
            foreach (var action in actions)
            {
                var permName = $"{settings.Entity}:{action}";
                var existing = allPerms.FirstOrDefault(p => p.Name == permName && p.EntityId == entity.Id);

                if (existing != null)
                {
                    if (!permIds.Contains(existing.Id))
                    {
                        permIds.Add(existing.Id);
                        added.Add(action);
                    }
                }
                else
                {
                    Renderer.Warn($"Permission '{Markup.Escape(permName)}' not found globally.");
                }
            }

            if (added.Count == 0)
            {
                Renderer.Info("No new permissions to add (already assigned or not found).");
                return 0;
            }

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Updating role permissions...", async _ =>
                {
                    await client.UpdateRoleWithPermissionsAsync(settings.RoleId,
                        new UpdateRolePermissionsRequest(
                            role.Name, role.Description, role.IsActive, role.AnyApiAccess, permIds));
                });

            Renderer.Success($"Added [{string.Join(", ", added)}] on [#F97316]{Markup.Escape(settings.Entity)}[/] to role {settings.RoleId}.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}
