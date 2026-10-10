using AnythinkCli.Client;
using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Text.Json.Nodes;

namespace AnythinkCli.Commands;

public class JsonSettings : CommandSettings
{
    [CommandOption("--json")]
    [Description("Output JSON instead of a table")]
    public bool Json { get; set; }
}

internal static class ChartsOutput
{
    private static readonly System.Text.Json.JsonSerializerOptions Compact = new(JsonOutput.PrettyRelaxed) { WriteIndented = false };

    public static async Task<T> WithStatusAsync<T>(bool json, string markup, Func<Task<T>> work)
    {
        if (json) return await work();
        return await AnsiConsole.Status().Spinner(Spinner.Known.Dots).StartAsync(markup, async _ => await work());
    }

    public static void Print(JsonNode? node) =>
        Console.WriteLine(node is null ? "null" : node.ToJsonString(JsonOutput.PrettyRelaxed));

    public static int Refuse(string what, IReadOnlyList<string> problems)
    {
        Renderer.Error(problems.Count == 1 ? $"{what}: {problems[0]}" : $"{what}:\n  - {string.Join("\n  - ", problems)}");
        return 1;
    }

    public static string Display(JsonNode? value) => value switch
    {
        null => "—",
        JsonValue text when text.TryGetValue<string>(out var s) => s,
        _ => value.ToJsonString(Compact),
    };

    public static void KeyValues(JsonObject view, params string[] skip)
    {
        foreach (var (key, value) in view)
            if (!skip.Contains(key)) Renderer.KeyValue(JsonNames.Label(key), Display(value));
    }

    public static void RenderPayload(JsonNode? payload)
    {
        if (payload is not JsonObject data)
        {
            Renderer.Info(payload is null ? "No data." : Markup.Escape(Display(payload)));
            return;
        }

        foreach (var key in new[] { "value", "target", "unit", "label", "delta_pct" })
            if (data[key] is { } scalar) Renderer.KeyValue(JsonNames.Label(key), Display(scalar));

        if (data["series"] is JsonArray series)
            Renderer.KeyValue("Series", string.Join(", ", series.Select(Display)));

        foreach (var key in new[] { "items", "stages" })
            if (data[key] is JsonArray rows) RenderRows(key, rows, data);

        if (data["primary"] is JsonObject primary) RenderPayload(primary);
    }

    private static void RenderRows(string key, JsonArray rows, JsonObject data)
    {
        if (rows.Count == 0)
        {
            Renderer.Info($"No {key}.");
            return;
        }

        var columns = rows.OfType<JsonObject>().SelectMany(r => r.Select(p => p.Key)).Distinct().ToArray();
        if (columns.Length == 0) return;

        var table = Renderer.BuildTable(columns);
        foreach (var row in rows.OfType<JsonObject>())
            Renderer.AddRow(table, columns.Select(c => Shorten(Display(row[c]))).ToArray());
        AnsiConsole.Write(table);

        if (data[key + "_total"] is { } total)
            Renderer.Info($"Showing {rows.Count} of {Markup.Escape(Display(total))}. Raise --rows to see more.");
    }

    private static string Shorten(string text) => text.Length > 60 ? text[..57] + "..." : text;
}

// ── shared chart options ──────────────────────────────────────────────────────

public class ChartConfigSettings : JsonSettings
{
    [CommandOption("--entity <ENTITY>")]
    [Description("Entity whose records the chart plots (for an entity chart)")]
    public string? Entity { get; set; }

    [CommandOption("--type <TYPE>")]
    [Description("Chart type: column (bar chart), line, pie, area, stat, gauge or funnel")]
    public string? Type { get; set; }

    [CommandOption("--source <SOURCE>")]
    [Description("Where the data comes from: entity (default), platform (usage metrics) or quota (plan limits)")]
    public string? Source { get; set; }

    [CommandOption("--group-by <FIELD>")]
    [Description("Field for the x axis, or the slices of a pie. A date field is bucketed with --interval")]
    public string? GroupBy { get; set; }

    [CommandOption("--interval <INTERVAL>")]
    [Description("Bucket size for a date --group-by: hour, day, week, month, quarter or year")]
    public string? Interval { get; set; }

