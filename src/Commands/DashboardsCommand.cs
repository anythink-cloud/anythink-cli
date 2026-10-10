using AnythinkCli.Client;
using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Text.Json.Nodes;

namespace AnythinkCli.Commands;

internal static class DashboardsOutput
{
    public const int DefaultRows = 20;

    public static string Text(JsonNode? node) => ChartsOutput.Display(node);

    public static void Render(JsonObject detail)
    {
        Renderer.Header($"Dashboard: {Text(detail["name"])}");
        foreach (var key in new[] { "id", "description", "is_home", "is_default", "is_shared", "allow_shared_edit", "owner_user_id" })
            if (detail[key] is { } value) Renderer.KeyValue(JsonNames.Label(key), Text(value));

        var widgets = detail["widgets"] as JsonArray ?? [];
        if (widgets.Count == 0)
        {
            Renderer.Info("No widgets yet. Add a chart with: anythink dashboards add-chart <dashboard-id> <chart-id>");
            return;
        }

        AnsiConsole.WriteLine();
        var table = Renderer.BuildTable("ID", "Type", "Title", "Shows", "Position");
        foreach (var widget in widgets.OfType<JsonObject>())
            Renderer.AddRow(table, Text(widget["id"]), Text(widget["type"]), Text(widget["title"]), Shows(widget), DashboardView.Position(widget));
        AnsiConsole.Write(table);
    }

    private static string Shows(JsonObject widget)
    {
        if (widget["chart_id"] is { } chart) return $"chart {Text(chart)}";
        var text = widget["config"] is { } config ? Text(config) : "—";
        return text.Length > 40 ? text[..37] + "..." : text;
    }

    public static JsonObject WidgetRequest(JsonObject widget) => new()
    {
        ["id"] = widget["id"]?.DeepClone(),
        ["type"] = widget["type"]?.DeepClone(),
        ["title"] = widget["title"]?.DeepClone(),
        ["config_json"] = widget["config_json"]?.DeepClone(),
        ["sort_order"] = widget["sort_order"]?.DeepClone() ?? 0,
    };

    public static string LayoutOf(JsonObject dashboard) =>
        (dashboard["layout_json"] as JsonValue)?.TryGetValue<string>(out var text) == true ? text : "";

    public static string Describe(IEnumerable<JsonObject> widgets) =>
        string.Join(", ", widgets.Select(w => $"{Text(w["id"])} ({Text(w["title"])})"));
}

// ── dashboards list ───────────────────────────────────────────────────────────

public class DashboardsListCommand : BaseCommand<JsonSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, JsonSettings settings)
    {
        try
        {
            var client = GetClient();
            var dashboards = await ChartsOutput.WithStatusAsync(settings.Json, "Fetching dashboards...",
                () => client.GetDashboardsAsync());
            var summaries = dashboards.OfType<JsonObject>().Select(DashboardView.Summary).ToList();

            if (settings.Json)
            {
                ChartsOutput.Print(new JsonArray(summaries.Select(s => (JsonNode)s).ToArray()));
                return 0;
            }

            Renderer.Header($"Dashboards ({summaries.Count})");
            if (summaries.Count == 0)
            {
                Renderer.Info("No dashboards found.");
                return 0;
            }

            var table = Renderer.BuildTable("ID", "Name", "Home", "Default", "Shared", "Owner");
            foreach (var d in summaries)
                Renderer.AddRow(table, DashboardsOutput.Text(d["id"]), DashboardsOutput.Text(d["name"]),
                    DashboardsOutput.Text(d["is_home"]), DashboardsOutput.Text(d["is_default"]), DashboardsOutput.Text(d["is_shared"]),
                    d["owner_user_id"] is { } owner ? DashboardsOutput.Text(owner) : "project");
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

// ── dashboards get / mine ─────────────────────────────────────────────────────

public class DashboardIdSettings : JsonSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("Dashboard ID")]
    public int Id { get; set; }
}

