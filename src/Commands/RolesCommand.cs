using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace AnythinkCli.Commands;

// ── roles list ────────────────────────────────────────────────────────────────

[CommandGuide("Lists the project's roles with id, name, description, whether each is active and whether it has API access (without it, the role's entity permissions never apply and its users get 403 on data calls). Access works through roles: each user holds one role, and a role grants actions (read, create, update, delete) per entity. Use `roles_permissions_list` to see what one role grants.")]
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

            var table = Renderer.BuildTable("ID", "Name", "Description", "Active", "API access");
            foreach (var r in roles.OrderBy(x => x.Id))
            {
                Renderer.AddRow(table,
                    r.Id.ToString(),
                    r.Name,
                    r.Description,
                    r.IsActive ? "yes" : "no",
                    r.AnyApiAccess ? "yes" : "no"
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

    [CommandOption("--api-access <BOOL>")]
    [Description("Let the role's users reach data through the API (true/false, default true). Entity permissions only apply when this is on")]
    public bool ApiAccess { get; set; } = true;
}

[CommandGuide("Adds a role with API access on (set api_access false to turn it off) and no entity permissions yet, so its users can't reach any data until you grant actions with `roles_permissions_add`. Then give it to people by passing its id as role_id to `users_invite`. Role names must be unique in the project.")]
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
                .StartAsync(Renderer.Status($"Creating role '{settings.Name}'..."), async _ =>
                {
                    role = await client.CreateRoleAsync(new CreateRoleRequest(
                        settings.Name,
                        settings.Description,
                        AnyApiAccess: settings.ApiAccess
                    ));
                });

            Renderer.Success($"Role [#F97316]{Markup.Escape(role!.Name)}[/] created.");
            Renderer.KeyValue("ID", role.Id.ToString());
            Renderer.KeyValue("Active", role.IsActive ? "yes" : "no");
            Renderer.KeyValue("API access", role.AnyApiAccess ? "yes" : "no");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── roles update ──────────────────────────────────────────────────────────────

public class RoleUpdateSettings : CommandSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("Role ID")]
    public int Id { get; set; }

    [CommandOption("--name <NAME>")]
    [Description("New role name")]
    public string? Name { get; set; }

    [CommandOption("--description <DESC>")]
    [Description("New description")]
    public string? Description { get; set; }

    [CommandOption("--api-access <BOOL>")]
    [Description("Let the role's users reach data through the API (true/false)")]
    public bool? ApiAccess { get; set; }
}

