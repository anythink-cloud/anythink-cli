using System.Text.Json;
using AnythinkCli.Models;

namespace AnythinkCli.Importers.Directus;

public record FlowStepTranslation(
    string Action,
    JsonElement Parameters,
    bool NeedsManualReview,
    string? ReviewNote,
    bool Enabled = true
);

public static class DirectusFlowMapping
{
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
                    var entity = "";
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
                                _ => "EntityCreated"
                            };
                        }
                    }

                    return new WorkflowTriggerRequest("Event", true,
                        new WorkflowTriggerConfig(Event: eventName, EventEntity: entity));
                }

            case "webhook":
                // Api triggers need a route and Directus webhooks have none, so derive it from the flow name.
                var route = SanitizeRoute(flow.Name);
                return new WorkflowTriggerRequest("Api", true,
                    new WorkflowTriggerConfig(ApiRoute: route));

            default: // manual, operation, or unknown
                return new WorkflowTriggerRequest("Manual", true,
                    new WorkflowTriggerConfig());
        }
    }

    public static FlowStepTranslation Translate(DirectusOperation op)
    {
        var opts = op.Options;
        var type = op.Type.ToLowerInvariant();

        return type switch
        {
            "log" => TranslateLog(opts),
            "mail" => TranslateMail(opts),
            "notification" => TranslateNotification(opts),
            "request" => TranslateRequest(opts),
            "webhook" => TranslateRequest(opts),
            "item-create" => TranslateItemCreate(opts),
            "item-read" => TranslateItemRead(opts),
            "item-update" => TranslateItemUpdate(opts),
            "item-delete" => TranslateItemDelete(opts),
            "condition" => TranslateCondition(opts),
            "transform" => TranslateTransform(opts),
            "exec-script" => TranslateExecScript(opts),
            "trigger" => TranslateTrigger(opts),
            "sleep" => TranslateSleep(opts),
            _ => Fallback("RunScript", opts,
                                  $"Directus operation type '{op.Type}' has no Anythink equivalent — review the auto-generated script.")
        };
    }

    // ── Per-operation translators ─────────────────────────────────────────────

    private static FlowStepTranslation TranslateLog(JsonElement? opts)
    {
        var message = TryGetString(opts, "message") ?? "(no message)";
        var script = $"console.log({JsonSerializer.Serialize(message)});";
        var review = ContainsMustache(message);
        return new FlowStepTranslation(
            Action: "RunScript",
            Parameters: Json(new { script }),
            NeedsManualReview: review,
            ReviewNote: review ? "Log message contains Directus templating ({{...}}) — adapt to Anythink's expression syntax." : null);
    }

    private static FlowStepTranslation TranslateMail(JsonElement? opts)
    {
        // Directus 'to' may be a string OR a string[]. Anythink wants a string.
        var to = FlattenToList(opts, "to") ?? "";
        var subject = TryGetString(opts, "subject") ?? "";
        var body = TryGetString(opts, "body") ?? "";

        var payload = JsonSerializer.Serialize(new { subject, body });
        return new FlowStepTranslation(
            Action: "SendAnEmail",
            Parameters: Json(new { to, template_type = "Custom", payload }),
            NeedsManualReview: ContainsMustache(subject) || ContainsMustache(body),
            ReviewNote: "Body/subject mapped to a Custom template — confirm template_type aligns with your Anythink email setup.");
    }

    private static FlowStepTranslation TranslateNotification(JsonElement? opts) =>
        Fallback("RunScript", opts,
            "Directus 'notification' has no Anythink equivalent. Consider wiring an Integration (Slack/email) or replacing with custom logic.");

    private static FlowStepTranslation TranslateRequest(JsonElement? opts)
    {
        var url = TryGetString(opts, "url") ?? "";
        var method = (TryGetString(opts, "method") ?? "GET").ToUpperInvariant();
        var body = TryGetString(opts, "body");

        // Directus headers can be either [{"header":"X","value":"Y"}] or {"X":"Y"}.
        Dictionary<string, string>? headers = null;
        if (opts.HasValue && opts.Value.TryGetProperty("headers", out var h))
        {
            headers = ExtractHeaders(h);
        }

        // Anonymous types holding a Dictionary don't round-trip through SerializeToElement.
        var (safeUrl, urlRedacted) = RedactUrl(url);
        var node = new System.Text.Json.Nodes.JsonObject
        {
            ["url"] = safeUrl,
            ["method"] = method,
        };
        var redacted = urlRedacted;
        if (headers is not null)
        {
            var hObj = new System.Text.Json.Nodes.JsonObject();
            foreach (var kv in headers)
            {
                var sensitive = IsSensitiveHeader(kv.Key);
                redacted |= sensitive;
                hObj[kv.Key] = sensitive ? RedactedHeaderValue : kv.Value;
            }
            node["headers"] = hObj;
        }
        if (!string.IsNullOrEmpty(body))
        {
            var (safeBody, bodyRedacted) = RedactJsonBody(body);
            redacted |= bodyRedacted;
            node["body"] = safeBody;
        }

        return new FlowStepTranslation(
            Action: "CallAnApi",
            Parameters: JsonSerializer.SerializeToElement(node),
            NeedsManualReview: redacted || ContainsMustache(url) || (body != null && ContainsMustache(body)),
            ReviewNote: redacted
                ? "Credentials in the URL, headers or body were replaced with a placeholder — store the real value as an Anythink secret and reference it."
                : null);
    }

    private static FlowStepTranslation TranslateItemCreate(JsonElement? opts)
    {
        var collection = TryGetString(opts, "collection") ?? "";
        var payloadStr = StringifyPayload(opts, "payload");
        return new FlowStepTranslation(
            Action: "CreateData",
            Parameters: Json(new { entity_name = collection, payload = payloadStr }),
            NeedsManualReview: ContainsMustache(payloadStr),
            ReviewNote: null);
    }

    private static FlowStepTranslation TranslateItemRead(JsonElement? opts)
    {
        var collection = TryGetString(opts, "collection") ?? "";
        return new FlowStepTranslation(
            Action: "ReadData",
            Parameters: Json(new { entity = collection }),
            NeedsManualReview: true,
            ReviewNote: "Directus 'item-read' query filters aren't auto-translated — set filter_conditions / limit / fields on the Anythink side.");
    }

    private static FlowStepTranslation TranslateItemUpdate(JsonElement? opts)
    {
        var collection = TryGetString(opts, "collection") ?? "";
        var key = TryGetString(opts, "key");
        var payloadStr = StringifyPayload(opts, "payload");
        var ids = key ?? "";
        return new FlowStepTranslation(
            Action: "UpdateData",
            Parameters: Json(new { entity_name = collection, ids, payload = payloadStr }),
            NeedsManualReview: ContainsMustache(payloadStr) || ContainsMustache(ids),
            ReviewNote: null);
    }

    private static FlowStepTranslation TranslateItemDelete(JsonElement? opts)
    {
        var collection = TryGetString(opts, "collection") ?? "";
        var rawKey = opts.HasValue && opts.Value.ValueKind == JsonValueKind.Object &&
                         opts.Value.TryGetProperty("key", out var el) ? el.GetRawText() : null;
        if (ContainsMustache(rawKey))
            return UntranslatedDelete(collection,
                $"Delete key was the template {rawKey}; the engine drops a filter whose value resolves empty or non-numeric, " +
                "which would delete every row. Rebuild it with an explicit ReadData step and a checked id.");

        var keys = ReadDeleteKeys(opts);
        if (collection.Length == 0 || keys is null)
            return UntranslatedDelete(collection);

        // The filter builder has no "in" operator, so several keys ride on the query syntax's IN: prefix.
        var value = keys.Count == 1 ? keys[0] : "IN:" + string.Join(",", keys);
        var filter = new[] { new { field = "id", @operator = "eq", value } };
        return new FlowStepTranslation(
            Action: "DeleteData",
            Parameters: Json(new { entity_name = collection, filter_conditions = filter }),
            NeedsManualReview: true,
            ReviewNote: $"Deletes '{collection}' rows with hard-coded id(s) {string.Join(", ", keys)} — confirm this is intended.");
    }

    private static FlowStepTranslation UntranslatedDelete(string collection, string? note = null)
    {
        var name = collection.Length > 0 && collection.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
            ? collection : "(unknown)";
        var script = $"// Directus item-delete on '{name}' could not be translated safely — no delete was created.";
        return new FlowStepTranslation(
            Action: "RunScript",
            Parameters: Json(new { script }),
            NeedsManualReview: true,
            ReviewNote: (note ?? "Directus 'item-delete' without a plain key can't be mapped to a filtered delete — rebuild it by hand.") + " Left disabled.",
            Enabled: false);
    }

    private static List<string>? ReadDeleteKeys(JsonElement? opts)
    {
        if (!opts.HasValue || opts.Value.ValueKind != JsonValueKind.Object ||
            !opts.Value.TryGetProperty("key", out var el))
            return null;

        var raw = el.ValueKind switch
        {
            JsonValueKind.String or JsonValueKind.Number => new List<string?> { el.ToString() },
            JsonValueKind.Array => el.EnumerateArray()
                .Select(e => e.ValueKind is JsonValueKind.String or JsonValueKind.Number ? e.ToString() : null)
                .ToList(),
            _ => []
        };
        if (raw.Count == 0 || raw.Any(string.IsNullOrWhiteSpace)) return null;

        var keys = raw.Select(k => k!.Trim()).ToList();
        return keys.All(k => k.All(char.IsAsciiDigit)) ? keys : null;
    }

    private static FlowStepTranslation TranslateCondition(JsonElement? opts) =>
        new(Action: "Condition",
            Parameters: Json(new { filter_conditions = Array.Empty<object>(), logical_operator = "AND" }),
            NeedsManualReview: true,
            ReviewNote: "Directus filter expression isn't auto-translated — rewrite as Anythink FilterConditions.");

    private static FlowStepTranslation TranslateTransform(JsonElement? opts)
    {
        var code = TryGetString(opts, "json") ?? "// Directus transform — replace with your logic";
        return new FlowStepTranslation(
            Action: "RunScript",
            Parameters: Json(new { script = code }),
            NeedsManualReview: true,
            ReviewNote: "Directus 'transform' templating syntax may differ from Anythink's script runtime — review the script body.");
    }

    private static FlowStepTranslation TranslateExecScript(JsonElement? opts)
    {
        var code = TryGetString(opts, "code") ?? "// (no code)";
        return new FlowStepTranslation(
            Action: "RunScript",
            Parameters: Json(new { script = code }),
            NeedsManualReview: true,
            ReviewNote: "Directus 'exec-script' code preserved verbatim — Directus and Anythink JS runtimes differ; verify it runs.");
    }

    private static FlowStepTranslation TranslateTrigger(JsonElement? opts)
    {
        var flowRef = TryGetString(opts, "flow") ?? "";
        var payload = StringifyPayload(opts, "payload");
        return new FlowStepTranslation(
            Action: "SendACommand",
            Parameters: Json(new { queue_name = flowRef, payload }),
            NeedsManualReview: true,
            ReviewNote: "Directus 'trigger' uses a flow UUID; map queue_name to the matching Anythink workflow / command queue.");
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
            Action: "RunScript",
            Parameters: Json(new { script }),
            NeedsManualReview: true,
            ReviewNote: "Anythink has no Delay action — replace with a scheduled trigger or external queue.");
    }

    private static FlowStepTranslation Fallback(string action, JsonElement? opts, string note) =>
        new(Action: action,
            Parameters: opts.HasValue ? opts.Value : Json(new { }),
            NeedsManualReview: true,
            ReviewNote: note);

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
                var v = item.TryGetProperty("value", out var vv) ? vv.GetString() : null;
                if (!string.IsNullOrEmpty(k) && v is not null) dict[k!] = v;
            }
        }
        return dict.Count == 0 ? null : dict;
    }

    internal const string RedactedHeaderValue = "REPLACE_WITH_SECRET";

    private static readonly string[] SensitiveHeaderFragments =
        ["auth", "token", "key", "secret", "cookie", "password", "signature", "sig", "session", "credential", "x-api"];

    private static readonly string[] SensitiveQueryFragments =
        ["key", "token", "secret", "password", "signature", "auth", "sig", "api", "session", "credential"];

    private static readonly System.Text.RegularExpressions.Regex UrlUserInfo =
        new(@"^([A-Za-z][A-Za-z0-9+.\-]*://)[^/?#@\s]*@", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static (string Url, bool Redacted) RedactUrl(string url)
    {
        var redacted = false;
        var stripped = UrlUserInfo.Replace(url, "$1");
        redacted |= stripped != url;

        var q = stripped.IndexOf('?');
        if (q < 0) return (stripped, redacted);

        var parts = stripped[(q + 1)..].Split('&').Select(p =>
        {
            var eq = p.IndexOf('=');
            if (eq <= 0) return p;
            var name = p[..eq];
            if (!SensitiveQueryFragments.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase))) return p;
            redacted = true;
            return name + "=" + RedactedHeaderValue;
        });
        return (stripped[..(q + 1)] + string.Join("&", parts), redacted);
    }

    private static (string Body, bool Redacted) RedactJsonBody(string body)
    {
        System.Text.Json.Nodes.JsonNode? root;
        try { root = System.Text.Json.Nodes.JsonNode.Parse(body); }
        catch (JsonException) { return (body, false); }
        if (root is null) return (body, false);

        var redacted = false;
        void Walk(System.Text.Json.Nodes.JsonNode? n)
        {
            if (n is System.Text.Json.Nodes.JsonObject o)
                foreach (var key in o.Select(kv => kv.Key).ToList())
                {
                    if (o[key] is System.Text.Json.Nodes.JsonValue && SensitiveQueryFragments.Any(f => key.Contains(f, StringComparison.OrdinalIgnoreCase)))
                    {
                        o[key] = RedactedHeaderValue;
                        redacted = true;
                    }
                    else Walk(o[key]);
                }
            else if (n is System.Text.Json.Nodes.JsonArray a)
                foreach (var item in a) Walk(item);
        }
        Walk(root);
        return redacted ? (root.ToJsonString(), true) : (body, false);
    }

    internal static bool IsSensitiveHeader(string name) =>
        SensitiveHeaderFragments.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase));

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
