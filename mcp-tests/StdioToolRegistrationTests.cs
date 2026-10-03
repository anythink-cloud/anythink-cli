using FluentAssertions;

namespace AnythinkMcp.Tests;

public class StdioToolRegistrationTests
{
    private static readonly string[] ExpectedStdioTools =
    [
        "signup", "login", "login_google", "login_direct", "logout",
        "config_show", "config_use", "config_remove",
        "accounts_list", "accounts_create", "accounts_use",
        "projects_list", "projects_create", "projects_use", "projects_delete",
        "cli",
    ];

    [Fact]
    public void StdioToolSet_IsUnchanged()
    {
        var names = McpToolRegistry.GetToolDefinitions()
            .Select(t => (string)t.GetType().GetProperty("name")!.GetValue(t)!);

        names.Should().BeEquivalentTo(ExpectedStdioTools);
    }

}
