using AnythinkCli.Config;
using FluentAssertions;
using System.Text.Json;

namespace AnythinkCli.Tests;

[Collection("SequentialConfig")]
public class PlatformsConfigTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string[] _envVarsToClear =
    {
        "MYANYTHINK_API_URL", "ANYTHINK_PLATFORM_API_URL",
        "BILLING_API_URL",   "ANYTHINK_BILLING_URL",
        "MYANYTHINK_ORG_ID", "ANYTHINK_PLATFORM_ORG_ID",
        "ANYTHINK_PLATFORM_TOKEN", "ANYTHINK_ACCOUNT_ID",
    };

    public PlatformsConfigTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"anythink-cli-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        ConfigService.ConfigDirOverride = _tempDir;
        ClearEnv();
    }

    public void Dispose()
    {
        ClearEnv();
        ConfigService.ConfigDirOverride = null;
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private void ClearEnv()
    {
        foreach (var v in _envVarsToClear) Environment.SetEnvironmentVariable(v, null);
    }

    // ── Schema defaults ──────────────────────────────────────────────────────

    [Fact]
    public void Fresh_Config_Starts_With_Empty_Platforms_And_Production_Active()
    {
        var config = ConfigService.Load();
        config.Platforms.Should().BeEmpty();
        config.ActivePlatform.Should().Be("production");
        config.Profiles.Should().BeEmpty();
    }

    // ── Migration: legacy `platform` → `platforms[derived-key]` ──────────────

    [Fact]
    public void Migration_Moves_Legacy_Platform_To_Platforms_Dict_Under_Derived_Key()
    {
        // Write a legacy-shape config to disk
        var legacy = """
        {
          "default_profile": "myapp",
          "profiles": {
            "myapp": {
              "org_id":          "1234",
              "instance_api_url":"https://api.my.anythink.cloud",
              "access_token":    "tok"
            }
          },
          "platform": {
            "myanythink_org_id": "20804318",
            "myanythink_url":    "https://api.my.anythink.cloud",
            "billing_url":       "https://api.billing.anythink.cloud",
            "token":             "platform-token"
          }
        }
        """;
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), legacy);

        var config = ConfigService.Load();

        config.Platforms.Should().ContainKey("production");
        config.Platforms["production"].Token.Should().Be("platform-token");
        config.ActivePlatform.Should().Be("production");
        // Legacy field cleared after migration
        config.LegacyPlatform.Should().BeNull();
    }

    [Fact]
    public void Migration_Tags_Profiles_Whose_Host_Matches_A_Migrated_Platform()
    {
        var legacy = """
        {
          "default_profile": "myapp",
          "profiles": {
            "myapp": {
              "org_id":          "1234",
              "instance_api_url":"https://api.my.anythink.cloud"
            },
            "local-test": {
              "org_id":          "9999",
              "instance_api_url":"https://localhost:7136"
            }
          },
          "platform": {
            "myanythink_url": "https://api.my.anythink.cloud",
            "billing_url":    "https://api.billing.anythink.cloud"
          }
        }
        """;
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), legacy);

        var config = ConfigService.Load();

        // Matching host → tagged
        config.Profiles["myapp"].PlatformKey.Should().Be("production");
        // Different host (localhost) → left untagged; user adds tag when they
        // later log in to a local platform.
        config.Profiles["local-test"].PlatformKey.Should().BeNull();
    }

    [Fact]
    public void Migration_Persists_Once_Then_Subsequent_Loads_Are_No_Op()
    {
        var legacy = """
        {
          "platforms": {},
          "platform": { "myanythink_url": "https://api.my.anythink.cloud", "billing_url": "https://api.billing.anythink.cloud" }
        }
        """;
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), legacy);

        ConfigService.Load();
        var firstWriteContents = File.ReadAllText(Path.Combine(_tempDir, "config.json"));

        ConfigService.Load();   // second load shouldn't re-migrate
        var secondWriteContents = File.ReadAllText(Path.Combine(_tempDir, "config.json"));

        secondWriteContents.Should().Be(firstWriteContents);
        // And the legacy field is gone for good
        secondWriteContents.Should().NotContain("\"platform\":");
    }

    // ── Sanitisation: hostile env values can't land on disk ──────────────────

    [Theory]
    [InlineData("19101998",          "19101998")]     // happy path
    [InlineData("  19101998  ",      "19101998")]     // trimmed
    [InlineData(null,                null)]
    [InlineData("",                  null)]
    [InlineData("19101998 MYANYTHINK_API_URL=https://x",  null)] // shell injection
    [InlineData("not-a-number",      null)]
    [InlineData("123\n456",          null)]           // newline injection
    [InlineData("999999999999999",   null)]           // too long
    public void SanitiseOrgId_Filters_Bad_Values(string? input, string? expected)
    {
        // SanitiseOrgId is internal — covered via the runtime-overlay path
        Environment.SetEnvironmentVariable("MYANYTHINK_ORG_ID", input);
        var ctx = ConfigService.ResolvePlatformContext();
        var eff = ConfigService.ApplyRuntimeOverrides(ctx.Platform);

        if (expected is null)
        {
            // Bad input → ignored → defaults stay in place
            eff.MyAnythinkOrgId.Should().Be(ApiDefaults.MyAnythinkOrgId);
        }
        else
        {
            eff.MyAnythinkOrgId.Should().Be(expected);
        }
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com")]
    [InlineData("javascript:alert(1)")]
    public void Bad_MyAnythink_Url_Env_Var_Is_Ignored(string badUrl)
    {
        Environment.SetEnvironmentVariable("MYANYTHINK_API_URL", badUrl);
        var ctx = ConfigService.ResolvePlatformContext();
        // Falls through to the default production URL
        ctx.Platform.MyAnythinkUrl.Should().Be(ApiDefaults.MyAnythinkApiUrl);
    }

    // ── Resolver priority chain ──────────────────────────────────────────────

    [Fact]
    public void Resolver_Uses_Defaults_When_Nothing_Configured()
    {
        var ctx = ConfigService.ResolvePlatformContext();
        ctx.Key.Should().Be("production");
        ctx.Platform.MyAnythinkUrl.Should().Be(ApiDefaults.MyAnythinkApiUrl);
    }

    [Fact]
    public void Resolver_Uses_Active_Platform_Saved_Urls()
    {
        ConfigService.SavePlatformAt("local", new PlatformConfig
        {
            MyAnythinkUrl = "https://localhost:7136",
            BillingUrl    = "https://localhost:7180",
            Token         = "local-tok",
        });

        // First saved → becomes active automatically
        var ctx = ConfigService.ResolvePlatformContext();
        ctx.Key.Should().Be("local");
        ctx.Platform.MyAnythinkUrl.Should().Be("https://localhost:7136");
    }

    [Fact]
    public void Env_Var_Selects_Matching_Saved_Platform_Without_Touching_Active()
    {
        // Two saved platforms — production is active
        ConfigService.SavePlatformAt("production", new PlatformConfig
        {
            MyAnythinkUrl = ApiDefaults.MyAnythinkApiUrl,
            BillingUrl    = ApiDefaults.BillingApiUrl,
            Token         = "prod-tok",
        });
        ConfigService.SavePlatformAt("local", new PlatformConfig
        {
            MyAnythinkUrl = "https://localhost:7136",
            BillingUrl    = "https://localhost:7180",
            Token         = "local-tok",
        });
        var config = ConfigService.Load();
        config.ActivePlatform = "production";
        ConfigService.Save(config);

        // Env var pointing at local → resolves to local's saved entry
        Environment.SetEnvironmentVariable("MYANYTHINK_API_URL", "https://localhost:7136");
        var ctx = ConfigService.ResolvePlatformContext();
        ctx.Key.Should().Be("local");
        ctx.Platform.Token.Should().Be("local-tok");

        // active_platform on disk wasn't touched
        ConfigService.Load().ActivePlatform.Should().Be("production");
    }

    [Fact]
    public void Flag_Takes_Priority_Over_Env_Var()
    {
        Environment.SetEnvironmentVariable("MYANYTHINK_API_URL", "https://localhost:7136");

        var ctx = ConfigService.ResolvePlatformContext(myanythinkUrlFlag: "https://api.eu.my.anythink.cloud");
        ctx.Key.Should().Be("production-eu");
        ctx.Platform.MyAnythinkUrl.Should().Be("https://api.eu.my.anythink.cloud");
    }

    [Fact]
    public void Unknown_Url_Creates_In_Memory_Platform_With_Derived_Key()
    {
        // No saved platforms — env var points at a fresh URL
        Environment.SetEnvironmentVariable("MYANYTHINK_API_URL", "https://api.anythink.acme-corp.com");
        var ctx = ConfigService.ResolvePlatformContext();

        // Derived key for self-hosted hostname
        ctx.Key.Should().Be("api-anythink-acme-corp-com");
        ctx.Platform.MyAnythinkUrl.Should().Be("https://api.anythink.acme-corp.com");
        // Not persisted yet — only saved on auth-success
        ConfigService.Load().Platforms.Should().BeEmpty();
    }

    // ── Key derivation ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("https://api.my.anythink.cloud",         "production")]
    [InlineData("https://api.eu.my.anythink.cloud",      "production-eu")]
    [InlineData("https://api.us-west.my.anythink.cloud", "production-us-west")]
    [InlineData("https://acme.my.anythink.cloud",        "production-acme")]
    [InlineData("https://api.anythink.acme-corp.com",    "api-anythink-acme-corp-com")]
    public void DerivePlatformKey_Maps_Known_Hosts_To_Friendly_Names(string url, string expectedKey)
    {
        ConfigService.DerivePlatformKey(url).Should().Be(expectedKey);
    }

    // ── SetDefault flips active_platform when profile is tagged ──────────────

    [Fact]
    public void SetDefault_Switches_Active_Platform_To_Profiles_Platform_Key()
    {
        ConfigService.SavePlatformAt("production", new PlatformConfig
        {
            MyAnythinkUrl = ApiDefaults.MyAnythinkApiUrl,
            BillingUrl    = ApiDefaults.BillingApiUrl,
        });
        ConfigService.SavePlatformAt("local", new PlatformConfig
        {
            MyAnythinkUrl = "https://localhost:7136",
            BillingUrl    = "https://localhost:7180",
        });

        ConfigService.SaveProfile("prod-app", new Profile
        {
            OrgId          = "1",
            InstanceApiUrl = ApiDefaults.MyAnythinkApiUrl,
            PlatformKey    = "production",
        });
        ConfigService.SaveProfile("local-app", new Profile
        {
            OrgId          = "2",
            InstanceApiUrl = "https://localhost:7136",
            PlatformKey    = "local",
        });

        // Switch active profile — active_platform should follow
        ConfigService.SetDefault("local-app");
        ConfigService.Load().ActivePlatform.Should().Be("local");

        ConfigService.SetDefault("prod-app");
        ConfigService.Load().ActivePlatform.Should().Be("production");
    }

    // ── Cross-platform-session isolation: prod login + local login coexist ───

    [Fact]
    public void Two_Logins_Against_Different_Platforms_Both_Persist_Independently()
    {
        // Login 1 — production
        ConfigService.SavePlatformAt("production", new PlatformConfig
        {
            MyAnythinkUrl = ApiDefaults.MyAnythinkApiUrl,
            BillingUrl    = ApiDefaults.BillingApiUrl,
            Token         = "prod-token",
            AccountId     = "prod-acct",
        });

        // Login 2 — local (via env var path), via separate SavePlatformAt call
        ConfigService.SavePlatformAt("local", new PlatformConfig
        {
            MyAnythinkUrl = "https://localhost:7136",
            BillingUrl    = "https://localhost:7180",
            Token         = "local-token",
            AccountId     = "local-acct",
        });

        var config = ConfigService.Load();
        config.Platforms.Should().HaveCount(2);
        config.Platforms["production"].Token.Should().Be("prod-token");
        config.Platforms["local"].Token.Should().Be("local-token");

        // active_platform was set on the FIRST save (production) and not
        // touched by the second.
        config.ActivePlatform.Should().Be("production");
    }

    // ── Env-var secrets must not bleed into the persisted record ─────────────

    [Fact]
    public void Env_Var_Token_Does_Not_Leak_Into_Resolved_Disk_Record()
    {
        ConfigService.SavePlatformAt("production", new PlatformConfig
        {
            MyAnythinkUrl = ApiDefaults.MyAnythinkApiUrl,
            BillingUrl    = ApiDefaults.BillingApiUrl,
            Token         = "real-disk-token",
            AccountId     = "real-disk-acct",
        });

        Environment.SetEnvironmentVariable("ANYTHINK_PLATFORM_TOKEN", "evil-env-token");
        Environment.SetEnvironmentVariable("ANYTHINK_ACCOUNT_ID",     "evil-env-acct");

        var ctx = ConfigService.ResolvePlatformContext();
        ctx.Platform.Token.Should().Be("real-disk-token");
        ctx.Platform.AccountId.Should().Be("real-disk-acct");

        // Simulate a save-after-resolve caller (signup, accounts-use, etc.)
        ConfigService.SavePlatformAt(ctx.Key, ctx.Platform);
        var reloaded = ConfigService.Load();
        reloaded.Platforms["production"].Token.Should().Be("real-disk-token");
        reloaded.Platforms["production"].AccountId.Should().Be("real-disk-acct");
    }

    // ── Hardening: charset, URL normalisation, ASCII-only digits ──────────────

    [Theory]
    [InlineData("https://" + "xn--ls8h.example",  "xn--ls8h-example")]  // punycode preserved
    [InlineData("https://API.MY.ANYTHINK.CLOUD.", "production")]        // trailing dot + caps
    public void DerivePlatformKey_Restricts_Charset(string url, string expected)
    {
        var key = ConfigService.DerivePlatformKey(url);
        key.Should().Be(expected);
        key.Should().MatchRegex("^[a-z0-9-]+$");
    }

    [Fact]
    public void DerivePlatformKey_Falls_Back_To_Hash_For_Pathological_Hosts()
    {
        // Punycode that contains only chars outside the [a-z0-9] charset after
        // dot-replace shouldn't be possible (xn-- prefix is always safe), but
        // an unparseable URL still falls back to "custom".
        ConfigService.DerivePlatformKey("not a url").Should().Be("custom");
    }

    [Fact]
    public void UrlsMatch_Treats_Trailing_Dot_And_Case_As_Equivalent()
    {
        ConfigService.UrlsMatch("https://api.my.anythink.cloud", "https://api.my.anythink.cloud.").Should().BeTrue();
        ConfigService.UrlsMatch("https://API.MY.anythink.cloud", "https://api.my.anythink.cloud").Should().BeTrue();
    }

    [Fact]
    public void UrlsMatch_Treats_Different_Schemes_As_Different_Hosts()
    {
        ConfigService.UrlsMatch("http://api.my.anythink.cloud", "https://api.my.anythink.cloud").Should().BeFalse();
    }

    [Theory]
    [InlineData("١٢٣٤٥٦٧٨")]                          // Eastern-Arabic digits — \d would match, [0-9] does not
    [InlineData("１２３")]                              // Fullwidth digits
    public void SanitiseOrgId_Rejects_NonAscii_Digits(string input)
    {
        Environment.SetEnvironmentVariable("MYANYTHINK_ORG_ID", input);
        var eff = ConfigService.ApplyRuntimeOverrides(ConfigService.ResolvePlatformContext().Platform);
        eff.MyAnythinkOrgId.Should().Be(ApiDefaults.MyAnythinkOrgId);
    }

    [Fact]
    public void SetDefault_Emits_Stderr_Notice_When_Active_Platform_Changes()
    {
        ConfigService.SavePlatformAt("production", new PlatformConfig
        {
            MyAnythinkUrl = ApiDefaults.MyAnythinkApiUrl,
            BillingUrl    = ApiDefaults.BillingApiUrl,
        });
        ConfigService.SavePlatformAt("local", new PlatformConfig
        {
            MyAnythinkUrl = "https://localhost:7136",
            BillingUrl    = "https://localhost:7180",
        });
        ConfigService.SaveProfile("a", new Profile
        {
            OrgId = "1", InstanceApiUrl = "https://localhost:7136", PlatformKey = "local",
        });

        var originalErr = Console.Error;
        var capture = new StringWriter();
        Console.SetError(capture);
        try { ConfigService.SetDefault("a"); }
        finally { Console.SetError(originalErr); }

        capture.ToString().Should().Contain("local");
    }

    [Fact]
    public void Env_Token_Targeting_Unknown_Host_Emits_Stderr_Warning()
    {
        Environment.SetEnvironmentVariable("ANYTHINK_PLATFORM_TOKEN", "sensitive-token");
        Environment.SetEnvironmentVariable("MYANYTHINK_API_URL", "https://attacker.example");

        var originalErr = Console.Error;
        var capture = new StringWriter();
        Console.SetError(capture);
        try
        {
            ConfigService.ResetEnvHostWarning();
            ConfigService.ResolvePlatformContext();
        }
        finally
        {
            Console.SetError(originalErr);
            ConfigService.ResetEnvHostWarning();
        }

        var output = capture.ToString();
        output.Should().Contain("ANYTHINK_PLATFORM_TOKEN");
        output.Should().Contain("attacker.example");
    }

    [Fact]
    public void Env_Token_Targeting_Saved_Platform_Emits_No_Warning()
    {
        ConfigService.SavePlatformAt("production", new PlatformConfig
        {
            MyAnythinkUrl = ApiDefaults.MyAnythinkApiUrl,
            BillingUrl    = ApiDefaults.BillingApiUrl,
        });
        Environment.SetEnvironmentVariable("ANYTHINK_PLATFORM_TOKEN", "any-token");
        Environment.SetEnvironmentVariable("MYANYTHINK_API_URL", ApiDefaults.MyAnythinkApiUrl);

        var originalErr = Console.Error;
        var capture = new StringWriter();
        Console.SetError(capture);
        try
        {
            ConfigService.ResetEnvHostWarning();
            ConfigService.ResolvePlatformContext();
        }
        finally
        {
            Console.SetError(originalErr);
            ConfigService.ResetEnvHostWarning();
        }

        capture.ToString().Should().BeEmpty();
    }

    [Fact]
    public void SaveAndActivatePlatform_Always_Sets_Active()
    {
        ConfigService.SavePlatformAt("production", new PlatformConfig
        {
            MyAnythinkUrl = ApiDefaults.MyAnythinkApiUrl,
            BillingUrl    = ApiDefaults.BillingApiUrl,
        });
        ConfigService.Load().ActivePlatform.Should().Be("production");

        ConfigService.SaveAndActivatePlatform("localhost", new PlatformConfig
        {
            MyAnythinkUrl = "https://localhost:7136",
            BillingUrl    = "https://localhost:7180",
            Token         = "fresh-token",
        });

        // Login to a different platform makes IT the active one — next command
        // without env vars sees the just-logged-in session, not the previous active.
        ConfigService.Load().ActivePlatform.Should().Be("localhost");
    }

    [Fact]
    public void New_Platform_Captures_Env_OrgId_Into_Saved_Record()
    {
        Environment.SetEnvironmentVariable("MYANYTHINK_API_URL", "https://localhost:7136");
        Environment.SetEnvironmentVariable("MYANYTHINK_ORG_ID",  "19101998");

        // First login: no saved platform → resolver seeds a new one with the env org id
        var (key, platform) = ConfigService.ResolvePlatformContext();
        platform.MyAnythinkOrgId.Should().Be("19101998");

        // Auth flow simulates: token arrives, save + activate
        platform.Token = "fresh-token";
        ConfigService.SaveAndActivatePlatform(key, platform);

        // Clear env vars — next command must still hit the right tenant
        Environment.SetEnvironmentVariable("MYANYTHINK_API_URL", null);
        Environment.SetEnvironmentVariable("MYANYTHINK_ORG_ID",  null);

        var ctx = ConfigService.ResolvePlatformContext();
        ctx.Platform.MyAnythinkOrgId.Should().Be("19101998");
        ctx.Platform.Token.Should().Be("fresh-token");
    }

    [Fact]
    public void Existing_Platform_OrgId_Not_Overwritten_By_Env_Var()
    {
        ConfigService.SavePlatformAt("localhost", new PlatformConfig
        {
            MyAnythinkUrl   = "https://localhost:7136",
            BillingUrl      = "https://localhost:7180",
            MyAnythinkOrgId = "111",
        });

        Environment.SetEnvironmentVariable("MYANYTHINK_API_URL", "https://localhost:7136");
        Environment.SetEnvironmentVariable("MYANYTHINK_ORG_ID",  "222");

        // Existing match → env org id stays runtime-only (via ApplyRuntimeOverrides);
        // the saved record is untouched.
        var ctx = ConfigService.ResolvePlatformContext();
        ctx.Platform.MyAnythinkOrgId.Should().Be("111");
    }

    [Fact]
    public void Runtime_Overlay_Applies_Env_Vars_To_A_Clone()
    {
        var disk = new PlatformConfig
        {
            MyAnythinkUrl   = ApiDefaults.MyAnythinkApiUrl,
            MyAnythinkOrgId = ApiDefaults.MyAnythinkOrgId,
            Token           = "disk-token",
            AccountId       = "disk-acct",
        };

        Environment.SetEnvironmentVariable("ANYTHINK_PLATFORM_TOKEN", "env-token");
        Environment.SetEnvironmentVariable("ANYTHINK_ACCOUNT_ID",     "env-acct");
        Environment.SetEnvironmentVariable("MYANYTHINK_ORG_ID",       "12345");

        var eff = ConfigService.ApplyRuntimeOverrides(disk);
        eff.Token.Should().Be("env-token");
        eff.AccountId.Should().Be("env-acct");
        eff.MyAnythinkOrgId.Should().Be("12345");

        // Original record is untouched.
        disk.Token.Should().Be("disk-token");
        disk.AccountId.Should().Be("disk-acct");
        disk.MyAnythinkOrgId.Should().Be(ApiDefaults.MyAnythinkOrgId);
    }
}
