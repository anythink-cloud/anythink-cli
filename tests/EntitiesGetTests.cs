using AnythinkCli.Commands;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkCli.Tests;

[Collection("ConsoleOutput")]
public class EntitiesGetTests
{
    [Theory]
    [InlineData(true, "Team reads: yes")]
    [InlineData(false, "Team reads: no")]
    public async Task EntitiesGet_ShowsWhetherTeamReadsIsOn(bool on, string expected)
    {
        var mock = new MockHttpMessageHandler();
        mock.ReplyWith(HttpMethod.Get, $"{CommandRunner.Org}/entities/posts",
            $$"""{"name":"posts","table_name":"posts","allow_group_scope":{{(on ? "true" : "false")}},"fields":[]}""");

        var (code, output) = await CommandRunner.RunAsync(mock,
            () => new EntitiesGetCommand().ExecuteAsync(null!, new EntityGetSettings { Name = "posts" }));

        code.Should().Be(0);
        output.Should().Contain(expected);
    }
}
