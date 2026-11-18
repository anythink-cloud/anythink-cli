using System.Text.Json;
using AnythinkMcp.Cli;
using AnythinkMcp.Ui;
using FluentAssertions;

namespace AnythinkMcp.Tests;

public class McpUiRoutesTests
{
    private static readonly Dictionary<string, CliCommandTool> Hosted =
        CliCommandTool.All(CliToolScope.Hosted).ToDictionary(t => t.ProtocolTool.Name);

    private static Dictionary<string, JsonElement> Args(params (string Name, string Value)[] args) =>
        args.ToDictionary(a => a.Name, a => JsonDocument.Parse(JsonSerializer.Serialize(a.Value)).RootElement.Clone());

    private static string PathFor(string tool, params (string, string)[] args)
    {
        McpUiRoutes.TryGet(tool, out var route).Should().BeTrue();
        return McpUiRoutes.Path(route, Args(args));
    }

    [Fact]
    public void EveryRoute_IsAHostedTool() =>
        McpUiRoutes.ToolNames.Should().OnlyContain(name => Hosted.ContainsKey(name));

    [Fact]
    public void DeleteCancelAndRemoveTools_HaveNoRoute() =>
        McpUiRoutes.ToolNames.Should().NotContain(name => System.Text.RegularExpressions.Regex.IsMatch(name, "_delete|_cancel|_remove|_disconnect|_purge|_revoke"));

    [Fact]
    public void EveryPlaceholder_IsAParameterOfItsTool()
    {
        foreach (var name in McpUiRoutes.ToolNames)
        {
            McpUiRoutes.TryGet(name, out var route);
            var properties = Hosted[name].ProtocolTool.InputSchema.GetProperty("properties");
            foreach (var placeholder in McpUiRoutes.Placeholders(route.Template))
                properties.TryGetProperty(placeholder, out _).Should().BeTrue($"{name} takes {placeholder}");
        }
    }

    [Fact]
    public void Path_EscapesArgumentValues() =>
        PathFor("data_get", ("entity", "a/../b?x#y"), ("id", "7")).Should().Be("/entities/a%2F..%2Fb%3Fx%23y/7");

    [Fact]
    public void Path_MissingPlaceholder_FallsBackToSection()
    {
        PathFor("data_get", ("entity", "posts")).Should().Be("/entities/posts");
        PathFor("data_get").Should().Be("/entities");
        PathFor("projects_create").Should().Be("/accounts");
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("%2e%2e")]
    [InlineData("%2E.")]
    public void Path_DotSegmentValue_StaysInSection(string dots)
    {
        var inEntity = PathFor("data_get", ("entity", dots), ("id", "7"));
        var inId = PathFor("data_get", ("entity", "posts"), ("id", dots));

        inEntity.Should().Be("/entities");
        inId.Should().Be("/entities/posts");
        foreach (var path in new[] { inEntity, inId })
            new Uri(new Uri("https://dash.test/org/42"), "/org/42" + path).AbsolutePath.Should().StartWith("/org/42/entities");
    }

    [Fact]
    public void Path_EmptyArgument_FallsBackToSection()
    {
        PathFor("data_get", ("entity", "posts"), ("id", "")).Should().Be("/entities/posts");
        PathFor("data_get", ("entity", "posts"), ("id", "  ")).Should().Be("/entities/posts");
    }
}
