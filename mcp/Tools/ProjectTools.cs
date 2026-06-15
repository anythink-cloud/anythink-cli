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
     Description(
        "List the projects (provisioned backend instances) in a billing account. " +
        "Requires platform login and an active billing account (set one with 'accounts_use'). " +
        "Returns each project's id, name, description, region, org id, status " +
        "(Initializing/Provisioning/Active/Suspended/Terminated/Error), API URL, and creation date. " +
        "Use this to find a project's id before connecting with 'projects_use' or removing it with 'projects_delete'.")]
    public async Task<string> ProjectsList(
        [Description("Billing account id to list projects for. Defaults to the active account set via 'accounts_use'.")] string? accountId = null)
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
     Description(
        "Provision a new project — a dedicated, isolated Anythink backend instance with its own " +
        "database, API, auth, and storage. Requires platform login and an active billing account " +
        "(set one with 'accounts_use'). Provisioning runs asynchronously: the project starts in a " +
        "Provisioning state, so poll 'projects_list' until it is Active. " +
        "Returns the new project's id, name, org id, and API URL. Connect to it with 'projects_use'.")]
    public async Task<string> ProjectsCreate(
        [Description("Human-readable project name, e.g. 'production' or 'my-app'")] string name,
        [Description("Plan id (UUID) that sets the project's resource tier and pricing. Run the 'plans' CLI command (via the 'cli' tool) to list available plan ids.")] string planId,
        [Description("Deployment region slug, e.g. 'lon1'. Defaults to 'lon1'. Choose the region closest to your users.")] string region = "lon1",
        [Description("Optional free-text description shown in the dashboard")] string? description = null,
        [Description("Billing account id to create the project in. Defaults to the active account set via 'accounts_use'.")] string? accountId = null)
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
        "Connect to a project and save it as the active profile so the 'cli' tool's data, entities, " +
        "users, and other commands target it. Resolves the project, then either stores the API key you " +
        "pass or exchanges a transfer token for project-scoped credentials automatically. " +
        "Requires an active billing account (set one with 'accounts_use'); the project must be Active " +
        "(see 'projects_list'). Returns the saved profile name, org id, API URL, and auth method.")]
    public async Task<string> ProjectsUse(
        [Description("Project to connect to — its name, org id, or UUID (a unique prefix is accepted). Find these via 'projects_list'.")] string id,
        [Description("Optional project API key (ak_...). If omitted, a project-scoped token is generated automatically via transfer-token exchange.")] string? apiKey = null,
        [Description("Billing account id the project belongs to. Defaults to the active account set via 'accounts_use'.")] string? accountId = null)
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
     Description(
        "Permanently delete a project and tear down its backend instance, including its database and " +
        "stored data. This is destructive and irreversible — always confirm with the user first, and " +
        "prefer matching by a specific id over a short prefix to avoid removing the wrong project. " +
        "Requires an active billing account (set one with 'accounts_use'). " +
        "Returns the deleted project's name and id on success.")]
    public async Task<string> ProjectsDelete(
        [Description("Project to delete — its UUID, name, or a unique prefix. Use a specific id to avoid accidental matches. Find ids via 'projects_list'.")] string id,
        [Description("Billing account id the project belongs to. Defaults to the active account set via 'accounts_use'.")] string? accountId = null)
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
