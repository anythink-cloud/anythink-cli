using System.Security.Cryptography;
using System.Text;
using AnythinkMcp.Ui;
using FluentAssertions;

namespace AnythinkMcp.Tests;

public class HostedConfigTests
{
    private static readonly Dictionary<string, string> Complete = new()
    {
        ["MCP_PUBLIC_URL"] = "https://mcp.anythink.dev/mcp",
        ["MCP_AUTH_ISSUER"] = "https://api.billing.anythink.dev",
        ["MCP_EXCHANGE_CLIENT_ID"] = "mcp",
        ["MCP_EXCHANGE_CLIENT_SECRET"] = "s3cret",
        ["MCP_UPSTREAM_AUDIENCE"] = "anythink-oauth",
    };

    private static HostedMode.Options Load(Action<Dictionary<string, string>>? edit = null)
    {
        var env = new Dictionary<string, string>(Complete);
        edit?.Invoke(env);
        return HostedConfig.FromEnvironment(name => env.GetValueOrDefault(name));
    }

    [Theory]
    [InlineData("MCP_PUBLIC_URL")]
    [InlineData("MCP_AUTH_ISSUER")]
    [InlineData("MCP_EXCHANGE_CLIENT_ID")]
    [InlineData("MCP_EXCHANGE_CLIENT_SECRET")]
    [InlineData("MCP_UPSTREAM_AUDIENCE")]
    public void MissingRequiredSetting_RefusesToStart_NamingIt(string name)
    {
        var act = () => Load(env => env.Remove(name));
        act.Should().Throw<InvalidOperationException>().WithMessage($"{name}*");
    }

    [Fact]
    public void EmptyRequiredSetting_RefusesToStart()
    {
        var act = () => Load(env => env["MCP_EXCHANGE_CLIENT_SECRET"] = "");
        act.Should().Throw<InvalidOperationException>().WithMessage("MCP_EXCHANGE_CLIENT_SECRET*");
    }

    [Fact]
    public void Defaults_AudienceIsPublicUrl_SuffixesAreAnythinkDomains_NoOriginsOrExtraHosts()
    {
        var options = Load();

        options.Audience.Should().Be("https://mcp.anythink.dev/mcp");
        options.AllowedInstanceHostSuffixes.Should().BeEquivalentTo(".anythink.cloud", ".anythink.dev", ".anythink.uk");
        options.AllowedOrigins.Should().BeEmpty();
        options.AllowedHosts.Should().BeEmpty();
    }

    [Fact]
    public void ListSettings_AreCommaSeparatedAndTrimmed()
    {
        var options = Load(env =>
        {
            env["MCP_INSTANCE_HOST_SUFFIXES"] = ".a.test, .b.test";
            env["MCP_ALLOWED_ORIGINS"] = "https://x.test,https://y.test";
            env["MCP_ALLOWED_HOSTS"] = "mcp.internal";
        });

        options.AllowedInstanceHostSuffixes.Should().Equal(".a.test", ".b.test");
        options.AllowedOrigins.Should().Equal("https://x.test", "https://y.test");
        options.AllowedHosts.Should().Equal("mcp.internal");
    }

    [Fact]
    public void PublicUrl_TrailingSlashIsStripped_AndTheDefaultAudienceMatchesIt()
    {
        var options = Load(env => env["MCP_PUBLIC_URL"] = "https://mcp.anythink.dev/mcp/");

        options.PublicUrl.Should().Be("https://mcp.anythink.dev/mcp");
        options.Audience.Should().Be(options.PublicUrl);
    }

    [Fact]
    public void LoopbackInstance_IsOffUnlessExplicitlyEnabled()
    {
        Load().AllowLoopbackInstance.Should().BeFalse();
        Load(env =>
        {
            env["MCP_ALLOW_LOOPBACK_INSTANCE"] = "true";
            env["ASPNETCORE_ENVIRONMENT"] = "Development";
        }).AllowLoopbackInstance.Should().BeTrue();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData(null)]
    public void LoopbackInstance_OutsideDevelopment_RefusesToStart(string? environment)
    {
        var act = () => Load(env =>
        {
            env["MCP_ALLOW_LOOPBACK_INSTANCE"] = "true";
            if (environment is not null) env["ASPNETCORE_ENVIRONMENT"] = environment;
        });

        act.Should().Throw<InvalidOperationException>().WithMessage("*Development*");
    }