    [CommandOption("--measure <FIELD>")]
    [Description("Numeric field to aggregate (needed unless --agg is count)")]
    public string? Measure { get; set; }

    [CommandOption("--agg <AGG>")]
    [Description("How to combine the --measure values: count (default), sum, avg, min or max")]
    public string? Agg { get; set; }

    [CommandOption("--stack-by <FIELD>")]
    [Description("Field that splits each bar or point into a second breakdown")]
    public string? StackBy { get; set; }

    [CommandOption("--filter <FILTER>")]
    [Description("Filter as field:operator:value, e.g. status:eq:published. Operators: eq, ne, gt, gte, lt, lte, contains, in, exists, not_exists. Repeat for several fields. Replaces the chart's existing filters")]
    public string[] Filter { get; set; } = [];

    [CommandOption("--timeframe <PRESET>")]
    [Description("Time window: all, 24h, 7d, 30d, 90d, 365d or custom")]
    public string? Timeframe { get; set; }

    [CommandOption("--from <DATE>")]
    [Description("Start of a custom time window, e.g. 2026-01-01")]
    public string? From { get; set; }

    [CommandOption("--to <DATE>")]
    [Description("End of a custom time window, e.g. 2026-03-31")]
    public string? To { get; set; }

    [CommandOption("--limit <N>")]
    [Description("Most points or rows to plot")]
    public int? Limit { get; set; }

    [CommandOption("--sort <DIRECTION>")]
    [Description("Sort direction: asc or desc")]
    public string? Sort { get; set; }

    [CommandOption("--cumulative <BOOL>")]
    [Description("Plot a running total (true/false)")]
    public bool? Cumulative { get; set; }

    [CommandOption("--target <N>")]
    [Description("Goal or warning level drawn on a stat or gauge")]
    public decimal? Target { get; set; }

    [CommandOption("--target-mode <MODE>")]
    [Description("What --target means: goal or warning")]
    public string? TargetMode { get; set; }

    [CommandOption("--compare <BOOL>")]
    [Description("Compare with the previous period (true/false)")]
    public bool? Compare { get; set; }

    [CommandOption("--compare-days <N>")]
    [Description("How many days back the comparison period starts (defaults to the length of the time window)")]
    public int? CompareDays { get; set; }

    [CommandOption("--compare-overlay <BOOL>")]
    [Description("Draw the previous period over the current one (true/false)")]
    public bool? CompareOverlay { get; set; }

    [CommandOption("--platform-metric <ID>")]
    [Description("Usage metric for a platform chart. List them with: charts platform-metrics")]
    public string? PlatformMetric { get; set; }

    [CommandOption("--platform-period <PERIOD>")]
    [Description("Window for a platform metric: day, week, month, quarter or year")]
    public string? PlatformPeriod { get; set; }

    [CommandOption("--quota-metric <ID>")]
    [Description("Plan limit for a quota chart: storage, files, users, workflows or emails")]
    public string? QuotaMetric { get; set; }

    [CommandOption("--config <JSON>")]
    [Description("Other chart settings as a JSON object, e.g. {\"limit\":10,\"target\":500} or funnelStages for a funnel. Typed options win over this. Setting names can be camelCase or snake_case")]
    public string? Config { get; set; }

    internal ChartInput ToInput(string? name = null) => new(
        name, Entity, Type, Source, GroupBy, Interval, Measure, Agg, StackBy, Filter is { Length: > 0 } ? Filter : null,
        Timeframe, From, To, Limit, Sort, Cumulative, Target, TargetMode, Compare, CompareDays, CompareOverlay,
        PlatformMetric, PlatformPeriod, QuotaMetric, Config);
}

// ── charts list ───────────────────────────────────────────────────────────────

public class ChartsListSettings : JsonSettings
{
    [CommandOption("--entity <ENTITY>")]
    [Description("Only charts that plot this entity")]
    public string? Entity { get; set; }
}

