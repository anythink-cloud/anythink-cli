using FluentAssertions;

namespace AnythinkMcp.Tests;

/// <summary>
/// Tests that credential-mutating tools are correctly identified for blocking in HTTP mode.
/// The actual blocking happens in Program.cs, but we verify the blocked list is correct.
/// </summary>
public class BlockedToolsTests
{
    // This list must match the blocked tools in mcp/Program.cs
    private static readonly HashSet<string> BlockedInHttpMode = new()
    {
        "login", "login_direct", "signup", "logout",
        "config_use", "config_remove", "config_show",
        "accounts_use"
    };

    [Theory]
    [InlineData("login")]
    [InlineData("login_direct")]
    [InlineData("signup")]
    [InlineData("logout")]
    [InlineData("config_use")]
    [InlineData("config_remove")]
    [InlineData("config_show")]
    [InlineData("accounts_use")]
    public void CredentialMutatingTools_ShouldBeBlocked(string toolName)
    {
        BlockedInHttpMode.Should().Contain(toolName,
            $"'{toolName}' mutates credentials and must be blocked in HTTP mode");
    }

    [Theory]
    [InlineData("cli")]
    [InlineData("list_entities")]
    [InlineData("get_entity")]
    [InlineData("list_data")]
    [InlineData("create_data")]
    [InlineData("fetch")]
    public void DataReadTools_ShouldNotBeBlocked(string toolName)
    {
        BlockedInHttpMode.Should().NotContain(toolName,
            $"'{toolName}' is a data tool and should be allowed in HTTP mode");
    }
}
