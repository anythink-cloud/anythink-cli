using FluentAssertions;

namespace AnythinkMcp.Tests;

public class HostedAuthTests
{
    // ── Rule: https instance_url is always accepted ──────────────────────────

    [Theory]
    [InlineData("https://42.api.anythink.cloud", false)]
    [InlineData("https://42.api.anythink.cloud", true)]
    public void IsAllowedInstanceUrl_Https_AlwaysAllowed(string url, bool isDevelopment) =>
        HostedAuth.IsAllowedInstanceUrl(url, isDevelopment).Should().BeTrue();

    // ── Rule: http instance_url is allowed only for localhost, and only outside Production ──

    [Fact]
    public void IsAllowedInstanceUrl_HttpLocalhost_AllowedInDevelopment() =>
        HostedAuth.IsAllowedInstanceUrl("http://localhost:5000", isDevelopment: true).Should().BeTrue();

    [Fact]
    public void IsAllowedInstanceUrl_HttpLocalhost_RefusedOutsideDevelopment() =>
        HostedAuth.IsAllowedInstanceUrl("http://localhost:5000", isDevelopment: false).Should().BeFalse();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IsAllowedInstanceUrl_HttpNonLocalhost_NeverAllowed(bool isDevelopment) =>
        HostedAuth.IsAllowedInstanceUrl("http://evil.example.com", isDevelopment).Should().BeFalse();

    [Fact]
    public void IsAllowedInstanceUrl_NotAUrl_Refused() =>
        HostedAuth.IsAllowedInstanceUrl("not-a-url", isDevelopment: true).Should().BeFalse();
}
