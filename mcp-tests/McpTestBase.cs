using AnythinkCli.Config;
using RichardSzalay.MockHttp;

namespace AnythinkMcp.Tests;

/// <summary>
/// Base class for MCP tool tests. Sets up a temp config directory and
/// provides helpers for creating factories with mocked HTTP.
/// </summary>
public abstract class McpTestBase : IDisposable
{
    protected readonly string TempDir;
    protected const string PlatformUrl = "https://api.my.anythink.cloud";
    protected const string BillingUrl = "https://api.billing.anythink.cloud";
    protected const string PlatformOrgId = "20804318";

    protected McpTestBase()
    {
        TempDir = Path.Combine(Path.GetTempPath(), $"mcp-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(TempDir);
        ConfigService.ConfigDirOverride = TempDir;
    }

    public void Dispose()
    {
        ConfigService.ConfigDirOverride = null;
        if (Directory.Exists(TempDir))
            Directory.Delete(TempDir, recursive: true);
    }

    /// <summary>Saves a platform config with a valid (non-expired) token.</summary>
    protected void SetupPlatformLogin(string? accountId = null)
    {
        ConfigService.SavePlatform(new PlatformConfig
        {
            MyAnythinkOrgId = PlatformOrgId,
            MyAnythinkUrl = PlatformUrl,
            BillingUrl = BillingUrl,
            Token = "test-platform-token",
            TokenExpiresAt = DateTime.UtcNow.AddHours(1),
            AccountId = accountId
        });
    }

    /// <summary>Saves a project profile with an API key.</summary>
    protected void SetupProjectProfile(string name = "test-project", string orgId = "12345",
        string apiKey = "ak_test123", string apiUrl = "https://12345.api.anythink.cloud")
    {
        ConfigService.SaveProfile(name, new Profile
        {
            OrgId = orgId,
            ApiKey = apiKey,
            InstanceApiUrl = apiUrl,
            Alias = name
        });
    }

    /// <summary>Creates a McpClientFactory with a mocked HTTP handler.</summary>
    protected McpClientFactory CreateFactory(MockHttpMessageHandler mock, string? profile = null)
        => new(profile, mock);
}
