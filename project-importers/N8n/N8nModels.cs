using System.Text.Json;
using System.Text.Json.Serialization;

namespace Anythink.ProjectImporters.N8n;

public record N8nWorkflowPage(
    [property: JsonPropertyName("data")] List<N8nWorkflow>? Data,
    [property: JsonPropertyName("nextCursor")] string? NextCursor
);

public record N8nWorkflow(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("nodes")] List<N8nNode>? Nodes,
    [property: JsonPropertyName("connections")] JsonElement? Connections
);

public record N8nNode(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("disabled")] bool? Disabled,
    [property: JsonPropertyName("parameters")] JsonElement? Parameters,
    [property: JsonPropertyName("credentials")] JsonElement? Credentials
);
