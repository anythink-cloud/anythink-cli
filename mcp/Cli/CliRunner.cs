using AnythinkCli;
using AnythinkCli.Client;
using Spectre.Console.Cli;

namespace AnythinkMcp.Cli;

public sealed record CliRunResult(int ExitCode, string Output);

public static class CliRunner
{
    static CliRunner() => AmbientConsole.Install();

    public static async Task<CliRunResult> RunAsync(IReadOnlyList<string> args, AnythinkClient? client)
    {
        using var output = new StringWriter();
        AmbientConsole.Use(output);
        ClientContext.Current = client;

        var app = new CommandApp();
        app.Configure(config =>
        {
            CliApp.Configure(config);
            config.Settings.Console = AmbientConsole.Instance;
            config.SetExceptionHandler((ex, _) =>
            {
                output.WriteLine($"Error: {ex.Message}");
                return 1;
            });
        });

        var exitCode = await app.RunAsync(args);
        return new CliRunResult(exitCode, output.ToString().Trim());
    }
}
