using System.Text.Json;
using AnythinkCli.Models;

namespace AnythinkCli.Importers.Directus;

// Maps Directus flow triggers and operations onto Anythink workflow triggers
// and WorkflowAction values, plus translates the per-operation `options` JSON
// into the parameter shape Anythink expects for that action.
//
// Anythink WorkflowAction values:
//   ReadData, CreateData, UpdateData, DeleteData, CallAnApi, RunScript,
//   SendACommand, Condition, SendAnEmail, Integration
//
// Anythink validates step parameters at run time against the action's
// parameter type, so this translator emits the snake_case property names
// each action type expects (e.g. RunScript → "script", SendAnEmail →
// "to" / "template_type" / "payload", CreateData → "entity_name" / "payload").

public record FlowStepTranslation(
    string       Action,
    JsonElement  Parameters,
    bool         NeedsManualReview,
    string?      ReviewNote
);

public static class DirectusFlowMapping
{
    /// <summary>
    /// Maps a Directus flow trigger to the Anythink Triggers array shape
    /// (a single trigger entry for each flow — Anythink supports multiple
    /// but Directus flows have exactly one trigger).
    /// </summary>
    public static WorkflowTriggerRequest MapTrigger(DirectusFlow flow)
    {
        var opts = flow.Options;

        switch (flow.Trigger.ToLowerInvariant())
        {
            case "schedule":
            {
                var cron = "0 9 * * *";
                if (opts.HasValue &&
                    opts.Value.TryGetProperty("cron", out var cronEl) &&
                    cronEl.ValueKind == JsonValueKind.String)
                    cron = cronEl.GetString() ?? cron;
                return new WorkflowTriggerRequest("Timed", true,
                    new WorkflowTriggerConfig(CronExpression: cron));
            }

            case "event":
            {
                // Directus event options: { scope: ["items.create"], collections: ["articles"] }
                var entity    = "";
                var eventName = "EntityCreated";

                if (opts.HasValue)
                {
                    if (opts.Value.TryGetProperty("collections", out var colsEl) &&
                        colsEl.ValueKind == JsonValueKind.Array &&
                        colsEl.GetArrayLength() > 0)
                        entity = colsEl[0].GetString() ?? "";

                    if (opts.Value.TryGetProperty("scope", out var scopeEl) &&
                        scopeEl.ValueKind == JsonValueKind.Array &&
                        scopeEl.GetArrayLength() > 0)
                    {
                        var scope = scopeEl[0].GetString() ?? "";
                        eventName = scope switch
                        {
                            "items.create" => "EntityCreated",
                            "items.update" => "EntityUpdated",
                            "items.delete" => "EntityDeleted",
                            _              => "EntityCreated"
                        };
                    }
                }

                return new WorkflowTriggerRequest("Event", true,
                    new WorkflowTriggerConfig(Event: eventName, EventEntity: entity));
            }

            case "webhook":
                // AnyAPI requires a non-empty api_route on Api triggers.
                // Directus webhooks don't carry a per-flow path, so derive one
                // from the flow name (lowercase, hyphenated).
                var route = SanitizeRoute(flow.Name);
                return new WorkflowTriggerRequest("Api", true,
                    new WorkflowTriggerConfig(ApiRoute: route));

            default: // manual, operation, or unknown
                return new WorkflowTriggerRequest("Manual", true,
                    new WorkflowTriggerConfig());
        }
    }

    /// <summary>
    /// Translates a Directus operation into an Anythink (action, parameters)
    /// pair with a flag indicating whether the result needs manual review
    /// after import (e.g. unmappable operation, lossy translation, or
    /// templating that may not work as-is).
    /// </summary>
    public static FlowStepTranslation Translate(DirectusOperation op)
    {
        var opts = op.Options;
        var type = op.Type.ToLowerInvariant();

        return type switch
        {
            "log"          => TranslateLog(opts),
            "mail"         => TranslateMail(opts),
            "notification" => TranslateNotification(opts),
            "request"      => TranslateRequest(opts),
            "webhook"      => TranslateRequest(opts),
            "item-create"  => TranslateItemCreate(opts),
            "item-read"    => TranslateItemRead(opts),
            "item-update"  => TranslateItemUpdate(opts),
            "item-delete"  => TranslateItemDelete(opts),
            "condition"    => TranslateCondition(opts),
            "transform"    => TranslateTransform(opts),
            "exec-script"  => TranslateExecScript(opts),
            "trigger"      => TranslateTrigger(opts),
            "sleep"        => TranslateSleep(opts),
            _              => Fallback("RunScript", opts,
                                  $"Directus operation type '{op.Type}' has no Anythink equivalent — review the auto-generated script.")
        };
    }

    // ── Per-operation translators ─────────────────────────────────────────────

