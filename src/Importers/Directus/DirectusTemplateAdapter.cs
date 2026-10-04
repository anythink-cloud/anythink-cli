using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AnythinkCli.Importers.Directus;

public static class DirectusTemplateAdapter
{
    // Must match the target workflow engine's own template pattern so nothing it would resolve slips through.
    private static readonly Regex EnginePattern = new(@"\{\{([^}]+)\}\}", RegexOptions.Compiled);

    private static readonly Regex RecognisedExpression =
        new(@"^\$?[A-Za-z_][A-Za-z0-9_\.\[\]]*$", RegexOptions.Compiled);

    public static (string Adapted, bool HasUnresolved) Adapt(
        string input,
        string? previousStepKey,
        HashSet<string> knownStepKeys)
    {
        bool unresolved = false;

        var sb = new System.Text.StringBuilder();
        var last = 0;
        foreach (Match m in EnginePattern.Matches(input))
        {
            sb.Append(BreakBareReferences(input[last..m.Index], ref unresolved));
            last = m.Index + m.Length;

            var expr = m.Groups[1].Value.Trim();
            var translated = RecognisedExpression.IsMatch(expr)
                ? TranslateExpression(expr, previousStepKey, knownStepKeys, ref unresolved)
                : null;
            if (translated is not null && RecognisedExpression.IsMatch(translated))
            {
                sb.Append("{{ ").Append(translated).Append(" }}");
                continue;
            }

            unresolved = true;
            // Breaking the opener keeps a hostile source from resolving e.g. target-project secrets at run time.
            sb.Append("{ { ").Append(expr.Replace("{", "{ ")).Append(" }} ");
        }
        sb.Append(BreakBareReferences(input[last..], ref unresolved));
        var rewritten = sb.ToString();

        return (rewritten, unresolved);
    }

    // The engine also resolves a whole string value starting with "$anythink." even without braces.
    private static string BreakBareReferences(string text, ref bool unresolved)
    {
        if (!text.Contains("$anythink", StringComparison.OrdinalIgnoreCase)) return text;
        unresolved = true;
        return Regex.Replace(text, @"\$(?=anythink)", "$ ", RegexOptions.IgnoreCase);
    }

    public static (JsonElement Adapted, bool HasUnresolved) AdaptElement(
        JsonElement element,
        string? previousStepKey,
        HashSet<string> knownStepKeys)
    {
        bool unresolved = false;
        var node = WalkAdapt(element, previousStepKey, knownStepKeys, ref unresolved);
        var serialised = node is null
            ? JsonSerializer.SerializeToElement<object?>(null)
            : JsonSerializer.SerializeToElement(node);
        return (serialised, unresolved);
    }

    // ── internals ─────────────────────────────────────────────────────────────

    private static JsonNode? WalkAdapt(
        JsonElement element,
        string? previousStepKey,
        HashSet<string> knownStepKeys,
        ref bool unresolved)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var obj = new JsonObject();
                foreach (var prop in element.EnumerateObject())
                    obj[prop.Name] = WalkAdapt(prop.Value, previousStepKey, knownStepKeys, ref unresolved);
                return obj;

            case JsonValueKind.Array:
                var arr = new JsonArray();
                foreach (var item in element.EnumerateArray())
                    arr.Add(WalkAdapt(item, previousStepKey, knownStepKeys, ref unresolved));
                return arr;

            case JsonValueKind.String:
                var s = element.GetString() ?? "";
                var (adapted, hadUnresolved) = Adapt(s, previousStepKey, knownStepKeys);
                if (hadUnresolved) unresolved = true;
                return JsonValue.Create(adapted);

            case JsonValueKind.Number:
                return element.TryGetInt64(out var i)
                    ? JsonValue.Create(i)
                    : JsonValue.Create(element.GetDouble());

            case JsonValueKind.True:  return JsonValue.Create(true);
            case JsonValueKind.False: return JsonValue.Create(false);
            case JsonValueKind.Null:  return null;
            default:                  return null;
        }
    }

    private static string? TranslateExpression(
        string expr,
        string? previousStepKey,
        HashSet<string> knownStepKeys,
        ref bool unresolved)
    {
        // Strip optional leading $
        var body = expr.StartsWith("$") ? expr[1..] : expr;
        var parts = body.Split('.');
        if (parts.Length == 0) return null;

        var head = parts[0];
        if (head.Equals("anythink", StringComparison.OrdinalIgnoreCase))
        {
            unresolved = true;
            return null;
        }
        var tail = parts.Length > 1 ? string.Join('.', parts.Skip(1)) : "";

        switch (head)
        {
            case "trigger":
            {
                // Directus: $trigger.payload.X (event), $trigger.body.X (webhook),
                //           $trigger.id, $trigger.collection
                // Anythink:  $anythink.trigger.data.X / $anythink.trigger.id
                if (parts.Length == 1) return "$anythink.trigger";
                var second = parts[1];
                if (second == "payload" || second == "body")
                {
                    var rest = parts.Length > 2 ? string.Join('.', parts.Skip(2)) : "";
                    return string.IsNullOrEmpty(rest)
                        ? "$anythink.trigger.data"
                        : $"$anythink.trigger.data.{rest}";
                }
                if (second == "id") return "$anythink.trigger.id";
                if (second == "collection") return "$anythink.trigger.data.entity_name";
                // Unknown trigger field — leave literal.
                unresolved = true;
                return null;
            }

            case "last":
            {
                if (previousStepKey is null)
                {
                    unresolved = true;
                    return null;
                }
                return string.IsNullOrEmpty(tail)
                    ? $"$anythink.steps.{previousStepKey}"
                    : $"$anythink.steps.{previousStepKey}.{tail}";
            }

            case "env":
            case "accountability":
                // No direct Anythink equivalent — leave literal so the user
                // can replace with $anythink.secrets.* or a runtime variable.
                unresolved = true;
                return null;

            default:
            {
                // Directus also lets you reference an upstream operation by
                // its key, e.g. {{ $fetch_payload.title }}. If the head
                // matches a known step key, route through $anythink.steps.
                if (knownStepKeys.Contains(head))
                {
                    return string.IsNullOrEmpty(tail)
                        ? $"$anythink.steps.{head}"
                        : $"$anythink.steps.{head}.{tail}";
                }
                unresolved = true;
                return null;
            }
        }
    }
}
