using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AnythinkCli.Client;

// The items endpoint takes one query param per field (`field=OP:value`), not a single `filter` param.
internal static class ItemFilterQuery
{
    private static readonly Dictionary<string, string> OperatorPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["eq"] = "",
        ["ne"] = "!",
        ["neq"] = "!",
        ["gt"] = "GT:",
        ["gte"] = "GTE:",
        ["lt"] = "LT:",
        ["lte"] = "LTE:",
        ["contains"] = "C:",
        ["not_contains"] = "NC:",
        ["ncontains"] = "NC:",
        ["starts_with"] = "SW:",
        ["ends_with"] = "EW:",
        ["in"] = "IN:",
    };

    public static List<KeyValuePair<string, string>> Parse(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return [];

        var trimmed = filter.Trim();
        return trimmed.StartsWith('{') ? ParseJson(trimmed) : ParsePairs(trimmed);
    }

    public static string ToQueryString(IEnumerable<KeyValuePair<string, string>> pairs)
        => string.Concat(pairs.Select(p => $"&{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

    private static List<KeyValuePair<string, string>> ParsePairs(string filter)
    {
        var result = new List<KeyValuePair<string, string>>();
        foreach (var part in filter.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
                throw new ArgumentException($"Invalid filter '{part}'. Use field=value or field=OP:value (e.g. status=draft, price=GT:10).");
            result.Add(new(part[..eq].Trim(), part[(eq + 1)..].Trim()));
        }
        return result;
    }

    private static List<KeyValuePair<string, string>> ParseJson(string filter)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(filter) as JsonObject
                   ?? throw new ArgumentException("Filter JSON must be an object, e.g. {\"status\":\"draft\"}.");
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"Invalid filter JSON: {ex.Message}");
        }

        var result = new List<KeyValuePair<string, string>>();
        foreach (var (field, node) in root)
        {
            if (node is JsonObject ops)
            {
                var conditions = ops.Select(op => ToCondition(field, op.Key, op.Value)).ToList();
                // Several operators on one field need distinct keys; the API strips the `__op` suffix.
                if (conditions.Count == 1)
                    result.Add(new(field, conditions[0].Value));
                else
                    result.AddRange(conditions);
            }
            else
            {
                result.Add(new(field, ToCondition(field, "eq", node).Value));
            }
        }
        return result;
    }

    private static KeyValuePair<string, string> ToCondition(string field, string op, JsonNode? value)
    {
        var key = $"{field}__{op.ToLowerInvariant()}";

        if (op.Equals("null", StringComparison.OrdinalIgnoreCase))
            return new(key, IsTrue(field, op, value) ? "NULL:" : "NNULL:");
        if (op.Equals("exists", StringComparison.OrdinalIgnoreCase))
            return new(key, IsTrue(field, op, value) ? "EXISTS:" : "NOT_EXISTS:");

        if (!OperatorPrefixes.TryGetValue(op, out var prefix))
            throw new ArgumentException(
                $"Unknown filter operator '{op}' on '{field}'. Supported: {string.Join(", ", OperatorPrefixes.Keys.Append("null").Append("exists"))}.");

        if (value is null)
        {
            // The API re-prefixes `__ne` values unless they carry an operator it knows, and NULL:/NNULL: aren't among them.
            return prefix switch
            {
                "" => new($"{field}__null", "NULL:"),
                "!" => new($"{field}__null", "NNULL:"),
                _ => throw new ArgumentException($"Filter operator '{op}' on '{field}' needs a value."),
            };
        }

        if (value is JsonArray arr)
        {
            if (prefix is not ("" or "IN:"))
                throw new ArgumentException($"Filter operator '{op}' on '{field}' doesn't take a list; use 'in'.");
            return new(key, "IN:" + string.Join(",", arr.Select(v => Scalar(field, v))));
        }

        return new(key, prefix + Scalar(field, value));
    }

    private static bool IsTrue(string field, string op, JsonNode? value)
        => value is JsonValue v && v.TryGetValue<bool>(out var b)
            ? b
            : throw new ArgumentException($"Filter operator '{op}' on '{field}' takes true or false.");

    private static string Scalar(string field, JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonValue v when v.TryGetValue<bool>(out var b) => b ? "true" : "false",
        JsonValue v => v.ToJsonString(),
        _ => throw new ArgumentException($"Filter value for '{field}' must be a string, number, boolean or array of those."),
    };
}
