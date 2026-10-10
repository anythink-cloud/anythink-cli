using AnythinkMcp.Cli;
using FluentAssertions;

namespace AnythinkMcp.Tests;

public class CliRunnerOutputCapTests
{
    private static readonly string[] XmlDoc = ["cli", "xmldoc"];

    [Fact]
    public async Task ToolOutput_IsStillCappedForToolCalls()
    {
        var result = await CliRunner.RunAsync(XmlDoc, client: null, CliToolScope.Local, maxOutputCharacters: 1_000);

        result.Output.Should().Contain("[Output cut off at 1,000 characters.");
        CliRunner.MaxOutputCharacters.Should().Be(200_000);
    }

    [Fact]
    public async Task CommandModel_LargerThanTheToolOutputCap_IsReadInFull()
    {
        var capped = await CliRunner.RunAsync(XmlDoc, client: null, CliToolScope.Local, maxOutputCharacters: 1_000);
        var full = await CliRunner.RunAsync(XmlDoc, client: null, CliToolScope.Local, maxOutputCharacters: int.MaxValue);

        full.Output.Length.Should().BeGreaterThan(capped.Output.Length);
        full.Output.Should().NotContain("[Output cut off");
        var commands = CliCommandModel.Parse(full.Output);
        commands.Should().NotBeEmpty();
        CliCommandModel.All.Count.Should().Be(commands.Count);
    }
}
