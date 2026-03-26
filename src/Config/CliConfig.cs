using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnythinkCli.Config;

/// <summary>
/// Holds the --profile value parsed before Spectre.Console processes args.
/// Populated in Program.cs; consumed by BaseCommand.GetClient().
/// </summary>
public static class ProfileContext
{
    public static string? Current { get; set; }
}

/// <summary>
/// Canonical API URL constants and environment presets.
/// </summary>
public static class ApiDefaults
{
    public const string MyAnythinkApiUrl = "https://api.my.anythink.cloud";
    public const string MyAnythinkOrgId = "20804318";
    public const string BillingApiUrl = "https://api.billing.anythink.cloud";
}

/// <summary>Credentials for a specific project (per-org API access).</summary>
public class Profile
{
    [JsonPropertyName("org_id")]            public string    OrgId          { get; set; } = null!;
    [JsonPropertyName("api_key")]           public string?   ApiKey         { get; set; }
    [JsonPropertyName("access_token")]      public string?   AccessToken    { get; set; }
    [JsonPropertyName("refresh_token")]     public string?   RefreshToken   { get; set; }
    [JsonPropertyName("token_expires_at")]  public DateTime? TokenExpiresAt { get; set; }
    [JsonPropertyName("instance_api_url")] public string InstanceApiUrl { get; set; } = null!;
    [JsonPropertyName("alias")]             public string?   Alias          { get; set; }

    [JsonIgnore]
    public bool IsTokenExpired
    {
        get
        {
            if (!string.IsNullOrEmpty(ApiKey))   return false;  // API keys never expire
            if (string.IsNullOrEmpty(AccessToken)) return true;

            // Primary: read the exp claim directly from the JWT payload.
            // This works even when TokenExpiresAt was never stored (e.g. old profiles).
            var jwtExpiry = ReadJwtExpiry(AccessToken);
            if (jwtExpiry.HasValue) return DateTime.UtcNow >= jwtExpiry.Value;

            // Fallback: use the stored expiry timestamp.
            return TokenExpiresAt.HasValue && DateTime.UtcNow >= TokenExpiresAt.Value;
        }
    }

    /// <summary>
    /// Decodes the JWT payload (base64url, no signature verification) and returns
    /// the Unix-epoch exp claim as a UTC DateTime, or null if the token is not a JWT
    /// or does not contain an exp claim.
    /// </summary>
    private static DateTime? ReadJwtExpiry(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return null;

            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload += (payload.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("exp", out var exp)
                ? DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64()).UtcDateTime
                : null;
        }
        catch { return null; }
    }
}

/// <summary>Central-platform credentials for signup, billing, and project management.</summary>
public class PlatformConfig
{
    [JsonPropertyName("myanythink_org_id")]          public string    MyAnythinkOrgId          { get; set; } = ApiDefaults.MyAnythinkOrgId;
    [JsonPropertyName("myanythink_url")]         public string    MyAnythinkUrl         { get; set; } = ApiDefaults.MyAnythinkApiUrl;
    [JsonPropertyName("billing_url")]     public string    BillingUrl     { get; set; } = ApiDefaults.BillingApiUrl;
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
    [JsonPropertyName("platform")] public PlatformConfig? Platform { get; set; } = new();
}

public static class ConfigService
{
    /// <summary>Override the config directory for unit tests. Set before any Load/Save call.</summary>
    internal static string? ConfigDirOverride { get; set; }

    private static string ConfigDir =>
        ConfigDirOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".anythink");

    private static string ConfigPath => Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
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

    public static PlatformConfig? GetPlatform()
    {
        var config = Load();
        return config.Platform;
    }

    /// <summary>
    /// Returns the platform configuration from disk, with overrides from environment variables.
    /// Priority: Environment variables > saved config > defaults.
    /// </summary>
    public static PlatformConfig ResolvePlatform()
    {
        var platform = GetPlatform() ?? new PlatformConfig();

        var envOrgId = Environment.GetEnvironmentVariable("MYANYTHINK_ORG_ID") 
                      ?? Environment.GetEnvironmentVariable("ANYTHINK_PLATFORM_ORG_ID");
        if (!string.IsNullOrEmpty(envOrgId)) platform.MyAnythinkOrgId = envOrgId;

        var envUrl = Environment.GetEnvironmentVariable("MYANYTHINK_API_URL") 
                    ?? Environment.GetEnvironmentVariable("ANYTHINK_PLATFORM_API_URL");
        if (!string.IsNullOrEmpty(envUrl)) platform.MyAnythinkUrl = envUrl;

        var envBillingUrl = Environment.GetEnvironmentVariable("BILLING_API_URL") 
                           ?? Environment.GetEnvironmentVariable("ANYTHINK_BILLING_URL");
        if (!string.IsNullOrEmpty(envBillingUrl)) platform.BillingUrl = envBillingUrl;

        var envToken = Environment.GetEnvironmentVariable("ANYTHINK_PLATFORM_TOKEN");
        if (!string.IsNullOrEmpty(envToken)) platform.Token = envToken;

        var envAccountId = Environment.GetEnvironmentVariable("ANYTHINK_ACCOUNT_ID");
        if (!string.IsNullOrEmpty(envAccountId)) platform.AccountId = envAccountId;

        return platform;
    }

    public static void SavePlatform(PlatformConfig platform)
    {
        var config = Load();
        config.Platform = platform;
        Save(config);
    }

    public static void Reset()
    {
        if (File.Exists(ConfigPath)) File.Delete(ConfigPath);
    }

    public static string ConfigFilePath => ConfigPath;
}