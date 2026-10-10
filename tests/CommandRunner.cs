using System.Net;
using System.Text;
using AnythinkCli.Client;
using RichardSzalay.MockHttp;
using Spectre.Console;

namespace AnythinkCli.Tests;

[CollectionDefinition("ConsoleOutput", DisableParallelization = true)]
public class ConsoleOutputCollection { }

internal static class MockHttpExtensions
{
    // MockHttp only counts matches, so the body of each request is kept here for the tests that read it.
    public static MockedRequest ReplyWith(
        this MockHttpMessageHandler mock, HttpMethod method, string url, string json, List<string>? bodies = null) =>
        mock.When(method, url).Respond(async request =>
        {
            bodies?.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        });
}

internal static class CommandRunner
{
    public const string Org = "https://api.example.com/org/99999";

    public static AnythinkClient ClientFor(MockHttpMessageHandler mock) =>
        new("99999", "https://api.example.com", new HttpClient(mock));

    public static async Task<(int Code, string Output)> RunAsync(MockHttpMessageHandler mock, Func<Task<int>> command)
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
        ClientContext.Current = ClientFor(mock);
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
