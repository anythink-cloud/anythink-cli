using System.Text.Json;
using System.Text.Json.Nodes;
using Anythink.ProjectImporters.Directus;
using Anythink.ProjectImporters.Models;

namespace Anythink.ProjectImporters.N8n;

public static class N8nFlowMapping
{
    private const string NodePrefix = "n8n-nodes-base.";

    public static string ShortType(string type) =>
        type.StartsWith(NodePrefix, StringComparison.Ordinal) ? type[NodePrefix.Length..] : "";

    public static bool IsTrigger(N8nNode node) =>
        ShortType(node.Type) is var t && (t is "webhook" or "cron" or "start" || t.EndsWith("Trigger", StringComparison.Ordinal))
        || node.Type.EndsWith("Trigger", StringComparison.Ordinal);

    public static bool IsAnnotation(N8nNode node) => ShortType(node.Type) == "stickyNote";

    public static List<WorkflowTriggerRequest>? MapTriggers(N8nNode node, string workflowName)
    {
        var p = node.Parameters;
        switch (ShortType(node.Type))
        {
            case "manualTrigger" or "start":
                return [new("Manual", true, new { manual_entities = Array.Empty<string>() })];
            case "webhook":
                return [new("Api", true, new { api_route = DirectusFlowMapping.SanitizeRoute(Text(p, "path") ?? workflowName) })];
            case "scheduleTrigger":
                return Timed(Items(p, "rule", "interval"), ScheduleCron);
            case "cron":
                return Timed(Items(p, "triggerTimes", "item"), CronNodeCron);
            default:
                return null;
        }
    }

    public static N8nStepTranslation? Translate(N8nNode node)
    {
        var p = node.Parameters;
        return ShortType(node.Type) switch
        {
            "httpRequest" => HttpRequest(p),
            "set" => SetNode(p),
            "code" when IsJavaScript(p) => Script(Text(p, "jsCode")),
            "function" => Script(Text(p, "functionCode")),
            "functionItem" => Script(Text(p, "functionCode")),
            "if" => new("Condition",
                Json(new { filter_conditions = new[] { new { field = "n8n_if_not_imported", @operator = "eq", value = "never" } }, logical_operator = "AND" }),
                true,
                "n8n 'If' conditions aren't auto-translated. The placeholder condition never matches, so the false branch always runs until you rewrite it as Anythink filter conditions. The true branch is wired to success and the false branch to failure."),
            "noOp" => Finish("RunScript", new() { ["script"] = "// n8n No Operation" }, false, null),
            "emailSend" => EmailSend(p),
            _ => null
        };
    }

    public static N8nStepTranslation Placeholder(N8nNode node)
    {
        var script = $"throw new Error({JsonSerializer.Serialize($"n8n node not imported: {OneLine(node.Name)} ({OneLine(node.Type)})")});";
        return new("RunScript", Json(new { script }), true,
            "No Anythink equivalent for this n8n node — rebuild it by hand. Left disabled; if enabled it stops the run rather than passing data through.", Enabled: false);
    }

    public static List<(string Type, string Name)> Credentials(N8nNode node)
    {
        var list = new List<(string, string)>();
        if (node.Credentials is { ValueKind: JsonValueKind.Object } creds)
            foreach (var c in creds.EnumerateObject())
                list.Add((c.Name, Text(c.Value, "name") ?? c.Name));
        return list;
    }

    // ── Triggers ──────────────────────────────────────────────────────────────

    private static List<WorkflowTriggerRequest>? Timed(List<JsonElement> items, Func<JsonElement, string?> cronFor)
    {
        if (items.Count == 0) return null;
        var triggers = new List<WorkflowTriggerRequest>();
        foreach (var item in items)
        {
            if (cronFor(item) is not { } cron) return null;
            triggers.Add(new("Timed", true, new { cron_expression = cron }));
        }
        return triggers;
    }

    private static string? ScheduleCron(JsonElement i)
    {
        var minute = Num(i, "triggerAtMinute") ?? 0;
        var hour = Num(i, "triggerAtHour") ?? 0;
        return Text(i, "field") switch
        {
            "cronExpression" => Text(i, "expression"),
            "minutes" => Every(Num(i, "minutesInterval") ?? 1, n => $"*/{n} * * * *", "* * * * *"),
            "hours" => Every(Num(i, "hoursInterval") ?? 1, n => $"{minute} */{n} * * *", $"{minute} * * * *"),
            "days" => Every(Num(i, "daysInterval") ?? 1, n => $"{minute} {hour} */{n} * *", $"{minute} {hour} * * *"),
            "weeks" when (Num(i, "weeksInterval") ?? 1) == 1 && Weekdays(i) is { } days => $"{minute} {hour} * * {days}",
            "months" when (Num(i, "monthsInterval") ?? 1) == 1 => $"{minute} {hour} {Num(i, "triggerAtDayOfMonth") ?? 1} * *",
            _ => null
        };
    }

