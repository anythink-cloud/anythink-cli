using System.Globalization;
using System.Text.Json.Nodes;

namespace Anythink.ProjectImporters.Supabase;

internal enum SupabaseKind { Text, Integer, Decimal, Float, Boolean, Timestamp, TimestampTz, Date, Json, Bytes }

internal record SupabaseFieldType(string Db, string Display, SupabaseKind Kind, string? Note = null);

internal static class SupabaseTypes
{
    private const int ShortTextLimit = 255;

    public static SupabaseFieldType Map(SupabaseColumn column, IReadOnlyDictionary<(string Schema, string Name), SupabaseEnum> enums)
    {
        if (column.DataType == "ARRAY") return new("jsonb", "jsonb", SupabaseKind.Json);

        return column.UdtName.ToLowerInvariant() switch
        {
            "int2" or "int4" => new("integer", "input", SupabaseKind.Integer),
            "int8" => new("bigint", "input", SupabaseKind.Integer),
            "numeric" => new("decimal", "input", SupabaseKind.Decimal),
            "float4" or "float8" => new("decimal", "input", SupabaseKind.Float),
            "bool" => new("boolean", "checkbox", SupabaseKind.Boolean),
            "text" or "citext" => new("text", "textarea", SupabaseKind.Text),
            "varchar" or "bpchar" => column.MaxLength is > 0 and <= ShortTextLimit
                ? new("varchar", "input", SupabaseKind.Text)
                : new("text", "textarea", SupabaseKind.Text),
            "name" or "uuid" or "inet" or "cidr" or "macaddr" or "macaddr8" => new("varchar", "input", SupabaseKind.Text),
            "date" => new("date", "short-date", SupabaseKind.Date),
            "timestamp" => new("timestamp", "timestamp", SupabaseKind.Timestamp),
            "timestamptz" => new("timestamp", "timestamp", SupabaseKind.TimestampTz),
            "json" or "jsonb" => new("jsonb", "jsonb", SupabaseKind.Json),
            "bytea" => new("text", "textarea", SupabaseKind.Bytes, "binary values are stored as base64 text."),
            "time" or "timetz" or "interval" or "money" =>
                new("varchar", "input", SupabaseKind.Text, $"{column.UdtName} has no matching Anythink type; values are stored as text."),
            var other when enums.TryGetValue((column.UdtSchema, column.UdtName), out var e) =>
                new("varchar", "input", SupabaseKind.Text, $"enum {e.Name} is stored as text and its allowed values ({string.Join(", ", e.Labels)}) are not enforced."),
            var other => new("text", "textarea", SupabaseKind.Text, $"type {other} has no matching Anythink type; values are stored as their text form."),
        };
    }

    public static string CellExpression(SupabaseColumn column, string quoted) =>
        column.DataType == "ARRAY" || column.UdtName is "json" or "jsonb" ? $"to_jsonb({quoted})::text"
        : column.UdtName == "bytea" ? $"encode({quoted}, 'base64')"
        : column.UdtName == "timestamptz" ? $"({quoted} AT TIME ZONE 'UTC')::text"
        : $"{quoted}::text";

    private const int MaxDecimalDigits = 28;

    public static bool TryToJson(SupabaseKind kind, string text, out JsonNode? node)
    {
        node = null;
        switch (kind)
        {
            case SupabaseKind.Integer:
                if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l)) return false;
                node = JsonValue.Create(l);
                return true;
            case SupabaseKind.Decimal:
                if (text.Count(char.IsAsciiDigit) > MaxDecimalDigits ||
                    !decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var m)) return false;
                node = JsonValue.Create(m);
                return true;
            case SupabaseKind.Float:
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || !double.IsFinite(d)) return false;
                node = JsonValue.Create(d);
                return true;
            case SupabaseKind.Boolean:
                if (text is not ("t" or "true" or "f" or "false")) return false;
                node = JsonValue.Create(text is "t" or "true");
                return true;
            case SupabaseKind.Date:
                if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return false;
                node = JsonValue.Create(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                return true;
            case SupabaseKind.Timestamp or SupabaseKind.TimestampTz:
                if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t)) return false;
                node = JsonValue.Create(t.ToString("O", CultureInfo.InvariantCulture));
                return true;
            case SupabaseKind.Json:
                try { node = JsonNode.Parse(text); return true; }
                catch (System.Text.Json.JsonException) { return false; }
            default:
                node = JsonValue.Create(text);
                return true;
        }
    }
}
