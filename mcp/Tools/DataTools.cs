using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace AnythinkMcp.Tools;

/// <summary>
/// MCP tools for reading and writing entity records.
/// Wraps AnythinkClient data methods.
/// </summary>
[McpServerToolType]
public class DataTools
{
    private readonly McpClientFactory _factory;
    public DataTools(McpClientFactory factory) => _factory = factory;

    [McpServerTool(Name = "data_list"), Description("List records from an entity with optional filtering")]
    public async Task<string> ListItems(
        [Description("Entity name")] string entity,
        [Description("Page number (starts at 1)")] int page = 1,
        [Description("Items per page")] int pageSize = 20,
        [Description("Filter as JSON (e.g. {\"status\":\"active\"})")] string? filter = null)
    {
        var result = await _factory.GetClient().ListItemsAsync(entity, page, pageSize, filter);
        return JsonSerializer.Serialize(new
        {
            items = result.Items,
            total = result.TotalCount,
            page = result.Page,
            hasNextPage = result.HasNextPage
        });
    }

    [McpServerTool(Name = "data_get"), Description("Get a single record by ID")]
    public async Task<string> GetItem(
        [Description("Entity name")] string entity,
        [Description("Record ID")] int id)
    {
        var item = await _factory.GetClient().GetItemAsync(entity, id);
        return item.ToJsonString();
    }

    [McpServerTool(Name = "data_create"), Description("Create a new record")]
    public async Task<string> CreateItem(
        [Description("Entity name")] string entity,
        [Description("Record data as JSON")] string data)
    {
        var json = JsonSerializer.Deserialize<JsonObject>(data)
            ?? throw new ArgumentException("Invalid JSON data.");
        var item = await _factory.GetClient().CreateItemAsync(entity, json);
        return item.ToJsonString();
    }

    [McpServerTool(Name = "data_update"), Description("Update an existing record")]
    public async Task<string> UpdateItem(
        [Description("Entity name")] string entity,
        [Description("Record ID")] int id,
        [Description("Fields to update as JSON")] string data)
    {
        var json = JsonSerializer.Deserialize<JsonObject>(data)
            ?? throw new ArgumentException("Invalid JSON data.");
        var item = await _factory.GetClient().UpdateItemAsync(entity, id, json);
        return item?.ToJsonString() ?? "{}";
    }

    [McpServerTool(Name = "data_delete"), Description("Delete a record by ID")]
    public async Task<string> DeleteItem(
        [Description("Entity name")] string entity,
        [Description("Record ID")] int id)
    {
        await _factory.GetClient().DeleteItemAsync(entity, id);
        return $"Record {id} deleted from '{entity}'.";
    }
}
