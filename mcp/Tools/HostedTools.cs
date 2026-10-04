using System.ComponentModel;
using AnythinkCli.Client;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace AnythinkMcp.Tools;

public partial class HostedTools
{
    [GeneratedRegex(@"^[a-z0-9_]+\z")]
    private static partial Regex EntityNamePattern();

    private static readonly HashSet<string> ReservedQueryParams =
        new(["page", "pageSize", "fields", "sortBy", "sortDirection", "f", "f[]", "group_scope"], StringComparer.OrdinalIgnoreCase);

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
        var tenant = await FromProject(() => client.GetTenantAsync());

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
        var entities = await FromProject(() => client.GetEntitiesAsync());

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
        "Find entity names with 'entities_list' first. " +
        "Filter syntax: a JSON object mapping field to a value (equality) or to an operator object, e.g. " +
        "{\"status\":\"draft\",\"price\":{\"gte\":10,\"lt\":50},\"deleted_at\":{\"null\":true}}; " +
        "or '&'-separated pairs such as status=draft&price=GTE:10. " +
        "Operators: eq, ne, gt, gte, lt, lte, contains, not_contains, starts_with, ends_with, in (array), " +
        "null (true/false), exists (true/false).")]
    public async Task<string> RecordsQuery(
        [Description("Entity name to query, e.g. 'blog_posts'. See 'entities_list' for valid names.")] string entity,
        [Description("Filter as a JSON object (e.g. '{\"status\":\"draft\"}') or 'field=OP:value' pairs. Omit to return every record.")] string? filter = null,
        [Description("Comma-separated field names to return, e.g. 'id,title,status'. Omit to return every field.")] string? fields = null,
        [Description("Page number, starting at 1.")] int page = 1,
        [Description("Records per page, 1 to 1000.")] int pageSize = 20)
    {
        if (!EntityNamePattern().IsMatch(entity))
            throw new McpException("Entity name may only contain lowercase letters, digits and underscores.");

        var client = _factory.GetClient(_credentials);
        try
        {
            if (ItemFilterQuery.Parse(filter).Select(p => p.Key.Split("__")[0]).FirstOrDefault(ReservedQueryParams.Contains) is { } reserved)
                throw new McpException($"'{reserved}' is a reserved query parameter and can't be used as a filter field.");

            var result = await FromProject(() => client.ListItemsAsync(entity, Math.Max(page, 1), Math.Clamp(pageSize, 1, 1000), filter, fields));
            return JsonSerializer.Serialize(result, SerializerOptions);
        }
        catch (ArgumentException ex)
        {
            throw new McpException(ex.Message);
        }
    }

    private static async Task<T> FromProject<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (AnythinkException ex)
        {
            throw new McpException($"The project API returned status {ex.StatusCode}.");
        }
    }
}
