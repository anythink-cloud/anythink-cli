using System.ComponentModel;
using System.Text.Json;
using AnythinkCli.Client;
using AnythinkCli.Config;
using AnythinkCli.Models;
using ModelContextProtocol.Server;

namespace AnythinkMcp.Tools;

/// <summary>
/// MCP tools for managing projects within a billing account.
/// Requires platform login and an active billing account.
/// </summary>
[McpServerToolType]
public class ProjectTools
{
    private readonly McpClientFactory _factory;
    public ProjectTools(McpClientFactory factory) => _factory = factory;

    [McpServerTool(Name = "projects_list"),
     Description("List all projects in the active billing account")]
    public async Task<string> ProjectsList(
        [Description("Billing account ID (uses active account if omitted)")] string? accountId = null)
    {
        var (client, acctId) = GetClientAndAccount(accountId);
        var projects = await client.GetProjectsAsync(acctId);

        var statusMap = new Dictionary<int, string>
        {
            [0] = "Initializing", [1] = "Provisioning", [2] = "Active",
            [3] = "Suspended", [4] = "Terminated", [5] = "Error"
        };

        return JsonSerializer.Serialize(projects.Select(p => new
        {
            p.Id,
            p.Name,
            p.Description,
            p.Region,
            OrgId = p.TenantId,
            Status = statusMap.GetValueOrDefault(p.Status, $"Unknown({p.Status})"),
            p.ApiUrl,
            p.CreatedAt
        }));
    }

    [McpServerTool(Name = "projects_create"),
     Description("Create a new project in the active billing account")]
    public async Task<string> ProjectsCreate(
        [Description("Project name")] string name,
        [Description("Plan ID (UUID) — use 'plans' CLI command to list available plans")] string planId,
        [Description("Deployment region (e.g. lon1)")] string region = "lon1",
        [Description("Project description (optional)")] string? description = null,
        [Description("Billing account ID (uses active account if omitted)")] string? accountId = null)
    {
        var (client, acctId) = GetClientAndAccount(accountId);
        var project = await client.CreateProjectAsync(acctId,
            new CreateSharedTenantRequest(name, Guid.Parse(planId), region, description));

        return JsonSerializer.Serialize(new
        {
            project.Id,
            project.Name,
            OrgId = project.TenantId,
            project.ApiUrl,
            Message = "Project created. Use 'projects_use' to connect to it."
        });
    }

    [McpServerTool(Name = "projects_use"),
     Description(
        "Connect to a project and save it as the active profile. " +
        "Exchanges a transfer token for project-scoped credentials.")]
    public async Task<string> ProjectsUse(
        [Description("Project name, org ID, or UUID (or prefix)")] string id,
        [Description("API key for the project (generates token if omitted)")] string? apiKey = null,
        [Description("Billing account ID (uses active account if omitted)")] string? accountId = null)
    {
        var (client, acctId) = GetClientAndAccount(accountId);
        var projects = await client.GetProjectsAsync(acctId);

        // Match by UUID prefix, TenantId (OrgId), or exact name.
        var match = projects.FirstOrDefault(p =>
            p.Id.ToString().StartsWith(id, StringComparison.OrdinalIgnoreCase) ||
            p.TenantId?.ToString() == id ||
            string.Equals(p.Name, id, StringComparison.OrdinalIgnoreCase));

        if (match is null)
            return $"No project found matching '{id}'. Use 'projects_list' to see available projects.";

        if (match.TenantId is null || string.IsNullOrEmpty(match.ApiUrl))
            return $"Project '{match.Name}' is not ready yet (status: {match.Status}). Wait for provisioning to complete.";

        var orgId = match.TenantId.Value.ToString();
        var instanceUrl = match.ApiUrl.TrimEnd('/');
        var profileName = match.Name?.ToLowerInvariant().Replace(' ', '-') ?? orgId;

        var profile = new Profile
        {
            OrgId = orgId,
            InstanceApiUrl = instanceUrl,
            Alias = match.Name
        };

        if (!string.IsNullOrEmpty(apiKey))
        {
            profile.ApiKey = apiKey;
        }
        else
        {
            // Exchange transfer token for project-scoped JWT.
            var transferResp = await client.GetTransferTokenAsync(acctId, match.Id);
            var projectClient = _factory.CreateAnythinkClient(profile);
            var loginResp = await projectClient.ExchangeTransferTokenAsync(transferResp.TransferToken);

            profile.AccessToken = loginResp.AccessToken;
            profile.RefreshToken = loginResp.RefreshToken;
            profile.TokenExpiresAt = loginResp.ExpiresIn.HasValue
                ? DateTime.UtcNow.AddSeconds(loginResp.ExpiresIn.Value)
                : DateTime.UtcNow.AddHours(1);
        }

        ConfigService.SaveProfile(profileName, profile);

        return JsonSerializer.Serialize(new
        {
            Profile = profileName,
            OrgId = orgId,
            ApiUrl = instanceUrl,
            Auth = !string.IsNullOrEmpty(apiKey) ? "api-key" : "token",
            Message = $"Connected to '{match.Name}'. Profile '{profileName}' saved and set as active."
        });
    }

    [McpServerTool(Name = "projects_delete"),
     Description("Delete a project from the billing account")]
    public async Task<string> ProjectsDelete(
        [Description("Project ID (UUID or prefix)")] string id,
        [Description("Billing account ID (uses active account if omitted)")] string? accountId = null)
    {
        var (client, acctId) = GetClientAndAccount(accountId);
        var projects = await client.GetProjectsAsync(acctId);

        var match = projects.FirstOrDefault(p =>
            p.Id.ToString().StartsWith(id, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.Name, id, StringComparison.OrdinalIgnoreCase));

        if (match is null)
            return $"No project found matching '{id}'.";

        await client.DeleteProjectAsync(acctId, match.Id);
        return $"Project '{match.Name}' ({match.Id}) deleted.";
    }

    private (BillingClient client, Guid accountId) GetClientAndAccount(string? accountIdOverride)
    {
        var client = _factory.GetBillingClient();

        string? rawId = accountIdOverride ?? ConfigService.ResolvePlatform().AccountId;
        if (string.IsNullOrEmpty(rawId) || !Guid.TryParse(rawId, out var acctId))
            throw new InvalidOperationException(
                "No active billing account. Use 'accounts_use' to set one, or pass accountId.");

        return (client, acctId);
    }
}