    private static string? CronNodeCron(JsonElement i)
    {
        var minute = Num(i, "minute") ?? 0;
        var hour = Num(i, "hour") ?? 0;
        return Text(i, "mode") switch
        {
            "everyMinute" => "* * * * *",
            "everyHour" => $"{minute} * * * *",
            "everyDay" => $"{minute} {hour} * * *",
            "everyWeek" => $"{minute} {hour} * * {Num(i, "weekday") ?? 1}",
            "everyMonth" => $"{minute} {hour} {Num(i, "dayOfMonth") ?? 1} * *",
            "everyX" => (Text(i, "unit"), Num(i, "value")) switch
            {
                ("minutes", { } n) => Every(n, v => $"*/{v} * * * *", "* * * * *"),
                ("hours", { } n) => Every(n, v => $"0 */{v} * * *", "0 * * * *"),
                ("days", { } n) => Every(n, v => $"0 0 */{v} * *", "0 0 * * *"),
                _ => null
            },
            _ => null
        };
    }

    private static string? Every(int n, Func<int, string> format, string once) =>
        n < 1 ? null : n == 1 ? once : format(n);

    private static string? Weekdays(JsonElement i)
    {
        if (!i.TryGetProperty("triggerAtDay", out var days) || days.ValueKind != JsonValueKind.Array) return null;
        var list = days.EnumerateArray().Where(d => d.ValueKind == JsonValueKind.Number).Select(d => d.GetInt32()).ToList();
        return list.Count == 0 ? null : string.Join(",", list);
    }

    // ── Steps ─────────────────────────────────────────────────────────────────

    private static N8nStepTranslation HttpRequest(JsonElement? p)
    {
        var url = Expr(Text(p, "url"), out var hasExpr) ?? "";
        var query = Pairs(p, "queryParameters", out var queryExpr);
        if (query.Count > 0)
            url += (url.Contains('?') ? "&" : "?") + string.Join("&", query.Select(q => $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(q.Value)}"));

        var (safeUrl, redacted) = DirectusFlowMapping.RedactUrl(url);
        var node = new JsonObject { ["url"] = safeUrl, ["method"] = (Text(p, "method") ?? "GET").ToUpperInvariant() };

        var headers = Pairs(p, "headerParameters", out var headerExpr);
        if (headers.Count > 0)
        {
            var h = new JsonObject();
            foreach (var kv in headers)
            {
                var sensitive = DirectusFlowMapping.IsSensitiveHeader(kv.Key);
                redacted |= sensitive;
                h[kv.Key] = sensitive ? DirectusFlowMapping.RedactedHeaderValue : kv.Value;
            }
            node["headers"] = h;
        }

        var bodyExpr = false;
        var body = Expr(Text(p, "jsonBody") ?? Text(p, "body"), out bodyExpr);
        if (body is null && Pairs(p, "bodyParameters", out bodyExpr) is { Count: > 0 } fields)
            body = new JsonObject(fields.Select(f => KeyValuePair.Create(f.Key, (JsonNode?)f.Value))).ToJsonString();
        if (!string.IsNullOrEmpty(body))
        {
            var (safeBody, bodyRedacted) = DirectusFlowMapping.RedactJsonBody(body);
            redacted |= bodyRedacted;
            node["body"] = safeBody;
        }

        var notes = new List<string>();
        if (HasUnmappedHttpOptions(p))
            notes.Add("Some request settings (JSON parameters, options, form or multipart bodies) have no Anythink equivalent and were not imported — check the request.");
        var usesCredential = Text(p, "authentication") is { } auth && auth != "none";
        if (redacted) notes.Add("Credentials in the URL, headers or body were replaced with a placeholder — store the real value as an Anythink secret and reference it.");
        if (usesCredential) notes.Add("The request used an n8n credential, which is never imported — set up the connection or add the auth header yourself.");

        return Finish("CallAnApi", node, hasExpr || queryExpr || headerExpr || bodyExpr || redacted || usesCredential || notes.Count > 0,
            notes.Count == 0 ? null : string.Join(" ", notes));
    }

    private static bool HasUnmappedHttpOptions(JsonElement? p)
    {
        if (p is not { ValueKind: JsonValueKind.Object } o) return false;
        foreach (var prop in o.EnumerateObject())
        {
            if (prop.Name.EndsWith("ParametersJson", StringComparison.Ordinal)) return true;
            if (prop.Name == "jsonParameters" && prop.Value.ValueKind == JsonValueKind.True) return true;
            if (prop.Name is "specifyHeaders" or "specifyQuery" && prop.Value.ValueKind == JsonValueKind.String && prop.Value.GetString() == "json") return true;
            if (prop.Name == "options" && prop.Value.ValueKind == JsonValueKind.Object && prop.Value.EnumerateObject().Any()) return true;
            if (prop.Name is "contentType" or "bodyContentType" && prop.Value.ValueKind == JsonValueKind.String
                && prop.Value.GetString() is "multipart-form-data" or "form-urlencoded") return true;
        }
        return false;
    }

    private static N8nStepTranslation SetNode(JsonElement? p)
    {
        var values = new JsonObject();
        var hasExpr = false;

        if (p.HasValue && p.Value.TryGetProperty("assignments", out var a) && a.TryGetProperty("assignments", out var list)
            && list.ValueKind == JsonValueKind.Array)
            foreach (var item in list.EnumerateArray())
                Assign(values, Text(item, "name"), Raw(item, "value"), Text(item, "type"), ref hasExpr);

        if (p.HasValue && p.Value.TryGetProperty("values", out var legacy) && legacy.ValueKind == JsonValueKind.Object)
            foreach (var group in legacy.EnumerateObject().Where(g => g.Value.ValueKind == JsonValueKind.Array))
                foreach (var item in group.Value.EnumerateArray())
                    Assign(values, Text(item, "name"), Raw(item, "value"), group.Name, ref hasExpr);

        var (safe, redacted) = DirectusFlowMapping.RedactJsonBody(values.ToJsonString());
        var note = "n8n 'Set' also passes through the incoming fields unless it keeps only the set ones — this step returns just the assigned values. n8n expressions are returned as literal text, not evaluated. Check the output.";
        if (redacted) note += " Secret-looking values were replaced with a placeholder — store the real value as an Anythink secret.";
        return Finish("RunScript", new() { ["script"] = $"return {safe};" }, true, note);
    }

    private static void Assign(JsonObject into, string? name, JsonElement? value, string? type, ref bool hasExpr)
    {
        if (string.IsNullOrEmpty(name) || value is not { } v) return;
        if (v.ValueKind == JsonValueKind.String)
        {
            var s = Expr(v.GetString(), out var expr);
            hasExpr |= expr;
            into[name] = type switch
            {
                "number" when !expr && double.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var d) => JsonValue.Create(d),
                "boolean" when !expr && bool.TryParse(s, out var b) => JsonValue.Create(b),
                _ => JsonValue.Create(s)
            };
        }
        else into[name] = JsonNode.Parse(v.GetRawText());
    }

