using System.ComponentModel;
using AnythinkCli.Client;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace AnythinkMcp.Tools;

public class HostedTools
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HostedProjects _projects;

    public HostedTools(HostedProjects projects) => _projects = projects;

    [McpServerTool(Name = "projects_list", Title = "List projects", ReadOnly = true),
     Description(
        "List the projects this connection can use, with each project's id, name and account. " +
        "Pass a project's id as 'project' to the other tools.")]
    public async Task<string> ProjectsList(CancellationToken cancellationToken) =>
        await FromHosted(() => _projects.ListAsync(cancellationToken));

    [McpServerTool(Name = "project_details", Title = "Get project details", ReadOnly = true),
     Description(
        "Get a project's id, API URL, name, and description. " +
        "Use this to confirm which project a call targets.")]
    public async Task<string> ProjectDetails(
        [Description(HostedProjects.ParameterDescription)] string? project = null,
        CancellationToken cancellationToken = default)
    {
        var client = await FromHosted(() => _projects.ClientAsync(project, cancellationToken));
        var tenant = await FromProject(() => client.GetTenantAsync());

        return JsonSerializer.Serialize(new
        {
            OrgId = client.OrgId,
            ApiUrl = client.BaseUrl,
            Name = tenant?.Name,
            Description = tenant?.Description
        }, SerializerOptions);
    }

    private static async Task<T> FromHosted<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (HostedProjectException ex)
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
            throw new McpException(ex.StatusOnlyMessage);
        }
    }
}
