using AnythinkCli.Models;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AnythinkCli.Commands;

internal class WorkflowInputException(string message) : Exception(message);

public class WorkflowTriggerFlagSettings : CommandSettings
{
    [CommandOption("--trigger <TYPE>")]
    [Description("Trigger type: Manual, Timed, Event or Api. On update, replaces all existing triggers")]
    public string? Trigger { get; set; }

    [CommandOption("--event <EVENT>")]
    [Description("Event for an Event trigger, e.g. EntityCreated, EntityUpdated, EntityDeleted, UserRegistered")]
    public string? Event { get; set; }

    [CommandOption("--entity <ENTITY>")]
    [Description("Entity for an Event trigger, or an entity a Manual trigger can run against (repeatable for Manual)")]
    public string[] Entities { get; set; } = [];

    [CommandOption("--cron <CRON>")]
    [Description("Cron expression for a Timed trigger, e.g. '0 9 * * *'")]
    public string? Cron { get; set; }

    [CommandOption("--api-route <ROUTE>")]
    [Description("Route for an Api trigger")]
    public string? ApiRoute { get; set; }

    [CommandOption("--filter <JSON>")]
    [Description("Filter expression JSON for an Event trigger (fires only when the row matches)")]
    public string? Filter { get; set; }

    [CommandOption("--filter-file <PATH>")]
    [Description("Path to a file containing the filter expression JSON")]
    public string? FilterFile { get; set; }

    [CommandOption("--triggers <JSON>")]
    [Description("JSON array of {type, enabled, config} trigger objects, as written by `workflows export`. On update, replaces all existing triggers")]
    public string? Triggers { get; set; }

    [CommandOption("--triggers-file <PATH>")]
    [Description("Path to a file containing the triggers JSON array")]
    public string? TriggersFile { get; set; }
}

internal static class WorkflowTriggers
{
    internal static readonly string[] Types = ["Manual", "Timed", "Event", "Api"];

