using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AnythinkCli.Config;

/// <summary>
/// Holds the --profile value parsed before Spectre.Console processes args.
/// Populated in Program.cs; consumed by BaseCommand.GetClient().
/// </summary>
public static class ProfileContext
{
    public static string? Current { get; set; }
}

/// <summary>Canonical API URL constants and environment presets.</summary>
public static class ApiDefaults
{
    public const string MyAnythinkApiUrl = "https://api.my.anythink.cloud";
    public const string MyAnythinkOrgId  = "20804318";
    public const string BillingApiUrl    = "https://api.billing.anythink.cloud";

    /// <summary>Key under which a fresh production platform session is stored.</summary>
    public const string ProductionKey = "production";
}

/// <summary>Credentials for a specific project (per-org API access).</summary>
public class Profile
{
    [JsonPropertyName("org_id")]            public string    OrgId          { get; set; } = null!;
    [JsonPropertyName("api_key")]           public string?   ApiKey         { get; set; }
    [JsonPropertyName("access_token")]      public string?   AccessToken    { get; set; }
    [JsonPropertyName("refresh_token")]     public string?   RefreshToken   { get; set; }
    [JsonPropertyName("token_expires_at")]  public DateTime? TokenExpiresAt { get; set; }
    [JsonPropertyName("instance_api_url")]  public string    InstanceApiUrl { get; set; } = null!;
    [JsonPropertyName("alias")]             public string?   Alias          { get; set; }

    [JsonPropertyName("platform_key")]      public string?   PlatformKey    { get; set; }

