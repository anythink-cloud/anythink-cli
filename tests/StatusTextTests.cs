using AnythinkCli.Commands;
using FluentAssertions;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace AnythinkCli.Tests;

public class StatusTextTests
{
    private static string Render(IRenderable renderable)
    {
        var output = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.Standard,
            Out = new AnsiConsoleOutput(output)
        });
        console.Write(renderable);
        return output.ToString();
    }

    // ── Rule: account status names are the ones the billing service sends ──

    [Theory]
    [InlineData(0, "active")]
    [InlineData(1, "past due")]
    [InlineData(2, "suspended")]
    [InlineData(3, "closed")]
    [InlineData(9, "9")]
    public void AccountStatusName_MatchesTheBillingService(int status, string expected)
        => AccountStatusText.Name(status).Should().Be(expected);

    // ── Rule: a status shown as a value carries no markup, so it never prints literal colour tags ──

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(9)]
    public void StatusName_NeverContainsMarkup(int status)
    {
        ProjectStatusText.Name(status).Should().NotContainAny("[", "]");
        AccountStatusText.Name(status).Should().NotContainAny("[", "]");
    }

    // ── Rule: a status cell is coloured by styling, not by markup text ──

    [Theory]
    [InlineData(0, "initializing")]
    [InlineData(2, "active")]
    [InlineData(4, "terminated")]
    public void ProjectStatusCell_IsStyledAndShowsOnlyTheName(int status, string name)
    {
        var rendered = Render(ProjectStatusText.Cell(status));

        rendered.Should().Contain(name).And.Contain("\u001b[");
        rendered.Should().NotContain("[/]").And.NotContain("[green]").And.NotContain("[red]");
    }

    [Theory]
    [InlineData(0, "active")]
    [InlineData(2, "suspended")]
    [InlineData(3, "closed")]
    public void AccountStatusCell_IsStyledAndShowsOnlyTheName(int status, string name)
    {
        var rendered = Render(AccountStatusText.Cell(status));

        rendered.Should().Contain(name).And.Contain("\u001b[");
        rendered.Should().NotContain("[/]").And.NotContain("[green]").And.NotContain("[red]");
    }

    [Fact]
    public void StatusCell_ForAnUnknownStatus_IsPlain()
    {
        Render(ProjectStatusText.Cell(9)).Should().Be("9");
        Render(AccountStatusText.Cell(9)).Should().Be("9");
    }
}
