using AnythinkMcp.Tools;
using FluentAssertions;

namespace AnythinkMcp.Tests;

// Rule: the generic cli tool never runs commands that change billing state; the dedicated confirm-gated tools are the route.
public class CliToolRefusalTests
{
    [Theory]
    [InlineData("pay subscriptions delete 3f2504e0-4f89-41d3-9a0c-0305e82c3301 --yes")]
    [InlineData("pay subscriptions force-expire abc --yes")]
    [InlineData("pay subscriptions relink abc --to-user-id 4 --yes")]
    [InlineData("pay subscriptions resync abc")]
    [InlineData("pay subscriptions cancel abc --yes")]
    [InlineData("pay plans delete 12 --yes")]
    [InlineData("pay offers delete abc --yes")]
    [InlineData("pay offers pause abc --yes")]
    [InlineData("pay offers update abc --status paused --yes")]
    [InlineData("pay offers update abc --status=expired")]
    [InlineData("pay apple credentials set --issuer-id x")]
    [InlineData("pay apple verify --signed-transaction jws")]
    public void MoneyAffectingPayCommands_AreRefused(string command)
        => CliTool.RefusalFor(command).Should().Contain("billing state");

    [Theory]
    [InlineData("pay subscriptions list --status active")]
    [InlineData("pay plans list")]
    [InlineData("pay offers update abc --status active")]
    [InlineData("pay offers update abc --name X")]
    [InlineData("entities list")]
    public void ReadOnlyAndNonBillingCommands_AreNotRefused(string command)
        => CliTool.RefusalFor(command).Should().BeNull();

    [Fact]
    public async Task RunCli_RefusesPayDeleteBeforeAnythingRuns()
    {
        var tool = new CliTool(new McpClientFactory(null));

        var result = await tool.RunCli("pay subscriptions delete abc --yes");

        result.Should().StartWith("Refused");
    }

    [Fact]
    public void CliToolDescription_NoLongerTellsAgentsToAddYes()
    {
        var description = typeof(CliTool).GetMethod(nameof(CliTool.RunCli))!
            .GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
            .Cast<System.ComponentModel.DescriptionAttribute>().Single().Description;

        description.Should().NotContain("add '--yes' to skip");
        description.Should().Contain("must be run by a person");
    }
}
