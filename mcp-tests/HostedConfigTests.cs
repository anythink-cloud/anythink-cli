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
    public void Options_PublicUrlSetDirectly_IsNormalisedToo() =>
        new HostedMode.Options
        {
            PublicUrl = "https://mcp.test/mcp/", Issuer = "i", Audience = "a",
            Exchange = new TokenExchangeOptions { ClientId = "c", ClientSecret = "s", Audience = "a" }
        }.PublicUrl.Should().Be("https://mcp.test/mcp");

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
}
