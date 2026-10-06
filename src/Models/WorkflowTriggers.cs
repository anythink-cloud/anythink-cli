using System.Text.Json;
using System.Text.Json.Nodes;

namespace AnythinkCli.Models;

public static class WorkflowTriggers
{
    private static readonly string[] RunState = ["last_run_at", "next_run_at"];

    public static IReadOnlyList<WorkflowTrigger> Effective(Workflow wf)
    {
        if (wf.Triggers is { Count: > 0 })
            return wf.Triggers;
        if (string.IsNullOrWhiteSpace(wf.Trigger))
            return [];
        return [new WorkflowTrigger(null, wf.Trigger, true, wf.Options)];
    }

    public static string Summary(Workflow wf)
    {
        var triggers = Effective(wf);
        return triggers.Count == 0 ? "-" : string.Join("; ", triggers.Select(Summary));
    }

    public static string Summary(WorkflowTrigger trigger)
    {
        var type = string.IsNullOrWhiteSpace(trigger.Type) ? "Unknown" : trigger.Type;
        var config = trigger.Config;

        var text = type.ToLowerInvariant() switch
        {
            "event" => EventSummary(type, config),
            "timed" => WithDetail(type, ": ", Text(config, "cron_expression")),
            "api" => WithDetail(type, ": /", Text(config, "api_route")?.TrimStart('/')),
            "manual" => WithDetail(type, " on ", Entities(config)),
            _ => type,
        };

        return trigger.Enabled ? text : text + " (disabled)";
    }

    public static bool HasEnabledApiTrigger(Workflow wf) =>
        Effective(wf).Any(t => t.Enabled && IsType(t.Type, "Api"));

    public static JsonElement? Filter(WorkflowTrigger trigger) =>
        trigger.Config is { ValueKind: JsonValueKind.Object } config
        && config.TryGetProperty("filter", out var filter)
        && filter.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? filter
            : null;

    public static List<WorkflowTriggerRequest> ForRequest(Workflow wf) => ForRequest(Effective(wf));

    public static List<WorkflowTriggerRequest> ForRequest(IEnumerable<WorkflowTrigger> triggers) =>
        triggers
            .Select(t => new WorkflowTriggerRequest(t.Type ?? "", t.Enabled, ConfigFor(t.Config)))
            .ToList();

    public static WorkflowTriggerRequest FromLegacy(string type, JsonElement? options, string? apiRoute)
    {
        var config = ConfigFor(options);
        if (IsType(type, "Api") && !string.IsNullOrEmpty(apiRoute))
            config["api_route"] = apiRoute;
        return new WorkflowTriggerRequest(type, true, config);
    }

    private static bool IsType(string? type, string expected) =>
        string.Equals(type, expected, StringComparison.OrdinalIgnoreCase);

    // Run state belongs to the source project; the target recomputes its own.
    private static JsonObject ConfigFor(JsonElement? config)
    {
        if (config is not { ValueKind: JsonValueKind.Object } element)
            return new JsonObject();

        var node = JsonNode.Parse(element.GetRawText())!.AsObject();
        foreach (var key in RunState)
            node.Remove(key);
        return node;
    }

    private static string EventSummary(string type, JsonElement? config)
    {
        var evt = Text(config, "event");
        var entity = Text(config, "event_entity");
        var detail = string.Join(" on ", new[] { evt, entity }.Where(v => !string.IsNullOrEmpty(v)));
        return WithDetail(type, ": ", detail);
    }

    private static string WithDetail(string type, string separator, string? detail) =>
        string.IsNullOrEmpty(detail) ? type : type + separator + detail;

    private static string? Text(JsonElement? config, string name) =>
        config is { ValueKind: JsonValueKind.Object } c
        && c.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? Entities(JsonElement? config)
    {
        if (config is not { ValueKind: JsonValueKind.Object } c
            || !c.TryGetProperty("manual_entities", out var list)
            || list.ValueKind != JsonValueKind.Array)
            return null;

        var names = list.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .ToList();
        return names.Count == 0 ? null : string.Join(", ", names);
    }
}
