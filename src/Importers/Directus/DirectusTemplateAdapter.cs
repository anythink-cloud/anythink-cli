using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AnythinkCli.Importers.Directus;

// Rewrites Directus mustache template expressions into Anythink's
// $anythink.* syntax so imported workflows substitute values correctly at
// runtime instead of printing the literal mustache.
//
// Directus uses:
//   {{ $trigger.payload.X }}     ← entity event payload (Event trigger)
//   {{ $trigger.body.X }}        ← webhook body (Webhook trigger)
//   {{ $last.X }}                ← output of the previous operation
//   {{ $<step_key>.X }}          ← output of a specific upstream operation
//   {{ $env.X }}                 ← environment variable
//   {{ $accountability.user }}   ← authenticated user metadata
//
// Anythink uses:
//   {{ $anythink.trigger.id }}
//   {{ $anythink.trigger.data.X }}
//   {{ $anythink.steps.<step_key>.X }}
//   {{ $anythink.secrets.X }}
//   {{ $anythink.now }}
//
// Unresolved cases (env, accountability) remain as-is and the step is
// flagged for manual review so the user can adapt them by hand.

public static class DirectusTemplateAdapter
{
    private static readonly Regex MustachePattern =
        new(@"\{\{\s*(?<expr>\$?[A-Za-z_][A-Za-z0-9_\.\[\]]*)\s*\}\}",
            RegexOptions.Compiled);

    /// <summary>
    /// Rewrite a single template string. Returns the adapted string plus a
    /// flag indicating whether any expression couldn't be translated (so the
    /// caller can keep the manual-review marker).
    /// </summary>
    public static (string Adapted, bool HasUnresolved) Adapt(
        string input,
        string? previousStepKey,
        HashSet<string> knownStepKeys)
    {
        bool unresolved = false;

        var rewritten = MustachePattern.Replace(input, m =>
        {
            var expr = m.Groups["expr"].Value;
            var translated = TranslateExpression(expr, previousStepKey, knownStepKeys, ref unresolved);
            if (translated is not null)
                return "{{ " + translated + " }}";

            // Unresolved: NEUTER the mustache so Anythink's server-side
            // PayloadTemplater (regex `\{\{([^}]+)\}\}`) can't resolve it.
            // We break the leading `{{` with a space → the regex won't match
            // and the expression renders literally. Critical: stops a hostile
            // source from injecting e.g. `{{ $anythink.secrets.X }}` and
            // exfiltrating target tenant secrets at workflow run time.
            return "{ { " + expr + " }} ";
        });

        return (rewritten, unresolved);
    }

    /// <summary>
    /// Recursively walk a JsonElement, rewriting every string value via Adapt.
    /// Returns the new JsonElement and whether any expression was left
    /// unresolved during the walk.
    /// </summary>
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

    /// <summary>
    /// Translate a single template expression (the bit inside the {{ }}).
    /// Returns null if no translation rule matched — the caller leaves the
    /// expression literal and flags it for manual review.
    /// </summary>
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
