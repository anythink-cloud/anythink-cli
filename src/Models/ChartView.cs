using System.Text.Json.Nodes;

namespace AnythinkCli.Models;

public static class ChartView
{
    public const int DefaultRows = 50;

    private static readonly string[] Noise = ["tenant_id", "created_by", "updated_by", "locked"];
    private static readonly string[] Leading = ["id", "name", "chart_type", "data_source"];

    public static JsonObject Summary(JsonObject chart)
    {
        var summary = new JsonObject();
        foreach (var key in new[] { "id", "name", "chart_type", "data_source", "entity_name", "platform_metric", "quota_metric", "x_field", "y_field", "y_agg", "updated_at" })
            if (chart[key] is { } value) summary[key] = value.DeepClone();
        return summary;
    }

    public static JsonObject Detail(JsonObject chart)
    {
        var detail = new JsonObject();
        foreach (var key in Leading)
            if (chart[key] is { } value) detail[key] = value.DeepClone();

        foreach (var (key, value) in chart)
        {
            if (value is null || Noise.Contains(key) || detail.ContainsKey(key)) continue;
            if (value is JsonValue text && text.TryGetValue<string>(out var raw) && string.IsNullOrWhiteSpace(raw)) continue;

            detail[key] = key is "filters" or "funnel_stages" && value is JsonValue json && json.TryGetValue<string>(out var encoded)
                ? Decode(encoded)
                : value.DeepClone();
        }
        return detail;
    }

    private static JsonNode? Decode(string json)
    {
        try { return JsonNode.Parse(json); }
        catch (System.Text.Json.JsonException) { return JsonValue.Create(json); }
    }

    public static JsonNode? Payload(JsonNode? payload, int rows)
    {
        if (payload is not JsonObject source) return payload?.DeepClone();

        var compact = new JsonObject();
        foreach (var (key, value) in source)
        {
            if (key == "chart") continue;

            switch (value)
            {
                case JsonArray list when list.Count > rows:
                    var kept = new JsonArray();
                    foreach (var item in list.Take(rows)) kept.Add(item?.DeepClone());
                    compact[key] = kept;
                    compact[key + "_total"] = list.Count;
                    break;
                case JsonObject nested:
                    compact[key] = Payload(nested, rows);
                    break;
                default:
                    compact[key] = value?.DeepClone();
                    break;
            }
        }
        return compact;
    }

    public static string? PayloadError(JsonNode? payload)
    {
        if (payload is not JsonObject obj) return null;
        if (obj["error"] is JsonValue error && error.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)) return text;
        return PayloadError(obj["primary"]);
    }
}
