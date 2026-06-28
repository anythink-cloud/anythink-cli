using System.ComponentModel;
using System.Text.Json;
using AnythinkCli.Auth;
using AnythinkCli.Client;
using AnythinkCli.Config;
using AnythinkCli.Models;
using ModelContextProtocol.Server;

namespace AnythinkMcp.Tools;

/// <summary>
/// MCP tools for authentication: signup, login (email/password, Google, or direct credentials), and logout.
/// Google sign-in opens a local browser, so it works over stdio only.
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
        "On success the user may need to click an email confirmation link before 'login' works; tell them to confirm, then call 'login'.")]
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

        return "Account created. If a confirmation email was sent, the user must click the link in it before 'login' works — then call 'login' to sign in.";
    }

    [McpServerTool(Name = "login"),
     Description(
        "Log in to the Anythink platform with email and password. " +
        "Returns a session token used for account and project management. " +
        "For Google sign-in, use the 'login_google' tool.")]
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

    [McpServerTool(Name = "login_google"),
     Description(
        "Sign in to the Anythink platform with Google. Opens the user's browser to Google's consent "
        + "screen and waits for them to approve, then stores the session token. The browser step must "
        + "be completed by the user; everything before and after is tool-driven. This does NOT pick a "
        + "billing account or project — after it returns, call 'accounts_list' then 'accounts_use', and "
        + "'projects_list' then 'projects_use'. Needs a local browser, so it only works over stdio (not "
        + "the hosted HTTP server).")]
    public async Task<string> LoginGoogle()
    {
        if (McpClientFactory.IsHttpMode)
            return "Google sign-in needs a local browser and isn't available over the hosted HTTP server. Use the CLI: 'anythink login --google'.";

        var platform = ConfigService.ResolvePlatform();
        var eff = ConfigService.ApplyRuntimeOverrides(platform);

        LoginResponse tokens;
        try
        {
            tokens = await GoogleAuthFlow.RunAsync(eff,
                url => Console.Error.WriteLine($"[anythink] Complete Google sign-in in your browser: {url}"));
        }
        catch (Exception ex)
        {
            return $"Google sign-in failed: {ex.Message}";
        }

        platform.Token = tokens.AccessToken;
        platform.TokenExpiresAt = tokens.ExpiresIn.HasValue
            ? DateTime.UtcNow.AddSeconds(tokens.ExpiresIn.Value - 30)
            : DateTime.UtcNow.AddHours(1);
        ConfigService.SavePlatform(platform);

        return "Signed in with Google. Next: call 'accounts_list' to choose a billing account (then 'accounts_use'), "
            + "then 'projects_list' / 'projects_use' to connect to a project.";
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
