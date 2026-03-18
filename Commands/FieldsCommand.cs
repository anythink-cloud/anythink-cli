using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace AnythinkCli.Commands;

// ── fields list ───────────────────────────────────────────────────────────────

public class FieldsEntitySettings : CommandSettings
{
    [CommandArgument(0, "<ENTITY>")]
    [Description("Entity name")]
    public string Entity { get; set; } = "";
}

public class FieldsListCommand : BaseCommand<FieldsEntitySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, FieldsEntitySettings settings)
    {
        try
        {
            var client = GetClient();
            List<Field> fields = [];

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Fetching fields for '{settings.Entity}'...", async _ =>
                {
                    fields = await client.GetFieldsAsync(settings.Entity);
                });

            Renderer.Header($"{settings.Entity} → Fields ({fields.Count})");

            if (fields.Count == 0)
            {
                Renderer.Info("No fields found.");
                return 0;
            }

            var table = Renderer.BuildTable("ID", "Name", "Label", "DB Type", "Display", "Required", "Unique", "Indexed", "Locked");
            foreach (var f in fields.OrderBy(x => x.Name))
            {
                Renderer.AddRow(table,
                    f.Id.ToString(),
                    f.Name,
                    f.Label ?? "",
                    f.DatabaseType,
                    f.DisplayType,
                    f.IsRequired ? "yes" : "no",
                    f.IsUnique ? "yes" : "no",
                    f.IsIndexed ? "yes" : "no",
                    f.Locked ? "yes" : "no"
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

// ── fields add ────────────────────────────────────────────────────────────────

public class FieldAddSettings : CommandSettings
{
    [CommandArgument(0, "<ENTITY>")]
    [Description("Entity name")]
    public string Entity { get; set; } = "";

    [CommandArgument(1, "<NAME>")]
    [Description("Field name (snake_case)")]
    public string Name { get; set; } = "";

    [CommandOption("--type <TYPE>")]
    [Description("Database type: varchar, varchar[], text, integer, integer[], bigint, bigint[], decimal, decimal[], boolean, date, timestamp, jsonb, geo, file, user, one-to-one, one-to-many, many-to-one, many-to-many, dynamic-reference")]
    public string? DatabaseType { get; set; }

    [CommandOption("--display <DISPLAY>")]
    [Description("Display type: input, textarea, rich-text, select, entity-select, radio, country-select, checkbox, short-date, long-date, timestamp, relationship, file, secret, user, jsonb, geo, dynamic-reference")]
    public string? DisplayType { get; set; }

    [CommandOption("--label <LABEL>")]
    [Description("Human-readable label")]
    public string? Label { get; set; }

    [CommandOption("--description <DESC>")]
    [Description("Field description")]
    public string? Description { get; set; }

    [CommandOption("--default <VALUE>")]
    [Description("Default value")]
    public string? DefaultValue { get; set; }

    [CommandOption("--required")]
    [Description("Mark field as required")]
    public bool IsRequired { get; set; }

    [CommandOption("--unique")]
    [Description("Enforce unique constraint")]
    public bool IsUnique { get; set; }

    [CommandOption("--searchable")]
    [Description("Enable full-text search on this field")]
    public bool IsSearchable { get; set; }

    [CommandOption("--indexed")]
    [Description("Add database index")]
    public bool IsIndexed { get; set; }
}

public class FieldsAddCommand : BaseCommand<FieldAddSettings>
{
    // Sensible default display type for each db type
    private static readonly Dictionary<string, string> DefaultDisplay = new(StringComparer.OrdinalIgnoreCase)
    {
        ["varchar"]           = "input",
        ["varchar[]"]         = "select",
        ["text"]              = "textarea",
        ["integer"]           = "input",
        ["integer[]"]         = "select",
        ["bigint"]            = "input",
        ["bigint[]"]          = "select",
        ["decimal"]           = "input",
        ["decimal[]"]         = "select",
        ["boolean"]           = "checkbox",
        ["date"]              = "short-date",
        ["timestamp"]         = "timestamp",
        ["jsonb"]             = "jsonb",
        ["geo"]               = "geo",
        ["file"]              = "file",
        ["user"]              = "user",
        ["one-to-one"]        = "relationship",
        ["one-to-many"]       = "relationship",
        ["many-to-one"]       = "relationship",
        ["many-to-many"]      = "relationship",
        ["dynamic-reference"] = "dynamic-reference",
    };

    public override async Task<int> ExecuteAsync(CommandContext context, FieldAddSettings settings)
    {
        var dbType = settings.DatabaseType;
        if (string.IsNullOrEmpty(dbType))
        {
            dbType = AnsiConsole.Prompt(
                Renderer.Prompt<string>()
                    .Title("Select [#F97316]database type[/]:")
                    .AddChoices(
                        "varchar", "varchar[]", "text",
                        "integer", "integer[]", "bigint", "bigint[]", "decimal", "decimal[]",
                        "boolean", "date", "timestamp", "jsonb",
                        "geo", "file", "user",
                        "one-to-one", "one-to-many", "many-to-one", "many-to-many", "dynamic-reference"));
        }

        var displayType = settings.DisplayType
            ?? DefaultDisplay.GetValueOrDefault(dbType, "input");

        try
        {
            var client = GetClient();
            Field? field = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Adding field '{settings.Name}' to '{settings.Entity}'...", async _ =>
                {
                    field = await client.AddFieldAsync(settings.Entity, new CreateFieldRequest(
                        settings.Name,
                        dbType,
                        displayType,
                        settings.Label,
                        settings.Description,
                        settings.DefaultValue,
                        settings.IsRequired,
                        settings.IsUnique,
                        settings.IsSearchable,
                        settings.IsIndexed
                    ));
                });

            Renderer.Success($"Field [#F97316]{Markup.Escape(field!.Name)}[/] (id: {Markup.Escape(field.Id.ToString())}) added to [#F97316]{Markup.Escape(settings.Entity)}[/].");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── fields delete ─────────────────────────────────────────────────────────────

public class FieldDeleteSettings : CommandSettings
{
    [CommandArgument(0, "<ENTITY>")]
    [Description("Entity name")]
    public string Entity { get; set; } = "";

    [CommandArgument(1, "<FIELD_ID>")]
    [Description("Field ID (integer)")]
    public int FieldId { get; set; }

    [CommandOption("--yes")]
    [Description("Skip confirmation")]
    public bool Yes { get; set; }
}

public class FieldsDeleteCommand : BaseCommand<FieldDeleteSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, FieldDeleteSettings settings)
    {
        if (!settings.Yes)
        {
            var confirm = AnsiConsole.Confirm(
                $"[yellow]Delete field[/] [bold red]{settings.FieldId}[/] [yellow]from[/] [bold]{settings.Entity}[/][yellow]?[/]",
                defaultValue: false);
            if (!confirm) { Renderer.Info("Cancelled."); return 0; }
        }

        try
        {
            var client = GetClient();

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Deleting field...", async _ =>
                {
                    await client.DeleteFieldAsync(settings.Entity, settings.FieldId);
                });

            Renderer.Success($"Field [#F97316]{Markup.Escape(settings.FieldId.ToString())}[/] deleted from [#F97316]{Markup.Escape(settings.Entity)}[/].");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}