public class ChartsListCommand : BaseCommand<ChartsListSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ChartsListSettings settings)
    {
        try
        {
            var client = GetClient();
            var charts = await ChartsOutput.WithStatusAsync(settings.Json, "Fetching charts...",
                () => client.GetChartsAsync(settings.Entity));

            var summaries = charts.OfType<JsonObject>().Select(ChartView.Summary).ToList();

            if (settings.Json)
            {
                ChartsOutput.Print(new JsonArray(summaries.Select(s => (JsonNode)s).ToArray()));
                return 0;
            }

            Renderer.Header($"Charts ({summaries.Count})");
            if (summaries.Count == 0)
            {
                Renderer.Info("No charts found.");
                return 0;
            }

            var table = Renderer.BuildTable("ID", "Name", "Type", "Source", "Entity / metric", "Updated");
            foreach (var chart in summaries)
                Renderer.AddRow(table,
                    ChartsOutput.Display(chart["id"]), ChartsOutput.Display(chart["name"]), ChartsOutput.Display(chart["chart_type"]),
                    ChartsOutput.Display(chart["data_source"]),
                    ChartsOutput.Display(chart["entity_name"] ?? chart["platform_metric"] ?? chart["quota_metric"]),
                    ChartsOutput.Display(chart["updated_at"]));
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

// ── charts get ────────────────────────────────────────────────────────────────

public class ChartIdSettings : JsonSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("Chart ID")]
    public int Id { get; set; }
}

