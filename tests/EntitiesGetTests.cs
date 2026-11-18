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

    [Theory]
    [InlineData("\"team_group_id\"", "Team field: team_group_id")]
    [InlineData("null", "Team field: none")]
    public async Task EntitiesGet_ShowsTheTeamField(string field, string expected)
    {
        var mock = new MockHttpMessageHandler();
        mock.ReplyWith(HttpMethod.Get, $"{CommandRunner.Org}/entities/posts",
            $$"""{"name":"posts","table_name":"posts","group_scope_field":{{field}},"fields":[]}""");

        var (code, output) = await CommandRunner.RunAsync(mock,
            () => new EntitiesGetCommand().ExecuteAsync(null!, new EntityGetSettings { Name = "posts" }));

        code.Should().Be(0);
        output.Should().Contain(expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Docs_ListTheTeamFieldOption(bool json)
    {
        var (code, output) = await CommandRunner.RunAsync(new MockHttpMessageHandler(),
            () => Task.FromResult(new DocsCommand().Execute(null!, new DocsSettings { Json = json })));

        code.Should().Be(0);
        output.Should().Contain("--team-field");
    }
}
