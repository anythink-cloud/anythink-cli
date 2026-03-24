using System.ComponentModel;
using System.Text.Json;
using AnythinkCli.Models;
using ModelContextProtocol.Server;

namespace AnythinkMcp.Tools;

/// <summary>
/// MCP tools for managing roles and permissions.
/// Wraps AnythinkClient role methods.
/// </summary>
[McpServerToolType]
public class RoleTools
{
    private readonly McpClientFactory _factory;
    public RoleTools(McpClientFactory factory) => _factory = factory;

    [McpServerTool(Name = "roles_list"), Description("List all roles in the project")]
    public async Task<string> ListRoles()
    {
        var roles = await _factory.GetClient().GetRolesAsync();
        return JsonSerializer.Serialize(roles);
    }

    [McpServerTool(Name = "roles_get"), Description("Get a role with its permissions")]
    public async Task<string> GetRole([Description("Role ID")] int id)
    {
        var role = await _factory.GetClient().GetRoleAsync(id);
        return JsonSerializer.Serialize(role);
    }

    [McpServerTool(Name = "roles_create"), Description("Create a new role")]
    public async Task<string> CreateRole(
        [Description("Role name")] string name,
        [Description("Role description")] string? description = null)
    {
        var role = await _factory.GetClient().CreateRoleAsync(
            new CreateRoleRequest(name, description));
        return JsonSerializer.Serialize(role);
    }

    [McpServerTool(Name = "roles_delete"), Description("Delete a role by ID")]
    public async Task<string> DeleteRole([Description("Role ID")] int id)
    {
        await _factory.GetClient().DeleteRoleAsync(id);
        return $"Role {id} deleted.";
    }
}
