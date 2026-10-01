using AnythinkCli.Commands;
using AnythinkCli.Config;
using FluentAssertions;
using Spectre.Console.Cli;

namespace AnythinkCli.Tests;

// ── login --base-url: connect a profile straight to a project's API instance ──

[Collection("SequentialConfig")]
public class PlatformLoginBaseUrlTests : IDisposable
{
    private static readonly string[] EnvVars =
        ["MYANYTHINK_API_URL", "ANYTHINK_PLATFORM_API_URL", "BILLING_API_URL", "ANYTHINK_BILLING_URL"];

    private readonly string _tempDir;
    private readonly Dictionary<string, string?> _savedEnv = new();

    public PlatformLoginBaseUrlTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"anythink-cli-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        ConfigService.ConfigDirOverride = _tempDir;
        foreach (var v in EnvVars)
        {
            _savedEnv[v] = Environment.GetEnvironmentVariable(v);
            Environment.SetEnvironmentVariable(v, null);
        }
    }

    public void Dispose()
    {
        ConfigService.ConfigDirOverride = null;
        foreach (var (k, v) in _savedEnv) Environment.SetEnvironmentVariable(k, v);
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private static int RunLogin(params string[] args)
    {
        var app = new CommandApp();
        app.Configure(c => c.AddCommand<PlatformLoginCommand>("login"));
        return app.Run(["login", .. args]);
    }

    [Fact]
    public void InstanceUrlFor_WithoutFlag_UsesThePlatformUrl()
    {
        PlatformLoginCommand.InstanceUrlFor(null, "https://api.my.anythink.cloud")
            .Should().Be("https://api.my.anythink.cloud");
        PlatformLoginCommand.InstanceUrlFor("  ", "https://api.my.anythink.cloud")
            .Should().Be("https://api.my.anythink.cloud");
    }

    [Fact]
    public void InstanceUrlFor_WithFlag_UsesTheNormalisedFlag()
    {
        PlatformLoginCommand.InstanceUrlFor(" https://api.uk01-lon.anythink.cloud/ ", "https://api.my.anythink.cloud")
            .Should().Be("https://api.uk01-lon.anythink.cloud");
    }

    [Theory]
    [InlineData("api.example.com")]
    [InlineData("ftp://api.example.com")]
    [InlineData("not a url")]
    public void InstanceUrlFor_WithInvalidFlag_ReturnsNull(string flag)
    {
        PlatformLoginCommand.InstanceUrlFor(flag, "https://api.my.anythink.cloud").Should().BeNull();
    }

    [Fact]
    public void DirectLogin_WithBaseUrl_SavesProfileAgainstThatInstance()
    {
        var exit = RunLogin("--org-id", "12345678", "--api-key", "ak_test", "--profile", "direct",
            "--base-url", "https://api.uk01-lon.anythink.cloud");

        exit.Should().Be(0);
        var profile = ConfigService.GetProfile("direct");
        profile.Should().NotBeNull();
        profile!.InstanceApiUrl.Should().Be("https://api.uk01-lon.anythink.cloud");
        profile.OrgId.Should().Be("12345678");
    }

    [Fact]
    public void DirectLogin_WithBaseUrl_DoesNotChangeThePlatform()
    {
        RunLogin("--org-id", "12345678", "--api-key", "ak_test", "--profile", "direct",
            "--base-url", "https://api.uk01-lon.anythink.cloud");

        ConfigService.Load().Platforms.Values
            .Should().NotContain(p => p.MyAnythinkUrl.Contains("uk01-lon"));
    }

    [Fact]
    public void DirectLogin_WithoutBaseUrl_SavesProfileAgainstThePlatform()
    {
        RunLogin("--org-id", "12345678", "--api-key", "ak_test", "--profile", "viaplatform");

        ConfigService.GetProfile("viaplatform")!.InstanceApiUrl.Should().Be(ApiDefaults.MyAnythinkApiUrl);
    }

    [Fact]
    public void DirectLogin_WithInvalidBaseUrl_FailsWithoutSavingAProfile()
    {
        var exit = RunLogin("--org-id", "12345678", "--api-key", "ak_test", "--profile", "bad",
            "--base-url", "api.example.com");

        exit.Should().Be(1);
        ConfigService.GetProfile("bad").Should().BeNull();
    }
}
