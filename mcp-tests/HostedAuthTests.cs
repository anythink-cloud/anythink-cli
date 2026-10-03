using FluentAssertions;

namespace AnythinkMcp.Tests;

public class HostedAuthTests
{
    private static readonly string[] Suffixes = HostedMode.DefaultInstanceHostSuffixes;

    [Theory]
    [InlineData("https://api.my.anythink.cloud", false)]
    [InlineData("https://api.my.anythink.dev", true)]
    [InlineData("https://api.my.anythink.uk", false)]
    [InlineData("https://api.uk01-lon.anythink.cloud", false)]
    public void IsAllowedInstanceUrl_HttpsOnAllowedSuffix_Allowed(string url, bool allowLoopback) =>
        HostedAuth.IsAllowedInstanceUrl(url, allowLoopback, Suffixes).Should().BeTrue();

    [Theory]
    [InlineData("https://evilanythink.cloud")]
    [InlineData("https://api.my.evilanythink.cloud")]
    [InlineData("https://anythink.cloud.evil.example")]
    [InlineData("https://evil.example/anythink.cloud")]
    [InlineData("https://user@evil.example")]
    [InlineData("https://api.example.com")]
    [InlineData("https://anythink.cloud")]
    [InlineData("https://my.anythink.cloud")]
    [InlineData("https://docs.anythink.cloud")]
    [InlineData("https://42.api.anythink.cloud")]
    public void IsAllowedInstanceUrl_HostOutsideAllowlist_Refused(string url) =>
        HostedAuth.IsAllowedInstanceUrl(url, allowLoopback: false, Suffixes).Should().BeFalse();

    [Fact]
    public void IsAllowedInstanceUrl_CustomSuffixes_ReplaceTheDefaults()
    {
        string[] custom = [".self-hosted.test"];
        HostedAuth.IsAllowedInstanceUrl("https://api.a.self-hosted.test", false, custom).Should().BeTrue();
        HostedAuth.IsAllowedInstanceUrl("https://api.my.anythink.cloud", false, custom).Should().BeFalse();
    }

    [Fact]
    public void IsAllowedInstanceUrl_HttpLocalhost_AllowedWhenLoopbackOptedIn() =>
        HostedAuth.IsAllowedInstanceUrl("http://localhost:5000", allowLoopback: true, Suffixes).Should().BeTrue();

    [Fact]
    public void IsAllowedInstanceUrl_HttpLocalhost_RefusedWithoutLoopbackOptIn() =>
        HostedAuth.IsAllowedInstanceUrl("http://localhost:5000", allowLoopback: false, Suffixes).Should().BeFalse();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IsAllowedInstanceUrl_HttpOnAllowedSuffix_NeverAllowed(bool allowLoopback) =>
        HostedAuth.IsAllowedInstanceUrl("http://api.my.anythink.cloud", allowLoopback, Suffixes).Should().BeFalse();

    [Fact]
    public void IsAllowedInstanceUrl_NotAUrl_Refused() =>
        HostedAuth.IsAllowedInstanceUrl("not-a-url", allowLoopback: true, Suffixes).Should().BeFalse();

    [Theory]
    [InlineData("https://user:pw@api.my.anythink.cloud")]
    [InlineData("https://api.my.anythink.cloud@evil.example")]
    [InlineData("https://api.my.anythink.cloud?x=1")]
    [InlineData("https://api.my.anythink.cloud/?")]
    [InlineData("https://api.my.anythink.cloud#frag")]
    [InlineData("https://api.my.anythink.cloud:8443")]
    [InlineData("https://api.my.anythink.cloud/v2")]
    [InlineData("https://api.my.anythink.cloud/../x")]
    public void IsAllowedInstanceUrl_NotABareOrigin_Refused(string url) =>
        HostedAuth.IsAllowedInstanceUrl(url, allowLoopback: false, Suffixes).Should().BeFalse();

    [Theory]
    [InlineData("https://api.my.anythink.cloud")]
    [InlineData("https://api.my.anythink.cloud/")]
    [InlineData("https://api.my.anythink.cloud:443")]
    public void IsAllowedInstanceUrl_BareOrigin_Allowed(string url) =>
        HostedAuth.IsAllowedInstanceUrl(url, allowLoopback: false, Suffixes).Should().BeTrue();

    [Fact]
    public void IsAllowedInstanceUrl_LoopbackWithUserinfo_StillRefused() =>
        HostedAuth.IsAllowedInstanceUrl("http://user@localhost:5000", allowLoopback: true, Suffixes).Should().BeFalse();
}
