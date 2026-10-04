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