[CommandGuide("Changes a role's name, description or API access, keeping its permissions and active state; only what you pass changes. Turn api_access on for a role whose users get 403 on data calls: without it the role's entity permissions never apply.")]
public class RolesUpdateCommand : BaseCommand<RoleUpdateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, RoleUpdateSettings settings)
    {
        try
        {
            var client = GetClient();
            var role = await client.GetRoleAsync(settings.Id)
                ?? throw new AnythinkCli.Client.AnythinkException($"Role {settings.Id} not found.", 404);

            var apiAccess = settings.ApiAccess ?? role.AnyApiAccess;
            await client.UpdateRoleWithPermissionsAsync(settings.Id,
                new UpdateRolePermissionsRequest(
                    settings.Name ?? role.Name,
                    settings.Description ?? role.Description,
                    role.IsActive,
                    apiAccess,
                    (role.Permissions ?? []).Select(p => p.Id).ToList()));

            Renderer.Success($"Role [#F97316]{settings.Id}[/] updated.");
            Renderer.KeyValue("Name", settings.Name ?? role.Name);
            Renderer.KeyValue("API access", apiAccess ? "yes" : "no");
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

[CommandGuide("Removes a role and the permissions it grants. The API refuses while any user still holds the role, and the built-in admin role can't be deleted, so check `users_list` for its holders first. To take away only some access, use `roles_permissions_remove` instead.")]
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
                .StartAsync(Renderer.Status($"Deleting role {settings.Id}..."), async _ =>
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

[CommandGuide("Shows, entity by entity, which actions (read, create, update, delete) a role grants. Use it before `roles_permissions_add` or `roles_permissions_remove`, or to explain why users with that role can or can't reach some data.")]
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
            foreach (var p in perms)
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

[CommandGuide("Grants a role one or more actions (read, create, update, delete; default read) on one entity, and every user holding the role gains them at once. Its other permissions are kept and actions it already has are skipped. An action with no matching permission for that entity is reported and skipped.")]
public class RolesPermissionsAddCommand : BaseCommand<RolePermissionsAddSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, RolePermissionsAddSettings settings)
    {
        try
        {
            var client = GetClient();

            int? entityId = await TryResolveEntityIdAsync(client, settings.Entity);

            var role = await client.GetRoleAsync(settings.RoleId)
                ?? throw new AnythinkCli.Client.AnythinkException($"Role {settings.RoleId} not found.", 404);

            var permIds = (role.Permissions ?? []).Select(p => p.Id).ToList();

            var allPerms = await client.GetPermissionsAsync();
            var actions = settings.Actions.Split(',').Select(a => a.Trim().ToLower()).ToList();

            var added = new List<string>();
            foreach (var action in actions)
            {
                var permName = $"{settings.Entity}:{action}";
                var existing = FindPermission(allPerms, permName, entityId);

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

            Renderer.Success($"Added {Markup.Escape(string.Join(", ", added))} on [#F97316]{Markup.Escape(settings.Entity)}[/] to role {settings.RoleId}.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }

    internal static async Task<int?> TryResolveEntityIdAsync(AnythinkCli.Client.AnythinkClient client, string entityName)
    {
        try
        {
            var entity = await client.GetEntityAsync(entityName);
            return entity.Id;
        }
        catch (AnythinkCli.Client.AnythinkException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    internal static Permission? FindPermission(List<Permission> allPerms, string permName, int? entityId)
    {
        return allPerms.FirstOrDefault(p =>
            p.Name == permName &&
            (entityId.HasValue ? p.EntityId == entityId.Value : p.EntityId == null));
    }
}

// ── roles permissions remove ─────────────────────────────────────────────────

public class RolePermissionsRemoveSettings : CommandSettings
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

    [CommandOption("-y|--yes")]
    [Description("Skip confirmation prompt")]
    public bool Yes { get; set; }
}

[CommandGuide("Takes one or more actions (default read) on one entity away from a role; every user holding the role loses them at once, and its other permissions are kept. Actions the role doesn't have are ignored. Use `roles_permissions_list` first to see what it grants.")]
public class RolesPermissionsRemoveCommand : BaseCommand<RolePermissionsRemoveSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, RolePermissionsRemoveSettings settings)
    {
        try
        {
            var client = GetClient();

            int? entityId = await RolesPermissionsAddCommand.TryResolveEntityIdAsync(client, settings.Entity);

            var role = await client.GetRoleAsync(settings.RoleId)
                ?? throw new AnythinkCli.Client.AnythinkException($"Role {settings.RoleId} not found.", 404);

            var permIds = (role.Permissions ?? []).Select(p => p.Id).ToList();

            var allPerms = await client.GetPermissionsAsync();
            var actions = settings.Actions.Split(',').Select(a => a.Trim().ToLower()).ToList();

            var removed = new List<string>();
            foreach (var action in actions)
            {
                var permName = $"{settings.Entity}:{action}";
                var existing = RolesPermissionsAddCommand.FindPermission(allPerms, permName, entityId);

                if (existing != null && permIds.Remove(existing.Id))
                {
                    removed.Add(action);
                }
            }

            if (removed.Count == 0)
            {
                Renderer.Info("No matching permissions to remove (not assigned or not found).");
                return 0;
            }

            if (!settings.Yes && !AnsiConsole.Confirm(
                    $"[yellow]Remove[/] [bold red]{Markup.Escape(string.Join(", ", removed))}[/] [yellow]on[/] {Markup.Escape(settings.Entity)} [yellow]from role[/] {settings.RoleId}[yellow]?[/]",
                    defaultValue: false))
            {
                Renderer.Info("Cancelled.");
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

            Renderer.Success($"Removed {Markup.Escape(string.Join(", ", removed))} on [#F97316]{Markup.Escape(settings.Entity)}[/] from role {settings.RoleId}.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}
