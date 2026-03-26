using System.ComponentModel;
using System.Text.Json;
using AnythinkCli.Client;
using AnythinkCli.Config;
using AnythinkCli.Models;
using ModelContextProtocol.Server;

namespace AnythinkMcp.Tools;

/// <summary>
/// MCP tools for authentication: signup, login (email/password and direct credentials), and logout.
/// Google OAuth is not supported in MCP (requires browser) — use the CLI for that.
/// </summary>
[McpServerToolType]
public class AuthTools
{
    private readonly McpClientFactory _factory;
    public AuthTools(McpClientFactory factory) => _factory = factory;

    [McpServerTool(Name = "signup"),
     Description("Create a new Anythink account")]
    public async Task<string> Signup(
        [Description("First name")] string firstName,
        [Description("Last name")] string lastName,
        [Description("Email address")] string email,
        [Description("Password")] string password,
        [Description("Referral code (optional)")] string? referralCode = null)
    {
        var client = _factory.GetUnauthenticatedBillingClient();
        await client.RegisterAsync(new RegisterRequest(firstName, lastName, email, password, referralCode));

        // Save platform config so subsequent login knows the URLs.
        var platform = ConfigService.ResolvePlatform();
        ConfigService.SavePlatform(platform);

        return "Account created successfully. Use the 'login' tool to sign in.";
    }

    [McpServerTool(Name = "login"),
     Description(
        "Log in to the Anythink platform with email and password. " +
        "Returns a session token used for account and project management. " +
        "For Google OAuth, use the CLI instead ('anythink login --google').")]
    public async Task<string> Login(
        [Description("Email address")] string email,
        [Description("Password")] string password)
    {
        var client = _factory.GetUnauthenticatedBillingClient();

        var response = await client.LoginAsync(email, password);

        var platform = ConfigService.ResolvePlatform();

        platform.Token = response.AccessToken;
        platform.TokenExpiresAt = response.ExpiresIn.HasValue
            ? DateTime.UtcNow.AddSeconds(response.ExpiresIn.Value + 30)
            : DateTime.UtcNow.AddHours(1);

        ConfigService.SavePlatform(platform);

        return "Logged in successfully. Use 'accounts_list' and 'projects_use' to connect to a project.";
    }

    [McpServerTool(Name = "login_direct"),
     Description(
        "Store credentials directly for a project (bypasses billing login). " +
        "Use this when you already have an org ID and API key or JWT token.")]
    public Task<string> LoginDirect(
        [Description("Organization/tenant ID")] string orgId,
        [Description("Profile name to save as (defaults to org ID)")] string? profile = null,
        [Description("API key (ak_...)")] string? apiKey = null,
        [Description("JWT access token")] string? token = null,
        [Description("Override API base URL")] string? baseUrl = null)
    {
        if (string.IsNullOrEmpty(apiKey) && string.IsNullOrEmpty(token))
            return Task.FromResult("Error: provide either apiKey or token.");

        var profileName = profile ?? orgId;
        var instanceUrl = baseUrl ?? $"https://{orgId}.api.anythink.cloud";

        var p = new Profile
        {
            OrgId = orgId,
            ApiKey = apiKey,
            AccessToken = token,
            InstanceApiUrl = instanceUrl,
            Alias = profileName
        };

        ConfigService.SaveProfile(profileName, p);

        return Task.FromResult($"Profile '{profileName}' saved and set as active.");
    }

    [McpServerTool(Name = "logout"),
     Description("Remove saved credentials for a project profile")]
    public Task<string> Logout(
        [Description("Profile name to remove (uses active profile if omitted)")] string? profile = null)
    {
        var config = ConfigService.Load();
        var key = profile ?? config.DefaultProfile;

        if (string.IsNullOrEmpty(key) || !config.Profiles.ContainsKey(key))
            return Task.FromResult($"Profile '{key}' not found.");

        ConfigService.RemoveProfile(key);
        return Task.FromResult($"Profile '{key}' removed.");
    }
}
