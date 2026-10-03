using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace AnythinkMcp.Tests;

public class InternalApiTests
{
    private static Func<string, string?> Env(params (string, string)[] pairs)
    {
        var d = pairs.ToDictionary(p => p.Item1, p => p.Item2);
        return name => d.GetValueOrDefault(name);
    }

    private static int StatusOf(IResult result) =>
        ((Microsoft.AspNetCore.Http.IStatusCodeHttpResult)result).StatusCode!.Value;

    private static HttpContext WithToken(string? token)
    {
        var ctx = new DefaultHttpContext();
        if (token is not null) ctx.Request.Headers[InternalApiOptions.TokenHeader] = token;
        return ctx;
    }

    [Fact]
    public void Hosted_DefaultBind_IsLoopback_AndNeedsNoToken()
    {
        var options = InternalApi.ForHosted(Env());

        options.Bind.Should().Be("127.0.0.1");
        options.Token.Should().BeNull();
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("10.1.2.3")]
    public void Hosted_NonLoopbackBind_WithoutToken_RefusesToStart(string bind)
    {
        var act = () => InternalApi.ForHosted(Env(("MCP_INTERNAL_BIND", bind)));
        act.Should().Throw<InvalidOperationException>().WithMessage("MCP_INTERNAL_TOKEN*");
    }

    [Fact]
    public void Hosted_NonLoopbackBind_WithToken_Starts() =>
        InternalApi.ForHosted(Env(("MCP_INTERNAL_BIND", "0.0.0.0"), ("MCP_INTERNAL_TOKEN", "t"))).Token.Should().Be("t");

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("localhost")]
    public void Hosted_ExplicitLoopbackBind_NeedsNoToken(string bind) =>
        InternalApi.ForHosted(Env(("MCP_INTERNAL_BIND", bind))).Bind.Should().Be(bind);

    [Fact]
    public void Http_Defaults_AreUnchanged()
    {
        var options = InternalApi.ForInternal(Env());

        options.Bind.Should().Be("0.0.0.0");
        options.Token.Should().BeNull();
    }

    [Fact]
    public void CheckToken_NoneConfigured_AllowsEveryone() =>
        InternalApi.CheckToken(WithToken(null), InternalApi.ForInternal(Env())).Should().BeNull();

    [Fact]
    public void CheckToken_MatchingHeader_Allowed() =>
        InternalApi.CheckToken(WithToken("s3cret"), InternalApi.ForInternal(Env(("MCP_INTERNAL_TOKEN", "s3cret")))).Should().BeNull();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong")]
    [InlineData("s3cre")]
    public void CheckToken_MissingOrWrongHeader_Is401(string? supplied)
    {
        var denied = InternalApi.CheckToken(WithToken(supplied), InternalApi.ForInternal(Env(("MCP_INTERNAL_TOKEN", "s3cret"))));

        denied.Should().NotBeNull();
        StatusOf(denied!).Should().Be(401);
    }

    [Fact]
    public void CheckInstanceUrl_AllowedHost_Passes() =>
        InternalApi.CheckInstanceUrl("https://api.my.anythink.cloud", InternalApi.ForInternal(Env())).Should().BeNull();

    [Theory]
    [InlineData("https://evilanythink.cloud")]
    [InlineData("http://api.my.anythink.cloud")]
    [InlineData("http://localhost:5000")]
    public void CheckInstanceUrl_OutsideAllowlist_Is400(string url)
    {
        var denied = InternalApi.CheckInstanceUrl(url, InternalApi.ForInternal(Env()));

        denied.Should().NotBeNull();
        StatusOf(denied!).Should().Be(400);
    }

    [Fact]
    public void CheckInstanceUrl_Loopback_AllowedOnlyWithExplicitOptIn() =>
        InternalApi.CheckInstanceUrl("http://localhost:5000", InternalApi.ForInternal(Env(("MCP_ALLOW_LOOPBACK_INSTANCE", "true"), ("ASPNETCORE_ENVIRONMENT", "Development")))).Should().BeNull();
}
