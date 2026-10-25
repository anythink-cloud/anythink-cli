using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Spectre.Console;

namespace AnythinkCli.Output;

public static class JsonOutput
{
    // Console output isn't HTML, so quotes, angle brackets and accented text stay readable instead of becoming \uXXXX.
    public static readonly JsonSerializerOptions PrettyRelaxed = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void Write(JsonNode? node) => Write(node is null ? "null" : node.ToJsonString(PrettyRelaxed));

    // Not through Spectre: its JSON view wraps long strings at the console width, which breaks the document.
    public static void Write(string json) => Console.Out.WriteLine(json);

    public static async Task<T> WithStatusAsync<T>(bool json, string text, Func<Task<T>> work)
    {
        if (json) return await work();
        return await AnsiConsole.Status().Spinner(Spinner.Known.Dots).StartAsync(Renderer.Status(text), async _ => await work());
    }

    // Scripts pipe stdout into a parser, so with --json every spinner, status line, warning and error goes to stderr.
    public static void SendConsoleToStderrFor(string[] args)
    {
        if (!args.Contains("--json")) return;
        AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(Console.Error) });
    }
}
