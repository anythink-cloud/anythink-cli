using System.ComponentModel;
using System.Text.Json;
using AnythinkCli.Models;
using ModelContextProtocol.Server;

namespace AnythinkMcp.Tools;

/// <summary>
/// MCP tools for managing entities (database tables).
/// Wraps AnythinkClient entity methods.
/// </summary>
[McpServerToolType]
public class EntityTools
{
    private readonly McpClientFactory _factory;
    public EntityTools(McpClientFactory factory) => _factory = factory;

    [McpServerTool(Name = "entities_list"), Description("List all entities in the project")]
    public async Task<string> ListEntities()
    {
        var entities = await _factory.GetClient().GetEntitiesAsync();
        return JsonSerializer.Serialize(entities.Select(e => new
        {
            e.Name, e.IsPublic, e.EnableRls, e.IsSystem, e.IsJunction
        }));
    }

    [McpServerTool(Name = "entities_get"), Description("Get an entity by name, including its fields")]
    public async Task<string> GetEntity(
        [Description("Entity name (e.g. products, blog_posts)")] string name)
    {
        var entity = await _factory.GetClient().GetEntityAsync(name);
        return JsonSerializer.Serialize(entity);
    }

    [McpServerTool(Name = "entities_create"), Description("Create a new entity")]
    public async Task<string> CreateEntity(
        [Description("Entity name (snake_case)")] string name,
        [Description("Enable row-level security")] bool enableRls = false,
        [Description("Make entity publicly readable")] bool isPublic = false)
    {
        var entity = await _factory.GetClient().CreateEntityAsync(
            new CreateEntityRequest(name, enableRls, isPublic));
        return JsonSerializer.Serialize(entity);
    }

    [McpServerTool(Name = "entities_delete"), Description("Delete an entity by name")]
    public async Task<string> DeleteEntity(
        [Description("Entity name")] string name)
    {
        await _factory.GetClient().DeleteEntityAsync(name);
        return $"Entity '{name}' deleted.";
    }
}
