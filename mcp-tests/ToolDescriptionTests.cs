using System.Reflection;
using System.Text.RegularExpressions;
using AnythinkMcp.Cli;
using AnythinkMcp.Tools;
using FluentAssertions;
using ModelContextProtocol.Server;

namespace AnythinkMcp.Tests;

public class ToolDescriptionTests
{
    private const int MinimumDescriptionLength = 100;

    private static CliCommandTool Tool(string name, CliToolScope scope = CliToolScope.Local) =>
        CliCommandTool.All(scope).Single(t => t.ProtocolTool.Name == name);

    private static string ParameterDescription(CliCommandTool tool, string parameter) =>
        tool.ProtocolTool.InputSchema.GetProperty("properties").GetProperty(parameter).GetProperty("description").GetString()!;

    [Theory]
    [InlineData(CliToolScope.Local)]
    [InlineData(CliToolScope.Hosted)]
    public void EveryGeneratedTool_SaysWhenToUseIt_NotJustWhatItIs(CliToolScope scope)
    {
        var thin = CliCommandModel.All
            .Where(c => CliToolPolicy.Includes(c, scope))
            .Select(c => new CliCommandTool(c, scope))
            .Where(t => (t.ProtocolTool.Description?.Length ?? 0) < MinimumDescriptionLength)
            .Select(t => $"{t.ProtocolTool.Name}: {t.ProtocolTool.Description}")
            .ToList();

        thin.Should().BeEmpty("every tool needs a [CommandGuide] saying when to use it, what it returns and what it changes");
    }

    private static IEnumerable<string> HandWrittenToolNames(IEnumerable<Type> types) =>
        types.SelectMany(t => t.GetMethods()).Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name).OfType<string>();

    private static IEnumerable<string> OfferedTools(CliToolScope scope) => scope switch
    {
        CliToolScope.Local => StdioToolRegistrationTests.ExpectedStdioTools,
        CliToolScope.Hosted => CliCommandTool.All(scope).Where(t => !t.NeedsAccountAccess).Select(t => t.ProtocolTool.Name)
            .Concat(HandWrittenToolNames([typeof(HostedTools)])),
        _ => CliCommandTool.All(scope).Select(t => t.ProtocolTool.Name)
    };

    [Theory]
    [InlineData(CliToolScope.Local)]
    [InlineData(CliToolScope.Internal)]
    [InlineData(CliToolScope.Hosted)]
    public void EveryToolNamedInADescription_IsOfferedInThatScope(CliToolScope scope)
    {
        var anyTool = CliCommandModel.All.Select(c => c.ToolName)
            .Concat(HandWrittenToolNames(typeof(McpClientFactory).Assembly.GetTypes())).ToHashSet();
        var offered = OfferedTools(scope).ToHashSet();
        var references = CliCommandTool.All(scope)
            .SelectMany(t => Regex.Matches(t.ProtocolTool.Description + t.ProtocolTool.InputSchema.GetRawText(), "`([a-z_]+)`")
                .Select(m => m.Groups[1].Value)
                .Where(anyTool.Contains)
                .Select(name => (Tool: t.ProtocolTool.Name, Named: name)))
            .ToList();

        references.Should().NotBeEmpty();
        references.Where(r => !offered.Contains(r.Named)).Select(r => $"{r.Tool} names {r.Named}")
            .Should().BeEmpty($"the {scope} server doesn't offer the tools these descriptions name");
    }

    [Fact]
    public void EveryGeneratedCommand_HasAGuide() =>
        CliCommandModel.All
            .Where(c => CliToolPolicy.Includes(c, CliToolScope.Local) && c.Guide.Length == 0)
            .Select(c => c.Key)
            .Should().BeEmpty("add a [CommandGuide] to the command class");

    [Fact]
    public void EveryHandWrittenTool_SetsATitleAndAnExplicitHint()
    {
        var missing = typeof(McpClientFactory).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods())
            .SelectMany(m => m.GetCustomAttributesData().Where(a => a.AttributeType == typeof(McpServerToolAttribute)))
            .Where(a =>
            {
                var named = a.NamedArguments.Select(n => n.MemberName).ToHashSet();
                return !named.Contains("Title") || !(named.Contains("ReadOnly") || named.Contains("Destructive"));
            })
            .Select(a => a.NamedArguments.First(n => n.MemberName == "Name").TypedValue.Value)
            .ToList();

        missing.Should().BeEmpty();
    }

    [Fact]
    public void JsonCommands_SayTheyReturnJson() =>
        Tool("data_list").ProtocolTool.Description.Should().Contain("Returns JSON.");

    [Theory]
    [InlineData(CliToolScope.Local)]
    [InlineData(CliToolScope.Hosted)]
    public void DestructiveCommands_SayItThroughTheHint_NotAnInstructionInTheDescription(CliToolScope scope)
    {
        var tool = Tool("entities_delete", scope).ProtocolTool;

        tool.Annotations!.DestructiveHint.Should().BeTrue();
        tool.Description.Should().NotContain("check with the user");
    }

    [Theory]
    [InlineData("users_get", "id", "users_list")]
    [InlineData("roles_permissions_add", "role_id", "roles_list")]
    [InlineData("workflows_step_add", "workflow_id", "workflows_list")]
    public void IdParameters_SayWhichToolListsThem(string tool, string parameter, string source) =>
        ParameterDescription(Tool(tool), parameter).Should().EndWith($"Find it with {source}.");

    [Fact]
    public void ReferralCodeLookup_IsNotReadOnly_BecauseItCreatesACodeWhenMissing()
    {
        var annotations = Tool("pay_offers_user_code").ProtocolTool.Annotations!;

        annotations.ReadOnlyHint.Should().BeFalse();
        annotations.DestructiveHint.Should().BeFalse();
    }
}
