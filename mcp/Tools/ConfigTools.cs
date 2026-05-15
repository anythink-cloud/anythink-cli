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
     Description("List all configured profiles and show which is active")]
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
     Description("Set the active project profile")]
    public Task<string> ConfigUse(
        [Description("Profile name to activate")] string profile)
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
     Description("Remove a project profile from the CLI configuration")]
    public Task<string> ConfigRemove(
        [Description("Profile name to remove")] string profile)
    {
        var config = ConfigService.Load();
        if (!config.Profiles.ContainsKey(profile))
            return Task.FromResult($"Profile '{profile}' not found.");

        ConfigService.RemoveProfile(profile);
        return Task.FromResult($"Profile '{profile}' removed.");
    }
}
