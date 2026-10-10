using System.Net;
using System.Text;
using System.Text.Json;
using AnythinkCli.Client;
using AnythinkCli.Config;
using FluentAssertions;
using RichardSzalay.MockHttp;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AnythinkCli.Tests;

[Collection("SequentialConfig")]
public class DocsGuidesTests : IDisposable
{
    private const string Docs = "https://docs.anythink.cloud";
    private const string WorkflowsPage = "/guides/workflows/how-workflows-work";
    private const string WorkflowsMarkdown = "# How workflows work\n\nWorkflows run your backend logic.";

    private const string SearchJson = """
        {"query":"workflows","results":[
          {"title":"How workflows work","url":"/building_my_app/automation/workflows","excerpt":"Workflows run your backend logic in Anythink.","type":"page"},
          {"title":"Test workflows","url":"/building_my_app/automation/workflow-monitoring","excerpt":"Run a test of any workflow.","type":"page"}
        ],"total":2}
        """;

    private const string LlmsTxt = $"""
        # Anythink Docs

        ## Workflows

        - [How workflows work]({Docs}{WorkflowsPage}): The model and what happens at runtime.
        - [Build your first app]({Docs}/get-started/first-app): A small orders backend.
        """;

    private sealed class CountingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return base.SendAsync(request, cancellationToken);
        }
    }

    private readonly MockHttpMessageHandler _mock = new();
    private readonly CountingHandler _counter;
    private readonly DocsSiteClient _client;
    private readonly string _configDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public DocsGuidesTests()
    {
        Directory.CreateDirectory(_configDir);
        ConfigService.ConfigDirOverride = _configDir;
        _counter = new CountingHandler(_mock);
        _client = new DocsSiteClient(new HttpClient(_counter));
    }

    public void Dispose()
    {
        ProfileContext.Current = null;
        ConfigService.ConfigDirOverride = null;
        if (Directory.Exists(_configDir)) Directory.Delete(_configDir, recursive: true);
    }

    private static HttpResponseMessage Markdown(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/markdown") };

    private static HttpResponseMessage Redirect(HttpStatusCode status, string location) =>
        new(status) { Headers = { Location = new Uri(location, UriKind.RelativeOrAbsolute) } };

    private void ServeWorkflowsPage() =>
        _mock.When($"{Docs}{WorkflowsPage}.md").Respond(_ => Markdown(WorkflowsMarkdown));

    private async Task<(int Code, string Output)> RunCli(params string[] args)
    {
        DocsSiteClient.Current = _client;
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
        try
        {
            var app = new CommandApp();
            app.Configure(config =>
            {
                CliApp.Configure(config);
                config.Settings.Console = console;
            });
            var code = await app.RunAsync(args);
            return (code, writer.ToString());
        }
        finally
        {
            AnsiConsole.Console = originalConsole;
            Console.SetOut(originalOut);
        }
    }

    // ── Rule: search returns each guide's title, path, full URL and a snippet ─────

    [Fact]
    public async Task DocsSearch_ReturnsTitlePathAndSnippet()
    {
        _mock.Expect($"{Docs}/api/search").WithQueryString(new Dictionary<string, string> { ["q"] = "workflows", ["maxResults"] = "5" })
            .Respond("application/json", SearchJson);

        var (code, output) = await RunCli("docs", "search", "workflows", "--limit", "5", "--json");

        code.Should().Be(0);
        _mock.VerifyNoOutstandingExpectation();
        var json = JsonSerializer.Deserialize<JsonElement>(output);
        var first = json.GetProperty("results")[0];
        first.GetProperty("title").GetString().Should().Be("How workflows work");
        first.GetProperty("path").GetString().Should().Be("/building_my_app/automation/workflows");
        first.GetProperty("url").GetString().Should().Be($"{Docs}/building_my_app/automation/workflows");
        first.GetProperty("snippet").GetString().Should().Be("Workflows run your backend logic in Anythink.");
        json.GetProperty("total").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task DocsSearch_PrintsUnwrappedUrlsForPeople()
    {
        _mock.When($"{Docs}/api/search").Respond("application/json", SearchJson);

        var (code, output) = await RunCli("docs", "search", "workflows");

        code.Should().Be(0);
        output.Should().Contain("How workflows work").And.Contain($"  {Docs}/building_my_app/automation/workflows\n")
            .And.Contain("Workflows run your backend logic in Anythink.");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("51")]
    public async Task DocsSearch_LimitOutsideOneToFifty_IsRefusedBeforeAnyRequest(string limit)
    {
        var (code, _) = await RunCli("docs", "search", "workflows", "--limit", limit);

        code.Should().NotBe(0);
        _counter.Requests.Should().Be(0);
    }

    // ── Rule: the guide commands never need a signed-in profile ───────────────────

    [Fact]
    public async Task DocsSearch_WorksWithoutLogin()
    {
        ProfileContext.Current = "no-such-profile";
        _mock.When($"{Docs}/api/search").Respond("application/json", SearchJson);

        var (code, output) = await RunCli("docs", "search", "workflows", "--json");

        code.Should().Be(0, output);
        output.Should().Contain("How workflows work");
    }

    [Fact]
    public async Task DocsRead_WorksWithoutLogin()
    {
        ProfileContext.Current = "no-such-profile";
        ServeWorkflowsPage();

        var (code, output) = await RunCli("docs", "read", WorkflowsPage);

        code.Should().Be(0, output);
        output.Should().Contain("Workflows run your backend logic.");
    }

    // ── Rule: read accepts a path, a slug or a full docs URL for the same guide ───

    [Theory]
    [InlineData(WorkflowsPage)]
    [InlineData("guides/workflows/how-workflows-work")]
    [InlineData("guides/workflows/how-workflows-work.md")]
    [InlineData("https://docs.anythink.cloud/guides/workflows/how-workflows-work")]
    [InlineData("https://docs.anythink.cloud/guides/workflows/how-workflows-work?tab=cli#steps")]
    [InlineData("docs.anythink.cloud/guides/workflows/how-workflows-work")]
    [InlineData("how-workflows-work")]
    public async Task DocsRead_AcceptsSlugOrFullUrl(string input)
    {
        ServeWorkflowsPage();
        _mock.When($"{Docs}/how-workflows-work.md").Respond(HttpStatusCode.NotFound);
        _mock.When($"{Docs}/how-workflows-work").Respond(HttpStatusCode.NotFound);
        _mock.When($"{Docs}/llms.txt").Respond("text/plain", LlmsTxt);

        var (code, output) = await RunCli("docs", "read", input, "--json");

        code.Should().Be(0, output);
        var page = JsonSerializer.Deserialize<JsonElement>(output);
        page.GetProperty("title").GetString().Should().Be("How workflows work");
        page.GetProperty("path").GetString().Should().Be(WorkflowsPage);
        page.GetProperty("url").GetString().Should().Be(Docs + WorkflowsPage);
        page.GetProperty("markdown").GetString().Should().Be(WorkflowsMarkdown);
    }

    [Fact]
    public async Task DocsRead_PrintsTheMarkdownAndItsSource()
    {
        ServeWorkflowsPage();

        var (code, output) = await RunCli("docs", "read", WorkflowsPage);

        code.Should().Be(0);
        output.Should().StartWith(WorkflowsMarkdown).And.Contain($"Source: {Docs}{WorkflowsPage}");
    }

    // ── Rule: a path from search that has moved still reads the current page ─────

    [Fact]
    public async Task DocsRead_PathFromSearch_FollowsTheSiteRedirectsToTheCurrentPage()
    {
        _mock.When($"{Docs}/building_my_app/automation/workflows.md")
            .Respond(_ => Redirect(HttpStatusCode.MovedPermanently, "/building-my-app/automation/workflows.md"));
        _mock.When($"{Docs}/building-my-app/automation/workflows.md").Respond(HttpStatusCode.NotFound);
        _mock.When($"{Docs}/building_my_app/automation/workflows")
            .Respond(_ => Redirect(HttpStatusCode.MovedPermanently, "/building-my-app/automation/workflows"));
        _mock.When($"{Docs}/building-my-app/automation/workflows")
            .Respond(_ => Redirect(HttpStatusCode.PermanentRedirect, $"{Docs}{WorkflowsPage}"));
        _mock.When($"{Docs}{WorkflowsPage}").Respond("text/html", "<html></html>");
        ServeWorkflowsPage();

        var (code, output) = await RunCli("docs", "read", "/building_my_app/automation/workflows", "--json");

        code.Should().Be(0, output);
        JsonSerializer.Deserialize<JsonElement>(output).GetProperty("url").GetString().Should().Be(Docs + WorkflowsPage);
    }

    // ── Rule: only docs.anythink.cloud is ever requested ──────────────────────────

    [Theory]
    [InlineData("https://evil.example.com/guides/workflows")]
    [InlineData("http://docs.anythink.cloud.evil.example.com/x")]
    [InlineData("file:///etc/passwd")]
    [InlineData("../../etc/passwd")]
    public async Task DocsRead_InputOffTheDocsSite_IsRefusedWithoutARequest(string input)
    {
        var (code, output) = await RunCli("docs", "read", input);

        code.Should().Be(1);
        output.Should().NotBeEmpty();
        _counter.Requests.Should().Be(0);
    }

    [Fact]
    public async Task DocsRead_RedirectOffTheDocsSite_IsNotFollowed()
    {
        var elsewhere = _mock.When("https://evil.example.com/*").Respond(_ => Markdown("# Not ours"));
        _mock.When($"{Docs}/guides/moved.md").Respond(_ => Redirect(HttpStatusCode.Found, "https://evil.example.com/page.md"));
        _mock.When($"{Docs}/guides/moved").Respond(_ => Redirect(HttpStatusCode.Found, "https://evil.example.com/page"));

        var (code, _) = await RunCli("docs", "read", "/guides/moved");

        code.Should().Be(1);
        _mock.GetMatchCount(elsewhere).Should().Be(0);
    }

    [Fact]
    public async Task DocsRead_UnknownGuide_FailsWithAPointerToSearch()
    {
        _mock.When("*").Respond(HttpStatusCode.NotFound);

        var (code, output) = await RunCli("docs", "read", "/guides/no-such-page");

        code.Should().Be(1);
        output.Should().Contain("No guide found").And.Contain("docs search");
    }

    // ── Rule: plain `docs` still prints the CLI's own reference ───────────────────

    [Fact]
    public async Task Docs_WithJson_StillPrintsTheCliReferenceAsJson()
    {
        var (code, output) = await RunCli("docs", "--json");

        code.Should().Be(0);
        var spec = JsonSerializer.Deserialize<JsonElement>(output);
        spec.GetProperty("tool").GetString().Should().Be("anythink");
        spec.GetProperty("commands").EnumerateArray().Select(c => c.GetProperty("syntax").GetString())
            .Should().Contain(["anythink docs search QUERY", "anythink docs read SLUG_OR_URL"]);
        _counter.Requests.Should().Be(0);
    }

    [Fact]
    public async Task Docs_WithoutJson_StillPrintsTheMarkdownReference()
    {
        var (code, output) = await RunCli("docs");

        code.Should().Be(0);
        output.Should().StartWith("# Anythink CLI — Reference").And.Contain("anythink docs search QUERY");
    }
}
