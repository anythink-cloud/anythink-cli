using System.Text.Json;
using System.Text.Json.Nodes;

namespace AnythinkCli.Models;

public sealed record WidgetPlacement(int? X = null, int? Y = null, int? Width = null, int? Height = null)
{
    public bool Any => X is not null || Y is not null || Width is not null || Height is not null;
}

public static class DashboardLayout
{
    public const int Columns = 12;
    public const int DefaultWidth = 4;
    public const int DefaultHeight = 3;

    public static JsonArray Parse(string? layoutJson)
    {
        if (string.IsNullOrWhiteSpace(layoutJson)) return [];
        try { return JsonNode.Parse(layoutJson) as JsonArray ?? []; }
        catch (JsonException) { return []; }
    }

    public static IReadOnlyList<string> Check(WidgetPlacement place)
    {
        var problems = new List<string>();
        if (place.X is < 0) problems.Add("--column can't be negative.");
        if (place.Y is < 0) problems.Add("--row can't be negative.");
        if (place.Width is < 1) problems.Add("--width must be at least 1.");
        if (place.Height is < 1) problems.Add("--height must be at least 1.");
        if (place.Width is > Columns) problems.Add($"--width can't be more than {Columns}, the number of columns in the grid.");
        if ((place.X ?? 0) + (place.Width ?? DefaultWidth) > Columns && problems.Count == 0)
            problems.Add($"The widget would run past the right edge: --column plus --width can't be more than {Columns}.");
        return problems;
    }

    public static (int Width, int Height) AutoSize(string type) => type switch
    {
        "quick_action" => (3, 1),
        "getting_started" => (6, 2),
        _ => (DefaultWidth, DefaultHeight),
    };

    public static JsonObject Place(JsonArray layout, IReadOnlyList<JsonObject> widgets, int newWidgetId, WidgetPlacement place)
    {
        var item = new JsonObject
        {
            ["widgetId"] = newWidgetId,
            ["x"] = place.X ?? 0,
            ["y"] = place.Y ?? BottomEdge(layout, widgets),
            ["w"] = place.Width ?? DefaultWidth,
            ["h"] = place.Height ?? DefaultHeight,
        };
        layout.Add(item);
        return item;
    }

    public static bool Remove(JsonArray layout, int widgetId)
    {
        var stale = layout.Where(n => n is JsonObject o && WidgetIdOf(o) == widgetId).ToList();
        foreach (var node in stale) layout.Remove(node);
        return stale.Count > 0;
    }

    // A widget with no saved position is flowed into the grid by its order, so the bottom edge counts those too.
    internal static int BottomEdge(JsonArray layout, IReadOnlyList<JsonObject> widgets)
    {
        var bottom = 0;
        for (var i = 0; i < widgets.Count; i++)
        {
            var id = widgets[i]["id"]?.GetValue<int>();
            var saved = layout.OfType<JsonObject>().FirstOrDefault(n => WidgetIdOf(n) == id);
            if (saved is not null)
            {
                bottom = Math.Max(bottom, Int(saved, "y") + Int(saved, "h"));
                continue;
            }

            var (width, height) = AutoSize(widgets[i]["type"]?.GetValue<string>() ?? "");
            bottom = Math.Max(bottom, (i * width / Columns) * height + height);
        }
        return bottom;
    }

    internal static int? WidgetIdOf(JsonObject item) =>
        (item["widgetId"] ?? item["widget_id"]) is JsonValue value && value.TryGetValue<int>(out var id) ? id : null;

    private static int Int(JsonObject item, string key) =>
        item[key] is JsonValue value && value.TryGetValue<int>(out var number) ? number : 0;
}

public static class DashboardView
{
    private static readonly string[] Flags = ["is_default", "is_home", "is_shared", "allow_shared_edit"];

    public static JsonObject Summary(JsonObject dashboard)
    {
        var summary = new JsonObject();
        foreach (var key in new[] { "id", "name", "description" })
            if (dashboard[key] is { } value && !IsBlank(value)) summary[key] = value.DeepClone();
        foreach (var key in Flags.Take(3))
            summary[key] = dashboard[key]?.DeepClone() ?? false;
        if (dashboard["owner_user_id"] is { } owner) summary["owner_user_id"] = owner.DeepClone();
        return summary;
    }

    public static JsonObject Detail(JsonObject dashboard)
    {
        var detail = Summary(dashboard);
        detail["allow_shared_edit"] = dashboard["allow_shared_edit"]?.DeepClone() ?? false;

        var layout = DashboardLayout.Parse((dashboard["layout_json"] as JsonValue)?.TryGetValue<string>(out var text) == true ? text : null);
        var widgets = new JsonArray();
        foreach (var widget in Widgets(dashboard))
            widgets.Add(Widget(widget, layout));
        detail["widgets"] = widgets;
        return detail;
    }

    public static IReadOnlyList<JsonObject> Widgets(JsonObject dashboard) =>
        (dashboard["widgets"] as JsonArray ?? []).OfType<JsonObject>()
            .OrderBy(w => w["sort_order"] is JsonValue o && o.TryGetValue<int>(out var order) ? order : 0)
            .ToList();

    private static JsonObject Widget(JsonObject widget, JsonArray layout)
    {
        var view = new JsonObject();
        foreach (var key in new[] { "id", "type", "title", "sort_order" })
            if (widget[key] is { } value) view[key] = value.DeepClone();

        var configJson = (widget["config_json"] as JsonValue)?.TryGetValue<string>(out var raw) == true ? raw : null;
        if (DashboardSpec.ChartIdOf(configJson, out var chartId))
            view["chart_id"] = chartId;
        else if (!string.IsNullOrWhiteSpace(configJson))
            view["config"] = TryParse(configJson);

        var id = widget["id"]?.GetValue<int>();
        if (layout.OfType<JsonObject>().FirstOrDefault(n => DashboardLayout.WidgetIdOf(n) == id) is { } place)
            view["position"] = new JsonObject { ["x"] = place["x"]?.DeepClone(), ["y"] = place["y"]?.DeepClone(), ["w"] = place["w"]?.DeepClone(), ["h"] = place["h"]?.DeepClone() };
        return view;
    }

    private static JsonNode? TryParse(string json)
    {
        try { return JsonNode.Parse(json); }
        catch (JsonException) { return JsonValue.Create(json); }
    }

    private static bool IsBlank(JsonNode value) => value is JsonValue text && text.TryGetValue<string>(out var s) && string.IsNullOrWhiteSpace(s);

    public static string Position(JsonObject widget) =>
        widget["position"] is JsonObject p ? $"x{p["x"]} y{p["y"]} {p["w"]}x{p["h"]}" : "auto";
}