    [Fact]
    public void TokenEndpointOverride_IsNullByDefault_AndReadFromConfig()
    {
        Load().Exchange.TokenEndpoint.Should().BeNull();
        Load(env => env["MCP_TOKEN_ENDPOINT"] = "https://sts.test/token").Exchange.TokenEndpoint.Should().Be("https://sts.test/token");
    }

    // ── Rule: the billing service has its own setting, and falls back to the issuer ──

    [Fact]
    public void BillingUrl_DefaultsToTheIssuer()
    {
        var options = Load();

        options.BillingUrl.Should().BeNull();
        options.BillingBaseUrl.Should().Be(options.Issuer);
    }

    [Fact]
    public void BillingUrl_WhenSet_IsUsedInsteadOfTheIssuer_WithoutATrailingSlash()
    {
        var options = Load(env => env["MCP_BILLING_URL"] = "https://billing.test/");

        options.BillingBaseUrl.Should().Be("https://billing.test");
        options.Issuer.Should().Be(Complete["MCP_AUTH_ISSUER"]);
    }

    [Theory]
    [InlineData("billing.test")]
    [InlineData("http://billing.test")]
    [InlineData("/v1")]
    public void BillingUrl_ThatIsNotAnAbsoluteHttpsUrl_RefusesToStart(string value)
    {
        var act = () => Load(env => env["MCP_BILLING_URL"] = value);

        act.Should().Throw<InvalidOperationException>().WithMessage("MCP_BILLING_URL*");
    }

    [Fact]
    public void UiConfig_Unset_LeavesUiOff()
    {
        Load().Ui.Should().BeNull();
    }

    [Fact]
    public void UiConfig_ParsesInstanceToDashboardPairs()
    {
        var ui = Load(env => env["MCP_UI_DASHBOARDS"] =
            "https://api.uk01-lon.anythink.cloud=https://uk01-lon.anythink.cloud, https://api.my.anythink.dev=https://my-dev.anythink.dev").Ui!;

        ui.Dashboards.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["https://api.uk01-lon.anythink.cloud"] = "https://uk01-lon.anythink.cloud",
            ["https://api.my.anythink.dev"] = "https://my-dev.anythink.dev",
        });
        ui.FrameDomains.Should().BeEquivalentTo("https://uk01-lon.anythink.cloud", "https://my-dev.anythink.dev");
    }

    [Theory]
    [InlineData("http://x=https://y")]
    [InlineData("https://api.x=https://y/path")]
    [InlineData("https://api.x")]
    [InlineData("https://api.x=https://*.y")]
    public void UiConfig_RejectsNonHttpsOrPathOrMalformedPairs(string value)
    {
        var act = () => Load(env => env["MCP_UI_DASHBOARDS"] = value);

        act.Should().Throw<InvalidOperationException>().WithMessage("*MCP_UI_DASHBOARDS*");
    }

    [Fact]
    public void UiConfig_MyAnythinkUrl_MustBeHttpsOrigin()
    {
        const string pair = "https://api.x=https://y";

        Load(env => { env["MCP_UI_DASHBOARDS"] = pair; env["MCP_UI_MY_ANYTHINK_URL"] = "https://my.anythink.cloud"; })
            .Ui!.MyAnythinkUrl.Should().Be("https://my.anythink.cloud");

        var act = () => Load(env => { env["MCP_UI_DASHBOARDS"] = pair; env["MCP_UI_MY_ANYTHINK_URL"] = "https://my.anythink.cloud/x"; });
        act.Should().Throw<InvalidOperationException>().WithMessage("*MCP_UI_MY_ANYTHINK_URL*");
    }

    [Fact]
    public void SandboxDomain_IsSha256OfPublicUrl()
    {
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("https://mcp.anythink.dev/mcp"))).ToLowerInvariant()[..32] + ".claudemcpcontent.com";

        McpUiOptions.SandboxDomainFor("https://mcp.anythink.dev/mcp").Should().Be(expected);
        McpUiOptions.SandboxDomainFor("https://example.com/mcp").Should().Be("c3d80a4ed901ee05b21755a88273b4a4.claudemcpcontent.com");
        Load(env => env["MCP_UI_DASHBOARDS"] = "https://api.x=https://y").Ui!.SandboxDomain.Should().Be(expected);
    }
}
