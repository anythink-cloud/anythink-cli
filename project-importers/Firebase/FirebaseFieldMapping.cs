using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Anythink.ProjectImporters.Firebase;

internal enum FirebaseKind { String, Integer, Double, Boolean, Timestamp, Map, Array, Reference, GeoPoint, Bytes }

internal record InferredField(ImportFieldSpec Spec, string SourceName);

internal static class FirebaseFieldMapping
{
    internal const string PathField = "firebase_path";
    internal const string ExtraField = "firebase_extra";

    private const int LongTextThreshold = 255;

    private const int MaxNameLength = 63;

    private static readonly HashSet<string> ReservedTargetNames =
        new([.. ReservedFieldNames.Names, PathField, ExtraField], StringComparer.OrdinalIgnoreCase);

    public static string SnakeCase(string name)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (!char.IsAsciiLetterOrDigit(c)) { sb.Append('_'); continue; }
            var startsWord = char.IsAsciiLetterUpper(c) && i > 0 && (
                char.IsAsciiLetterLower(name[i - 1]) || char.IsAsciiDigit(name[i - 1]) ||
                (char.IsAsciiLetterUpper(name[i - 1]) && i + 1 < name.Length && char.IsAsciiLetterLower(name[i + 1])));
            if (startsWord) sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return string.Join('_', sb.ToString().Split('_', StringSplitOptions.RemoveEmptyEntries));
    }

    public static bool IsValidName(string name) =>
        name.Length is > 0 and <= MaxNameLength && char.IsAsciiLetter(name[0]) &&
        name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') &&
        !name.StartsWith("anythink", StringComparison.OrdinalIgnoreCase);

    public static string? EntityName(string collectionId)
    {
        var name = SnakeCase(collectionId);
        return name.Length == 0 ? null : IsValidName(name) ? name : IsValidName($"firebase_{name}") ? $"firebase_{name}" : null;
    }

    public static bool Accepts(ImportFieldSpec column, object? value) => column.DatabaseType switch
    {
        _ when column.ForeignKeyCollection is not null => value is FirebaseReference,
        "varchar" => value is FirebaseReference || value is string { Length: <= LongTextThreshold },
        "text" => value is string or byte[],
        "integer" => value is long or int && Convert.ToInt64(value) is >= int.MinValue and <= int.MaxValue,
        "bigint" => value is long or int,
        "decimal" => value is long or int || (value is double or float or decimal && double.IsFinite(Convert.ToDouble(value))),
        "boolean" => value is bool,
        "timestamp" => value is DateTimeOffset or DateTime,
        _ => true
    };

    public static FirebaseKind? KindOf(object? value) => value switch
    {
        null => null,
        string => FirebaseKind.String,
        long or int => FirebaseKind.Integer,
        double or float or decimal => FirebaseKind.Double,
        bool => FirebaseKind.Boolean,
        DateTimeOffset or DateTime => FirebaseKind.Timestamp,
        FirebaseReference => FirebaseKind.Reference,
        FirebaseGeoPoint => FirebaseKind.GeoPoint,
        byte[] => FirebaseKind.Bytes,
        IReadOnlyDictionary<string, object?> => FirebaseKind.Map,
        System.Collections.IEnumerable => FirebaseKind.Array,
        _ => FirebaseKind.String
    };

    public static long StableId(string documentPath) =>
        (long)(BitConverter.ToUInt64(SHA256.HashData(Encoding.UTF8.GetBytes(documentPath)), 0) >> 2);

    public static JsonNode? ToJson(object? value) => value switch
    {
        null => null,
        string s => JsonValue.Create(s),
        long l => JsonValue.Create(l),
        int i => JsonValue.Create(i),
        double d => double.IsFinite(d) ? JsonValue.Create(d) : JsonValue.Create(d.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        float f => float.IsFinite(f) ? JsonValue.Create(f) : JsonValue.Create(f.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        decimal m => JsonValue.Create(m),
        bool b => JsonValue.Create(b),
        DateTimeOffset t => JsonValue.Create(t.UtcDateTime.ToString("O")),
        DateTime t => JsonValue.Create(t.ToUniversalTime().ToString("O")),
        FirebaseReference r => JsonValue.Create(r.Path),
        FirebaseGeoPoint g => new JsonObject { ["latitude"] = g.Latitude, ["longitude"] = g.Longitude },
        byte[] bytes => JsonValue.Create(Convert.ToBase64String(bytes)),
        IReadOnlyDictionary<string, object?> map =>
            new JsonObject(map.Select(kv => KeyValuePair.Create(kv.Key, ToJson(kv.Value)))),
        System.Collections.IEnumerable items => new JsonArray(items.Cast<object?>().Select(ToJson).ToArray()),
        _ => JsonValue.Create(value.ToString())
    };

    public static (List<InferredField> Fields, List<string> Warnings) Infer(
        string entityName, IReadOnlyList<FirebaseDocument> documents, Func<string, string?> entityForCollection)
    {
        var fields = new List<InferredField>();
        var warnings = new List<string>();
        var used = new HashSet<string>(ReservedTargetNames.Where(n => n is PathField or ExtraField), StringComparer.OrdinalIgnoreCase);

        foreach (var source in documents.SelectMany(d => d.Fields.Keys).Distinct().OrderBy(k => k, StringComparer.Ordinal))
        {
            var name = SnakeCase(source);
            if (ReservedTargetNames.Contains(name)) name = $"firebase_{name}";
            if (!IsValidName(name) || !used.Add(name))
            {
                warnings.Add($"{entityName}: field '{source}' cannot be a column of its own (its name is not valid for Anythink or clashes with another field); its values are kept in {ExtraField}.");
                continue;
            }

            var values = documents.Where(d => d.Fields.ContainsKey(source)).Select(d => d.Fields[source]).Where(v => v is not null).ToList();
            var (db, display, foreignKey, note) = MapType(values, entityForCollection);
            if (note is not null) warnings.Add($"{entityName}.{source}: {note}");

            fields.Add(new InferredField(new ImportFieldSpec(
                Name: name, DatabaseType: db, DisplayType: display, Label: name.Replace('_', ' '),
                IsRequired: false, IsUnique: false, IsIndexed: false, ForeignKeyCollection: foreignKey), source));
        }

        return (fields, warnings);
    }

    private static (string Db, string Display, string? ForeignKey, string? Note) MapType(
        List<object?> values, Func<string, string?> entityForCollection)
    {
        var kinds = values.Select(KindOf).Select(k => k!.Value).Distinct().ToList();

        if (kinds.Count == 0) return ("varchar", "input", null, null);
        if (kinds.Count == 2 && kinds.Contains(FirebaseKind.Integer) && kinds.Contains(FirebaseKind.Double))
            return ("decimal", "input", null, null);
        if (kinds.Count > 1)
            return ("jsonb", "jsonb", null, $"mixed value types ({string.Join(", ", kinds.Select(k => k.ToString().ToLowerInvariant()).Order())}) are stored as json.");

        return kinds[0] switch
        {
            FirebaseKind.String => values.Max(v => ((string)v!).Length) > LongTextThreshold ? ("text", "textarea", null, null) : ("varchar", "input", null, null),
            FirebaseKind.Integer => values.All(v => Convert.ToInt64(v) is >= int.MinValue and <= int.MaxValue) ? ("integer", "input", null, null) : ("bigint", "input", null, null),
            FirebaseKind.Double => ("decimal", "input", null, null),
            FirebaseKind.Boolean => ("boolean", "checkbox", null, null),
            FirebaseKind.Timestamp => ("timestamp", "timestamp", null, null),
            FirebaseKind.Bytes => ("text", "textarea", null, "binary values are stored as base64 text."),
            FirebaseKind.Reference => MapReference(values.Cast<FirebaseReference>(), entityForCollection),
            _ => ("jsonb", "jsonb", null, null)
        };
    }

    private static (string, string, string?, string?) MapReference(
        IEnumerable<FirebaseReference> references, Func<string, string?> entityForCollection)
    {
        var targets = references.Select(r => r.Path.Split('/')).Select(p => p.Length == 2 ? entityForCollection(p[0]) : null).Distinct().ToList();
        return targets is [{ } entity]
            ? ("integer", "input", entity, null)
            : ("varchar", "input", null, "references to more than one collection, or to a subcollection or an unimported collection, are stored as path text.");
    }
}
