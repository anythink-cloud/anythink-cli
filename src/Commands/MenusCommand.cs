using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

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
                        Markup.Escape(item.DisplayName),
                        item.Icon,
                        Markup.Escape(item.Href),
                        "—"
                    );

                    if (children.TryGetValue(item.Id, out var kids))
                    {
                        foreach (var kid in kids)
                        {
                            Renderer.AddRow(table,
                                kid.Id.ToString(),
                                $"  └ {Markup.Escape(kid.DisplayName)}",
                                kid.Icon,
                                Markup.Escape(kid.Href),
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

            var displayName = settings.DisplayName
                ?? string.Join(' ', settings.Entity.Split('_').Select(w =>
                    char.ToUpper(w[0]) + w[1..]));

            var href = $"/org/{client.OrgId}/entities/{settings.Entity}";

            MenuItemResponse? item = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Adding '{displayName}' to menu {settings.MenuId}...", async _ =>
                {
                    item = await client.CreateMenuItemAsync(settings.MenuId, new CreateMenuItemRequest(
                        displayName,
                        settings.Icon,
                        href,
                        settings.ParentId
                    ));
                });

            Renderer.Success($"Menu item [#F97316]{Markup.Escape(item!.DisplayName)}[/] (id: {item.Id}) added to menu {settings.MenuId}.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}
