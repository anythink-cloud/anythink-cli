using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace AnythinkMcp.Tests;

public class HostedAuthTests
{
    private static readonly string[] Suffixes = HostedMode.DefaultInstanceHostSuffixes;

    [Theory]
    [InlineData("https://api.my.anythink.cloud")]
    [InlineData("https://api.my.anythink.dev")]
    [InlineData("https://api.my.anythink.uk")]
    [InlineData("https://api.acme.anythink.cloud")]
    public void IsAllowedInstanceUrl_HttpsOnAllowedSuffix_Allowed(string url) =>
        HostedAuth.IsAllowedInstanceUrl(url, allowLoopback: false, Suffixes).Should().BeTrue();

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

    [Theory]
    [InlineData("mcp.test", true)]
    [InlineData("localhost", true)]
    [InlineData("evil.example", false)]
    public async Task ValidateHostAndOrigin_AcceptsThePublicHostAndExtraAllowedHosts(string host, bool allowed)
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host);
        context.Request.Path = "/mcp";
        var reachedNext = false;

        await HostedAuth.ValidateHostAndOrigin(context, _ => { reachedNext = true; return Task.CompletedTask; }, ["mcp.test", "localhost"], []);

        reachedNext.Should().Be(allowed);
    }

    // ── Rule: a project connection is limited per project, an all-projects one per user ──

    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "test"));

    [Fact]
    public void RateLimitPartition_AProjectToken_IsTheProjectId() =>
        HostedAuth.RateLimitPartition(Principal(("sub", "user-1"), ("tid", "42"))).Should().Be("42");

    [Fact]
    public void RateLimitPartition_AProjectToken_SharesItsPartitionAcrossUsers() =>
        HostedAuth.RateLimitPartition(Principal(("sub", "user-2"), ("tid", "42")))
            .Should().Be(HostedAuth.RateLimitPartition(Principal(("sub", "user-1"), ("tid", "42"))));

    [Fact]
    public void RateLimitPartition_AnAllProjectsToken_IsTheUser() =>
        HostedAuth.RateLimitPartition(Principal(("sub", "user-1"), ("projects", "all"))).Should().Be("user:user-1");

    [Fact]
    public void RateLimitPartition_TwoAllProjectsUsers_DontShareABucket() =>
        HostedAuth.RateLimitPartition(Principal(("sub", "user-1"), ("projects", "all")))
            .Should().NotBe(HostedAuth.RateLimitPartition(Principal(("sub", "user-2"), ("projects", "all"))));

    [Theory]
    [InlineData("1")]
    [InlineData("42")]
    [InlineData("user")]
    public void RateLimitPartition_AUserBucket_NeverCollidesWithAProjectBucket(string sub) =>
        HostedAuth.RateLimitPartition(Principal(("sub", sub), ("projects", "all")))
            .Should().NotBe(HostedAuth.RateLimitPartition(Principal(("sub", "x"), ("tid", sub))));

    [Theory]
    [InlineData("42", true)]
    [InlineData("0", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("4 2", false)]
    [InlineData("-1", false)]
    [InlineData("4a", false)]
    public void IsValidOrgId_IsNonEmptyAndNumeric(string? value, bool expected) =>
        HostedAuth.IsValidOrgId(value).Should().Be(expected);
}
