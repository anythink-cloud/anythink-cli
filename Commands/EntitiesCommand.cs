using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace AnythinkCli.Commands;

// ── entities list ─────────────────────────────────────────────────────────────

public class EmptySettings : CommandSettings { }

public class EntitiesListCommand : BaseCommand<EmptySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EmptySettings settings)
    {
        try
        {
            var client = GetClient();
            List<Entity> entities = [];

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching entities...", async _ =>
                {
                    entities = await client.GetEntitiesAsync();
                });

            if (entities.Count == 0)
            {
                Renderer.Info("No entities found.");
                return 0;
            }

            Renderer.Header($"Entities ({entities.Count})");

            var table = Renderer.BuildTable("Name", "Table", "Fields", "Public", "RLS", "System");
            foreach (var e in entities.OrderBy(x => x.Name))
            {
                Renderer.AddRow(table,
                    e.Name,
                    e.TableName,
                    (e.Fields?.Count ?? 0).ToString(),
                    e.IsPublic ? "yes" : "no",
                    e.EnableRls ? "yes" : "no",
                    e.IsSystem ? "yes" : "no"
                );
            }

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine("Run [bold #F97316]anythink fields list <entity>[/] to see the fields on an entity.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── entities get ─────────────────────────────────────────────────────────────

public class EntityGetSettings : CommandSettings
{
    [CommandArgument(0, "<NAME>")]
    [Description("Entity name")]
    public string Name { get; set; } = "";
}

public class EntitiesGetCommand : BaseCommand<EntityGetSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EntityGetSettings settings)
    {
        try
        {
            var client = GetClient();
            Entity? entity = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Fetching entity '{settings.Name}'...", async _ =>
                {
                    entity = await client.GetEntityAsync(settings.Name);
                });

            if (entity == null) return 1;

            Renderer.Header($"Entity: {entity.Name}");
            Renderer.KeyValue("Table", entity.TableName);
            Renderer.KeyValue("Public", entity.IsPublic ? "yes" : "no");
            Renderer.KeyValue("RLS enabled", entity.EnableRls ? "yes" : "no");
            Renderer.KeyValue("System entity", entity.IsSystem ? "yes" : "no");
            Renderer.KeyValue("Lock new records", entity.LockNewRecords ? "yes" : "no");

            var fields = entity.Fields ?? [];
            if (fields.Count > 0)
            {
                AnsiConsole.WriteLine();
                Renderer.Header($"Fields ({fields.Count})");
                var table = Renderer.BuildTable("ID", "Name", "Type", "Display", "Required", "Unique", "Indexed");
                foreach (var f in fields.OrderBy(x => x.Name))
                {
                    Renderer.AddRow(table,
                        f.Id.ToString(),
                        f.Name,
                        f.DatabaseType,
                        f.DisplayType,
                        f.IsRequired ? "yes" : "no",
                        f.IsUnique ? "yes" : "no",
                        f.IsIndexed ? "yes" : "no"
                    );
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

// ── entities create ───────────────────────────────────────────────────────────

public class EntityCreateSettings : CommandSettings
{
    [CommandArgument(0, "<NAME>")]
    [Description("Entity name (e.g. 'customers', 'orders')")]
    public string Name { get; set; } = "";

    [CommandOption("--public")]
    [Description("Make entity publicly queryable (no auth required)")]
    public bool IsPublic { get; set; }

    [CommandOption("--rls")]
    [Description("Enable row-level security")]
    public bool EnableRls { get; set; }

    [CommandOption("--lock-records")]
    [Description("Prevent creation of new records via API")]
    public bool LockNewRecords { get; set; }

    [CommandOption("--junction")]
    [Description("Mark as a many-to-many junction table")]
    public bool IsJunction { get; set; }
}

public class EntitiesCreateCommand : BaseCommand<EntityCreateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EntityCreateSettings settings)
    {
        try
        {
            var client = GetClient();
            Entity? entity = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Creating entity '{settings.Name}'...", async _ =>
                {
                    entity = await client.CreateEntityAsync(new CreateEntityRequest(
                        settings.Name,
                        settings.EnableRls,
                        settings.IsPublic,
                        settings.LockNewRecords,
                        settings.IsJunction
                    ));
                });

            Renderer.Success($"Entity [#F97316]{Markup.Escape(entity!.Name)}[/] created (table: {Markup.Escape(entity.TableName)}).");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── entities update ───────────────────────────────────────────────────────────

public class EntityUpdateSettings : CommandSettings
{
    [CommandArgument(0, "<NAME>")]
    [Description("Entity name to update")]
    public string Name { get; set; } = "";

    [CommandOption("--public <BOOL>")]
    [Description("Set public access (true/false)")]
    public bool? IsPublic { get; set; }

    [CommandOption("--rls <BOOL>")]
    [Description("Enable/disable row-level security (true/false)")]
    public bool? EnableRls { get; set; }

    [CommandOption("--lock-records <BOOL>")]
    [Description("Enable/disable record locking (true/false)")]
    public bool? LockNewRecords { get; set; }
}

public class EntitiesUpdateCommand : BaseCommand<EntityUpdateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EntityUpdateSettings settings)
    {
        try
        {
            var client = GetClient();

            // Fetch current to fill defaults
            var current = await client.GetEntityAsync(settings.Name);

            var req = new UpdateEntityRequest(
                settings.EnableRls ?? current.EnableRls,
                settings.IsPublic ?? current.IsPublic,
                settings.LockNewRecords ?? current.LockNewRecords
            );

            Entity? updated = null;
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Updating entity '{settings.Name}'...", async _ =>
                {
                    updated = await client.UpdateEntityAsync(settings.Name, req);
                });

            Renderer.Success($"Entity [#F97316]{Markup.Escape(updated!.Name)}[/] updated.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── entities delete ───────────────────────────────────────────────────────────

public class EntityDeleteSettings : CommandSettings
{
    [CommandArgument(0, "<NAME>")]
    [Description("Entity name to delete")]
    public string Name { get; set; } = "";

    [CommandOption("--yes")]
    [Description("Skip confirmation prompt")]
    public bool Yes { get; set; }
}

public class EntitiesDeleteCommand : BaseCommand<EntityDeleteSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EntityDeleteSettings settings)
    {
        if (!settings.Yes)
        {
            var confirm = AnsiConsole.Confirm(
                $"[yellow]Delete entity[/] [bold red]{settings.Name}[/][yellow]? This will drop all data.[/]",
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
                .StartAsync($"Deleting entity '{settings.Name}'...", async _ =>
                {
                    await client.DeleteEntityAsync(settings.Name);
                });

            Renderer.Success($"Entity [#F97316]{Markup.Escape(settings.Name)}[/] deleted.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}