public class DashboardsGetCommand : BaseCommand<DashboardIdSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DashboardIdSettings settings)
    {
        try
        {
            var client = GetClient();
            var dashboard = await ChartsOutput.WithStatusAsync(settings.Json, $"Fetching dashboard {settings.Id}...",
                () => client.GetDashboardAsync(settings.Id));
            var detail = DashboardView.Detail(dashboard);

            if (settings.Json) ChartsOutput.Print(detail);
            else DashboardsOutput.Render(detail);
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

public class DashboardsMineCommand : BaseCommand<JsonSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, JsonSettings settings)
    {
        try
        {
            var client = GetClient();
            var dashboard = await ChartsOutput.WithStatusAsync(settings.Json, "Fetching your dashboard...",
                () => client.GetMyDashboardAsync());
            var detail = DashboardView.Detail(dashboard);

            if (settings.Json) ChartsOutput.Print(detail);
            else DashboardsOutput.Render(detail);
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── dashboards create ─────────────────────────────────────────────────────────

public class DashboardCreateSettings : JsonSettings
{
    [CommandArgument(0, "<NAME>")]
    [Description("Dashboard name")]
    public string Name { get; set; } = "";

    [CommandOption("--description <TEXT>")]
    [Description("What the dashboard is for")]
    public string? Description { get; set; }

    [CommandOption("--chart <ID>")]
    [Description("Chart to put on the dashboard. Repeat for several")]
    public int[] Chart { get; set; } = [];

    [CommandOption("--shared")]
    [Description("Let everyone in the project see this dashboard")]
    public bool Shared { get; set; }

    [CommandOption("--allow-shared-edit")]
    [Description("Let everyone who can see it edit it (needs --shared)")]
    public bool AllowSharedEdit { get; set; }

    [CommandOption("--home")]
    [Description("Make it your landing dashboard")]
    public bool Home { get; set; }

    [CommandOption("--default")]
    [Description("Make it the project's default dashboard, the one new users start from (administrators only)")]
    public bool Default { get; set; }

    [CommandOption("--config <JSON>")]
    [Description("Other settings as a JSON object: widgets (each with type, title and config_json) and layout_json. Typed options win over this")]
    public string? Config { get; set; }

    internal DashboardInput ToInput() => new(
        Name, Description, Shared ? true : null, AllowSharedEdit ? true : null, Home ? true : null, Default ? true : null, Config);
}

public class DashboardsCreateCommand : BaseCommand<DashboardCreateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DashboardCreateSettings settings)
    {
        try
        {
            var input = settings.ToInput();
            var check = DashboardSpec.Build(input, [], create: true);
            if (!check.Ok) return ChartsOutput.Refuse("The dashboard can't be created", check.Problems);

            var client = GetClient();
            var charts = new List<ChartWidgetRef>();
            foreach (var id in settings.Chart)
            {
                var chart = await client.GetChartAsync(id);
                charts.Add(new ChartWidgetRef(id, ChartsOutput.Display(chart["name"])));
            }

            var build = DashboardSpec.Build(input, charts, create: true);
            if (!build.Ok) return ChartsOutput.Refuse("The dashboard can't be created", build.Problems);

            var name = settings.Name.Trim();
            var created = await ChartsOutput.WithStatusAsync(settings.Json, $"Creating dashboard '{name}'...",
                () => client.CreateDashboardAsync(build.Body!));

            var detail = DashboardView.Detail(created);
            if (settings.Json)
            {
                ChartsOutput.Print(detail);
                return 0;
            }

            Renderer.Success($"Dashboard [#F97316]{Markup.Escape(name)}[/] created (id: {Markup.Escape(DashboardsOutput.Text(created["id"]))}).");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── dashboards update ─────────────────────────────────────────────────────────

public class DashboardUpdateSettings : DashboardIdSettings
{
    [CommandOption("--name <NAME>")]
    [Description("New dashboard name")]
    public string? Name { get; set; }

    [CommandOption("--description <TEXT>")]
    [Description("New description")]
    public string? Description { get; set; }

    [CommandOption("--shared <BOOL>")]
    [Description("Let everyone in the project see it (true/false). Only its owner can change this")]
    public bool? Shared { get; set; }

    [CommandOption("--allow-shared-edit <BOOL>")]
    [Description("Let everyone who can see it edit it (true/false). Only its owner can change this")]
    public bool? AllowSharedEdit { get; set; }

    [CommandOption("--config <JSON>")]
    [Description("Other changes as a JSON object. widgets replaces the whole widget list, so include every widget to keep (with its id); layout is a list of {widget_id, x, y, w, h}")]
    public string? Config { get; set; }
}

public class DashboardsUpdateCommand : BaseCommand<DashboardUpdateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DashboardUpdateSettings settings)
    {
        try
        {
            var build = DashboardSpec.Build(
                new DashboardInput(settings.Name, settings.Description, settings.Shared, settings.AllowSharedEdit, Config: settings.Config),
                [], create: false);
            if (!build.Ok) return ChartsOutput.Refuse($"Dashboard {settings.Id} can't be updated", build.Problems);

            var client = GetClient();
            var updated = await ChartsOutput.WithStatusAsync(settings.Json, $"Updating dashboard {settings.Id}...",
                () => client.UpdateDashboardAsync(settings.Id, build.Body!));

            if (settings.Json) ChartsOutput.Print(DashboardView.Detail(updated));
            else Renderer.Success($"Dashboard [#F97316]{Markup.Escape(DashboardsOutput.Text(updated["name"]))}[/] (id: {settings.Id}) updated.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── dashboards delete ─────────────────────────────────────────────────────────

public class DashboardDeleteSettings : DashboardIdSettings
{
    [CommandOption("--yes")]
    [Description("Skip confirmation prompt")]
    public bool Yes { get; set; }
}

public class DashboardsDeleteCommand : BaseCommand<DashboardDeleteSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DashboardDeleteSettings settings)
    {
        if (!settings.Yes && !AnsiConsole.Confirm(
                $"[yellow]Delete dashboard[/] [bold red]{settings.Id}[/][yellow] and its widgets? The charts on it are kept.[/]",
                defaultValue: false))
        {
            Renderer.Info("Cancelled.");
            return 0;
        }

        try
        {
            var client = GetClient();
            await ChartsOutput.WithStatusAsync(settings.Json, $"Deleting dashboard {settings.Id}...", async () =>
            {
                await client.DeleteDashboardAsync(settings.Id);
                return true;
            });

            if (settings.Json) ChartsOutput.Print(new JsonObject { ["id"] = settings.Id, ["deleted"] = true });
            else Renderer.Success($"Dashboard [#F97316]{settings.Id}[/] deleted.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── dashboards data ───────────────────────────────────────────────────────────

public class DashboardDataSettings : DashboardIdSettings
{
    [CommandOption("--widget <ID>")]
    [Description("Only this widget's data. Repeat for several; leave out for every widget")]
    public int[] Widget { get; set; } = [];

    [CommandOption("--rows <N>")]
    [Description("Most rows or points to show per widget (default 20)")]
    public int? Rows { get; set; }
}

public class DashboardsDataCommand : BaseCommand<DashboardDataSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DashboardDataSettings settings)
    {
        try
        {
            if (settings.Rows is < 1)
                return ChartsOutput.Refuse("The data can't be fetched", ["--rows must be at least 1."]);

            var client = GetClient();
            var (dashboard, data) = await ChartsOutput.WithStatusAsync(settings.Json, $"Fetching data for dashboard {settings.Id}...", async () =>
            {
                var d = await client.GetDashboardAsync(settings.Id);
                return (d, await client.GetDashboardDataAsync(settings.Id, settings.Widget));
            });

            var rows = settings.Rows ?? DashboardsOutput.DefaultRows;
            var results = new JsonArray();
            foreach (var widget in DashboardView.Widgets(dashboard))
            {
                var id = widget["id"]!.ToString();
                if (!data.ContainsKey(id)) continue;
                results.Add(new JsonObject
                {
                    ["widget_id"] = widget["id"]!.DeepClone(),
                    ["type"] = widget["type"]?.DeepClone(),
                    ["title"] = widget["title"]?.DeepClone(),
                    ["data"] = ChartView.Payload(data[id], rows),
                });
            }

            if (settings.Json)
            {
                ChartsOutput.Print(results);
                return 0;
            }

            if (results.Count == 0)
            {
                Renderer.Info("No widget data.");
                return 0;
            }

            foreach (var result in results.OfType<JsonObject>())
            {
                Renderer.Header($"{DashboardsOutput.Text(result["title"])} ({DashboardsOutput.Text(result["type"])}, widget {DashboardsOutput.Text(result["widget_id"])})");
                if (ChartView.PayloadError(result["data"]) is { } error) Renderer.Warn(Markup.Escape(error));
                else ChartsOutput.RenderPayload(result["data"]);
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

// ── dashboards set-home / promote-to-default ──────────────────────────────────

public class DashboardsSetHomeCommand : BaseCommand<DashboardIdSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DashboardIdSettings settings)
    {
        try
        {
            var client = GetClient();
            var dashboard = await ChartsOutput.WithStatusAsync(settings.Json, $"Setting dashboard {settings.Id} as your home...",
                () => client.SetHomeDashboardAsync(settings.Id));

            if (settings.Json) ChartsOutput.Print(DashboardView.Detail(dashboard));
            else Renderer.Success($"Dashboard [#F97316]{Markup.Escape(DashboardsOutput.Text(dashboard["name"]))}[/] (id: {settings.Id}) is now your home dashboard.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

public class DashboardsPromoteToDefaultCommand : BaseCommand<DashboardDeleteSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DashboardDeleteSettings settings)
    {
        if (!settings.Yes && !AnsiConsole.Confirm(
                $"[yellow]Make dashboard[/] [bold red]{settings.Id}[/][yellow] the project's default? It replaces the current default that new users start from.[/]",
                defaultValue: false))
        {
            Renderer.Info("Cancelled.");
            return 0;
        }

        try
        {
            var client = GetClient();
            var dashboard = await ChartsOutput.WithStatusAsync(settings.Json, $"Promoting dashboard {settings.Id}...",
                () => client.PromoteDashboardToDefaultAsync(settings.Id));

            if (settings.Json) ChartsOutput.Print(DashboardView.Detail(dashboard));
            else Renderer.Success($"The project's default dashboard now matches [#F97316]{Markup.Escape(DashboardsOutput.Text(dashboard["name"]))}[/] (id: {Markup.Escape(DashboardsOutput.Text(dashboard["id"]))}).");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── dashboards widget-preview ─────────────────────────────────────────────────

public class DashboardWidgetPreviewSettings : JsonSettings
{
    [CommandOption("--type <TYPE>")]
    [Description("Widget type: chart, materialised_view, list, quick_action, markdown, media or getting_started")]
    public string? Type { get; set; }

    [CommandOption("--chart <ID>")]
    [Description("Chart to preview (for a chart widget)")]
    public int? Chart { get; set; }

    [CommandOption("--config <JSON>")]
    [Description("The widget's settings as JSON, e.g. {\"entityName\":\"orders\",\"limit\":5} for a list")]
    public string? Config { get; set; }

    [CommandOption("--rows <N>")]
    [Description("Most rows or points to show (default 20)")]
    public int? Rows { get; set; }
}

public class DashboardsWidgetPreviewCommand : BaseCommand<DashboardWidgetPreviewSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DashboardWidgetPreviewSettings settings)
    {
        try
        {
            var type = settings.Type?.Trim().ToLowerInvariant();
            var problems = new List<string>();
            if (string.IsNullOrEmpty(type)) problems.Add($"A widget preview needs --type, one of: {string.Join(", ", DashboardSpec.WidgetTypes)}.");
            else if (!DashboardSpec.WidgetTypes.Contains(type)) problems.Add($"'{type}' isn't a widget type. Use one of: {string.Join(", ", DashboardSpec.WidgetTypes)}.");
            if (settings.Rows is < 1) problems.Add("--rows must be at least 1.");

            string? configJson = null;
            if (settings.Chart is { } chart) configJson = DashboardSpec.ChartConfig(chart);
            else if (!string.IsNullOrWhiteSpace(settings.Config))
            {
                try { configJson = JsonNode.Parse(settings.Config)?.ToJsonString(); }
                catch (System.Text.Json.JsonException) { problems.Add("--config isn't valid JSON."); }
            }

            if (type == "chart" && !DashboardSpec.ChartIdOf(configJson, out _))
                problems.Add("A chart widget needs --chart <ID> (or --config '{\"chartId\": <id>}').");
            if (problems.Count > 0) return ChartsOutput.Refuse("The widget can't be previewed", problems);

            var client = GetClient();
            var payload = await ChartsOutput.WithStatusAsync(settings.Json, "Previewing widget...",
                () => client.PreviewWidgetAsync(type!, configJson));

            if (ChartView.PayloadError(payload) is { } error)
            {
                Renderer.Error($"The widget can't be drawn: {error}");
                return 1;
            }

            var compact = ChartView.Payload(payload, settings.Rows ?? DashboardsOutput.DefaultRows);
            if (settings.Json) ChartsOutput.Print(compact);
            else
            {
                Renderer.Header($"Preview: {type} widget");
                ChartsOutput.RenderPayload(compact);
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

// ── dashboards add-chart ──────────────────────────────────────────────────────

public class DashboardAddChartSettings : JsonSettings
{
    [CommandArgument(0, "<DASHBOARD_ID>")]
    [Description("Dashboard ID")]
    public int DashboardId { get; set; }

    [CommandArgument(1, "<CHART_ID>")]
    [Description("Chart ID")]
    public int ChartId { get; set; }

    [CommandOption("--title <TITLE>")]
    [Description("Widget title (defaults to the chart's name)")]
    public string? Title { get; set; }

    [CommandOption("--column <COLUMN>")]
    [Description("Left edge, in grid columns from 0 (the grid is 12 columns wide)")]
    public int? Column { get; set; }

    [CommandOption("--row <ROW>")]
    [Description("Top edge, in grid rows from 0 (defaults to below the existing widgets)")]
    public int? Row { get; set; }

    [CommandOption("--width <COLUMNS>")]
    [Description("Width in grid columns (default 4)")]
    public int? Width { get; set; }

    [CommandOption("--height <ROWS>")]
    [Description("Height in grid rows (default 3)")]
    public int? Height { get; set; }
}

public class DashboardsAddChartCommand : BaseCommand<DashboardAddChartSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DashboardAddChartSettings settings)
    {
        try
        {
            var place = new WidgetPlacement(settings.Column, settings.Row, settings.Width, settings.Height);
            if (DashboardLayout.Check(place) is { Count: > 0 } problems)
                return ChartsOutput.Refuse("The chart can't be added", problems);

            var client = GetClient();
            var chart = await client.GetChartAsync(settings.ChartId);
            var dashboard = await client.GetDashboardAsync(settings.DashboardId);
            var widgets = DashboardView.Widgets(dashboard);

            var title = string.IsNullOrWhiteSpace(settings.Title) ? ChartsOutput.Display(chart["name"]) : settings.Title.Trim();
            var next = widgets.Select(w => w["sort_order"]?.GetValue<int>() ?? 0).DefaultIfEmpty(-1).Max() + 1;

            var request = new JsonArray();
            foreach (var widget in widgets) request.Add(DashboardsOutput.WidgetRequest(widget));
            request.Add(new JsonObject { ["type"] = "chart", ["title"] = title, ["config_json"] = DashboardSpec.ChartConfig(settings.ChartId), ["sort_order"] = next });

            var updated = await ChartsOutput.WithStatusAsync(settings.Json, $"Adding '{title}' to dashboard {settings.DashboardId}...",
                () => client.UpdateDashboardAsync(settings.DashboardId, new JsonObject { ["widgets"] = request }));

            if (place.Any)
            {
                var before = widgets.Select(w => w["id"]!.ToString()).ToHashSet();
                var added = DashboardView.Widgets(updated).FirstOrDefault(w => !before.Contains(w["id"]!.ToString()))
                    ?? throw new CliException("The chart was added, but its position couldn't be set because the new widget wasn't in the response.");

                var layout = DashboardLayout.Parse(DashboardsOutput.LayoutOf(updated));
                DashboardLayout.Place(layout, widgets, added["id"]!.GetValue<int>(), place);
                updated = await client.UpdateDashboardAsync(settings.DashboardId, new JsonObject { ["layout_json"] = layout.ToJsonString() });
            }

            if (settings.Json) ChartsOutput.Print(DashboardView.Detail(updated));
            else Renderer.Success($"Chart [#F97316]{Markup.Escape(title)}[/] added to dashboard {settings.DashboardId}.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── dashboards remove-widget ──────────────────────────────────────────────────

public class DashboardRemoveWidgetSettings : JsonSettings
{
    [CommandArgument(0, "<DASHBOARD_ID>")]
    [Description("Dashboard ID")]
    public int DashboardId { get; set; }

    [CommandArgument(1, "<WIDGET_ID>")]
    [Description("Widget ID (from dashboards get)")]
    public int WidgetId { get; set; }
}

public class DashboardsRemoveWidgetCommand : BaseCommand<DashboardRemoveWidgetSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DashboardRemoveWidgetSettings settings)
    {
        try
        {
            var client = GetClient();
            var dashboard = await client.GetDashboardAsync(settings.DashboardId);
            var widgets = DashboardView.Widgets(dashboard);

            var target = widgets.FirstOrDefault(w => w["id"]?.GetValue<int>() == settings.WidgetId);
            if (target is null)
            {
                Renderer.Error(widgets.Count == 0
                    ? $"Dashboard {settings.DashboardId} has no widgets."
                    : $"Widget {settings.WidgetId} isn't on dashboard {settings.DashboardId}. Its widgets are: {DashboardsOutput.Describe(widgets)}.");
                return 1;
            }

            var request = new JsonArray();
            foreach (var widget in widgets.Where(w => !ReferenceEquals(w, target))) request.Add(DashboardsOutput.WidgetRequest(widget));
            var body = new JsonObject { ["widgets"] = request };

            var layout = DashboardLayout.Parse(DashboardsOutput.LayoutOf(dashboard));
            if (DashboardLayout.Remove(layout, settings.WidgetId)) body["layout_json"] = layout.ToJsonString();

            var title = DashboardsOutput.Text(target["title"]);
            var updated = await ChartsOutput.WithStatusAsync(settings.Json, $"Removing '{title}' from dashboard {settings.DashboardId}...",
                () => client.UpdateDashboardAsync(settings.DashboardId, body));

            if (settings.Json) ChartsOutput.Print(DashboardView.Detail(updated));
            else Renderer.Success($"Widget [#F97316]{Markup.Escape(title)}[/] removed from dashboard {settings.DashboardId}.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}
