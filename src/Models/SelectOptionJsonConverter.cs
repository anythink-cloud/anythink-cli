using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnythinkCli.Models;

public sealed class SelectOptionJsonConverter : JsonConverter<SelectOption>
{
    public override SelectOption Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException($"Expected a select option object, got {root.ValueKind}.");

        var label = root.TryGetProperty("label", out var l) ? ToText(l) : "";
        if (!root.TryGetProperty("value", out var v))
            return new SelectOption(label, label) { ValueKind = JsonValueKind.Undefined };

        return new SelectOption(label, ToText(v)) { ValueKind = v.ValueKind };
    }

    public override void Write(Utf8JsonWriter writer, SelectOption value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("label", value.Label);
        switch (value.ValueKind)
        {
            case JsonValueKind.Undefined:
                break;
            case JsonValueKind.Null:
                writer.WriteNull("value");
                break;
            case var kind when kind != JsonValueKind.String && IsJsonOfKind(value.Value, kind):
                writer.WritePropertyName("value");
                writer.WriteRawValue(value.Value, skipInputValidation: true);
                break;
            default:
                writer.WriteString("value", value.Value);
                break;
        }
        writer.WriteEndObject();
    }

    private static bool IsJsonOfKind(string text, JsonValueKind kind)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.ValueKind == kind;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string ToText(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString() ?? "",
        JsonValueKind.Null or JsonValueKind.Undefined => "",
        _ => e.GetRawText(),
    };
}