    /// <summary>
    /// True iff the AccessToken is missing or past its expiry. ApiKey state is
    /// orthogonal — callers gating "is this profile usable" must check ApiKey
    /// first. Keeping this property purely about the token avoids masking an
    /// expired bearer when a stale ApiKey is also present.
    /// </summary>
    [JsonIgnore]
    public bool IsTokenExpired
    {
        get
        {
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

/// <summary>One platform session — URLs + central token. Multiple coexist.</summary>
public class PlatformConfig
{
    [JsonPropertyName("myanythink_org_id")]   public string    MyAnythinkOrgId { get; set; } = ApiDefaults.MyAnythinkOrgId;
    [JsonPropertyName("myanythink_url")]      public string    MyAnythinkUrl   { get; set; } = ApiDefaults.MyAnythinkApiUrl;
    [JsonPropertyName("billing_url")]         public string    BillingUrl      { get; set; } = ApiDefaults.BillingApiUrl;
    [JsonPropertyName("token")]               public string?   Token           { get; set; }
    [JsonPropertyName("token_expires_at")]    public DateTime? TokenExpiresAt  { get; set; }
    [JsonPropertyName("account_id")]          public string?   AccountId       { get; set; }
    [JsonPropertyName("display_name")]        public string?   DisplayName     { get; set; }

    [JsonIgnore]
    public bool IsTokenExpired =>
        Token == null || (TokenExpiresAt.HasValue && DateTime.UtcNow >= TokenExpiresAt.Value);
}

public class CliConfigData
{
    [JsonPropertyName("default_profile")] public string                             DefaultProfile { get; set; } = "";
    [JsonPropertyName("active_platform")] public string                             ActivePlatform { get; set; } = ApiDefaults.ProductionKey;
    [JsonPropertyName("platforms")]       public Dictionary<string, PlatformConfig> Platforms      { get; set; } = new();
    [JsonPropertyName("profiles")]        public Dictionary<string, Profile>        Profiles       { get; set; } = new();

    /// <summary>Read-only migration target for the old singleton field. Cleared after Load().</summary>
    [JsonPropertyName("platform")]        public PlatformConfig?                    LegacyPlatform { get; set; }
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
        WriteIndented          = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static CliConfigData Load()
    {
        if (!File.Exists(ConfigPath)) return new CliConfigData();
        var json = File.ReadAllText(ConfigPath);
        var data = JsonSerializer.Deserialize<CliConfigData>(json, JsonOpts) ?? new CliConfigData();

        if (Migrate(data))
            Save(data);

        return data;
    }

    public static void Save(CliConfigData config)
    {
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, JsonOpts));
        TightenPermissions();
    }

    private static void TightenPermissions()
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            File.SetUnixFileMode(ConfigDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(ConfigPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception) { /* best-effort; perms not enforceable on every fs */ }
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

        // If the profile is tagged with a platform, switch the active platform
        // to match so subsequent platform-scoped commands hit the right env.
        // Print a notice on stderr so a hand-edited `platform_key` can't silently
        // redirect billing/auth at a different platform without the user seeing.
        var profile = config.Profiles[key];
        if (!string.IsNullOrEmpty(profile.PlatformKey) &&
            config.Platforms.ContainsKey(profile.PlatformKey) &&
            config.ActivePlatform != profile.PlatformKey)
        {
            Console.Error.WriteLine(
                $"note: switching active platform '{config.ActivePlatform}' → '{profile.PlatformKey}' for profile '{key}'.");
            config.ActivePlatform = profile.PlatformKey;
        }

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
        if (config.Platforms.TryGetValue(config.ActivePlatform, out var p)) return p;
        return null;
    }

    /// <summary>
    /// Resolve the (key, PlatformConfig) pair for this invocation. URL env vars
    /// (MYANYTHINK_API_URL/BILLING_API_URL) act as platform SELECTORS — they
    /// pick or seed the platform record. Secret-bearing env vars (Token,
    /// AccountId, OrgId) are NOT applied here; they would be persisted if a
    /// caller saves the returned record. Use ApplyRuntimeOverrides() at
    /// HTTP-call time for those.
    /// </summary>
    public static PlatformContext ResolvePlatformContext(
        string? myanythinkUrlFlag = null, string? billingUrlFlag = null)
    {
        var config = Load();
        var active = config.Platforms.GetValueOrDefault(config.ActivePlatform);

        var myUrl = NormaliseHttpUrl(myanythinkUrlFlag)
                 ?? NormaliseHttpUrl(SafeEnv("MYANYTHINK_API_URL"))
                 ?? NormaliseHttpUrl(SafeEnv("ANYTHINK_PLATFORM_API_URL"))
                 ?? active?.MyAnythinkUrl
                 ?? ApiDefaults.MyAnythinkApiUrl;

        var billUrl = NormaliseHttpUrl(billingUrlFlag)
                   ?? NormaliseHttpUrl(SafeEnv("BILLING_API_URL"))
                   ?? NormaliseHttpUrl(SafeEnv("ANYTHINK_BILLING_URL"))
                   ?? active?.BillingUrl
                   ?? ApiDefaults.BillingApiUrl;

        var match = config.Platforms.FirstOrDefault(kv => UrlsMatch(kv.Value.MyAnythinkUrl, myUrl));
        var key = match.Key ?? DerivePlatformKey(myUrl);

        var platform = match.Value ?? new PlatformConfig
        {
            MyAnythinkUrl = myUrl,
            BillingUrl    = billUrl,
        };

        // When we're SEEDING a new platform (no saved match), capture the
        // env-var org id as part of the platform's identity — otherwise the
        // first login persists with the default org and later runs hit the
        // wrong tenant. Existing platforms are NOT mutated; runtime-only
        // env-var overrides go through ApplyRuntimeOverrides.
        if (match.Value is null)
        {
            var envOrgId = SafeEnv("MYANYTHINK_ORG_ID") ?? SafeEnv("ANYTHINK_PLATFORM_ORG_ID");
            if (SanitiseOrgId(envOrgId) is { } cleanOrg) platform.MyAnythinkOrgId = cleanOrg;
        }

        WarnIfEnvTokenTargetsUnknownHost(match.Value, myUrl);

        return new PlatformContext(key, platform);
    }

    private static bool _envHostWarningEmitted;
    private static void WarnIfEnvTokenTargetsUnknownHost(PlatformConfig? matchedSaved, string resolvedUrl)
    {
        if (_envHostWarningEmitted) return;
        if (string.IsNullOrEmpty(SafeEnv("ANYTHINK_PLATFORM_TOKEN"))) return;
        if (matchedSaved is not null) return;  // host belongs to a known platform

        _envHostWarningEmitted = true;
        Console.Error.WriteLine(
            $"warning: ANYTHINK_PLATFORM_TOKEN is set, and the bearer will be sent to {resolvedUrl}, " +
            $"which is not a saved platform. Verify this host is yours before continuing.");
    }

    internal static void ResetEnvHostWarning() => _envHostWarningEmitted = false;

    public static PlatformConfig ResolvePlatform() => ResolvePlatformContext().Platform;

    /// <summary>
    /// Returns a clone of <paramref name="p"/> with env-var overrides applied —
    /// for use at HTTP-call time only. The clone is NEVER persisted, so secrets
    /// set via env vars (ANYTHINK_PLATFORM_TOKEN, ANYTHINK_ACCOUNT_ID,
    /// MYANYTHINK_ORG_ID) stay in the process, not on disk.
    /// </summary>
    public static PlatformConfig ApplyRuntimeOverrides(PlatformConfig p)
    {
        var c = new PlatformConfig
        {
            MyAnythinkOrgId = p.MyAnythinkOrgId,
            MyAnythinkUrl   = p.MyAnythinkUrl,
            BillingUrl      = p.BillingUrl,
            Token           = p.Token,
            TokenExpiresAt  = p.TokenExpiresAt,
            AccountId       = p.AccountId,
            DisplayName     = p.DisplayName,
        };

        var envToken = SafeEnv("ANYTHINK_PLATFORM_TOKEN");
        if (!string.IsNullOrEmpty(envToken)) c.Token = envToken;

        var envAccountId = SafeEnv("ANYTHINK_ACCOUNT_ID");
        if (!string.IsNullOrEmpty(envAccountId)) c.AccountId = envAccountId;

        var envOrgId = SafeEnv("MYANYTHINK_ORG_ID") ?? SafeEnv("ANYTHINK_PLATFORM_ORG_ID");
        if (SanitiseOrgId(envOrgId) is { } cleanOrg) c.MyAnythinkOrgId = cleanOrg;

        return c;
    }

    public static void SavePlatformAt(string key, PlatformConfig platform)
    {
        var config = Load();
        config.Platforms[key] = platform;
        // First-ever save becomes the active platform. Subsequent saves don't
        // change active_platform (lets env-var logins coexist with the active).
        if (config.Platforms.Count == 1 || string.IsNullOrEmpty(config.ActivePlatform))
            config.ActivePlatform = key;
        Save(config);
    }

    /// <summary>
    /// Save the platform AND make it active. Use this from explicit user
    /// actions (login, signup, accounts use) — saving an already-known platform
    /// shouldn't strand the user on a different active platform that has no
    /// token for the call they just made.
    /// </summary>
    public static void SaveAndActivatePlatform(string key, PlatformConfig platform)
    {
        var config = Load();
        config.Platforms[key] = platform;
        config.ActivePlatform = key;
        Save(config);
    }

    public static void SavePlatform(PlatformConfig platform)
    {
        var config = Load();
        var match  = config.Platforms.FirstOrDefault(kv => UrlsMatch(kv.Value.MyAnythinkUrl, platform.MyAnythinkUrl));
        var key    = match.Key ?? DerivePlatformKey(platform.MyAnythinkUrl);
        config.Platforms[key] = platform;
        if (string.IsNullOrEmpty(config.ActivePlatform)) config.ActivePlatform = key;
        Save(config);
    }

    public static void Reset()
    {
        if (File.Exists(ConfigPath)) File.Delete(ConfigPath);
    }

    public static string ConfigFilePath => ConfigPath;

    // ── Helpers ───────────────────────────────────────────────────────────────

    public static string DerivePlatformKey(string myanythinkUrl)
    {
        if (!Uri.TryCreate(myanythinkUrl, UriKind.Absolute, out var uri)) return "custom";
        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();

        if (host == "api.my.anythink.cloud") return "production";

        const string anythinkCloud = ".my.anythink.cloud";
        var stripped = host.StartsWith("api.") ? host[4..] : host;
        if (stripped != "my.anythink.cloud" && stripped.EndsWith(anythinkCloud))
        {
            var label = SafeKey(stripped[..^anythinkCloud.Length]);
            return string.IsNullOrEmpty(label) ? CustomHashKey(uri) : $"production-{label}";
        }

        var key = SafeKey(host);
        return string.IsNullOrEmpty(key) ? CustomHashKey(uri) : key;
    }

    /// <summary>Replaces dots with hyphens and strips anything outside [a-z0-9-].</summary>
    private static string SafeKey(string raw)
    {
        var s = raw.Replace('.', '-');
        var chars = s.Where(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-').ToArray();
        return new string(chars).Trim('-');
    }

    private static string CustomHashKey(Uri uri)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(uri.GetLeftPart(UriPartial.Authority)));
        return "custom-" + Convert.ToHexString(bytes)[..8].ToLowerInvariant();
    }

