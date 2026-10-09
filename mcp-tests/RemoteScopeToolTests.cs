using System.Text.Json;
using AnythinkCli.Client;
using AnythinkMcp.Cli;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RichardSzalay.MockHttp;

namespace AnythinkMcp.Tests;

[Collection("SequentialConfig")]
public class RemoteScopeToolTests : McpTestBase
{
    private static CliCommandTool Tool(string name, CliToolScope scope) =>
        CliCommandTool.All(scope).Single(t => t.ProtocolTool.Name == name);

    private static readonly Dictionary<string, JsonElement> NoArguments = [];

    // ── Rule: remote callers never run as the server's own CLI login ──────────

    public static TheoryData<CliToolScope> RemoteScopes => new() { CliToolScope.Internal, CliToolScope.Hosted };

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task RemoteRun_WithoutACallerClient_DoesNotUseTheServersSavedLogin(CliToolScope scope)
    {
        SetupProjectProfile();

        var result = await Tool("entities_list", scope).RunAsync(NoArguments, client: null);

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

    [Fact]
    public async Task ARemoteToolServedWithoutRequestCredentials_DoesNotFallBackToTheSavedLogin()
    {
        SetupProjectProfile();
        var mock = new MockHttpMessageHandler();
        var any = mock.When("*").Respond("application/json", "[]");
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(CreateFactory(mock));
        builder.Services.AddMcpServer()
            .WithHttpTransport(http => http.SessionMode = HttpServerSessionMode.Stateless)
            .WithTools(CliCommandTool.All(CliToolScope.Internal));
        await using var app = builder.Build();
        app.MapMcp("/mcp");
        await app.StartAsync();
        using var http = app.GetTestClient();
        await using var mcp = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri("http://localhost/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        }, http, ownsHttpClient: false));

        var result = await mcp.CallToolAsync("entities_list");

        result.IsError.Should().BeTrue();
        ((TextContentBlock)result.Content[0]).Text.Should().Contain("no credentials");
        mock.GetMatchCount(any).Should().Be(0);
        await app.StopAsync();
    }

    // ── Rule: a remote caller can't mint credentials ───────────────────────────

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public void ApiKeyCreation_IsLocalOnly(CliToolScope scope)
    {
        CliCommandTool.All(CliToolScope.Local).Select(t => t.ProtocolTool.Name).Should().Contain("api_keys_create");
        CliCommandTool.All(scope).Select(t => t.ProtocolTool.Name).Should().NotContain("api_keys_create");
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public void RemoteToolDescriptions_DoNotMentionOptionsThatAreHiddenRemotely(CliToolScope scope)
    {
        var hidden = CliToolPolicy.HiddenRemotely.Select(entry => entry[(entry.LastIndexOf(' ') + 1)..]).Distinct().ToList();

        foreach (var tool in CliCommandTool.All(scope))
        {
            var text = tool.ProtocolTool.Description + " " + tool.ProtocolTool.InputSchema.GetRawText();
            foreach (var option in hidden)
                text.Should().NotContain(option, $"{tool.ProtocolTool.Name} must not advertise {option}");
        }
    }

    // ── Rule: admin recovery and Apple credential commands are never remote tools ──

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public void AdminRecoveryPayCommands_AreNotRemoteTools(CliToolScope scope)
    {
        var remote = CliCommandTool.All(scope).Select(t => t.ProtocolTool.Name).ToList();
        var local = CliCommandTool.All(CliToolScope.Local).Select(t => t.ProtocolTool.Name).ToList();

        string[] recovery =
        [
            "pay_subscriptions_delete", "pay_subscriptions_force_expire",
            "pay_subscriptions_relink", "pay_subscriptions_resync"
        ];
        local.Should().Contain(recovery);
        remote.Should().NotContain(recovery);
    }

    [Theory]
    [InlineData(CliToolScope.Local)]
    [InlineData(CliToolScope.Internal)]
    [InlineData(CliToolScope.Hosted)]
    public void AppleCredentialsAndVerification_AreNeverToolsInAnyScope(CliToolScope scope)
    {
        var names = CliCommandTool.All(scope).Select(t => t.ProtocolTool.Name).ToList();

        names.Should().NotContain(n => n.StartsWith("pay_apple"));
        names.Should().NotContain(n => n.Contains("apple") && (n.Contains("verify") || n.Contains("credentials_set")));
        CliCommandTool.All(scope)
            .Select(t => t.ProtocolTool.InputSchema.GetRawText())
            .Should().NotContain(schema => schema.Contains("private_key"));
    }

    [Theory]
    [InlineData(CliToolScope.Local)]
    [InlineData(CliToolScope.Internal)]
    [InlineData(CliToolScope.Hosted)]
    public void RiskyPayTools_AreAnnotatedAndReadOnlyOnesAreReadOnly(CliToolScope scope)
    {
        var tools = CliCommandTool.All(scope).ToDictionary(t => t.ProtocolTool.Name, t => t.ProtocolTool.Annotations!);

        foreach (var name in new[] { "pay_plans_delete", "pay_offers_delete", "pay_offers_pause", "pay_offers_update", "pay_subscriptions_cancel" })
            tools[name].DestructiveHint.Should().BeTrue(name);
        foreach (var name in new[] { "pay_plans_list", "pay_plans_get", "pay_subscriptions_list", "pay_subscriptions_get", "pay_offers_list", "pay_offers_get", "pay_trial_status" })
            tools[name].ReadOnlyHint.Should().BeTrue(name);
    }

    [Fact]
    public void TheGenericCliTool_IsNotARemoteTool()
    {
        foreach (var scope in new[] { CliToolScope.Internal, CliToolScope.Hosted })
            CliCommandTool.All(scope).Select(t => t.ProtocolTool.Name).Should().NotContain("cli");
    }
}
