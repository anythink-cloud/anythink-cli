using AnythinkCli.Client;
using AnythinkCli.Commands;
using FluentAssertions;

namespace AnythinkCli.Tests;

public class ImportDirectusCommandTests
{
    // ── Rule: plain http is refused unless explicitly allowed (loopback excepted) ──

    [Theory]
    [InlineData("http://cms.example.com")]
    [InlineData("http://10.0.0.5:8055")]
    public void Plain_Http_Is_Refused_By_Default(string url)
    {
        var act = () => ImportDirectusCommand.ValidateUrl(url, allowInsecure: false);

        act.Should().Throw<CliException>().WithMessage("*http://*");
    }

    [Theory]
    [InlineData("http://localhost:8055")]
    [InlineData("http://127.0.0.1:8055")]
    [InlineData("http://[::1]:8055")]
    [InlineData("https://cms.example.com")]
    public void Https_And_Loopback_Http_Are_Accepted(string url)
    {
        ImportDirectusCommand.ValidateUrl(url, allowInsecure: false).Should().Be(url);
    }

    [Fact]
    public void Plain_Http_Is_Accepted_With_Allow_Insecure()
    {
        ImportDirectusCommand.ValidateUrl("http://cms.example.com", allowInsecure: true).Should().NotBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ftp://cms.example.com")]
    [InlineData("not a url")]
    public void Missing_Or_Non_Http_Urls_Are_Rejected(string? url)
    {
        var act = () => ImportDirectusCommand.ValidateUrl(url, allowInsecure: true);

        act.Should().Throw<CliException>();
    }

    // ── Rule: the token is read from the flag, then the environment, then a hidden prompt ──

    [Fact]
    public void Token_Flag_Wins_Over_Environment()
    {
        ImportDirectusCommand.ResolveToken("flag", "env", false, () => "typed").Should().Be("flag");
    }

    [Fact]
    public void Token_Falls_Back_To_The_Environment_Variable()
    {
        ImportDirectusCommand.ResolveToken(null, "env", false, () => "typed").Should().Be("env");
    }

    [Fact]
    public void Token_Is_Prompted_For_When_Interactive_And_Not_Otherwise_Supplied()
    {
        ImportDirectusCommand.ResolveToken(null, null, true, () => "typed").Should().Be("typed");
    }

    [Fact]
    public void Token_Is_Never_Prompted_For_When_Not_Interactive()
    {
        var prompted = false;

        var act = () => ImportDirectusCommand.ResolveToken(" ", "", false, () => { prompted = true; return "typed"; });

        act.Should().Throw<CliException>().WithMessage("*DIRECTUS_TOKEN*");
        prompted.Should().BeFalse();
    }

    // ── Rule: a real import needs --yes unless a human can confirm it ──

    [Fact]
    public void Real_Import_Without_Yes_Is_Refused_When_Not_Interactive()
    {
        var act = () => ImportDirectusCommand.RequireConfirmationMode(dryRun: false, yes: false, interactive: false);

        act.Should().Throw<CliException>().WithMessage("*--yes*");
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void Dry_Run_Yes_Or_An_Interactive_Session_Are_Allowed(bool dryRun, bool yes, bool interactive)
    {
        var act = () => ImportDirectusCommand.RequireConfirmationMode(dryRun, yes, interactive);

        act.Should().NotThrow();
    }
}
