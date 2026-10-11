using FluentAssertions;
using RichardSzalay.MockHttp;
using Spectre.Console;

namespace AnythinkCli.Tests;

[Collection("ConsoleOutput")]
public class CommandRunnerTests
{
    // ── Rule: captured command output is plain text whatever CI the tests run in ──

    [Fact]
    public async Task CapturedOutput_HasNoAnsiEscapes_UnderGithubActions()
    {
        var previous = Environment.GetEnvironmentVariable("GITHUB_ACTIONS");
        Environment.SetEnvironmentVariable("GITHUB_ACTIONS", "true");
        try
        {
            var (_, output) = await CommandRunner.RunAsync(new MockHttpMessageHandler(), () =>
            {
                AnsiConsole.MarkupLine("[bold]~[/] entity [dim]articles[/]");
                return Task.FromResult(0);
            });

            output.Should().NotContain("\u001b").And.StartWith("~ entity articles");
        }
        finally { Environment.SetEnvironmentVariable("GITHUB_ACTIONS", previous); }
    }
}