    private static FlowStepTranslation TranslateLog(JsonElement? opts)
    {
        var message = TryGetString(opts, "message") ?? "(no message)";
        var script  = $"console.log({JsonSerializer.Serialize(message)});";
        var review  = ContainsMustache(message);
        return new FlowStepTranslation(
            Action:            "RunScript",
            Parameters:        Json(new { script }),
            NeedsManualReview: review,
            ReviewNote:        review ? "Log message contains Directus templating ({{...}}) — adapt to Anythink's expression syntax." : null);
    }

    private static FlowStepTranslation TranslateMail(JsonElement? opts)
    {
        // Directus 'to' may be a string OR a string[]. Anythink wants a string.
        var to       = FlattenToList(opts, "to") ?? "";
        var subject  = TryGetString(opts, "subject") ?? "";
        var body     = TryGetString(opts, "body")    ?? "";

        var payload  = JsonSerializer.Serialize(new { subject, body });
        return new FlowStepTranslation(
            Action:            "SendAnEmail",
            Parameters:        Json(new { to, template_type = "Custom", payload }),
            NeedsManualReview: ContainsMustache(subject) || ContainsMustache(body),
            ReviewNote:        "Body/subject mapped to a Custom template — confirm template_type aligns with your Anythink email setup.");
    }

    private static FlowStepTranslation TranslateNotification(JsonElement? opts) =>
        Fallback("RunScript", opts,
            "Directus 'notification' has no Anythink equivalent. Consider wiring an Integration (Slack/email) or replacing with custom logic.");

    private static FlowStepTranslation TranslateRequest(JsonElement? opts)
    {
        var url    = TryGetString(opts, "url")    ?? "";
        var method = (TryGetString(opts, "method") ?? "GET").ToUpperInvariant();
        var body   = TryGetString(opts, "body");

        // Directus headers can be either [{"header":"X","value":"Y"}] or {"X":"Y"}.
        Dictionary<string, string>? headers = null;
        if (opts.HasValue && opts.Value.TryGetProperty("headers", out var h))
        {
            headers = ExtractHeaders(h);
        }

        // Build as JsonNode then convert — anonymous types with embedded
        // Dictionary<string,string> don't round-trip cleanly through
        // SerializeToElement when later re-embedded in another payload.
        var node = new System.Text.Json.Nodes.JsonObject
        {
            ["url"]    = url,
            ["method"] = method,
        };
        if (headers is not null)
        {
            var hObj = new System.Text.Json.Nodes.JsonObject();
            foreach (var kv in headers) hObj[kv.Key] = kv.Value;
            node["headers"] = hObj;
        }
        if (!string.IsNullOrEmpty(body)) node["body"] = body;

        return new FlowStepTranslation(
            Action:            "CallAnApi",
            Parameters:        JsonSerializer.SerializeToElement(node),
            NeedsManualReview: ContainsMustache(url) || (body != null && ContainsMustache(body)),
            ReviewNote:        null);
    }

    private static FlowStepTranslation TranslateItemCreate(JsonElement? opts)
    {
        var collection = TryGetString(opts, "collection") ?? "";
        var payloadStr = StringifyPayload(opts, "payload");
        return new FlowStepTranslation(
            Action:            "CreateData",
            Parameters:        Json(new { entity_name = collection, payload = payloadStr }),
            NeedsManualReview: ContainsMustache(payloadStr),
            ReviewNote:        null);
    }

    private static FlowStepTranslation TranslateItemRead(JsonElement? opts)
    {
        var collection = TryGetString(opts, "collection") ?? "";
        return new FlowStepTranslation(
            Action:            "ReadData",
            Parameters:        Json(new { entity = collection }),
            NeedsManualReview: true,
            ReviewNote:        "Directus 'item-read' query filters aren't auto-translated — set filter_conditions / limit / fields on the Anythink side.");
    }

    private static FlowStepTranslation TranslateItemUpdate(JsonElement? opts)
    {
        var collection = TryGetString(opts, "collection") ?? "";
        var key        = TryGetString(opts, "key");
        var payloadStr = StringifyPayload(opts, "payload");
        var ids        = key ?? "";
        return new FlowStepTranslation(
            Action:            "UpdateData",
            Parameters:        Json(new { entity_name = collection, ids, payload = payloadStr }),
            NeedsManualReview: ContainsMustache(payloadStr) || ContainsMustache(ids),
            ReviewNote:        null);
    }

    private static FlowStepTranslation TranslateItemDelete(JsonElement? opts)
    {
        var collection = TryGetString(opts, "collection") ?? "";
        var key        = TryGetString(opts, "key") ?? "";
        var filter     = new[] { new { field = "id", @operator = "=", value = key } };
        return new FlowStepTranslation(
            Action:            "DeleteData",
            Parameters:        Json(new { entity_name = collection, filter_conditions = filter }),
            NeedsManualReview: ContainsMustache(key),
            ReviewNote:        null);
    }

