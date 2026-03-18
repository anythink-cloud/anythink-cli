using AnythinkCli.Config;
using FluentAssertions;

namespace AnythinkCli.Tests;

/// <summary>
/// Tests for ConfigService — uses a temporary directory to avoid touching ~/.anythink.
/// ConfigService.ConfigDirOverride is set before each test and cleared after.
/// Belongs to SequentialConfig collection to prevent parallel conflicts on the static override.
/// </summary>
[Collection("SequentialConfig")]
public class ConfigServiceTests : IDisposable
{
    private readonly string _tempDir;

    public ConfigServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"anythink-cli-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        ConfigService.ConfigDirOverride = _tempDir;
    }

    public void Dispose()
    {
        ConfigService.ConfigDirOverride = null;
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Load ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Load_WhenNoFileExists_ReturnsEmptyConfig()
    {
        var config = ConfigService.Load();
        config.Should().NotBeNull();
        config.Profiles.Should().BeEmpty();
        config.DefaultProfile.Should().BeEmpty();
        config.Platform.Should().BeNull();
    }

    // ── Save + Load round-trip ────────────────────────────────────────────────

    [Fact]
    public void Save_ThenLoad_RoundTripsCorrectly()
    {
        var config = new CliConfigData
        {
            DefaultProfile = "myapp",
            Profiles =
            {
                ["myapp"] = new Profile
                {
                    OrgId       = "org-123",
                    AccessToken = "tok-abc",
                    BaseUrl     = "https://api.my.anythink.cloud",
                    Alias       = "myapp"
                }
            }
        };

        ConfigService.Save(config);
        var loaded = ConfigService.Load();

        loaded.DefaultProfile.Should().Be("myapp");
        loaded.Profiles.Should().ContainKey("myapp");
        loaded.Profiles["myapp"].OrgId.Should().Be("org-123");
        loaded.Profiles["myapp"].AccessToken.Should().Be("tok-abc");
    }

    [Fact]
    public void Save_CreatesMissingDirectory()
    {
        var nested = Path.Combine(_tempDir, "deep", "nested");
        ConfigService.ConfigDirOverride = nested;

        ConfigService.Save(new CliConfigData());

        File.Exists(Path.Combine(nested, "config.json")).Should().BeTrue();

        // Restore override for Dispose
        ConfigService.ConfigDirOverride = _tempDir;
    }

    // ── SaveProfile ───────────────────────────────────────────────────────────

    [Fact]
    public void SaveProfile_StoresProfileAndSetsAsDefault_WhenFirst()
    {
        var profile = new Profile { OrgId = "org-1", BaseUrl = "https://api.my.anythink.cloud" };
        ConfigService.SaveProfile("app1", profile);

        var config = ConfigService.Load();
        config.Profiles.Should().ContainKey("app1");
        config.DefaultProfile.Should().Be("app1");
    }

    [Fact]
    public void SaveProfile_DoesNotOverrideExistingDefault()
    {
        ConfigService.SaveProfile("first",  new Profile { OrgId = "org-1" });
        ConfigService.SaveProfile("second", new Profile { OrgId = "org-2" });

        var config = ConfigService.Load();
        config.DefaultProfile.Should().Be("first");
    }

    // ── GetActiveProfile ──────────────────────────────────────────────────────

    [Fact]
    public void GetActiveProfile_WhenNoProfiles_ReturnsNull()
        => ConfigService.GetActiveProfile().Should().BeNull();

    [Fact]
    public void GetActiveProfile_ReturnsDefaultProfile()
    {
        ConfigService.SaveProfile("app1", new Profile { OrgId = "org-1" });
        ConfigService.SaveProfile("app2", new Profile { OrgId = "org-2" });

        var active = ConfigService.GetActiveProfile();
        active.Should().NotBeNull();
        active!.OrgId.Should().Be("org-1");
    }

    // ── SetDefault ────────────────────────────────────────────────────────────

    [Fact]
    public void SetDefault_SwitchesActiveProfile()
    {
        ConfigService.SaveProfile("app1", new Profile { OrgId = "org-1" });
        ConfigService.SaveProfile("app2", new Profile { OrgId = "org-2" });

        ConfigService.SetDefault("app2");

        ConfigService.GetActiveProfile()!.OrgId.Should().Be("org-2");
    }

    [Fact]
    public void SetDefault_ThrowsForUnknownProfile()
    {
        Action act = () => ConfigService.SetDefault("nonexistent");
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*nonexistent*");
    }

    // ── RemoveProfile ─────────────────────────────────────────────────────────

    [Fact]
    public void RemoveProfile_RemovesItFromProfiles()
    {
        ConfigService.SaveProfile("app1", new Profile { OrgId = "org-1" });
        ConfigService.SaveProfile("app2", new Profile { OrgId = "org-2" });

        ConfigService.RemoveProfile("app1");

        ConfigService.Load().Profiles.Should().NotContainKey("app1");
    }

    [Fact]
    public void RemoveProfile_WhenRemovingDefault_PromotesNextProfile()
    {
        ConfigService.SaveProfile("app1", new Profile { OrgId = "org-1" });
        ConfigService.SaveProfile("app2", new Profile { OrgId = "org-2" });

        ConfigService.RemoveProfile("app1");

        ConfigService.Load().DefaultProfile.Should().Be("app2");
    }

    [Fact]
    public void RemoveProfile_WhenRemovingLastProfile_ClearsDefault()
    {
        ConfigService.SaveProfile("only", new Profile { OrgId = "org-1" });

        ConfigService.RemoveProfile("only");

        var config = ConfigService.Load();
        config.Profiles.Should().BeEmpty();
        config.DefaultProfile.Should().BeEmpty();
    }

    // ── ConfigFilePath ────────────────────────────────────────────────────────

    [Fact]
    public void ConfigFilePath_ReflectsOverriddenDirectory()
    {
        ConfigService.ConfigFilePath.Should().StartWith(_tempDir);
        ConfigService.ConfigFilePath.Should().EndWith("config.json");
    }
}
