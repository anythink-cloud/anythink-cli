using System.Reflection;
using AnythinkMcp.Cli;
using FluentAssertions;
using ModelContextProtocol.Server;

namespace AnythinkMcp.Tests;

public class StdioToolRegistrationTests
{
    private static readonly string[] HandWrittenTools =
    [
        "signup",
        "login",
        "login_google",
        "login_direct",
        "logout",
        "config_show",
        "config_use",
        "config_remove",
        "accounts_list",
        "accounts_create",
        "accounts_use",
        "projects_list",
        "projects_create",
        "projects_use",
        "projects_delete",
        "cli",
    ];

    internal static IEnumerable<string> ExpectedStdioTools =>
        HandWrittenTools.Concat(CliCommandTool.All(CliToolScope.Local).Select(t => t.ProtocolTool.Name));

    [Fact]
    public void HandWrittenTools_AreUnchanged()
    {
        var names = typeof(McpClientFactory).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
            .SelectMany(t => t.GetMethods())
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .OfType<string>();

        names.Should().BeEquivalentTo(HandWrittenTools);
    }

    [Fact]
    public void GeneratedTools_DoNotCollideWithHandWrittenOnes() =>
        CliCommandTool.All(CliToolScope.Local).Select(t => t.ProtocolTool.Name).Should().NotIntersectWith(HandWrittenTools);
}
