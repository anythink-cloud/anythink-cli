using System.Text.Json;
using System.Text.Json.Nodes;

namespace AnythinkCli.Models;

public static class WorkflowTriggers
{
    public static readonly string[] Types = ["Manual", "Timed", "Event", "Api"];

    private const string LastRun = "last_run_at";
    private const string NextRun = "next_run_at";
    private static readonly string[] EntityEvents = ["EntityCreated", "EntityUpdated", "EntityDeleted"];

    public static string? CanonicalType(string? type) =>
        Types.FirstOrDefault(t => string.Equals(t, type?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<WorkflowTrigger> Effective(Workflow wf)
    {
        if (wf.Triggers is { Count: > 0 })
            return wf.Triggers;
        if (string.IsNullOrWhiteSpace(wf.Trigger))
            return [];

        var legacy = FromLegacy(wf.Trigger, wf.Options, wf.ApiRoute);
        return [new WorkflowTrigger(null, legacy.Type, legacy.Enabled, JsonSerializer.SerializeToElement(legacy.Config))];
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

    public static IEnumerable<string> ApiRoutes(Workflow wf) =>
        Effective(wf)
            .Where(t => t.Enabled && IsType(t.Type, "Api"))
            .Select(t => Text(t.Config, "api_route")?.TrimStart('/'))
            .Where(route => !string.IsNullOrEmpty(route))
            .Select(route => route!);

    public static JsonElement? Filter(WorkflowTrigger trigger) =>
        trigger.Config is { ValueKind: JsonValueKind.Object } config
        && config.TryGetProperty("filter", out var filter)
        && filter.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? filter
            : null;

    public static List<WorkflowTriggerRequest> ForRequest(Workflow wf, bool keepLastRun = false) =>
        ForRequest(Effective(wf), keepLastRun);

    // Run state belongs to the source project when copying. An update keeps last_run_at but not next_run_at, which the API recomputes.
    public static List<WorkflowTriggerRequest> ForRequest(IEnumerable<WorkflowTrigger> triggers, bool keepLastRun = false) =>
        triggers
            .Select(t => new WorkflowTriggerRequest(
                CanonicalType(t.Type) ?? t.Type ?? "", t.Enabled, ConfigFor(t.Config, keepLastRun)))
            .ToList();

    public static WorkflowTriggerRequest FromLegacy(string type, JsonElement? options, string? apiRoute)
    {
        var config = ConfigFor(options, keepLastRun: false);
        if (IsType(type, "Api") && !string.IsNullOrEmpty(apiRoute))
            config["api_route"] = apiRoute;
        return new WorkflowTriggerRequest(CanonicalType(type) ?? type, true, config);
    }

    public static JsonObject WithoutRunState(JsonElement? config) => ConfigFor(config, keepLastRun: false);

    public static string? MissingField(WorkflowTriggerRequest trigger)
    {
        var config = JsonSerializer.SerializeToNode(trigger.Config) as JsonObject;

        return CanonicalType(trigger.Type) switch
        {
            "Manual" => HasEntries(config, "manual_entities") ? null : "manual_entities",
            "Api" => HasText(config, "api_route") ? null : "api_route",
            "Timed" => HasText(config, "cron_expression") ? null : "cron_expression",
            "Event" => MissingEventField(config),
            _ => null,
        };
    }

    public static List<string> Problems(IEnumerable<WorkflowTriggerRequest> triggers) =>
        triggers
            .Select(t => (Trigger: t, Field: MissingField(t)))
            .Where(m => m.Field is not null)
            .Select(m => Describe(CanonicalType(m.Trigger.Type) ?? m.Trigger.Type, m.Field!))
            .ToList();

    private static string Describe(string type, string field) => field switch
    {
        "manual_entities" => $"{type} trigger has no entities (manual_entities needs at least one)",
        "event_entity" => $"{type} trigger has no event_entity (entity events need one)",
        _ => $"{type} trigger has no {field}",
    };

    private static string? MissingEventField(JsonObject? config)
    {
        if (!HasValue(config, "event")) return "event";
        var isEntityEvent = config!["event"] is JsonValue v
            && v.TryGetValue<string>(out var name)
            && EntityEvents.Contains(name, StringComparer.OrdinalIgnoreCase);
        return isEntityEvent && !HasText(config, "event_entity") ? "event_entity" : null;
    }

    private static bool HasValue(JsonObject? config, string name) =>
        config?[name] is { } node && !(node is JsonValue v && v.TryGetValue<string>(out var s) && string.IsNullOrWhiteSpace(s));

    private static bool HasText(JsonObject? config, string name) =>
        config?[name] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s);

    private static bool HasEntries(JsonObject? config, string name) =>
        config?[name] is JsonArray list && list.Any(e => e is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s));

    private static bool IsType(string? type, string expected) =>
        string.Equals(type, expected, StringComparison.OrdinalIgnoreCase);

    private static JsonObject ConfigFor(JsonElement? config, bool keepLastRun)
    {
        if (config is not { ValueKind: JsonValueKind.Object } element)
            return new JsonObject();

        var node = JsonNode.Parse(element.GetRawText())!.AsObject();
        node.Remove(NextRun);
        if (!keepLastRun)
            node.Remove(LastRun);
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
