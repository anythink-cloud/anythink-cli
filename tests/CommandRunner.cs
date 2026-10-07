using System.Net;
using System.Text;
using AnythinkCli.Client;
using Spectre.Console;

namespace AnythinkCli.Tests;

[CollectionDefinition("ConsoleOutput", DisableParallelization = true)]
public class ConsoleOutputCollection { }

internal sealed class RecordingHandler(Func<HttpRequestMessage, string, (HttpStatusCode, string)> respond) : HttpMessageHandler
{
    public List<(HttpMethod Method, string Url, string Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.Method, request.RequestUri!.ToString(), body));
        var (status, json) = respond(request, body);
        return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}

internal static class CommandRunner
{
    public const string Org = "https://api.example.com/org/99999";

    public static AnythinkClient ClientFor(RecordingHandler handler) =>
        new("99999", "https://api.example.com", new HttpClient(handler));

    public static async Task<(int Code, string Output)> RunAsync(RecordingHandler handler, Func<Task<int>> command)
    {
        var writer = new StringWriter();
        var originalOut = Console.Out;
        var originalConsole = AnsiConsole.Console;
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer),
        });
        console.Profile.Width = 240;

        Console.SetOut(writer);
        AnsiConsole.Console = console;
        ClientContext.Current = ClientFor(handler);
        try
        {
            var code = await command();
            return (code, writer.ToString());
        }
        finally
        {
            ClientContext.Current = null;
            AnsiConsole.Console = originalConsole;
            Console.SetOut(originalOut);
        }
    }
}
