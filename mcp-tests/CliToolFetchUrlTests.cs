using AnythinkMcp.Tools;
using FluentAssertions;

namespace AnythinkMcp.Tests;

public class CliToolFetchUrlTests
{
    private const string BaseUrl = "https://42.api.anythink.cloud";

    [Fact]
    public void Fetch_RelativePath_IsScopedToTheProject()
    {
        CliTool.ResolveFetchUrl("/entities", BaseUrl, "42").Should().Be("https://42.api.anythink.cloud/org/42/entities");
    }

    [Fact]
    public void Fetch_AbsoluteUrlOnTheProjectHost_IsAllowed()
    {
        CliTool.ResolveFetchUrl("https://42.api.anythink.cloud/org/42/entities", BaseUrl, "42")
            .Should().Be("https://42.api.anythink.cloud/org/42/entities");
    }

    [Theory]
    [InlineData("https://attacker.example/x")]
    [InlineData("http://42.api.anythink.cloud/org/42/entities")]
    [InlineData("https://42.api.anythink.cloud:8443/org/42")]
    [InlineData("https://42.api.anythink.cloud.attacker.example/x")]
    [InlineData("https://user@42.api.anythink.cloud/org/42")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("entities")]
    public void Fetch_UrlOffTheProjectHost_IsRefused(string path)
    {
        CliTool.ResolveFetchUrl(path, BaseUrl, "42").Should().BeNull();
    }
}
