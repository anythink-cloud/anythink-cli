using AnythinkMcp.Tools;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkMcp.Tests;

[Collection("SequentialConfig")]
public class CliToolTests : McpTestBase
{
    [Fact]
    public async Task NamedProfileThatCantBeUsed_IsAnError_NotTheDefaultProfile()
    {
        SetupProjectProfile(name: "default-project");
        var mock = new MockHttpMessageHandler();

        var output = await new CliTool(CreateFactory(mock, profile: "missing")).RunCli("entities list");

        output.Should().StartWith("Error:").And.Contain("missing");
        mock.GetMatchCount(mock.When("*")).Should().Be(0);
    }
}