    private static readonly HashSet<string> EntityEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "EntityCreated", "EntityUpdated", "EntityDeleted"
    };

    private static readonly JsonSerializerOptions ReadOpts = new() { PropertyNameCaseInsensitive = true };

    /// Returns null when no trigger flags were passed (update keeps the existing triggers).
    internal static List<WorkflowTriggerRequest>? FromSettings(WorkflowTriggerFlagSettings s)
    {
        var hasSingle = !string.IsNullOrEmpty(s.Trigger);
        var hasList   = !string.IsNullOrEmpty(s.Triggers) || !string.IsNullOrEmpty(s.TriggersFile);
        var hasFilter = !string.IsNullOrEmpty(s.Filter) || !string.IsNullOrEmpty(s.FilterFile);
        var hasDetail = hasFilter || s.Entities.Length > 0 || !string.IsNullOrEmpty(s.Event)
                        || !string.IsNullOrEmpty(s.Cron) || !string.IsNullOrEmpty(s.ApiRoute);

        if (hasSingle && hasList)
            throw new WorkflowInputException("Use either --trigger or --triggers, not both.");
        if (hasList && hasDetail)
            throw new WorkflowInputException("--triggers can't be combined with --event, --entity, --cron, --api-route or --filter.");
        if (!hasSingle && hasDetail)
            throw new WorkflowInputException("--event, --entity, --cron, --api-route and --filter need --trigger <TYPE>.");

        if (hasList)
        {
            var json = ReadJsonOption(s.Triggers, s.TriggersFile, "--triggers");
            JsonElement array;
            try { array = JsonSerializer.Deserialize<JsonElement>(json); }
            catch (JsonException ex) { throw new WorkflowInputException($"Invalid --triggers JSON: {ex.Message}"); }
            return FromJson(array, "triggers");
        }

        if (!hasSingle) return null;

        JsonElement? filter = null;
        if (hasFilter)
        {
            var json = ReadJsonOption(s.Filter, s.FilterFile, "--filter");
            try { filter = JsonSerializer.Deserialize<JsonElement>(json); }
            catch (JsonException ex) { throw new WorkflowInputException($"Invalid filter JSON: {ex.Message}"); }
        }

        return [FromFlags(s.Trigger!, s.Event, s.Entities, s.Cron, s.ApiRoute, filter)];
    }

    internal static WorkflowTriggerRequest FromFlags(
        string type, string? evt, IReadOnlyList<string> entities, string? cron, string? apiRoute, JsonElement? filter)
    {
        var normalised = NormaliseType(type, "--trigger");

        if (filter.HasValue && normalised != "Event")
            throw new WorkflowInputException("--filter only applies to Event triggers.");
        if (entities.Count > 1 && normalised != "Manual")
            throw new WorkflowInputException("Only Manual triggers accept more than one --entity.");

        var config = normalised switch
        {
            "Manual" => new WorkflowTriggerConfig(ManualEntities: entities.ToList()),
            "Timed"  => new WorkflowTriggerConfig(CronExpression: cron),
            "Event"  => new WorkflowTriggerConfig(Event: evt, EventEntity: entities.FirstOrDefault(), Filter: filter),
            _        => new WorkflowTriggerConfig(ApiRoute: apiRoute),
        };

        var trigger = new WorkflowTriggerRequest(normalised, true, config);
        Validate(trigger, "--trigger", fromFlags: true);
        return trigger;
    }

    internal static List<WorkflowTriggerRequest> FromJson(JsonElement array, string label)
    {
        if (array.ValueKind != JsonValueKind.Array)
            throw new WorkflowInputException($"{label} must be a JSON array.");

        var result = new List<WorkflowTriggerRequest>();
        var index = 0;
        foreach (var el in array.EnumerateArray())
        {
            var at = $"{label}[{index++}]";
            if (el.ValueKind != JsonValueKind.Object)
                throw new WorkflowInputException($"{at} must be an object.");

            var type = el.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            var enabled = !el.TryGetProperty("enabled", out var en) || en.ValueKind != JsonValueKind.False;

            WorkflowTriggerConfig? config = null;
            if (el.TryGetProperty("config", out var c) && c.ValueKind == JsonValueKind.Object)
                config = ReadConfig(c.GetRawText(), at);
            else if (el.TryGetProperty("config_json", out var cj) && cj.ValueKind == JsonValueKind.String
                     && !string.IsNullOrWhiteSpace(cj.GetString()))
                config = ReadConfig(cj.GetString()!, at);

            var trigger = new WorkflowTriggerRequest(NormaliseType(type, at), enabled, WithoutRunState(config));
            Validate(trigger, at, fromFlags: false);
            result.Add(trigger);
        }
        return result;
    }

    internal static WorkflowTriggerRequest FromLegacy(string type, JsonElement? options, string? apiRoute)
    {
        var config = options is { ValueKind: JsonValueKind.Object } o ? ReadConfig(o.GetRawText(), "options") : null;
        config = WithoutRunState(config);
        if (string.IsNullOrEmpty(config.ApiRoute) && !string.IsNullOrEmpty(apiRoute))
            config = config with { ApiRoute = apiRoute };
        if ((config.ManualEntities is null || config.ManualEntities.Count == 0) && !string.IsNullOrEmpty(config.EventEntity)
            && string.Equals(type, "Manual", StringComparison.OrdinalIgnoreCase))
            config = config with { ManualEntities = [config.EventEntity] };

        var trigger = new WorkflowTriggerRequest(NormaliseType(type, "trigger"), true, config);
        Validate(trigger, "trigger", fromFlags: false);
        return trigger;
    }

    // A copied next_run_at that is already in the past would fire the new workflow straight away.
    internal static List<WorkflowTriggerRequest> Copy(IEnumerable<WorkflowTrigger>? triggers, bool keepRunState)
        => (triggers ?? []).Select(t => new WorkflowTriggerRequest(
                t.Type ?? "",
                t.Enabled,
                keepRunState ? t.Config ?? new WorkflowTriggerConfig() : WithoutRunState(t.Config)))
            .ToList();

    internal static string Summarise(IEnumerable<WorkflowTrigger>? triggers)
    {
        var list = triggers?.ToList() ?? [];
        return list.Count == 0 ? "—" : string.Join(", ", list.Select(Describe));
    }

    internal static string Describe(WorkflowTrigger trigger)
    {
        var type = string.IsNullOrEmpty(trigger.Type) ? "?" : trigger.Type;
        var detail = Detail(trigger.Type, trigger.Config);
        var text = string.IsNullOrEmpty(detail) ? type : $"{type} ({detail})";
        return trigger.Enabled ? text : $"{text} disabled";
    }

    internal static string Detail(string? type, WorkflowTriggerConfig? config)
    {
        if (config is null) return "";
        return type switch
        {
            "Event"  => string.Join(" ", new[] { config.Event, config.EventEntity }.Where(v => !string.IsNullOrEmpty(v))),
            "Timed"  => config.CronExpression ?? "",
            "Api"    => config.ApiRoute ?? "",
            "Manual" => string.Join(", ", config.ManualEntities ?? []),
            _        => "",
        };
    }

    private static string NormaliseType(string? type, string at)
    {
        if (string.IsNullOrWhiteSpace(type))
            throw new WorkflowInputException($"{at}: trigger type is required (Manual, Timed, Event or Api).");
        return Types.FirstOrDefault(t => string.Equals(t, type.Trim(), StringComparison.OrdinalIgnoreCase))
               ?? throw new WorkflowInputException($"{at}: unknown trigger type '{type}' (expected Manual, Timed, Event or Api).");
    }

    private static void Validate(WorkflowTriggerRequest trigger, string at, bool fromFlags)
    {
        string Name(string flag, string field) => fromFlags ? flag : $"config.{field}";
        var c = trigger.Config;

        switch (trigger.Type)
        {
            case "Manual" when c.ManualEntities is null || c.ManualEntities.Count == 0
                               || c.ManualEntities.Any(string.IsNullOrWhiteSpace):
                throw new WorkflowInputException(
                    $"{at}: a Manual trigger needs at least one entity ({Name("--entity", "manual_entities")}), otherwise it can never be run.");
            case "Timed" when string.IsNullOrWhiteSpace(c.CronExpression):
                throw new WorkflowInputException($"{at}: a Timed trigger needs a cron expression ({Name("--cron", "cron_expression")}).");
            case "Api" when string.IsNullOrWhiteSpace(c.ApiRoute):
                throw new WorkflowInputException($"{at}: an Api trigger needs a route ({Name("--api-route", "api_route")}).");
            case "Event" when string.IsNullOrWhiteSpace(c.Event):
                throw new WorkflowInputException($"{at}: an Event trigger needs an event ({Name("--event", "event")}).");
            case "Event" when EntityEvents.Contains(c.Event!) && string.IsNullOrWhiteSpace(c.EventEntity):
                throw new WorkflowInputException($"{at}: an {c.Event} trigger needs an entity ({Name("--entity", "event_entity")}).");
        }
    }

    private static WorkflowTriggerConfig ReadConfig(string json, string at)
    {
        try
        {
            return JsonSerializer.Deserialize<WorkflowTriggerConfig>(json, ReadOpts) ?? new WorkflowTriggerConfig();
        }
        catch (JsonException ex)
        {
            throw new WorkflowInputException($"{at}: config is not valid: {ex.Message}");
        }
    }

    private static WorkflowTriggerConfig WithoutRunState(WorkflowTriggerConfig? config)
        => (config ?? new WorkflowTriggerConfig()) with { LastRunAt = null, NextRunAt = null };

    private static string ReadJsonOption(string? inline, string? path, string flag)
    {
        if (!string.IsNullOrEmpty(inline)) return inline;
        try { return File.ReadAllText(path!); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new WorkflowInputException($"Could not read {flag}-file '{path}': {ex.Message}");
        }
    }

    // The trigger endpoint takes `data` as a JSON string, not a nested object.
    internal static TriggerWorkflowRequest BuildRunRequest(string? payload, string? entity, int? entityId)
    {
        string? data = null;
        if (!string.IsNullOrEmpty(payload))
        {
            JsonNode? node;
            try { node = JsonNode.Parse(payload); }
            catch (JsonException ex) { throw new WorkflowInputException($"Invalid --payload JSON: {ex.Message}"); }
            if (node is not JsonObject obj)
                throw new WorkflowInputException("--payload must be a JSON object, e.g. '{\"title\":\"Hello\"}'.");
            data = obj.ToJsonString();
        }

        return new TriggerWorkflowRequest(
            string.IsNullOrWhiteSpace(entity) ? null : entity.Trim(),
            entityId,
            data);
    }
}
