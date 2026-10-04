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
    [InlineData("pay apple credentials set --issuer-id x")]
    [InlineData("pay apple verify --signed-transaction jws")]
    public void MoneyAffectingPayCommands_AreRefusedInStdioAndHttpMode(string command)
    {
        CliTool.RefusalFor(command, httpMode: false).Should().Contain("anythinkpay_");
        CliTool.RefusalFor(command, httpMode: true).Should().Contain("anythinkpay_");
    }

    [Theory]
    [InlineData("pay subscriptions list --status active")]
    [InlineData("pay plans list")]
    [InlineData("entities list")]
    public void ReadOnlyAndNonBillingCommands_AreNotRefused(string command)
    {
        CliTool.RefusalFor(command, httpMode: false).Should().BeNull();
        CliTool.RefusalFor(command, httpMode: true).Should().BeNull();
    }

    [Theory]
    [InlineData("fetch POST /integrations/anythinkpay/subscriptions/abc/admin/resync")]
    [InlineData("fetch /integrations/anythinkpay/subscriptions/abc --method DELETE")]
    public void FetchWritesToPayPaths_AreRefusedInHttpModeOnly(string command)
    {
        CliTool.RefusalFor(command, httpMode: true).Should().NotBeNull();
        CliTool.RefusalFor(command, httpMode: false).Should().BeNull();
    }

    [Theory]
    [InlineData("fetch /integrations/anythinkpay/subscriptions")]
    [InlineData("fetch POST /entities/posts/items --data {}")]
    public void FetchReadsAndNonPayWrites_AreNotRefusedInHttpMode(string command)
        => CliTool.RefusalFor(command, httpMode: true).Should().BeNull();

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
