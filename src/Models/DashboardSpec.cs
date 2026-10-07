using System.Text.Json;
using System.Text.Json.Nodes;

namespace AnythinkCli.Models;

public sealed record DashboardInput(
    string? Name = null, string? Description = null, bool? Shared = null, bool? AllowSharedEdit = null,
    bool? Home = null, bool? Default = null, string? Config = null);

public sealed record DashboardBuild(JsonObject? Body, IReadOnlyList<string> Problems)
{
    public bool Ok => Body is not null && Problems.Count == 0;
}

public sealed record ChartWidgetRef(int ChartId, string Title);

public static class DashboardSpec
{
    public static readonly string[] WidgetTypes = ["chart", "materialised_view", "list", "quick_action", "markdown", "media", "getting_started"];

    private static readonly string[] CreateKeys =
        ["name", "description", "is_default", "owner_user_id", "layout_json", "layout", "is_shared", "allow_shared_edit", "is_home", "widgets"];

    private static readonly string[] UpdateKeys =
        ["name", "description", "layout_json", "layout", "is_shared", "allow_shared_edit", "widgets"];

    private static readonly string[] WidgetKeys = ["id", "type", "title", "config_json", "config", "chart_id", "sort_order"];

    public static string ChartConfig(int chartId) => new JsonObject { ["chartId"] = chartId }.ToJsonString();

    public static DashboardBuild Build(DashboardInput input, IReadOnlyList<ChartWidgetRef> charts, bool create)
    {
        var problems = new List<string>();
        var body = new JsonObject();

        if (!string.IsNullOrWhiteSpace(input.Config))
            ApplyConfig(body, input.Config, create ? CreateKeys : UpdateKeys, problems);

        if (!string.IsNullOrWhiteSpace(input.Name)) body["name"] = input.Name.Trim();
        if (input.Description is not null) body["description"] = input.Description;
        if (input.Shared is { } shared) body["is_shared"] = shared;
        if (input.AllowSharedEdit is { } edit) body["allow_shared_edit"] = edit;
        if (create)
        {
            if (input.Home is { } home) body["is_home"] = home;
            if (input.Default is { } isDefault) body["is_default"] = isDefault;
        }

        if (charts.Count > 0)
            AddChartWidgets(body, charts);

        if (create && Text(body, "name") is null)
            problems.Add("A dashboard needs a name: anythink dashboards create <NAME> ...");

        if (body["is_default"]?.GetValueKind() == JsonValueKind.True && body["is_shared"]?.GetValueKind() == JsonValueKind.True)
            problems.Add("The project's default dashboard is already visible to everyone, so it can't also be marked shared.");

        if (!create && body.Count == 0 && problems.Count == 0)
            problems.Add("Nothing to update. Pass --name, --description, --shared, --allow-shared-edit or --config.");

        return new DashboardBuild(problems.Count == 0 ? body : null, problems);
    }

    private static void ApplyConfig(JsonObject body, string config, string[] allowed, List<string> problems)
    {
        JsonObject? parsed;
        try
        {
            parsed = JsonNode.Parse(config) as JsonObject;
        }
        catch (JsonException ex)
        {
            problems.Add($"--config isn't valid JSON ({ex.Message.Split('.')[0]}). Pass an object such as '{{\"widgets\":[...]}}'.");
            return;
        }

        if (parsed is null)
        {
            problems.Add("--config must be a JSON object such as '{\"description\":\"...\"}'.");
            return;
        }

        var unknown = parsed.Select(p => p.Key).Where(k => !allowed.Contains(k)).ToList();
        if (unknown.Count > 0)
            problems.Add($"--config has settings a dashboard can't take here: {string.Join(", ", unknown)}. Allowed: {string.Join(", ", allowed)}.");

        foreach (var (key, value) in parsed)
        {
            if (!allowed.Contains(key)) continue;

            switch (key)
            {
                case "widgets":
                    body["widgets"] = Widgets(value, problems);
                    break;
                case "layout":
                case "layout_json":
                    body["layout_json"] = Layout(value, problems);
                    break;
                default:
                    body[key] = value?.DeepClone();
                    break;
            }
        }
    }

