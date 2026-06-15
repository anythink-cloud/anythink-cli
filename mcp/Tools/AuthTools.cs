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
     Description(
        "Register a brand-new Anythink platform account with email and password. " +
        "Use this only when the user has no account yet; if they already have one, use 'login' instead. " +
        "This creates the top-level user identity — it does not create a billing account or project " +
        "(do that with 'accounts_create' and 'projects_create' after logging in). " +
        "On success, follow up with the 'login' tool to obtain a session.")]
    public async Task<string> Signup(
        [Description("User's first name")] string firstName,
        [Description("User's last name")] string lastName,
        [Description("Email address — becomes the login identifier and must be unique")] string email,
        [Description("Password for the new account; choose a strong value")] string password,
        [Description("Optional referral code, if the user was invited by another customer")] string? referralCode = null)
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
     Description(
        "Remove the saved credentials for a project profile from local CLI config. " +
        "Use this to disconnect from a project or clear a stale token; it deletes only the " +
        "stored profile locally and does not revoke the token server-side or affect the project. " +
        "Omit the profile to remove the currently active one. Run 'config_show' to see profile names.")]
    public Task<string> Logout(
        [Description("Name of the profile to remove. Defaults to the active profile. See 'config_show' for names.")] string? profile = null)
    {
        var config = ConfigService.Load();
        var key = profile ?? config.DefaultProfile;

        if (string.IsNullOrEmpty(key) || !config.Profiles.ContainsKey(key))
            return Task.FromResult($"Profile '{key}' not found.");

        ConfigService.RemoveProfile(key);
        return Task.FromResult($"Profile '{key}' removed.");
    }
}
