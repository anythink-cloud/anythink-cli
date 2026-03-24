using System.ComponentModel;
using System.Text.Json;
using AnythinkCli.Models;
using ModelContextProtocol.Server;

namespace AnythinkMcp.Tools;

/// <summary>
/// MCP tools for managing entity fields.
/// Wraps AnythinkClient field methods.
/// </summary>
[McpServerToolType]
public class FieldTools
{
    private readonly McpClientFactory _factory;
    public FieldTools(McpClientFactory factory) => _factory = factory;

    [McpServerTool(Name = "fields_list"), Description("List all fields for an entity")]
    public async Task<string> ListFields(
        [Description("Entity name")] string entity)
    {
        var fields = await _factory.GetClient().GetFieldsAsync(entity);
        return JsonSerializer.Serialize(fields);
    }

    [McpServerTool(Name = "fields_add"), Description("Add a field to an entity")]
    public async Task<string> AddField(
        [Description("Entity name")] string entity,
        [Description("Field name (snake_case)")] string name,
        [Description("Database type: varchar, text, int, boolean, decimal, datetime, json")] string databaseType,
        [Description("Display type: text, number, rich-text, dropdown, toggle, date, json, image, file")] string displayType,
        [Description("Human-readable label")] string? label = null,
        [Description("Whether the field is required")] bool isRequired = false,
        [Description("Whether the field is searchable")] bool isSearchable = false)
    {
        var field = await _factory.GetClient().AddFieldAsync(entity,
            new CreateFieldRequest(name, databaseType, displayType,
                Label: label, IsRequired: isRequired, IsSearchable: isSearchable));
        return JsonSerializer.Serialize(field);
    }

    [McpServerTool(Name = "fields_update"), Description("Update field properties")]
    public async Task<string> UpdateField(
        [Description("Entity name")] string entity,
        [Description("Field ID")] int fieldId,
        [Description("Display type")] string displayType,
        [Description("Human-readable label")] string? label = null,
        [Description("Whether the field is searchable")] bool isSearchable = false)
    {
        var field = await _factory.GetClient().UpdateFieldAsync(entity, fieldId,
            new UpdateFieldRequest(displayType, Label: label, IsSearchable: isSearchable));
        return JsonSerializer.Serialize(field);
    }

    [McpServerTool(Name = "fields_delete"), Description("Delete a field from an entity")]
    public async Task<string> DeleteField(
        [Description("Entity name")] string entity,
        [Description("Field ID")] int fieldId)
    {
        await _factory.GetClient().DeleteFieldAsync(entity, fieldId);
        return $"Field {fieldId} deleted from '{entity}'.";
    }
}