    private static N8nStepTranslation Script(string? code) =>
        Finish("RunScript", new() { ["script"] = code ?? "// (no code)" }, true,
            "n8n Code uses $input, $json and items — the Anythink script runtime differs; verify it runs.");

    private static N8nStepTranslation EmailSend(JsonElement? p)
    {
        var to = Expr(Text(p, "toEmail"), out var e1) ?? "";
        var subject = Expr(Text(p, "subject"), out var e2) ?? "";
        var body = Expr(Text(p, "html") ?? Text(p, "text"), out var e3) ?? "";
        return Finish("SendAnEmail",
            new() { ["to"] = to, ["template_type"] = "Custom", ["payload"] = new JsonObject { ["subject"] = subject, ["body"] = body }.ToJsonString() },
            true,
            "Sender address and the n8n email credential aren't imported — confirm the Anythink email setup.");
    }

    private static N8nStepTranslation Finish(string action, JsonObject parameters, bool review, string? note)
    {
        var (adapted, unresolved) = DirectusTemplateAdapter.AdaptElement(
            JsonSerializer.SerializeToElement(parameters), null, new HashSet<string>());
        return new(action, adapted, review || unresolved,
            note ?? (unresolved ? "n8n expressions couldn't be auto-translated to Anythink syntax — review the values." : null));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? Expr(string? value, out bool isExpression)
    {
        isExpression = value is not null && (value.StartsWith('=') || value.Contains("{{"));
        return value is not null && value.StartsWith('=') ? value[1..] : value;
    }

    private static Dictionary<string, string> Pairs(JsonElement? p, string group, out bool hasExpr)
    {
        hasExpr = false;
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Items(p, group, "parameters"))
            if (Text(item, "name") is { Length: > 0 } name)
            {
                result[name] = Expr(Text(item, "value"), out var expr) ?? "";
                hasExpr |= expr;
            }
        return result;
    }

    private static List<JsonElement> Items(JsonElement? p, string group, string list) =>
        p.HasValue && p.Value.TryGetProperty(group, out var g) && g.ValueKind == JsonValueKind.Object
        && g.TryGetProperty(list, out var items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object).ToList()
            : [];

    private static bool IsJavaScript(JsonElement? p) =>
        Text(p, "language") is null or "javaScript";

    private static string? Text(JsonElement? el, string name) =>
        el is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => v.ToString(),
                _ => null
            }
            : null;

    private static JsonElement? Raw(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) ? v : null;

    private static int? Num(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)
            ? n : null;

    private static string OneLine(string s) =>
        new(s.Select(c => char.IsControl(c) || char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.LineSeparator or System.Globalization.UnicodeCategory.ParagraphSeparator ? ' ' : c).ToArray());

    private static JsonElement Json(object o) => JsonSerializer.SerializeToElement(o);
}

public record N8nStepTranslation(
    string Action,
    JsonElement Parameters,
    bool NeedsManualReview,
    string? ReviewNote,
    bool Enabled = true
);