    private static FlowStepTranslation TranslateCondition(JsonElement? opts) =>
        new(Action:            "Condition",
            Parameters:        Json(new { filter_conditions = Array.Empty<object>(), logical_operator = "AND" }),
            NeedsManualReview: true,
            ReviewNote:        "Directus filter expression isn't auto-translated — rewrite as Anythink FilterConditions.");

    private static FlowStepTranslation TranslateTransform(JsonElement? opts)
    {
        var code = TryGetString(opts, "json") ?? "// Directus transform — replace with your logic";
        return new FlowStepTranslation(
            Action:            "RunScript",
            Parameters:        Json(new { script = code }),
            NeedsManualReview: true,
            ReviewNote:        "Directus 'transform' templating syntax may differ from Anythink's script runtime — review the script body.");
    }

    private static FlowStepTranslation TranslateExecScript(JsonElement? opts)
    {
        var code = TryGetString(opts, "code") ?? "// (no code)";
        return new FlowStepTranslation(
            Action:            "RunScript",
            Parameters:        Json(new { script = code }),
            NeedsManualReview: true,
            ReviewNote:        "Directus 'exec-script' code preserved verbatim — Directus and Anythink JS runtimes differ; verify it runs.");
    }

    private static FlowStepTranslation TranslateTrigger(JsonElement? opts)
    {
        var flowRef = TryGetString(opts, "flow") ?? "";
        var payload = StringifyPayload(opts, "payload");
        return new FlowStepTranslation(
            Action:            "SendACommand",
            Parameters:        Json(new { queue_name = flowRef, payload }),
            NeedsManualReview: true,
            ReviewNote:        "Directus 'trigger' uses a flow UUID; map queue_name to the matching Anythink workflow / command queue.");
    }

    private static FlowStepTranslation TranslateSleep(JsonElement? opts)
    {
        var ms = 0;
        if (opts.HasValue &&
            opts.Value.TryGetProperty("milliseconds", out var msEl) &&
            msEl.ValueKind == JsonValueKind.Number)
            msEl.TryGetInt32(out ms);

        var script = $"// TODO: Anythink has no Delay action — original Directus sleep was {ms}ms";
        return new FlowStepTranslation(
            Action:            "RunScript",
            Parameters:        Json(new { script }),
            NeedsManualReview: true,
            ReviewNote:        "Anythink has no Delay action — replace with a scheduled trigger or external queue.");
    }

    private static FlowStepTranslation Fallback(string action, JsonElement? opts, string note) =>
        new(Action:            action,
            Parameters:        opts.HasValue ? opts.Value : Json(new { }),
            NeedsManualReview: true,
            ReviewNote:        note);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static JsonElement Json(object o) =>
        JsonSerializer.SerializeToElement(o,
            new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });

    private static string? TryGetString(JsonElement? opts, string key)
    {
        if (!opts.HasValue || opts.Value.ValueKind != JsonValueKind.Object) return null;
        if (!opts.Value.TryGetProperty(key, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => el.ToString(),
            _ => null
        };
    }

    private static string? FlattenToList(JsonElement? opts, string key)
    {
        if (!opts.HasValue || opts.Value.ValueKind != JsonValueKind.Object) return null;
        if (!opts.Value.TryGetProperty(key, out var el)) return null;
        if (el.ValueKind == JsonValueKind.String) return el.GetString();
        if (el.ValueKind == JsonValueKind.Array)
            return string.Join(",", el.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()));
        return null;
    }

    private static string StringifyPayload(JsonElement? opts, string key)
    {
        if (!opts.HasValue || opts.Value.ValueKind != JsonValueKind.Object) return "{}";
        if (!opts.Value.TryGetProperty(key, out var el)) return "{}";
        return el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? "{}"
            : el.GetRawText();
    }

    private static Dictionary<string, string>? ExtractHeaders(JsonElement node)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (node.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in node.EnumerateObject())
                if (prop.Value.ValueKind == JsonValueKind.String)
                    dict[prop.Name] = prop.Value.GetString()!;
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in node.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var k = item.TryGetProperty("header", out var h) ? h.GetString() : null;
                var v = item.TryGetProperty("value",  out var vv) ? vv.GetString() : null;
                if (!string.IsNullOrEmpty(k) && v is not null) dict[k!] = v;
            }
        }
        return dict.Count == 0 ? null : dict;
    }

    private static bool ContainsMustache(string? s) =>
        s is not null && s.Contains("{{");

    private static string SanitizeRoute(string name)
    {
        var slug = new System.Text.StringBuilder();
        foreach (var ch in name.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) slug.Append(ch);
            else if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
        }
        var s = slug.ToString().Trim('-');
        return string.IsNullOrEmpty(s) ? "webhook" : s;
    }
}
