using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnythinkCli.Config;

/// <summary>
/// Canonical API URL constants and environment presets.
/// Default is production. Set ANYTHINK_ENV=local|dev to switch environments.
/// </summary>
public static class ApiDefaults
{
    // Production — hardcoded, these are the public-facing service endpoints.
    public const string ProdApi      = "https://api.my.anythink.cloud";
    public const string BillingCloud = "https://api.billing.anythink.cloud";
    public const string PlatformOrgIdProd = "20804318";

    // Dev / local — read from environment variables (set in .env for contributors).
    // See .env.example at the repo root.
    public static string DevApi       => Env("ANYTHINK_DEV_API")      ?? "https://api.my.anythink.dev";
    public static string BillingDev   => Env("ANYTHINK_DEV_BILLING")  ?? "https://api.billing.anythink.dev";
    public static string LocalApi     => Env("ANYTHINK_LOCAL_API")    ?? "http://localhost:5099";
    public static string LocalBilling => Env("ANYTHINK_LOCAL_BILLING")?? "http://localhost:5100";

    // Org IDs for dev/local — no hardcoded fallback; contributors must set these in .env.
    public static string? PlatformOrgIdDev => Env("ANYTHINK_DEV_ORG_ID");
    public static string? LocalOrgId       => Env("ANYTHINK_LOCAL_ORG_ID");

    private static string? Env(string key) => Environment.GetEnvironmentVariable(key);

    /// <summary>Returns (ApiUrl, BillingUrl, PlatformOrgId?) for a named environment.</summary>
    public static (string ApiUrl, string BillingUrl, string? OrgId) ForEnv(string env) =>
        env.ToLowerInvariant() switch
        {
            "local" => (LocalApi,  LocalBilling, LocalOrgId),
            "dev"   => (DevApi,    BillingDev,   PlatformOrgIdDev),
            _       => (ProdApi,   BillingCloud, PlatformOrgIdProd)
        };
}

/// <summary>Credentials for a specific project (per-org API access).</summary>
public class Profile
{
    [JsonPropertyName("org_id")]            public string    OrgId          { get; set; } = "";
    [JsonPropertyName("api_key")]           public string?   ApiKey         { get; set; }
    [JsonPropertyName("access_token")]      public string?   AccessToken    { get; set; }
    [JsonPropertyName("refresh_token")]     public string?   RefreshToken   { get; set; }
    [JsonPropertyName("token_expires_at")]  public DateTime? TokenExpiresAt { get; set; }
    [JsonPropertyName("base_url")]          public string    BaseUrl        { get; set; } = ApiDefaults.ProdApi;
    [JsonPropertyName("alias")]             public string?   Alias          { get; set; }

    [JsonIgnore]
    public bool IsTokenExpired =>
        string.IsNullOrEmpty(ApiKey) &&
        (string.IsNullOrEmpty(AccessToken) ||
         (TokenExpiresAt.HasValue && DateTime.UtcNow >= TokenExpiresAt.Value));
}

/// <summary>Central-platform credentials for signup, billing, and project management.</summary>
public class PlatformConfig
{
    [JsonPropertyName("org_id")]          public string    OrgId          { get; set; } = "";
    [JsonPropertyName("api_url")]         public string    ApiUrl         { get; set; } = ApiDefaults.ProdApi;
    [JsonPropertyName("billing_url")]     public string    BillingUrl     { get; set; } = ApiDefaults.BillingCloud;
    [JsonPropertyName("token")]           public string?   Token          { get; set; }
    [JsonPropertyName("token_expires_at")] public DateTime? TokenExpiresAt { get; set; }
    [JsonPropertyName("account_id")]      public string?   AccountId      { get; set; }

    [JsonIgnore]
    public bool IsTokenExpired =>
        Token == null || (TokenExpiresAt.HasValue && DateTime.UtcNow >= TokenExpiresAt.Value);
}

public class CliConfigData
{
    [JsonPropertyName("default_profile")] public string                     DefaultProfile { get; set; } = "";
    [JsonPropertyName("profiles")]        public Dictionary<string, Profile> Profiles      { get; set; } = new();
    [JsonPropertyName("platform")]        public PlatformConfig?             Platform      { get; set; }  // prod
    [JsonPropertyName("platform_dev")]    public PlatformConfig?             PlatformDev   { get; set; }  // dev/staging
    [JsonPropertyName("platform_local")]  public PlatformConfig?             PlatformLocal { get; set; }  // local
}

public static class ConfigService
{
    private static readonly string ConfigDir  = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".anythink");
    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented          = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static CliConfigData Load()
    {
        if (!File.Exists(ConfigPath)) return new CliConfigData();
        var json = File.ReadAllText(ConfigPath);
        return JsonSerializer.Deserialize<CliConfigData>(json, JsonOpts) ?? new CliConfigData();
    }

    public static void Save(CliConfigData config)
    {
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, JsonOpts));
    }

    public static Profile? GetActiveProfile()
    {
        var config = Load();
        if (string.IsNullOrEmpty(config.DefaultProfile)) return null;
        config.Profiles.TryGetValue(config.DefaultProfile, out var profile);
        return profile;
    }

    public static Profile? GetProfile(string key)
    {
        var config = Load();
        config.Profiles.TryGetValue(key, out var profile);
        return profile;
    }

    public static void SaveProfile(string key, Profile profile)
    {
        var config = Load();
        config.Profiles[key] = profile;
        if (string.IsNullOrEmpty(config.DefaultProfile)) config.DefaultProfile = key;
        Save(config);
    }

    public static void SetDefault(string key)
    {
        var config = Load();
        if (!config.Profiles.ContainsKey(key))
            throw new InvalidOperationException($"Profile '{key}' not found.");
        config.DefaultProfile = key;
        Save(config);
    }

    public static void RemoveProfile(string key)
    {
        var config = Load();
        config.Profiles.Remove(key);
        if (config.DefaultProfile == key)
            config.DefaultProfile = config.Profiles.Keys.FirstOrDefault() ?? "";
        Save(config);
    }

    public static PlatformConfig? GetPlatform(string? env = null)
    {
        var config = Load();
        return env?.ToLowerInvariant() switch
        {
            "dev"   => config.PlatformDev,
            "local" => config.PlatformLocal,
            _       => config.Platform
        };
    }

    public static void SavePlatform(PlatformConfig platform, string? env = null)
    {
        var config = Load();
        switch (env?.ToLowerInvariant())
        {
            case "dev":   config.PlatformDev   = platform; break;
            case "local": config.PlatformLocal = platform; break;
            default:      config.Platform      = platform; break;
        }
        Save(config);
    }

    public static string ConfigFilePath => ConfigPath;
}
