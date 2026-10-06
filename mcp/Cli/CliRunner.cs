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
        IReadOnlyList<string> args, AnythinkClient? client, CliToolScope scope, CancellationToken cancellationToken = default,
        BillingClient? billing = null)
    {
        using var output = new StringWriter();
        AmbientConsole.Use(output);
        ClientContext.Current = client;
        ClientContext.Billing = billing;
        ClientContext.Cancellation = cancellationToken;
        ClientContext.Remote = CliToolPolicy.IsRemote(scope);

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
            : new CliRunResult(exitCode, Capped(output.ToString().Trim()));
    }

    private static string Capped(string text)
    {
        if (text.Length <= MaxOutputCharacters)
            return text;

        var length = char.IsHighSurrogate(text[MaxOutputCharacters - 1]) ? MaxOutputCharacters - 1 : MaxOutputCharacters;
        return text[..length]
            + $"\n[Output cut off at {MaxOutputCharacters:N0} characters. Narrow the query (a filter, a smaller limit, fewer fields) to see the rest.]";
    }
}
