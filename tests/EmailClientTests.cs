using System.Net;
using System.Text.Json;
using AnythinkCli.Client;
using AnythinkCli.Models;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkCli.Tests;

public class EmailClientTests
{
    private const string OrgPath = "https://api.example.com/org/99999";

    private const string TemplateJson =
        """{"id":1,"template_type":"confirmation","subject":"Hi","content":"<p>Body</p>","display_name":null,"description":null,"is_active":true,"is_system":true,"locked":false}""";

    private static AnythinkClient Client(MockHttpMessageHandler handler) =>
        new("99999", "https://api.example.com", new HttpClient(handler));

    // ── Rule: each operation calls its own email-templates route ────────────

    [Fact]
    public async Task GetEmailTemplatesAsync_ReadsTheTemplateList()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Get, $"{OrgPath}/email-templates").Respond("application/json", $"[{TemplateJson}]");

        var templates = await Client(handler).GetEmailTemplatesAsync();

        templates.Should().ContainSingle().Which.TemplateType.Should().Be("confirmation");
        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task GetEmailTemplateAsync_ReadsOneTemplateByType()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Get, $"{OrgPath}/email-templates/confirmation").Respond("application/json", TemplateJson);

        var template = await Client(handler).GetEmailTemplateAsync("confirmation");

        template.Subject.Should().Be("Hi");
        template.Content.Should().Be("<p>Body</p>");
        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task PreviewEmailTemplateAsync_UsesThePreviewRoute()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Get, $"{OrgPath}/email-templates/confirmation/preview")
            .Respond("application/json", """{"subject":"Hi","html_content":"<html></html>"}""");

        var preview = await Client(handler).PreviewEmailTemplateAsync("confirmation");

        preview.HtmlContent.Should().Be("<html></html>");
        handler.VerifyNoOutstandingExpectation();
    }

    // ── Rule: a template type is one path segment and can't reach another route ─

    [Theory]
    [InlineData("../shell", "..%2Fshell")]
    [InlineData("a/b", "a%2Fb")]
    [InlineData("a?b#c", "a%3Fb%23c")]
    public async Task GetEmailTemplateAsync_TemplateType_IsEscapedAsOnePathSegment(string templateType, string escaped)
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Get, $"{OrgPath}/email-templates/{escaped}").Respond("application/json", TemplateJson);

        await Client(handler).GetEmailTemplateAsync(templateType);

        handler.VerifyNoOutstandingExpectation();
    }

    // ── Rule: bodies are snake_case and carry only the fields that change ───

    [Fact]
    public async Task UpdateEmailTemplateAsync_SendsOnlySubjectAndContent_InSnakeCase()
    {
        string? body = null;
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Put, $"{OrgPath}/email-templates/confirmation").Respond(async req =>
        {
            body = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage { Content = new StringContent(TemplateJson) };
        });

        await Client(handler).UpdateEmailTemplateAsync("confirmation", new UpdateEmailTemplateRequest("New", "<p>New</p>"));

        using var json = JsonDocument.Parse(body!);
        json.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("subject", "content");
        json.RootElement.GetProperty("subject").GetString().Should().Be("New");
    }

    [Fact]
    public async Task PreviewRawEmailAsync_PostsSnakeCaseBody_WithWrapperHtml()
    {
        string? body = null;
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Post, $"{OrgPath}/email-templates/preview-raw").Respond(async req =>
        {
            body = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage { Content = new StringContent("""{"subject":"S","html_content":"<b>x</b>"}""") };
        });

        var preview = await Client(handler).PreviewRawEmailAsync(new PreviewRawEmailRequest("S", "<b>x</b>", "<div>{{content}}</div>"));

        preview.HtmlContent.Should().Be("<b>x</b>");
        using var json = JsonDocument.Parse(body!);
        json.RootElement.GetProperty("wrapper_html").GetString().Should().Be("<div>{{content}}</div>");
        json.RootElement.TryGetProperty("wrapperHtml", out _).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateEmailShellAsync_NullHtml_SendsNoHtml_ToResetTheShell()
    {
        string? body = null;
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Put, $"{OrgPath}/email-templates/shell").Respond(async req =>
        {
            body = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage { Content = new StringContent("""{"html":"<d/>","is_customised":false,"default_html":"<d/>"}""") };
        });

        await Client(handler).UpdateEmailShellAsync(new UpdateEmailShellRequest(null));

        using var json = JsonDocument.Parse(body!);
        (!json.RootElement.TryGetProperty("html", out var html) || html.ValueKind == JsonValueKind.Null).Should().BeTrue();
    }

    [Fact]
    public async Task GetEmailShellAsync_ReadsSnakeCaseFields()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Get, $"{OrgPath}/email-templates/shell")
            .Respond("application/json", """{"html":"<x/>","is_customised":true,"default_html":"<d/>"}""");

        var shell = await Client(handler).GetEmailShellAsync();

        shell.IsCustomised.Should().BeTrue();
        shell.DefaultHtml.Should().Be("<d/>");
    }

    // ── Rule: a missing template is a clear 404, not a null reference ───────

    [Fact]
    public async Task GetEmailTemplateAsync_UnknownTemplate_ThrowsAnythinkException404()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, $"{OrgPath}/email-templates/nope").Respond(HttpStatusCode.NotFound, "application/json", "\"Not found\"");

        var act = () => Client(handler).GetEmailTemplateAsync("nope");

        (await act.Should().ThrowAsync<AnythinkException>()).Which.StatusCode.Should().Be(404);
    }

    // ── Rule: saving the project's theme never drops settings the CLI doesn't model ─

    [Fact]
    public void ThemeSettings_RoundTrip_KeepsTheEmailShellFields()
    {
        const string theme =
            """{"primary_color":"#F97316","gray_color":"slate","email_wrapper_html":"<table>{{content}}</table>","email_shell_design_config_json":"{\"width\":600}"}""";

        var parsed = JsonSerializer.Deserialize<ThemeSettingsDto>(theme)!;
        using var written = JsonDocument.Parse(JsonSerializer.Serialize(parsed));

        written.RootElement.GetProperty("primary_color").GetString().Should().Be("#F97316");
        written.RootElement.GetProperty("email_wrapper_html").GetString().Should().Be("<table>{{content}}</table>");
        written.RootElement.GetProperty("email_shell_design_config_json").GetString().Should().Be("{\"width\":600}");
    }
}
