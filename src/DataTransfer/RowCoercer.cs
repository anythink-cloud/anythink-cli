using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AnythinkCli.Commands;
using AnythinkCli.Models;

namespace AnythinkCli.DataTransfer;

internal sealed partial class RowCoercer
{
    private static readonly HashSet<string> SystemColumns =
        new(["id", "created_at", "updated_at", "tenant_id", "locked"], StringComparer.Ordinal);
    private static readonly string[] IdTypes = ["many-to-many", "file", "user"];

    private readonly Dictionary<string, string> _types;
    private readonly HashSet<string> _skipped;
    private readonly bool _ignoreUnknown;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _notes = new();

    public RowCoercer(IEnumerable<Field> fields, bool ignoreUnknown)
    {
        var all = fields.ToList();
        _ignoreUnknown = ignoreUnknown;
        _types = all.ToDictionary(f => f.Name, f => f.DatabaseType.ToLowerInvariant(), StringComparer.Ordinal);
        _skipped = all.Where(f => _types[f.Name] is "one-to-many" or "secret" || f.DisplayType == "secret")
            .Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}([T ]\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:?\d{2})?)?$")]
    private static partial Regex IsoDate();

    public IReadOnlyList<string> Notes()
    {
        string Join(string kind) => string.Join(", ", _notes.Where(n => n.Value == kind).Select(n => n.Key).Order(StringComparer.Ordinal));
        var notes = new List<string>();
        if (Join("system") is { Length: > 0 } sys) notes.Add($"ignored system columns: {sys}");
        if (Join("skipped") is { Length: > 0 } skipped) notes.Add($"ignored columns that can't be copied (one-to-many collections and secrets): {skipped}");
        foreach (var column in _notes.Where(n => n.Value == "ids").Select(n => n.Key).Order(StringComparer.Ordinal))
            notes.Add($"'{column}' ids are copied verbatim and are only valid within the same project");
        return notes;
    }

    public bool TryCoerce(JsonObject raw, bool fromCsv, out JsonObject payload, out string? error)
    {
        payload = new JsonObject();
        var problems = new List<string>();
        foreach (var (name, value) in raw)
        {
            if (SystemColumns.Contains(name)) { _notes[name] = "system"; continue; }
            if (!_types.TryGetValue(name, out var type))
            {
                if (!_ignoreUnknown) problems.Add($"unknown column '{name}'");
                continue;
            }
            if (_skipped.Contains(name)) { _notes[name] = "skipped"; continue; }
            if (IdTypes.Contains(type)) _notes[name] = "ids";
            try { payload[name] = Convert(type, value, fromCsv); }
            catch (FormatException ex) { problems.Add($"{name}: {ex.Message}"); }
        }
        if (problems.Count == 0 && payload.All(kv => kv.Value is null)) problems.Add("row has no values");
        error = problems.Count == 0 ? null : string.Join("; ", problems);
        return error is null;
    }

    private static JsonNode? Convert(string type, JsonNode? node, bool fromCsv)
    {
        if (node is null) return null;
        var kind = node.GetValueKind();
        if (kind == JsonValueKind.Null) return null;
        var text = kind == JsonValueKind.String ? node.GetValue<string>() : null;
        if (fromCsv && text is { Length: 0 }) return null;

        if (type.EndsWith("[]"))
        {
            var parsed = text is null ? node : ParseJson(text);
            return parsed is JsonArray ? parsed.DeepClone() : throw new FormatException("expected a JSON array");
        }

        switch (type)
        {
            case "varchar" or "text":
                if (kind == JsonValueKind.Number) return JsonValue.Create(node.ToJsonString());
                return text is not null ? JsonValue.Create(text) : throw new FormatException($"expected text, got {Describe(kind)}");
            case "integer" or "bigint":
            {
                long n = 0;
                var ok = text is not null
                    ? long.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out n)
                    : kind == JsonValueKind.Number && node.AsValue().TryGetValue(out n);
                if (!ok || (type == "integer" && n is > int.MaxValue or < int.MinValue)) throw new FormatException("expected a whole number");
                return JsonValue.Create(n);
            }
            case "decimal":
            {
                var token = text ?? (kind == JsonValueKind.Number ? node.ToJsonString() : "");
                // Sent as a string: the server parses strings exactly but routes JSON numbers through double.
                return decimal.TryParse(token.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var d)
                    ? JsonValue.Create(d.ToString(CultureInfo.InvariantCulture))
                    : throw new FormatException("expected a decimal number using '.' as the separator");
            }
            case "boolean":
                return JsonValue.Create(ToBool(kind, text, node));
            case "date" or "timestamp":
            {
                if (text is null || !IsoDate().IsMatch(text.Trim())
                    || !DateTimeOffset.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when))
                    throw new FormatException("expected an ISO 8601 date such as 2025-03-31 or 2025-03-31T09:30:00Z");
                return JsonValue.Create(type == "date"
                    ? when.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : when.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture));
            }
            case "jsonb":
            {
                if (text is null) return JsonbFieldHelper.Stringify(node.DeepClone());
                var jsonText = text.Trim();
                return jsonText.StartsWith('{') || jsonText.StartsWith('[') ? JsonbFieldHelper.Stringify(ParseJson(jsonText)) : JsonValue.Create(text);
            }
            default:
                if (text is null) return node.DeepClone();
                var trimmed = text.Trim();
                if (trimmed.StartsWith('{') || trimmed.StartsWith('[')) return ParseJson(trimmed);
                if (type is "many-to-one" or "one-to-one" or "user")
                    return long.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                        ? JsonValue.Create(id) : throw new FormatException("expected a record id");
                return JsonValue.Create(text);
        }
    }

    private static bool ToBool(JsonValueKind kind, string? text, JsonNode node)
    {
        if (kind is JsonValueKind.True or JsonValueKind.False) return kind == JsonValueKind.True;
        var word = text?.Trim().ToLowerInvariant() ?? (kind == JsonValueKind.Number ? node.ToJsonString() : "");
        return word switch
        {
            "true" or "1" or "yes" => true,
            "false" or "0" or "no" => false,
            _ => throw new FormatException("expected true/false, 1/0 or yes/no")
        };
    }

    private static JsonNode ParseJson(string text)
    {
        try { return JsonNode.Parse(text) ?? throw new FormatException("expected JSON, got null"); }
        catch (JsonException) { throw new FormatException("not valid JSON"); }
    }

    private static string Describe(JsonValueKind kind) => kind.ToString().ToLowerInvariant();
}
