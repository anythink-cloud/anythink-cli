using AnythinkCli.Client;
using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Text.Json;

namespace AnythinkCli.Commands;

// ── menus list ───────────────────────────────────────────────────────────────

public class MenusListCommand : BaseCommand<EmptySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EmptySettings settings)
    {
        try
        {
            var client = GetClient();
            List<MenuResponse> menus = [];

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching menus...", async _ =>
                {
                    menus = await client.GetMenusAsync();
                });

            if (menus.Count == 0)
            {
                Renderer.Info("No menus found.");
                return 0;
            }

            foreach (var menu in menus)
            {
                Renderer.Header($"{menu.Name} (id: {menu.Id}, role: {menu.RoleId})");

                if (menu.Items.Count == 0)
                {
                    Renderer.Info("  No items.");
                    continue;
                }

                // Find top-level items (no parent) and group items
                var topLevel = menu.Items.Where(i => i.ParentId == null).OrderBy(i => i.SortOrder).ToList();
                var children = menu.Items.Where(i => i.ParentId != null).GroupBy(i => i.ParentId!.Value).ToDictionary(g => g.Key, g => g.OrderBy(i => i.SortOrder).ToList());

                var table = Renderer.BuildTable("ID", "Name", "Icon", "Href", "Parent");
                foreach (var item in topLevel)
                {
                    Renderer.AddRow(table,
                        item.Id.ToString(),
                        item.DisplayName,
                        item.Icon,
                        item.Href,
                        "—"
                    );

                    if (children.TryGetValue(item.Id, out var kids))
                    {
                        foreach (var kid in kids)
                        {
                            Renderer.AddRow(table,
                                kid.Id.ToString(),
                                $"  └ {kid.DisplayName}",
                                kid.Icon,
                                kid.Href,
                                item.Id.ToString()
                            );
                        }
                    }
                }

                AnsiConsole.Write(table);
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

// ── menus add-item ───────────────────────────────────────────────────────────

public class MenuAddItemSettings : CommandSettings
{
    [CommandArgument(0, "<MENU_ID>")]
    [Description("Menu ID to add the item to")]
    public int MenuId { get; set; }

    [CommandArgument(1, "<ENTITY>")]
    [Description("Entity name (used for display name and href)")]
    public string Entity { get; set; } = null!;

    [CommandOption("--icon <ICON>")]
    [Description("Lucide icon name (e.g. MessageCircle, Target, FileText)")]
    [DefaultValue("Database")]
    public string Icon { get; set; } = "Database";

    [CommandOption("--name <NAME>")]
    [Description("Display name (defaults to entity name, title-cased)")]
    public string? DisplayName { get; set; }

    [CommandOption("--parent <ID>")]
    [Description("Parent menu item ID (for nested items)")]
    public int ParentId { get; set; }
}

public class MenuAddItemCommand : BaseCommand<MenuAddItemSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, MenuAddItemSettings settings)
    {
        try
        {
            var client = GetClient();

            var lookup = await MenuTree.LookUpEntityAsync(client, settings.Entity);
            if (lookup == MenuTree.EntityLookup.Missing)
            {
                Renderer.Error(MenuTree.EntityMissing(settings.Entity));
                return 1;
            }

            var displayName = settings.DisplayName
                ?? string.Join(' ', settings.Entity.Split('_').Select(w =>
                    char.ToUpper(w[0]) + w[1..]));

            var href = MenuTree.EntityHref(client, settings.Entity);

            MenuItemResponse? item = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Adding '{Markup.Escape(displayName)}' to menu {settings.MenuId}...", async _ =>
                {
                    item = await client.CreateMenuItemAsync(settings.MenuId, new CreateMenuItemRequest(
                        displayName,
                        settings.Icon,
                        href,
                        settings.ParentId
                    ));
                });

            Renderer.Success($"Menu item [#F97316]{Markup.Escape(item!.DisplayName)}[/] (id: {item.Id}) added to menu {settings.MenuId}.");
            if (lookup == MenuTree.EntityLookup.Unchecked)
                Renderer.Warn(Markup.Escape(MenuTree.EntityUnchecked(settings.Entity)));
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── menus get ────────────────────────────────────────────────────────────────

public class MenuGetSettings : CommandSettings
{
    [CommandArgument(0, "<MENU_ID>")]
    [Description("Menu ID")]
    public int MenuId { get; set; }

    [CommandOption("--json")]
    [Description("Output raw JSON instead of a table")]
    public bool Json { get; set; }
}

public class MenuGetCommand : BaseCommand<MenuGetSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, MenuGetSettings settings)
    {
        try
        {
            var client = GetClient();
            var menu = await MenuTree.TryGetMenuAsync(client, settings.MenuId);
            if (menu is null)
            {
                Renderer.Error($"Menu {settings.MenuId} not found.");
                return 1;
            }

            // The single-menu read has no role, so it comes from the list (when that can be read).
            var roleId = (await MenuTree.TryGetMenusAsync(client)).FirstOrDefault(m => m.Id == menu.Id)?.RoleId;

            if (settings.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(
                    new { id = menu.Id, name = menu.Name, role_id = roleId, items = menu.Items }, JsonOutput.PrettyRelaxed));
                return 0;
            }

            Renderer.Header(roleId is { } role ? $"{menu.Name} (id: {menu.Id}, role: {role})" : $"{menu.Name} (id: {menu.Id})");

            if (menu.Items.Count == 0)
            {
                Renderer.Info("No items.");
                return 0;
            }

            var table = Renderer.BuildTable("ID", "Name", "Icon", "Href", "Parent", "Locked");
            foreach (var node in MenuTree.Walk(menu.Items))
            {
                Renderer.AddRow(table,
                    node.Item.Id.ToString(),
                    node.Depth == 0 ? node.Item.DisplayName : $"{new string(' ', node.Depth * 2)}└ {node.Item.DisplayName}",
                    node.Item.Icon,
                    node.Item.Href,
                    node.Item.ParentId?.ToString(),
                    node.Item.Locked ? "yes" : null);
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

// ── menus create ─────────────────────────────────────────────────────────────

public class MenuCreateSettings : CommandSettings
{
    [CommandArgument(0, "<NAME>")]
    [Description("Menu name")]
    public string Name { get; set; } = "";

    [CommandArgument(1, "<ROLE_ID>")]
    [Description("ID of the role the menu is shown to")]
    public int RoleId { get; set; }
}

public class MenuCreateCommand : BaseCommand<MenuCreateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, MenuCreateSettings settings)
    {
        try
        {
            var client = GetClient();
            var existing = await MenuTree.TryGetMenusAsync(client);
            MenuResponse? menu = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Creating menu '{Markup.Escape(settings.Name)}'...", async _ =>
                {
                    menu = await client.CreateMenuAsync(new CreateMenuRequest(settings.Name, settings.RoleId));
                });

            Renderer.Success($"Menu [#F97316]{Markup.Escape(menu!.Name)}[/] (id: {menu.Id}) created for role {settings.RoleId}.");
            if (MenuTree.SharedRoleWarning(existing, settings.RoleId) is { } warning)
                Renderer.Warn(Markup.Escape(warning));
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── menus update ─────────────────────────────────────────────────────────────

public class MenuUpdateSettings : CommandSettings
{
    [CommandArgument(0, "<MENU_ID>")]
    [Description("Menu ID to update")]
    public int MenuId { get; set; }

    [CommandOption("--name <NAME>")]
    [Description("New menu name")]
    public string? Name { get; set; }

    [CommandOption("--role <ROLE_ID>")]
    [Description("ID of the role the menu is shown to")]
    public int? RoleId { get; set; }
}

public class MenuUpdateCommand : BaseCommand<MenuUpdateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, MenuUpdateSettings settings)
    {
        if (settings.Name is null && settings.RoleId is null)
        {
            Renderer.Error("Nothing to update. Pass --name and/or --role.");
            return 1;
        }

        try
        {
            var client = GetClient();
            // The list is the only read that includes each menu's role, and the update replaces both fields.
            var menus = await client.GetMenusAsync();
            var current = menus.FirstOrDefault(m => m.Id == settings.MenuId);
            if (current is null)
            {
                Renderer.Error($"Menu {settings.MenuId} not found.");
                return 1;
            }

            var request = new CreateMenuRequest(settings.Name ?? current.Name, settings.RoleId ?? current.RoleId);

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Updating menu {settings.MenuId}...", async _ =>
                {
                    await client.UpdateMenuAsync(settings.MenuId, request);
                });

            Renderer.Success($"Menu [#F97316]{Markup.Escape(request.Name)}[/] (id: {settings.MenuId}) updated; shown to role {request.RoleId}.");
            if (request.RoleId != current.RoleId && MenuTree.SharedRoleWarning(menus, request.RoleId, settings.MenuId) is { } warning)
                Renderer.Warn(Markup.Escape(warning));
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── menus delete ─────────────────────────────────────────────────────────────

public class MenuDeleteSettings : CommandSettings
{
    [CommandArgument(0, "<MENU_ID>")]
    [Description("Menu ID to delete")]
    public int MenuId { get; set; }

    [CommandOption("-y|--yes")]
    [Description("Skip confirmation prompt")]
    public bool Yes { get; set; }
}

public class MenuDeleteCommand : BaseCommand<MenuDeleteSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, MenuDeleteSettings settings)
    {
        try
        {
            var client = GetClient();
            var menu = await MenuTree.TryGetMenuAsync(client, settings.MenuId);
            if (menu is null)
            {
                Renderer.Error($"Menu {settings.MenuId} not found.");
                return 1;
            }

            var impact = MenuTree.DeleteImpact(menu, await MenuTree.TryGetMenusAsync(client));
            var name = Markup.Escape(menu.Name);
            var itemsNote = Markup.Escape(impact.ItemsNote);
            var roleNote = Markup.Escape(impact.RoleWillBeNote);
            var roleDoneNote = Markup.Escape(impact.RoleNowNote);

            if (!settings.Yes && !AnsiConsole.Confirm(
                    $"[yellow]Delete menu[/] [bold red]{name}[/] [yellow](id: {menu.Id}){itemsNote}?{roleNote}[/]",
                    defaultValue: false))
            {
                Renderer.Info("Cancelled.");
                return 0;
            }

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Deleting menu {settings.MenuId}...", async _ =>
                {
                    await client.DeleteMenuAsync(settings.MenuId);
                });

            Renderer.Success($"Menu [#F97316]{name}[/] (id: {menu.Id}){itemsNote} deleted.{roleDoneNote}");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── menus update-item ────────────────────────────────────────────────────────

public class MenuUpdateItemSettings : CommandSettings
{
    [CommandArgument(0, "<MENU_ID>")]
    [Description("Menu ID the item belongs to")]
    public int MenuId { get; set; }

    [CommandArgument(1, "<ITEM_ID>")]
    [Description("Menu item ID to update")]
    public int ItemId { get; set; }

    [CommandOption("--name <NAME>")]
    [Description("New display name")]
    public string? DisplayName { get; set; }

    [CommandOption("--icon <ICON>")]
    [Description("New Lucide icon name (e.g. MessageCircle, Target, FileText)")]
    public string? Icon { get; set; }

    [CommandOption("--entity <ENTITY>")]
    [Description("Point the item at an entity's page (use this or --href)")]
    public string? Entity { get; set; }

    [CommandOption("--href <PATH>")]
    [Description("Point the item at any path (use this or --entity)")]
    public string? Href { get; set; }

    [CommandOption("--parent <ID>")]
    [Description("Parent menu item ID to nest under (0 for top level)")]
    public int? ParentId { get; set; }
}

public class MenuUpdateItemCommand : BaseCommand<MenuUpdateItemSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, MenuUpdateItemSettings settings)
    {
        if (settings.Entity is not null && settings.Href is not null)
        {
            Renderer.Error("Use --entity or --href, not both.");
            return 1;
        }

        if (settings.DisplayName is null && settings.Icon is null && settings.Entity is null
            && settings.Href is null && settings.ParentId is null)
        {
            Renderer.Error("Nothing to update. Pass --name, --icon, --entity, --href and/or --parent.");
            return 1;
        }

        try
        {
            var client = GetClient();
            var menu = await MenuTree.TryGetMenuAsync(client, settings.MenuId);
            if (menu is null)
            {
                Renderer.Error($"Menu {settings.MenuId} not found.");
                return 1;
            }

            var node = MenuTree.Find(menu, settings.ItemId);
            if (node is null)
            {
                Renderer.Error($"Menu item {settings.ItemId} not found in menu {settings.MenuId}.");
                return 1;
            }

            var item = node.Item;
            if (item.Locked)
            {
                Renderer.Error($"Menu item {item.Id} is locked and can't be changed.");
                return 1;
            }

            var parentId = settings.ParentId ?? item.ParentId ?? 0;
            if (settings.ParentId is { } requestedParent && MenuTree.ParentProblem(menu, item, requestedParent) is { } problem)
            {
                Renderer.Error(problem);
                return 1;
            }

            var lookup = settings.Entity is null ? MenuTree.EntityLookup.Found : await MenuTree.LookUpEntityAsync(client, settings.Entity);
            if (lookup == MenuTree.EntityLookup.Missing)
            {
                Renderer.Error(MenuTree.EntityMissing(settings.Entity!));
                return 1;
            }

            var request = new CreateMenuItemRequest(
                settings.DisplayName ?? item.DisplayName,
                settings.Icon ?? item.Icon,
                settings.Entity is not null ? MenuTree.EntityHref(client, settings.Entity) : settings.Href ?? item.Href,
                parentId);

            var moved = parentId != (item.ParentId ?? 0);

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Updating menu item {item.Id}...", async _ =>
                {
                    await client.UpdateMenuItemAsync(settings.MenuId, item.Id, request);
                });

            if (moved)
            {
                try
                {
                    await client.ReorderMenuItemsAsync(settings.MenuId, [MenuTree.PlaceLast(menu, item, parentId)]);
                }
                catch (Exception ex)
                {
                    Renderer.Warn(Markup.Escape("The item was updated, but it could not be placed last among its new siblings. " +
                        $"Running update-item again won't retry that; to place it, run '{MenuTree.PlaceLastCommand(menu, item, parentId, settings.MenuId)}'."));
                    HandleError(ex);
                    return 1;
                }
            }

            var placedNote = Markup.Escape(!moved ? "" : parentId == 0 ? "; now last at the top level" : $"; now last under item {parentId}");
            Renderer.Success($"Menu item [#F97316]{Markup.Escape(request.DisplayName)}[/] (id: {item.Id}) updated in menu {settings.MenuId}{placedNote}.");
            if (lookup == MenuTree.EntityLookup.Unchecked)
                Renderer.Warn(Markup.Escape(MenuTree.EntityUnchecked(settings.Entity!)));
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── menus remove-item ────────────────────────────────────────────────────────

public class MenuRemoveItemSettings : CommandSettings
{
    [CommandArgument(0, "<MENU_ID>")]
    [Description("Menu ID the item belongs to")]
    public int MenuId { get; set; }

    [CommandArgument(1, "<ITEM_ID>")]
    [Description("Menu item ID to remove")]
    public int ItemId { get; set; }

    [CommandOption("-y|--yes")]
    [Description("Skip confirmation prompt")]
    public bool Yes { get; set; }
}

public class MenuRemoveItemCommand : BaseCommand<MenuRemoveItemSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, MenuRemoveItemSettings settings)
    {
        try
        {
            var client = GetClient();
            var menu = await MenuTree.TryGetMenuAsync(client, settings.MenuId);
            if (menu is null)
            {
                Renderer.Error($"Menu {settings.MenuId} not found.");
                return 1;
            }

            var node = MenuTree.Find(menu, settings.ItemId);
            if (node is null)
            {
                Renderer.Error($"Menu item {settings.ItemId} not found in menu {settings.MenuId}.");
                return 1;
            }

            var item = node.Item;
            if (item.Locked)
            {
                Renderer.Error($"Menu item {item.Id} is locked and can't be removed.");
                return 1;
            }

            var stuck = item.Items.Where(c => c.Locked || c.Items.Count > 0).Select(c => c.Id).ToList();
            if (stuck.Count > 0)
            {
                Renderer.Error($"Menu item {item.Id} can't be removed: child items that are locked or have items of their own " +
                               $"({string.Join(", ", stuck)}) must be removed or moved first.");
                return 1;
            }

            var name = Markup.Escape(item.DisplayName);
            var childrenNote = Markup.Escape(item.Items.Count == 0
                ? ""
                : $" and its {MenuTree.Count(item.Items.Count, "child item")} ({string.Join(", ", item.Items.Select(c => c.Id))})");

            if (!settings.Yes && !AnsiConsole.Confirm(
                    $"[yellow]Remove menu item[/] [bold red]{name}[/] [yellow](id: {item.Id}){childrenNote} from menu {menu.Id}?[/]",
                    defaultValue: false))
            {
                Renderer.Info("Cancelled.");
                return 0;
            }

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Removing menu item {item.Id}...", async _ =>
                {
                    await client.DeleteMenuItemAsync(settings.MenuId, item.Id);
                });

            Renderer.Success($"Menu item [#F97316]{name}[/] (id: {item.Id}){childrenNote} removed from menu {menu.Id}.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── menus reorder-items ──────────────────────────────────────────────────────

public class MenuReorderItemsSettings : CommandSettings
{
    [CommandArgument(0, "<MENU_ID>")]
    [Description("Menu ID the items belong to")]
    public int MenuId { get; set; }

    [CommandArgument(1, "<ITEM_IDS>")]
    [Description("Comma-separated item IDs in the order they should appear, e.g. 301,299,300. All must share one parent. The items you name take the first places within their group; siblings you leave out follow in their current order, and locked items keep their place")]
    public string ItemIds { get; set; } = "";
}

public class MenuReorderItemsCommand : BaseCommand<MenuReorderItemsSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, MenuReorderItemsSettings settings)
    {
        if (!MenuTree.TryParseIds(settings.ItemIds, "Item", out var ids, out var parseError))
        {
            Renderer.Error(parseError!);
            return 1;
        }

        try
        {
            var client = GetClient();
            var menu = await MenuTree.TryGetMenuAsync(client, settings.MenuId);
            if (menu is null)
            {
                Renderer.Error($"Menu {settings.MenuId} not found.");
                return 1;
            }

            var (plan, error) = MenuTree.PlanItemOrder(menu, ids);
            if (plan is null)
            {
                Renderer.Error(error!);
                return 1;
            }

            if (plan.Count == 0)
            {
                Renderer.Info("The items are already in that order.");
                return 0;
            }

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Reordering items in menu {settings.MenuId}...", async _ =>
                {
                    await client.ReorderMenuItemsAsync(settings.MenuId, plan);
                });

            Renderer.Success($"Items in menu {settings.MenuId} reordered; moved {Markup.Escape(string.Join(", ", plan.Select(p => p.ItemId)))}.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── menus reorder ────────────────────────────────────────────────────────────

public class MenuReorderSettings : CommandSettings
{
    [CommandArgument(0, "<MENU_IDS>")]
    [Description("Comma-separated menu IDs in the order they should appear, e.g. 92,90,91. Menus you leave out follow in their current order")]
    public string MenuIds { get; set; } = "";
}

public class MenuReorderCommand : BaseCommand<MenuReorderSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, MenuReorderSettings settings)
    {
        if (!MenuTree.TryParseIds(settings.MenuIds, "Menu", out var ids, out var parseError))
        {
            Renderer.Error(parseError!);
            return 1;
        }

        try
        {
            var client = GetClient();
            var menus = await client.GetMenusAsync();

            var (plan, error) = MenuTree.PlanMenuOrder(menus, ids);
            if (plan is null)
            {
                Renderer.Error(error!);
                return 1;
            }

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Reordering menus...", async _ =>
                {
                    await client.ReorderMenusAsync(plan);
                });

            Renderer.Success($"Menus reordered: {Markup.Escape(string.Join(", ", plan.Select(p => p.MenuId)))}.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── shared helpers ───────────────────────────────────────────────────────────

internal sealed record MenuNode(MenuItemResponse Item, IReadOnlyList<MenuItemResponse> Siblings, int Depth);

internal static class MenuTree
{
    public static string EntityHref(AnythinkClient client, string entity) => $"/org/{client.OrgId}/entities/{entity}";

    public static string Count(int n, string noun) => n == 1 ? $"{n} {noun}" : $"{n} {noun}s";

    public static async Task<MenuResponse?> TryGetMenuAsync(AnythinkClient client, int menuId)
    {
        try
        {
            return await client.GetMenuAsync(menuId);
        }
        catch (AnythinkException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    public enum EntityLookup { Found, Missing, Unchecked }

    // Only a 404 means the entity is missing; the API decides everything else, so a lookup that fails any other way doesn't block.
    public static async Task<EntityLookup> LookUpEntityAsync(AnythinkClient client, string entity)
    {
        try
        {
            await client.GetEntityAsync(entity, includeSystem: true);
            return EntityLookup.Found;
        }
        catch (AnythinkException ex) when (ex.StatusCode == 404)
        {
            return EntityLookup.Missing;
        }
        catch (Exception ex) when (ex is AnythinkException or HttpRequestException)
        {
            return EntityLookup.Unchecked;
        }
    }

    public static string EntityMissing(string entity) =>
        $"Entity '{entity}' not found, so the menu item would point at nothing. Check the name with 'anythink entities list'.";

    public static string EntityUnchecked(string entity) =>
        $"Couldn't check that entity '{entity}' exists, so the menu item was saved as given. Check the name with 'anythink entities list'.";

    // Context only (roles, warnings): a failed lookup must not stop the command it is decorating.
    public static async Task<List<MenuResponse>> TryGetMenusAsync(AnythinkClient client)
    {
        try
        {
            return await client.GetMenusAsync();
        }
        catch (AnythinkException)
        {
            return [];
        }
    }

    public sealed record DeleteSummary(string ItemsNote, string RoleWillBeNote, string RoleNowNote);

    public static DeleteSummary DeleteImpact(MenuResponse menu, IReadOnlyList<MenuResponse> menus)
    {
        var itemCount = Walk(menu.Items).Count();
        var lockedCount = Walk(menu.Items).Count(n => n.Item.Locked);
        var itemsNote = itemCount == 0
            ? ""
            : $" and its {Count(itemCount, "item")}" + (lockedCount > 0 ? $", including {Count(lockedCount, "locked built-in item")}" : "");

        var roleId = menus.FirstOrDefault(m => m.Id == menu.Id)?.RoleId;
        var emptied = roleId is { } role && menus.All(m => m.Id == menu.Id || m.RoleId != role);
        return new DeleteSummary(
            itemsNote,
            emptied ? $" Role {roleId} will be left with no menu." : "",
            emptied ? $" Role {roleId} now has no menu." : "");
    }

    public static string? SharedRoleWarning(IEnumerable<MenuResponse> menus, int roleId, int exceptMenuId = 0)
    {
        var other = menus.FirstOrDefault(m => m.RoleId == roleId && m.Id != exceptMenuId);
        return other is null
            ? null
            : $"Role {roleId} already has a menu ('{other.Name}', id: {other.Id}). A role is shown only its first menu, so this one may not appear.";
    }

    public static IEnumerable<MenuNode> Walk(IReadOnlyList<MenuItemResponse> items, int depth = 0)
    {
        foreach (var item in items)
        {
            yield return new MenuNode(item, items, depth);
            foreach (var child in Walk(item.Items, depth + 1))
                yield return child;
        }
    }

    public static MenuNode? Find(MenuResponse menu, int itemId) =>
        Walk(menu.Items).FirstOrDefault(n => n.Item.Id == itemId);

    public static string? ParentProblem(MenuResponse menu, MenuItemResponse item, int parentId)
    {
        if (parentId == 0)
            return null;
        if (parentId == item.Id)
            return "A menu item can't be its own parent.";
        if (Find(menu, parentId) is null)
            return $"Parent menu item {parentId} not found in menu {menu.Id}.";
        if (Walk(item.Items).Any(n => n.Item.Id == parentId))
            return $"Menu item {parentId} sits inside item {item.Id}, so it can't become its parent.";
        return null;
    }

    public static bool TryParseIds(string text, string noun, out List<int> ids, out string? error)
    {
        ids = [];
        error = null;

        foreach (var token in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(token, out var id) || id <= 0)
            {
                error = $"{noun} IDs must be whole numbers separated by commas; '{token}' isn't one.";
                return false;
            }

            if (ids.Contains(id))
            {
                error = $"{noun} {id} is listed more than once.";
                return false;
            }

            ids.Add(id);
        }

        if (ids.Count == 0)
            error = $"Give at least one {noun.ToLowerInvariant()} ID.";
        return ids.Count > 0;
    }

    public static (List<ReorderMenuItemRequest>? Plan, string? Error) PlanItemOrder(MenuResponse menu, IReadOnlyList<int> ids)
    {
        var named = new List<MenuNode>();
        foreach (var id in ids)
        {
            var node = Find(menu, id);
            if (node is null)
                return (null, $"Menu item {id} not found in menu {menu.Id}.");
            if (node.Item.Locked)
                return (null, $"Menu item {id} is locked and can't be moved.");
            if (named.Count > 0 && node.Item.ParentId != named[0].Item.ParentId)
                return (null, $"Items {named[0].Item.Id} and {id} have different parents. Reorder one group of siblings at a time.");
            named.Add(node);
        }

        var slots = named[0].Siblings.OrderBy(s => s.SortOrder).ToList();
        var order = named.Select(n => n.Item).Concat(slots.Where(s => !s.Locked && !ids.Contains(s.Id))).ToList();
        return Number(slots, order);
    }

    public static string PlaceLastCommand(MenuResponse menu, MenuItemResponse item, int parentId, int menuId)
    {
        var siblings = (parentId == 0 ? menu.Items : Find(menu, parentId)!.Item.Items)
            .Where(s => s.Id != item.Id && !s.Locked)
            .OrderBy(s => s.SortOrder)
            .Select(s => s.Id);
        return $"anythink menus reorder-items {menuId} {string.Join(",", siblings.Append(item.Id))}";
    }

    // The update keeps the old sort order, which can tie with a new sibling; last is where add-item puts new items.
    public static ReorderMenuItemRequest PlaceLast(MenuResponse menu, MenuItemResponse item, int parentId)
    {
        var siblings = (parentId == 0 ? menu.Items : Find(menu, parentId)!.Item.Items).Where(s => s.Id != item.Id).ToList();
        return new ReorderMenuItemRequest(item.Id, siblings.Count == 0 ? 0 : siblings.Max(s => s.SortOrder) + 1, parentId == 0 ? null : parentId);
    }

    // Locked items keep their place and number (the API ignores them); each run of movable items between them reuses its own numbers unless they tie.
    private static (List<ReorderMenuItemRequest>? Plan, string? Error) Number(
        IReadOnlyList<MenuItemResponse> slots, IReadOnlyList<MenuItemResponse> order)
    {
        var plan = new List<ReorderMenuItemRequest>();
        var next = 0;
        for (var i = 0; i < slots.Count;)
        {
            if (slots[i].Locked)
            {
                i++;
                continue;
            }

            var end = i;
            while (end < slots.Count && !slots[end].Locked)
                end++;

            var count = end - i;
            var current = slots.Skip(i).Take(count).ToList();
            var arranged = order.Skip(next).Take(count).ToList();
            next += count;
            if (arranged.Select(item => item.Id).SequenceEqual(current.Select(item => item.Id)))
            {
                i = end;
                continue;
            }

            var numbers = current.Select(item => item.SortOrder).ToList();
            if (numbers.Distinct().Count() != count)
            {
                int? lower = i > 0 ? slots[i - 1].SortOrder : null;
                int? upper = end < slots.Count ? slots[end].SortOrder : null;
                if (lower is { } below && upper is { } above && above - below - 1 < count)
                    return (null, $"There isn't room to number the items between locked items {slots[i - 1].Id} and {slots[end].Id}, and locked items can't be renumbered. " +
                                  "Move some of those items to another parent with 'menus update-item', or reorder only items outside that pair.");

                var first = lower is { } l ? l + 1 : upper is { } u ? u - count : 0;
                numbers = Enumerable.Range(first, count).ToList();
            }

            for (var n = 0; n < count; n++)
                if (arranged[n].SortOrder != numbers[n])
                    plan.Add(new ReorderMenuItemRequest(arranged[n].Id, numbers[n], arranged[n].ParentId));

            i = end;
        }

        return (plan, null);
    }

    public static (List<ReorderMenuRequest>? Plan, string? Error) PlanMenuOrder(IReadOnlyList<MenuResponse> menus, IReadOnlyList<int> ids)
    {
        foreach (var id in ids)
            if (menus.All(m => m.Id != id))
                return (null, $"Menu {id} not found.");

        var order = ids.Concat(menus.Select(m => m.Id).Where(id => !ids.Contains(id)))
            .Select((id, index) => new ReorderMenuRequest(id, index))
            .ToList();
        return (order, null);
    }
}
