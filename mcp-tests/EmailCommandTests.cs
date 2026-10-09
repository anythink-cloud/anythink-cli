using System.Net;
using System.Text.Json;
using AnythinkCli.Client;
using AnythinkMcp.Cli;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkMcp.Tests;

public class EmailCommandTests
{
    private const string ApiUrl = "https://api.my.anythink.cloud";
    private const string Org = ApiUrl + "/org/42";

    private static CliCommandTool Tool(string name, CliToolScope scope = CliToolScope.Local) =>
        CliCommandTool.All(scope).Single(t => t.ProtocolTool.Name == name);

    private static Dictionary<string, JsonElement> Args(object values) =>
        JsonSerializer.SerializeToElement(values).EnumerateObject().ToDictionary(p => p.Name, p => p.Value);

    private static AnythinkClient Client(MockHttpMessageHandler mock) => new("42", ApiUrl, new HttpClient(mock));

    private static string Template(string type = "confirmation", string subject = "Hi", string content = "<p>Body</p>") =>
        JsonSerializer.Serialize(new
        {
            id = 1,
            template_type = type,
            subject,
            content,
            display_name = (string?)null,
            description = (string?)null,
            is_active = true,
            is_system = true,
            locked = false,
        });

    private static IEnumerable<string> Properties(CliCommandTool tool) =>
        tool.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name);

    private static MockedRequest Capture(MockHttpMessageHandler mock, HttpMethod method, string url, string response, Action<string> body) =>
        mock.When(method, url).Respond(async req =>
        {
            body(await req.Content!.ReadAsStringAsync());
            return new HttpResponseMessage { Content = new StringContent(response, System.Text.Encoding.UTF8, "application/json") };
        });

    // ── Rule: reading is read-only, overwriting is destructive, in every scope ──

    [Theory]
    [InlineData("email_templates_list", true, null)]
    [InlineData("email_templates_show", true, null)]
    [InlineData("email_preview", true, null)]
    [InlineData("email_shell_show", true, null)]
    [InlineData("email_templates_update", false, true)]
    [InlineData("email_shell_update", false, true)]
    public void EmailTools_AreClassifiedByWhatTheyDo(string name, bool readOnly, bool? destructive)
    {
        foreach (var scope in new[] { CliToolScope.Local, CliToolScope.Internal, CliToolScope.Hosted })
        {
            var annotations = Tool(name, scope).ProtocolTool.Annotations!;

            annotations.ReadOnlyHint.Should().Be(readOnly, $"{name} in {scope}");
            annotations.DestructiveHint.Should().Be(destructive, $"{name} in {scope}");
        }
    }

    // ── Rule: remote callers send the HTML inline; options that read this machine's files are hidden ──

    [Theory]
    [InlineData("email_templates_update", "content", "content_text")]
    [InlineData("email_preview", "content", "content_text")]
    [InlineData("email_preview", "wrapper", "wrapper_text")]
    [InlineData("email_shell_update", "html", "html_text")]
    public void FileOptions_AreHiddenRemotely_AndInlineTextIsOffered(string name, string fileOption, string inlineOption)
    {
        Properties(Tool(name)).Should().Contain([fileOption, inlineOption]);

        foreach (var scope in new[] { CliToolScope.Internal, CliToolScope.Hosted })
            Properties(Tool(name, scope)).Should().NotContain(fileOption).And.Contain(inlineOption);
    }

    [Fact]
    public async Task EmailTemplatesUpdate_RemoteFilePath_IsRefused_AndNothingIsSent()
    {
        var mock = new MockHttpMessageHandler();
        var any = mock.When("*").Respond("application/json", Template());

        var result = await CliRunner.RunAsync(
            ["email", "templates", "update", "confirmation", "--content", "/etc/passwd"], Client(mock), CliToolScope.Internal);

        result.ExitCode.Should().NotBe(0);
        result.Output.Should().Contain("can't be used remotely");
        mock.GetMatchCount(any).Should().Be(0);
    }

    // ── Rule: listing and showing print what the API holds ──────────────────

    [Fact]
    public async Task EmailTemplatesList_ShowsEveryTemplate()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, $"{Org}/email-templates").Respond("application/json",
            $"[{Template("recovery", "Reset")},{Template("confirmation", "Confirm")}]");

        var result = await Tool("email_templates_list").RunAsync(null, Client(mock));

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("confirmation").And.Contain("recovery");
        result.Output.IndexOf("confirmation", StringComparison.Ordinal).Should().BeLessThan(result.Output.IndexOf("recovery", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EmailTemplatesShow_PrintsTheRawHtmlUnwrapped()
    {
        var longLine = "<p>" + new string('x', 500) + "</p>";
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, $"{Org}/email-templates/confirmation").Respond("application/json", Template(content: longLine));

        var result = await Tool("email_templates_show").RunAsync(Args(new { template_type = "confirmation" }), Client(mock));

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain(longLine);
    }

    [Fact]
    public async Task EmailTemplatesShow_Rendered_PrintsThePreview()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, $"{Org}/email-templates/confirmation/preview").Respond("application/json",
            """{"subject":"Welcome","html_content":"<html><body>Hello</body></html>"}""");

        var result = await Tool("email_templates_show").RunAsync(Args(new { template_type = "confirmation", rendered = true }), Client(mock));

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("Welcome").And.Contain("<html><body>Hello</body></html>");
    }

    // ── Rule: text from the project is never read as console markup ─────────

    [Fact]
    public async Task EmailTemplatesList_BracketedSubject_IsPrintedLiterally()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, $"{Org}/email-templates").Respond("application/json",
            $"[{Template("invite[1]", "[red]Welcome[/] [unclosed")}]");

        var result = await Tool("email_templates_list").RunAsync(null, Client(mock));

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("invite[1]").And.Contain("[red]Welcome[/] [unclosed");
    }

    [Fact]
    public async Task EmailTemplatesShow_BracketedSubjectAndContent_ArePrintedLiterally()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, $"{Org}/email-templates/confirmation").Respond("application/json",
            Template(subject: "[bold]Hi[/] [", content: "<p>[/]</p>"));

        var result = await Tool("email_templates_show").RunAsync(Args(new { template_type = "confirmation" }), Client(mock));

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("[bold]Hi[/] [").And.Contain("<p>[/]</p>");
    }

    [Fact]
    public async Task EmailTemplatesUpdate_BracketedTemplateType_IsPrintedLiterally()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, $"{Org}/email-templates/x").Respond("application/json", Template("x[0]"));
        mock.When(HttpMethod.Put, $"{Org}/email-templates/x").Respond("application/json", Template("x[/]"));

        var result = await Tool("email_templates_update").RunAsync(Args(new { template_type = "x", subject = "S" }), Client(mock));

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("x[/]");
    }

    // ── Rule: an update changes only what was asked for ─────────────────────

    [Fact]
    public async Task EmailTemplatesUpdate_ContentOnly_KeepsTheCurrentSubject()
    {
        string? sent = null;
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, $"{Org}/email-templates/confirmation").Respond("application/json", Template(subject: "Keep me"));
        Capture(mock, HttpMethod.Put, $"{Org}/email-templates/confirmation", Template(), b => sent = b);

        var result = await Tool("email_templates_update").RunAsync(
            Args(new { template_type = "confirmation", content_text = "<p>New</p>" }), Client(mock));

        result.ExitCode.Should().Be(0, result.Output);
        using var json = JsonDocument.Parse(sent!);
        json.RootElement.GetProperty("subject").GetString().Should().Be("Keep me");
        json.RootElement.GetProperty("content").GetString().Should().Be("<p>New</p>");
    }

    [Fact]
    public async Task EmailTemplatesUpdate_SubjectOnly_KeepsTheCurrentContent()
    {
        string? sent = null;
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, $"{Org}/email-templates/confirmation").Respond("application/json", Template(content: "<p>Keep me</p>"));
        Capture(mock, HttpMethod.Put, $"{Org}/email-templates/confirmation", Template(), b => sent = b);

        var result = await Tool("email_templates_update").RunAsync(
            Args(new { template_type = "confirmation", subject = "New subject" }), Client(mock));

        result.ExitCode.Should().Be(0, result.Output);
        using var json = JsonDocument.Parse(sent!);
        json.RootElement.GetProperty("subject").GetString().Should().Be("New subject");
        json.RootElement.GetProperty("content").GetString().Should().Be("<p>Keep me</p>");
    }

    [Fact]
    public async Task EmailTemplatesUpdate_FromAFile_SendsTheFileContent()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "<p>From file</p>");
            string? sent = null;
            var mock = new MockHttpMessageHandler();
            mock.When(HttpMethod.Get, $"{Org}/email-templates/confirmation").Respond("application/json", Template());
            Capture(mock, HttpMethod.Put, $"{Org}/email-templates/confirmation", Template(), b => sent = b);

            var result = await Tool("email_templates_update").RunAsync(
                Args(new { template_type = "confirmation", content = path }), Client(mock));

            result.ExitCode.Should().Be(0, result.Output);
            using var json = JsonDocument.Parse(sent!);
            json.RootElement.GetProperty("content").GetString().Should().Be("<p>From file</p>");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task EmailTemplatesUpdate_NothingToChange_FailsWithoutCallingTheApi()
    {
        var mock = new MockHttpMessageHandler();
        var any = mock.When("*").Respond("application/json", Template());

        var result = await Tool("email_templates_update").RunAsync(Args(new { template_type = "confirmation" }), Client(mock));

        result.ExitCode.Should().NotBe(0);
        result.Output.Should().Contain("Nothing to update");
        mock.GetMatchCount(any).Should().Be(0);
    }

    [Fact]
    public async Task EmailTemplatesUpdate_EmptyContent_IsRefused_SoATemplateIsNeverBlanked()
    {
        var mock = new MockHttpMessageHandler();
        var any = mock.When("*").Respond("application/json", Template());

        var result = await Tool("email_templates_update").RunAsync(
            Args(new { template_type = "confirmation", content_text = "  " }), Client(mock));

        result.ExitCode.Should().NotBe(0);
        mock.GetMatchCount(any).Should().Be(0);
    }

    [Fact]
    public async Task EmailTemplatesUpdate_ContentFromBothAFileAndInlineText_IsRefused()
    {
        var mock = new MockHttpMessageHandler();
        var any = mock.When("*").Respond("application/json", Template());

        var result = await Tool("email_templates_update").RunAsync(
            Args(new { template_type = "confirmation", content = "a.html", content_text = "<p/>" }), Client(mock));

        result.ExitCode.Should().NotBe(0);
        result.Output.Should().Contain("not both");
        mock.GetMatchCount(any).Should().Be(0);
    }

    [Fact]
    public async Task EmailTemplatesUpdate_OversizedHtml_IsRefusedBeforeAnyRequest()
    {
        var mock = new MockHttpMessageHandler();
        var any = mock.When("*").Respond("application/json", Template());

        var result = await Tool("email_templates_update").RunAsync(
            Args(new { template_type = "confirmation", content_text = new string('a', 1_000_001) }), Client(mock));

        result.ExitCode.Should().NotBe(0);
        result.Output.Should().Contain("too large");
        mock.GetMatchCount(any).Should().Be(0);
    }

    // ── Rule: a missing template is a clear error and a non-zero exit ───────

    [Fact]
    public async Task EmailTemplatesShow_UnknownTemplate_FailsWithAClearError()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, $"{Org}/email-templates/nope").Respond(HttpStatusCode.NotFound, "application/json", "\"Not found\"");

        var result = await Tool("email_templates_show").RunAsync(Args(new { template_type = "nope" }), Client(mock));

        result.ExitCode.Should().NotBe(0);
        result.Output.Should().Contain("404");
    }

    [Fact]
    public async Task EmailTemplatesUpdate_UnknownTemplate_FailsAndSavesNothing()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, $"{Org}/email-templates/nope").Respond(HttpStatusCode.NotFound, "application/json", "\"Not found\"");
        var put = mock.When(HttpMethod.Put, "*").Respond("application/json", Template());

        var result = await Tool("email_templates_update").RunAsync(
            Args(new { template_type = "nope", subject = "S" }), Client(mock));

        result.ExitCode.Should().NotBe(0);
        mock.GetMatchCount(put).Should().Be(0);
    }

    // ── Rule: preview renders on the server and prints; it saves nothing ────

    [Fact]
    public async Task EmailPreview_PostsTheDraft_AndPrintsTheHtmlWithoutSaving()
    {
        string? sent = null;
        var mock = new MockHttpMessageHandler();
        Capture(mock, HttpMethod.Post, $"{Org}/email-templates/preview-raw", """{"subject":"S","html_content":"<html>rendered</html>"}""", b => sent = b);
        var put = mock.When(HttpMethod.Put, "*").Respond("application/json", Template());

        var result = await Tool("email_preview").RunAsync(
            Args(new { subject = "S", content_text = "<p>Draft</p>", wrapper_text = "<div>{{content}}</div>" }), Client(mock));

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("<html>rendered</html>");
        using var json = JsonDocument.Parse(sent!);
        json.RootElement.GetProperty("content").GetString().Should().Be("<p>Draft</p>");
        json.RootElement.GetProperty("wrapper_html").GetString().Should().Be("<div>{{content}}</div>");
        mock.GetMatchCount(put).Should().Be(0);
    }

    // ── Rule: the shell is replaced or reset only on an explicit request ────

    [Fact]
    public async Task EmailShellUpdate_WithNoOptions_DoesNotResetTheShell()
    {
        var mock = new MockHttpMessageHandler();
        var any = mock.When("*").Respond("application/json", "{}");

        var result = await Tool("email_shell_update").RunAsync(null, Client(mock));

        result.ExitCode.Should().NotBe(0);
        mock.GetMatchCount(any).Should().Be(0);
    }

    [Fact]
    public async Task EmailShellUpdate_HtmlAndReset_AreMutuallyExclusive()
    {
        var mock = new MockHttpMessageHandler();
        var any = mock.When("*").Respond("application/json", "{}");

        var result = await Tool("email_shell_update").RunAsync(Args(new { html_text = "<d/>", reset = true }), Client(mock));

        result.ExitCode.Should().NotBe(0);
        mock.GetMatchCount(any).Should().Be(0);
    }

    [Fact]
    public async Task EmailShellUpdate_Reset_SendsNoHtml()
    {
        string? sent = null;
        var mock = new MockHttpMessageHandler();
        Capture(mock, HttpMethod.Put, $"{Org}/email-templates/shell", """{"html":"<d/>","is_customised":false,"default_html":"<d/>"}""", b => sent = b);

        var result = await Tool("email_shell_update").RunAsync(Args(new { reset = true }), Client(mock));

        result.ExitCode.Should().Be(0, result.Output);
        using var json = JsonDocument.Parse(sent!);
        (!json.RootElement.TryGetProperty("html", out var html) || html.ValueKind == JsonValueKind.Null).Should().BeTrue();
    }

    [Fact]
    public async Task EmailShellUpdate_HtmlText_SendsOnlyTheHtml()
    {
        string? sent = null;
        var mock = new MockHttpMessageHandler();
        Capture(mock, HttpMethod.Put, $"{Org}/email-templates/shell", """{"html":"<d/>","is_customised":true,"default_html":"<x/>"}""", b => sent = b);

        var result = await Tool("email_shell_update").RunAsync(Args(new { html_text = "<table>{{content}}</table>" }), Client(mock));

        result.ExitCode.Should().Be(0, result.Output);
        using var json = JsonDocument.Parse(sent!);
        json.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("html");
        json.RootElement.GetProperty("html").GetString().Should().Be("<table>{{content}}</table>");
    }

    [Fact]
    public async Task EmailShellShow_PrintsTheHtmlAndWhetherItIsCustomised()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, $"{Org}/email-templates/shell").Respond("application/json",
            """{"html":"<table>[x]</table>","is_customised":true,"default_html":"<d/>"}""");

        var result = await Tool("email_shell_show").RunAsync(null, Client(mock));

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("Customised wrapper").And.Contain("<table>[x]</table>");
    }
}
