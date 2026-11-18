using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anythink.ProjectImporters.Models;

namespace Anythink.ProjectImporters.Engine;

public static class TargetHasher
{
    public const string Absent = "absent";

    private static readonly HashSet<string> VolatileKeys = ["id", "created_at", "updated_at"];

    public static string Hash(Entity entity) => Hash(new JsonObject
    {
        ["name"] = entity.Name,
        ["is_public"] = entity.IsPublic,
        ["is_junction"] = entity.IsJunction,
        ["fields"] = new JsonArray((entity.Fields ?? []).OrderBy(f => f.Name, StringComparer.Ordinal).Select(f => (JsonNode?)FieldNode(f)).ToArray())
    });

    public static string Hash(Field field) => Hash(FieldNode(field));

    public static string Hash(Workflow workflow) => Hash(new JsonObject
    {
        ["name"] = workflow.Name,
        ["enabled"] = workflow.Enabled,
        ["triggers"] = new JsonArray((workflow.Triggers ?? []).OrderBy(t => t.Type, StringComparer.Ordinal).ThenBy(t => t.Config?.GetRawText(), StringComparer.Ordinal).Select(t => (JsonNode?)new JsonObject
        {
            ["type"] = t.Type,
            ["enabled"] = t.Enabled,
            ["config"] = ToNode(t.Config)
        }).ToArray()),
        ["steps"] = new JsonArray((workflow.Steps ?? []).OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => (JsonNode?)new JsonObject
        {
            ["key"] = s.Key,
            ["action"] = s.Action,
            ["enabled"] = s.Enabled,
            ["is_start_step"] = s.IsStartStep
        }).ToArray())
    });

    public static string Hash(RoleResponse role) => Hash(new JsonObject
    {
        ["name"] = role.Name,
        ["description"] = role.Description,
        ["permissions"] = new JsonArray((role.Permissions ?? []).Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal).Select(n => (JsonNode?)n).ToArray())
    });

    public static string Hash(Permission permission) => Hash(new JsonObject
    {
        ["name"] = permission.Name,
        ["is_active"] = permission.IsActive
    });

    public static string Hash(string value) => Hash(new JsonObject { ["value"] = value });

    public static string Hash(JsonNode node) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(node)))).ToLowerInvariant();

    public static string Canonical(JsonNode? node) => Normalise(node)?.ToJsonString() ?? "null";

    private static JsonObject FieldNode(Field f) => new()
    {
        ["name"] = f.Name,
        ["label"] = f.Label,
        ["database_type"] = f.DatabaseType,
        ["display_type"] = f.DisplayType,
        ["is_required"] = f.IsRequired,
        ["is_unique"] = f.IsUnique,
        ["is_indexed"] = f.IsIndexed,
        ["relationship"] = ToNode(f.Relationship)
    };

    private static JsonNode? ToNode(JsonElement? element) =>
        element is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } e ? JsonNode.Parse(e.GetRawText()) : null;

    private static JsonNode? Normalise(JsonNode? node) => node switch
    {
        JsonObject obj => obj.Where(kv => !VolatileKeys.Contains(kv.Key))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Aggregate(new JsonObject(), (acc, kv) => { acc[kv.Key] = Normalise(kv.Value); return acc; }),
        JsonArray arr => new JsonArray(arr.Select(Normalise).ToArray()),
        null => null,
        _ => node.DeepClone()
    };
}
