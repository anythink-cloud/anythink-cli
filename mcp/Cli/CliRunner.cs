using AnythinkCli;
using AnythinkCli.Client;
using Spectre.Console.Cli;

namespace AnythinkMcp.Cli;

public sealed record CliRunResult(int ExitCode, string Output);

public static class CliRunner
{
    public const int MaxOutputCharacters = 200_000;

    static CliRunner() => AmbientConsole.Install();

    public static async Task<CliRunResult> RunAsync(
        IReadOnlyList<string> args, AnythinkClient? client, CliToolScope scope, CancellationToken cancellationToken = default, int maxOutputCharacters = MaxOutputCharacters)
    {
        using var output = new StringWriter();
        AmbientConsole.Use(output);
        ClientContext.Current = client;
        ClientContext.Cancellation = cancellationToken;
        ClientContext.Remote = CliToolPolicy.IsRemote(scope);
        ClientContext.MachineOutput = true;

        var app = new CommandApp();
        app.Configure(config =>
        {
            CliApp.Configure(config);
            config.Settings.Console = AmbientConsole.Instance;
            config.SetExceptionHandler((ex, _) =>
            {
                var message = CliToolPolicy.IsRemote(scope) && ex is AnythinkException api ? api.StatusOnlyMessage : ex.Message;
                output.WriteLine($"Error: {message}");
                return 1;
            });
        });

        var exitCode = await app.RunAsync(args);
        return cancellationToken.IsCancellationRequested
            ? new CliRunResult(1, "Cancelled.")
            : new CliRunResult(exitCode, Capped(output.ToString().Trim(), maxOutputCharacters));
    }

    private static string Capped(string text, int limit)
    {
        if (text.Length <= limit)
            return text;

        var length = char.IsHighSurrogate(text[limit - 1]) ? limit - 1 : limit;
        return text[..length]
            + $"\n[Output cut off at {limit:N0} characters. Narrow the query (a filter, a smaller limit, fewer fields) to see the rest.]";
    }
}
