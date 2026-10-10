using System.Reflection;
using AnythinkMcp.Cli;
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

    [Fact]
    public void DestructiveCommands_AskForConfirmation() =>
        Tool("entities_delete").ProtocolTool.Description.Should().Contain("check with the user before running it");

    [Fact]
    public void ReadOnlyCommands_DoNotAskForConfirmation() =>
        Tool("entities_list").ProtocolTool.Description.Should().NotContain("check with the user");

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
