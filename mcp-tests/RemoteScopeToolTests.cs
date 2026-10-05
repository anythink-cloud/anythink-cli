using System.Text.Json;
using AnythinkCli.Client;
using AnythinkMcp.Cli;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RichardSzalay.MockHttp;

namespace AnythinkMcp.Tests;

[Collection("SequentialConfig")]
public class RemoteScopeToolTests : McpTestBase
{
    private static CliCommandTool Tool(string name, CliToolScope scope) =>
        CliCommandTool.All(scope).Single(t => t.ProtocolTool.Name == name);

    private static readonly Dictionary<string, JsonElement> NoArguments = [];

    // ── Rule: remote callers never run as the server's own CLI login ──────────

    [Fact]
    public async Task RemoteRun_WithoutACallerClient_DoesNotUseTheServersSavedLogin()
    {
        SetupProjectProfile();

        var result = await Tool("entities_list", CliToolScope.Remote).RunAsync(NoArguments, client: null);

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("aren't available to remote callers");
    }

    [Fact]
    public async Task LocalRun_WithoutAClient_StillReportsMissingCredentials()
    {
        var result = await Tool("entities_list", CliToolScope.Local).RunAsync(NoArguments, client: null);

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("No credentials");
    }

    [Fact]
    public async Task InternalRest_WithoutRequestCredentials_DoesNotFallBackToTheSavedLogin()
    {
        SetupProjectProfile();
        var mock = new MockHttpMessageHandler();
        var any = mock.When("*").Respond("application/json", "[]");
        var services = new ServiceCollection().AddSingleton(CreateFactory(mock)).BuildServiceProvider();

        var act = () => McpToolRegistry.ExecuteToolAsync("entities_list", JsonSerializer.SerializeToElement(new { }), services);

        await act.Should().ThrowAsync<InvalidOperationException>();
        mock.GetMatchCount(any).Should().Be(0);
    }

    // ── Rule: a remote caller can't mint credentials ───────────────────────────

    [Fact]
    public void ApiKeyCreation_IsLocalOnly()
    {
        CliCommandTool.All(CliToolScope.Local).Select(t => t.ProtocolTool.Name).Should().Contain("api_keys_create");
        CliCommandTool.All(CliToolScope.Remote).Select(t => t.ProtocolTool.Name).Should().NotContain("api_keys_create");
    }

    [Fact]
    public void RemoteToolDescriptions_DoNotMentionOptionsThatAreHiddenRemotely()
    {
        var hidden = CliToolPolicy.HiddenRemotely.Select(entry => entry[(entry.LastIndexOf(' ') + 1)..]).Distinct().ToList();

        foreach (var tool in CliCommandTool.All(CliToolScope.Remote))
        {
            var text = tool.ProtocolTool.Description + " " + tool.ProtocolTool.InputSchema.GetRawText();
            foreach (var option in hidden)
                text.Should().NotContain(option, $"{tool.ProtocolTool.Name} must not advertise {option}");
        }
    }
}