public class ChartsGetCommand : BaseCommand<ChartIdSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ChartIdSettings settings)
    {
        try
        {
            var client = GetClient();
            var chart = await ChartsOutput.WithStatusAsync(settings.Json, $"Fetching chart {settings.Id}...",
                () => client.GetChartAsync(settings.Id));
            var detail = ChartView.Detail(chart);

            if (settings.Json)
            {
                ChartsOutput.Print(detail);
                return 0;
            }

            Renderer.Header($"Chart: {ChartsOutput.Display(detail["name"])}");
            ChartsOutput.KeyValues(detail, "name");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── charts create ─────────────────────────────────────────────────────────────

public class ChartCreateSettings : ChartConfigSettings
{
    [CommandArgument(0, "<NAME>")]
    [Description("Chart name")]
    public string Name { get; set; } = "";
}

public class ChartsCreateCommand : BaseCommand<ChartCreateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ChartCreateSettings settings)
    {
        try
        {
            var build = ChartSpec.Build(settings.ToInput(settings.Name), existing: null, create: true);
            if (!build.Ok) return ChartsOutput.Refuse("The chart can't be created", build.Problems);

            var client = GetClient();
            var name = settings.Name.Trim();
            var created = await ChartsOutput.WithStatusAsync(settings.Json, $"Creating chart '{Markup.Escape(name)}'...",
                () => client.CreateChartAsync(build.Body!));

            if (settings.Json)
            {
                ChartsOutput.Print(ChartView.Detail(created));
                return 0;
            }

            Renderer.Success($"Chart [#F97316]{Markup.Escape(name)}[/] created (id: {ChartsOutput.Display(created["id"])}).");
            Renderer.Info($"Put it on a dashboard with: anythink dashboards add-chart <dashboard-id> {ChartsOutput.Display(created["id"])}");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── charts update ─────────────────────────────────────────────────────────────

public class ChartUpdateSettings : ChartConfigSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("Chart ID")]
    public int Id { get; set; }

    [CommandOption("--name <NAME>")]
    [Description("New chart name")]
    public string? Name { get; set; }
}

public class ChartsUpdateCommand : BaseCommand<ChartUpdateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ChartUpdateSettings settings)
    {
        try
        {
            var input = settings.ToInput(settings.Name);
            if (input == new ChartInput())
                return ChartsOutput.Refuse("Nothing to update", ["Pass at least one setting to change, such as --name, --type, --entity or --config."]);

            var client = GetClient();
            var existing = await ChartsOutput.WithStatusAsync(settings.Json, $"Fetching chart {settings.Id}...",
                () => client.GetChartAsync(settings.Id));

            var build = ChartSpec.Build(input, existing, create: false);
            if (!build.Ok) return ChartsOutput.Refuse($"Chart {settings.Id} can't be updated", build.Problems);

            var updated = await ChartsOutput.WithStatusAsync(settings.Json, $"Updating chart {settings.Id}...",
                () => client.UpdateChartAsync(settings.Id, build.Body!));

            if (settings.Json)
            {
                ChartsOutput.Print(ChartView.Detail(updated));
                return 0;
            }

            Renderer.Success($"Chart [#F97316]{Markup.Escape(ChartsOutput.Display(updated["name"]))}[/] (id: {settings.Id}) updated.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── charts delete ─────────────────────────────────────────────────────────────

public class ChartDeleteSettings : ChartIdSettings
{
    [CommandOption("--yes")]
    [Description("Skip confirmation prompt")]
    public bool Yes { get; set; }
}

public class ChartsDeleteCommand : BaseCommand<ChartDeleteSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ChartDeleteSettings settings)
    {
        if (!settings.Yes && !AnsiConsole.Confirm(
                $"[yellow]Delete chart[/] [bold red]{settings.Id}[/][yellow]? Dashboards that show it will lose its data.[/]",
                defaultValue: false))
        {
            Renderer.Info("Cancelled.");
            return 0;
        }

        try
        {
            var client = GetClient();
            await ChartsOutput.WithStatusAsync(settings.Json, $"Deleting chart {settings.Id}...", async () =>
            {
                await client.DeleteChartAsync(settings.Id);
                return true;
            });

            if (settings.Json)
                ChartsOutput.Print(new JsonObject { ["id"] = settings.Id, ["deleted"] = true });
            else
                Renderer.Success($"Chart [#F97316]{settings.Id}[/] deleted. Remove it from any dashboard with: anythink dashboards remove-widget <dashboard-id> <widget-id>");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── charts preview ────────────────────────────────────────────────────────────

public class ChartPreviewSettings : ChartConfigSettings
{
    [CommandOption("--rows <N>")]
    [Description("Most rows or points to show (default 50)")]
    public int? Rows { get; set; }
}

public class ChartsPreviewCommand : BaseCommand<ChartPreviewSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ChartPreviewSettings settings)
    {
        try
        {
            if (settings.Rows is < 1)
                return ChartsOutput.Refuse("The chart can't be previewed", ["--rows must be at least 1."]);

            var build = ChartSpec.Build(settings.ToInput(), existing: null, create: false);
            if (!build.Ok) return ChartsOutput.Refuse("The chart can't be previewed", build.Problems);

            var client = GetClient();
            var payload = await ChartsOutput.WithStatusAsync(settings.Json, "Previewing chart...",
                () => client.PreviewChartAsync(build.Body!));

            if (ChartView.PayloadError(payload) is { } error)
            {
                Renderer.Error($"The chart can't be drawn: {error}");
                return 1;
            }

            var compact = ChartView.Payload(payload, settings.Rows ?? ChartView.DefaultRows);
            if (settings.Json)
            {
                ChartsOutput.Print(compact);
                return 0;
            }

            Renderer.Header($"Preview: {ChartsOutput.Display(build.Body!["chartType"])} chart");
            ChartsOutput.RenderPayload(compact);
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── charts platform-metrics ───────────────────────────────────────────────────

public class ChartsPlatformMetricsCommand : BaseCommand<JsonSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, JsonSettings settings)
    {
        try
        {
            var client = GetClient();
            var metrics = await ChartsOutput.WithStatusAsync(settings.Json, "Fetching platform metrics...",
                () => client.GetPlatformMetricsAsync());

            if (settings.Json)
            {
                ChartsOutput.Print(metrics);
                return 0;
            }

            Renderer.Header($"Platform metrics ({metrics.Count})");
            var table = Renderer.BuildTable("ID", "Label", "Shape", "Chart types", "Breakdowns");
            foreach (var metric in metrics.OfType<JsonObject>())
                Renderer.AddRow(table,
                    ChartsOutput.Display(metric["id"]), ChartsOutput.Display(metric["label"]), ChartsOutput.Display(metric["shape"]),
                    string.Join(", ", (metric["supported_chart_types"] as JsonArray ?? []).Select(ChartsOutput.Display)),
                    string.Join(", ", (metric["stack_by_options"] as JsonArray ?? []).OfType<JsonObject>().Select(o => ChartsOutput.Display(o["id"]))));
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
