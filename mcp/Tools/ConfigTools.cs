using System.ComponentModel;
using System.Text.Json;
using AnythinkCli.Config;
using ModelContextProtocol.Server;

namespace AnythinkMcp.Tools;

/// <summary>
/// MCP tools for managing CLI configuration and profiles.
/// </summary>
[McpServerToolType]
public class ConfigTools
{
    [McpServerTool(Name = "config_show"),
     Description(
        "Show the local CLI configuration: all saved project profiles and platform logins, and which " +
        "of each is active. For every profile it returns the name, org id, auth method (api-key or token), " +
        "alias, and platform; for every platform it returns the URLs, billing account, and login status. " +
        "Use this to discover profile names for 'config_use', 'config_remove', or 'logout', and to check " +
        "which project the 'cli' tool currently targets. Reads local config only — makes no network calls.")]
    public Task<string> ConfigShow()
    {
        var config = ConfigService.Load();
        var profiles = config.Profiles.Select(kv => new
        {
            Name = kv.Key,
            IsActive = kv.Key == config.DefaultProfile,
            OrgId = kv.Value.OrgId,
            Auth = !string.IsNullOrEmpty(kv.Value.ApiKey) ? "api-key" : "token",
            Alias = kv.Value.Alias,
            PlatformKey = kv.Value.PlatformKey,
        });

        var platforms = config.Platforms.Select(kv => new
        {
            Key = kv.Key,
            IsActive = kv.Key == config.ActivePlatform,
            kv.Value.MyAnythinkUrl,
            kv.Value.BillingUrl,
            kv.Value.AccountId,
            LoggedIn = !kv.Value.IsTokenExpired,
        });

        return Task.FromResult(JsonSerializer.Serialize(new
        {
            ActiveProfile  = config.DefaultProfile,
            ActivePlatform = config.ActivePlatform,
            Profiles       = profiles,
            Platforms      = platforms,
        }));
    }

    [McpServerTool(Name = "config_use"),
     Description(
        "Switch the active project profile that the 'cli' tool's commands operate against. " +
        "Use this to move between already-connected projects without re-authenticating. " +
        "The profile must already exist (created by 'projects_use' or 'login_direct'); " +
        "run 'config_show' to see available profile names. Returns confirmation, or an error if " +
        "the profile does not exist.")]
    public Task<string> ConfigUse(
        [Description("Name of an existing profile to make active. See 'config_show' for valid names.")] string profile)
    {
        try
        {
            ConfigService.SetDefault(profile);
            return Task.FromResult($"Active profile set to '{profile}'.");
        }
        catch (InvalidOperationException ex)
        {
            return Task.FromResult($"Error: {ex.Message}");
        }
    }

    [McpServerTool(Name = "config_remove"),
     Description(
        "Delete a saved project profile from the local CLI configuration. " +
        "Removes only the locally stored credentials and settings — it does not delete the project " +
        "or revoke tokens server-side (to remove the project itself, use 'projects_delete'). " +
        "Run 'config_show' to see profile names. Returns confirmation, or a not-found message.")]
    public Task<string> ConfigRemove(
        [Description("Name of the profile to remove from local config. See 'config_show' for valid names.")] string profile)
    {
        var config = ConfigService.Load();
        if (!config.Profiles.ContainsKey(profile))
            return Task.FromResult($"Profile '{profile}' not found.");

        ConfigService.RemoveProfile(profile);
        return Task.FromResult($"Profile '{profile}' removed.");
    }
}
