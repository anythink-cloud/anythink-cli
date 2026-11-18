using System.Net;
using System.Text;
using AnythinkCli.Client;
using RichardSzalay.MockHttp;
using Spectre.Console;
using Spectre.Console.Rendering;

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

internal sealed class EnterKeyConsole(IAnsiConsole inner) : IAnsiConsole, IAnsiConsoleInput
{
    public Profile Profile => inner.Profile;
    public IAnsiConsoleCursor Cursor => inner.Cursor;
    public IAnsiConsoleInput Input => this;
    public IExclusivityMode ExclusivityMode => inner.ExclusivityMode;
    public RenderPipeline Pipeline => inner.Pipeline;
    public void Clear(bool home) => inner.Clear(home);
    public void Write(IRenderable renderable) => inner.Write(renderable);
    public bool IsKeyAvailable() => true;
    public ConsoleKeyInfo? ReadKey(bool intercept) => new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false);
    public Task<ConsoleKeyInfo?> ReadKeyAsync(bool intercept, CancellationToken cancellationToken) => Task.FromResult(ReadKey(intercept));
}

internal static class CommandRunner
{
    public const string Org = "https://api.example.com/org/99999";

    public static AnythinkClient ClientFor(MockHttpMessageHandler mock) =>
        new("99999", "https://api.example.com", new HttpClient(mock));

    public static async Task<(int Code, string Output)> RunAsync(MockHttpMessageHandler mock, Func<Task<int>> command, bool interactive = false)
    {
        var writer = new StringWriter();
        var originalOut = Console.Out;
        var originalConsole = AnsiConsole.Console;
        // Spectre re-enables ANSI under GITHUB_ACTIONS, which puts escape codes in the captured output
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = interactive ? AnsiSupport.Yes : AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = interactive ? InteractionSupport.Yes : InteractionSupport.No,
            Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
            Out = new AnsiConsoleOutput(writer),
        });
        console.Profile.Width = 240;

        Console.SetOut(writer);
        AnsiConsole.Console = interactive ? new EnterKeyConsole(console) : console;
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
