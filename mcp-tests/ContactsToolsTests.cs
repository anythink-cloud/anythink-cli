using System.Text.Json;
using AnythinkCli.Client;
using AnythinkMcp.Cli;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkMcp.Tests;

public class ContactsToolsTests
{
    private const string ApiUrl = "https://api.my.anythink.cloud";
    private const string Org = ApiUrl + "/org/42";

    private static CliCommandTool Tool(string name, CliToolScope scope = CliToolScope.Local) =>
        CliCommandTool.All(scope).Single(t => t.ProtocolTool.Name == name);

    private static Dictionary<string, JsonElement> Args(object values) =>
        JsonSerializer.SerializeToElement(values).EnumerateObject().ToDictionary(p => p.Name, p => p.Value);

    private static AnythinkClient Client(MockHttpMessageHandler mock) => new("42", ApiUrl, new HttpClient(mock));

    // ── Rule: everything the CLI does with contacts is offered as a tool, in every scope ──

    [Theory]
    [InlineData("contacts_list")]
    [InlineData("contacts_get")]
    [InlineData("contacts_create")]
    [InlineData("contacts_update")]
    [InlineData("contacts_unsubscribe")]
    [InlineData("contacts_erase")]
    [InlineData("contacts_export")]
    [InlineData("contacts_import")]
    [InlineData("email_marketing_settings_get")]
    [InlineData("email_marketing_settings_set")]
    [InlineData("email_marketing_options")]
    public void ContactTools_AreOfferedInEveryScope(string name)
    {
        foreach (var scope in new[] { CliToolScope.Local, CliToolScope.Internal, CliToolScope.Hosted })
            CliCommandTool.All(scope).Should().Contain(t => t.ProtocolTool.Name == name, $"{name} in {scope}");
    }

    // ── Rule: signing someone up anonymously is never offered as a tool, because it fakes form-signup consent ──

    [Theory]
    [InlineData(CliToolScope.Local)]
    [InlineData(CliToolScope.Internal)]
    [InlineData(CliToolScope.Hosted)]
    public void ContactsSubscribe_IsNeverOfferedAsATool(CliToolScope scope) =>
        CliCommandTool.All(scope).Should().NotContain(t => t.ProtocolTool.Name == "contacts_subscribe");

    // ── Rule: reading is read-only, erasing and changing are destructive ──

    [Theory]
    [InlineData("contacts_list", true, null)]
    [InlineData("contacts_get", true, null)]
    [InlineData("contacts_export", true, null)]
    [InlineData("email_marketing_settings_get", true, null)]
    [InlineData("email_marketing_options", true, null)]
    [InlineData("contacts_create", false, false)]
    [InlineData("contacts_erase", false, true)]
    [InlineData("contacts_unsubscribe", false, true)]
    [InlineData("contacts_update", false, true)]
    [InlineData("contacts_import", false, true)]
    [InlineData("email_marketing_settings_set", false, true)]
    public void ContactTools_AreClassifiedByWhatTheyDo(string name, bool readOnly, bool? destructive)
    {
        var annotations = Tool(name).ProtocolTool.Annotations!;

        annotations.ReadOnlyHint.Should().Be(readOnly);
        annotations.DestructiveHint.Should().Be(destructive);
    }

    // ── Rule: erasing from a tool needs no typed id, because the tool call is the confirmation ──

    [Fact]
    public async Task ContactsErase_RunsWithoutAPrompt()
    {
        var mock = new MockHttpMessageHandler();
        var erase = mock.When(HttpMethod.Post, $"{Org}/contacts/1/erase").Respond(System.Net.HttpStatusCode.NoContent);

        var result = await Tool("contacts_erase").RunAsync(Args(new { id = 1 }), Client(mock));

        result.ExitCode.Should().Be(0, result.Output);
        mock.GetMatchCount(erase).Should().Be(1);
    }

    // ── Rule: a remote caller sends the CSV inline; a file path on the server is refused ──

    [Fact]
    public async Task ContactsImport_RemoteFilePath_IsRefused_AndNothingIsSent()
    {
        var mock = new MockHttpMessageHandler();
        var any = mock.When("*").Respond("application/json", "{}");

        var result = await CliRunner.RunAsync(["contacts", "import", "/etc/passwd"], Client(mock), CliToolScope.Internal);

        result.ExitCode.Should().NotBe(0);
        result.Output.Should().Contain("can't be used remotely");
        mock.GetMatchCount(any).Should().Be(0);
    }

    [Fact]
    public async Task ContactsImport_InlineCsv_IsImported_AndNoAddressIsPrinted()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, $"{Org}/contacts/import").Respond("application/json", """{"created":1,"updated":0,"suppressed":0,"invalid":0}""");

        var result = await Tool("contacts_import", CliToolScope.Internal)
            .RunAsync(Args(new { csv_text = "email,status\nprivate-person@example.com,subscribed\n" }), Client(mock));

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("Created").And.NotContain("private-person");
    }

    // ── Rule: an email address is a free-text argument, not a path value ──

    [Fact]
    public async Task ContactsCreate_RemoteEmailAddress_IsAccepted()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, $"{Org}/contacts").Respond(System.Net.HttpStatusCode.Created, "application/json",
            """{"id":1,"email":"a@example.com","is_user":false,"email_marketing_status":"not_subscribed"}""");

        var result = await Tool("contacts_create", CliToolScope.Internal).RunAsync(Args(new { email = "a@example.com" }), Client(mock));

        result.ExitCode.Should().Be(0, result.Output);
    }
}