    private static JsonArray Widgets(JsonNode? value, List<string> problems)
    {
        var result = new JsonArray();
        if (value is not JsonArray list)
        {
            problems.Add("widgets must be a list of widgets, each with a type and a title.");
            return result;
        }

        for (var i = 0; i < list.Count; i++)
        {
            var label = $"Widget {i + 1}";
            if (list[i] is not JsonObject widget)
            {
                problems.Add($"{label} must be an object with a type and a title.");
                continue;
            }

            var unknown = widget.Select(p => p.Key).Where(k => !WidgetKeys.Contains(k)).ToList();
            if (unknown.Count > 0)
                problems.Add($"{label} has settings a widget doesn't have: {string.Join(", ", unknown)}. Allowed: {string.Join(", ", WidgetKeys)}.");

            var type = Text(widget, "type");
            var title = Text(widget, "title");
            if (type is null) problems.Add($"{label} needs a type, one of: {string.Join(", ", WidgetTypes)}.");
            else if (!WidgetTypes.Contains(type)) problems.Add($"{label} has type '{type}'. Use one of: {string.Join(", ", WidgetTypes)}.");
            if (title is null) problems.Add($"{label} needs a title.");

            var settings = widget["chart_id"] is { } chartId
                ? new JsonObject { ["chartId"] = chartId.DeepClone() }.ToJsonString()
                : ConfigText(widget["config_json"] ?? widget["config"]);

            if (type == "chart" && !ChartIdOf(settings, out _))
                problems.Add($"{label} is a chart, so it needs a chart_id (or config {{\"chartId\": <chart id>}}).");

            var item = new JsonObject { ["type"] = type, ["title"] = title, ["config_json"] = settings, ["sort_order"] = widget["sort_order"]?.DeepClone() ?? i };
            if (widget["id"] is { } id) item["id"] = id.DeepClone();
            result.Add(item);
        }
        return result;
    }

    private static string? ConfigText(JsonNode? config) => config switch
    {
        null => null,
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        _ => config.ToJsonString(),
    };

    internal static bool ChartIdOf(string? configJson, out int chartId)
    {
        chartId = 0;
        if (string.IsNullOrWhiteSpace(configJson)) return false;
        try
        {
            if (JsonNode.Parse(configJson) is not JsonObject config) return false;
            var node = config["chartId"];
            if (node is JsonValue value && value.TryGetValue<int>(out chartId)) return chartId > 0;
            return node is JsonValue text && text.TryGetValue<string>(out var raw) && int.TryParse(raw, out chartId) && chartId > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? Layout(JsonNode? value, List<string> problems)
    {
        if (value is JsonValue text && text.TryGetValue<string>(out var raw))
            return raw;

        if (value is not JsonArray items)
        {
            problems.Add("layout must be a list of positions: [{\"widget_id\":1,\"x\":0,\"y\":0,\"w\":4,\"h\":3}].");
            return null;
        }

        var layout = new JsonArray();
        foreach (var node in items)
        {
            if (node is not JsonObject item || (item["widget_id"] ?? item["widgetId"]) is not { } id)
            {
                problems.Add("Each layout position needs a widget_id, x, y, w and h.");
                continue;
            }

            layout.Add(new JsonObject
            {
                ["widgetId"] = id.DeepClone(),
                ["x"] = item["x"]?.DeepClone(),
                ["y"] = item["y"]?.DeepClone(),
                ["w"] = item["w"]?.DeepClone(),
                ["h"] = item["h"]?.DeepClone(),
            });
        }
        return layout.ToJsonString();
    }

    private static void AddChartWidgets(JsonObject body, IReadOnlyList<ChartWidgetRef> charts)
    {
        var widgets = body["widgets"] as JsonArray ?? [];
        var next = widgets.OfType<JsonObject>().Select(w => w["sort_order"]?.GetValue<int>() ?? 0).DefaultIfEmpty(-1).Max() + 1;
        foreach (var chart in charts)
            widgets.Add(new JsonObject { ["type"] = "chart", ["title"] = chart.Title, ["config_json"] = ChartConfig(chart.ChartId), ["sort_order"] = next++ });
        body["widgets"] = widgets;
    }

    private static string? Text(JsonObject node, string key) =>
        node[key] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;
}
