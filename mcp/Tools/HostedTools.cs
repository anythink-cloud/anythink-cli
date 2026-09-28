using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace AnythinkMcp.Tools;

/// <summary>Hosted-only proving tools; deliberately not [McpServerToolType] so stdio's WithToolsFromAssembly() never picks them up.</summary>
public class HostedTools
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly McpClientFactory _factory;
    private readonly HostedCredentials _credentials;

    public HostedTools(McpClientFactory factory, HostedCredentials credentials)
    {
        _factory = factory;
        _credentials = credentials;
    }

    [McpServerTool(Name = "project_details", Title = "Get project details", ReadOnly = true),
     Description(
        "Get the connected project's id, API URL, name, and description. " +
        "Use this to confirm which project the connection is targeting.")]
    public async Task<string> ProjectDetails()
    {
        var client = _factory.GetClient(_credentials);
        var tenant = await client.GetTenantAsync();

        return JsonSerializer.Serialize(new
        {
            OrgId = client.OrgId,
            ApiUrl = client.BaseUrl,
            Name = tenant?.Name,
            Description = tenant?.Description
        }, SerializerOptions);
    }

    [McpServerTool(Name = "entities_list", Title = "List entities", ReadOnly = true),
     Description(
        "List the entities (data tables) defined in the project, with each entity's field count " +
        "and access flags. Use this to discover entity names before calling 'records_query'.")]
    public async Task<string> EntitiesList()
    {
        var client = _factory.GetClient(_credentials);
        var entities = await client.GetEntitiesAsync();

        return JsonSerializer.Serialize(entities.Select(e => new
        {
            e.Name,
            e.TableName,
            FieldCount = e.Fields?.Count ?? 0,
            e.IsPublic,
            e.EnableRls
        }), SerializerOptions);
    }

    [McpServerTool(Name = "records_query", Title = "Query records", ReadOnly = true),
     Description(
        "Query records of an entity, with an optional filter, field selection, and pagination. " +
        "Find entity names with 'entities_list' first.")]
    public async Task<string> RecordsQuery(
        [Description("Entity name to query, e.g. 'blog_posts'. See 'entities_list' for valid names.")] string entity,
        [Description("Filter expression as a JSON object, e.g. '{\"status\":\"draft\"}'. Omit to return every record.")] string? filter = null,
        [Description("Comma-separated field names to return, e.g. 'id,title,status'. Omit to return every field.")] string? fields = null,
        [Description("Page number, starting at 1.")] int page = 1,
        [Description("Records per page (max 1000).")] int pageSize = 20)
    {
        var client = _factory.GetClient(_credentials);
        var result = await client.ListItemsAsync(entity, page, pageSize, filter, fields);
        return JsonSerializer.Serialize(result, SerializerOptions);
    }
}
