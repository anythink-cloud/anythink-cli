using System.Text.Json.Serialization;

namespace AnythinkCli.Importers.Directus;

public record DirectusCollection(
    [property: JsonPropertyName("collection")] string Collection,
    [property: JsonPropertyName("meta")]       DirectusCollectionMeta?   Meta,
    [property: JsonPropertyName("schema")]     DirectusCollectionSchema? Schema
);

public record DirectusCollectionMeta(
    [property: JsonPropertyName("hidden")]    bool?   Hidden,
    [property: JsonPropertyName("singleton")] bool?   Singleton,
    [property: JsonPropertyName("note")]      string? Note
);

public record DirectusCollectionSchema(
    [property: JsonPropertyName("name")] string? Name
);

public record DirectusField(
    [property: JsonPropertyName("collection")] string             Collection,
    [property: JsonPropertyName("field")]      string             FieldName,
    [property: JsonPropertyName("type")]       string             Type,
    [property: JsonPropertyName("meta")]       DirectusFieldMeta?   Meta,
    [property: JsonPropertyName("schema")]     DirectusFieldSchema? Schema
);

public record DirectusFieldMeta(
    [property: JsonPropertyName("interface")]  string?                        Interface,
    [property: JsonPropertyName("note")]       string?                        Note,
    [property: JsonPropertyName("required")]   bool?                          Required,
    [property: JsonPropertyName("hidden")]     bool?                          Hidden,
    [property: JsonPropertyName("readonly")]   bool?                          Readonly,
    [property: JsonPropertyName("options")]    System.Text.Json.JsonElement?  Options
);

public record DirectusFieldSchema(
    [property: JsonPropertyName("name")]          string?                       Name,
    [property: JsonPropertyName("data_type")]     string?                       DataType,
    [property: JsonPropertyName("default_value")] System.Text.Json.JsonElement? DefaultValue,
    [property: JsonPropertyName("is_nullable")]   bool?                         IsNullable,
    [property: JsonPropertyName("is_unique")]     bool?                         IsUnique,
    [property: JsonPropertyName("is_indexed")]    bool?                         IsIndexed,
    [property: JsonPropertyName("is_primary_key")] bool?                        IsPrimaryKey
);

public record DirectusListResponse<T>(
    [property: JsonPropertyName("data")] List<T> Data
);

// ── Flows ─────────────────────────────────────────────────────────────────────

public record DirectusFlow(
    [property: JsonPropertyName("id")]              string                        Id,
    [property: JsonPropertyName("name")]            string                        Name,
    [property: JsonPropertyName("status")]          string                        Status,
    [property: JsonPropertyName("trigger")]         string                        Trigger,
    [property: JsonPropertyName("options")]         System.Text.Json.JsonElement? Options,
    [property: JsonPropertyName("first_operation")] string?                       FirstOperation
);

public record DirectusOperation(
    [property: JsonPropertyName("id")]      string                        Id,
    [property: JsonPropertyName("name")]    string                        Name,
    [property: JsonPropertyName("key")]     string                        Key,
    [property: JsonPropertyName("type")]    string                        Type,
    [property: JsonPropertyName("flow")]    string                        Flow,
    [property: JsonPropertyName("resolve")] string?                       Resolve,
    [property: JsonPropertyName("reject")]  string?                       Reject,
    [property: JsonPropertyName("options")] System.Text.Json.JsonElement? Options
);