    public static bool UrlsMatch(string? a, string? b)
    {
        if (!Uri.TryCreate(a, UriKind.Absolute, out var ua)) return false;
        if (!Uri.TryCreate(b, UriKind.Absolute, out var ub)) return false;
        return string.Equals(ua.Scheme, ub.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(NormaliseHost(ua), NormaliseHost(ub), StringComparison.OrdinalIgnoreCase)
            && ua.Port == ub.Port;
    }

    private static string NormaliseHost(Uri u) => u.IdnHost.TrimEnd('.');

    internal static string? SanitiseOrgId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        return OrgIdPattern.IsMatch(trimmed) ? trimmed : null;
    }

    private static readonly Regex OrgIdPattern = new(@"^[0-9]{1,12}$", RegexOptions.Compiled);

    private static string? NormaliseHttpUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
        return uri.GetLeftPart(UriPartial.Authority);   // strip path/query
    }

    private static string? SafeEnv(string name)
    {
        var v = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }

    /// <summary>One-time forward-migration of legacy single-platform configs.</summary>
    private static bool Migrate(CliConfigData data)
    {
        bool changed = false;

        if (data.LegacyPlatform is not null && data.Platforms.Count == 0)
        {
            // The org_id capture bug let shell strings land in this field.
            data.LegacyPlatform.MyAnythinkOrgId =
                SanitiseOrgId(data.LegacyPlatform.MyAnythinkOrgId) ?? ApiDefaults.MyAnythinkOrgId;

            var key = DerivePlatformKey(data.LegacyPlatform.MyAnythinkUrl);
            data.Platforms[key] = data.LegacyPlatform;
            if (string.IsNullOrEmpty(data.ActivePlatform) || data.ActivePlatform == ApiDefaults.ProductionKey)
                data.ActivePlatform = key;
            changed = true;
        }
        if (data.LegacyPlatform is not null)
        {
            data.LegacyPlatform = null;
            changed = true;
        }

        // Tag profiles ONLY when the host matches a known platform. Profiles
        // for hosts we don't have a session for (e.g. a localhost profile
        // imported alongside a production-only legacy config) stay untagged
        // until the user logs in to that platform.
        if (data.Profiles.Count > 0 && data.Platforms.Count > 0)
        {
            foreach (var (_, profile) in data.Profiles)
            {
                if (!string.IsNullOrEmpty(profile.PlatformKey)) continue;

                foreach (var (pk, pv) in data.Platforms)
                {
                    if (UrlsMatch(pv.MyAnythinkUrl, profile.InstanceApiUrl))
                    {
                        profile.PlatformKey = pk;
                        changed = true;
                        break;
                    }
                }
            }
        }

        return changed;
    }
}

public record PlatformContext(string Key, PlatformConfig Platform);
