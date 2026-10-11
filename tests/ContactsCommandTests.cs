using System.Net;
using AnythinkCli.Commands;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkCli.Tests;

[Collection("ConsoleOutput")]
public class ContactsCommandTests
{
    private const string Org = "https://api.example.com/org/99999";

    // ── Rule: erasing needs the contact's id typed back, unless --yes is given ──

    [Fact]
    public void ConfirmErase_WithYes_NeedsNoPrompt() =>
        ContactsOutput.ConfirmErase("c1", yes: true, interactive: false).Should().BeNull();

    [Fact]
    public async Task ConfirmErase_NonInteractiveWithoutYes_RefusesAndErasesNothing()
    {
        var handler = new MockHttpMessageHandler();
        var erases = 0;
        handler.When(HttpMethod.Post, $"{Org}/contacts/c1/erase").Respond(_ => { erases++; return new HttpResponseMessage(HttpStatusCode.NoContent); });

        var (code, _) = await CommandRunner.RunAsync(handler, () =>
            new ContactsEraseCommand().ExecuteAsync(null!, new ContactsEraseSettings { Id = "c1" }));

        code.Should().NotBe(0);
        erases.Should().Be(0);
    }

    [Fact]
    public void ConfirmErase_TypingTheId_Proceeds() =>
        ContactsOutput.ConfirmErase("c1", yes: false, interactive: true, ask: () => " c1 ").Should().BeNull();

    [Theory]
    [InlineData("")]
    [InlineData("c2")]
    [InlineData("yes")]
    [InlineData("C1")]
    public void ConfirmErase_TypingAnythingElse_Cancels(string typed) =>
        ContactsOutput.ConfirmErase("c1", yes: false, interactive: true, ask: () => typed).Should().Be(0);

    [Fact]
    public async Task Erase_WithYes_PostsTheEraseRoute()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Post, $"{Org}/contacts/c1/erase").Respond(HttpStatusCode.NoContent);

        var (code, output) = await CommandRunner.RunAsync(handler, () =>
            new ContactsEraseCommand().ExecuteAsync(null!, new ContactsEraseSettings { Id = "c1", Yes = true }));

        code.Should().Be(0);
        output.Should().Contain("erased");
        handler.VerifyNoOutstandingExpectation();
    }

    // ── Rule: consent details only make sense with --consent ─────────────────

    [Fact]
    public void Create_WordingWithoutConsent_IsRejected()
    {
        new ContactsCreateSettings { Email = "a@example.com", Wording = "Yes" }.Validate().Successful.Should().BeFalse();
        new ContactsCreateSettings { Email = "a@example.com", Consent = true, Wording = "Yes" }.Validate().Successful.Should().BeTrue();
    }

    [Fact]
    public void Update_WithNothingToChange_IsRejected()
    {
        new ContactsUpdateSettings { Id = "c1" }.Validate().Successful.Should().BeFalse();
        new ContactsUpdateSettings { Id = "c1", Source = "web" }.Validate().Successful.Should().BeTrue();
    }

    // ── Rule: settings set changes only what is passed and sends the rest back unchanged ──

    [Fact]
    public async Task SettingsSet_MergesOntoTheCurrentSettings()
    {
        var current = """{"double_opt_in":true,"soft_opt_in":false,"postal_address":"1 High St","default_consent_wording":"Yes","privacy_policy_url":"https://x.test/p","signup_opt_out_wording":"No"}""";
        var handler = new MockHttpMessageHandler();
        var bodies = new List<string>();
        handler.When(HttpMethod.Get, $"{Org}/email-marketing/settings").Respond("application/json", current);
        handler.ReplyWith(HttpMethod.Put, $"{Org}/email-marketing/settings", current, bodies);

        var (code, _) = await CommandRunner.RunAsync(handler, () =>
            new EmailMarketingSettingsSetCommand().ExecuteAsync(null!, new EmailMarketingSettingsSetSettings { PostalAddress = "2 Low Rd" }));

        code.Should().Be(0);
        bodies.Should().ContainSingle().Which.Should()
            .Contain("\"double_opt_in\":true").And.Contain("\"postal_address\":\"2 Low Rd\"").And.Contain("\"privacy_policy_url\":\"https://x.test/p\"")
            .And.NotContain("soft_opt_in_declaration");
    }

    [Fact]
    public async Task SettingsSet_TurningSoftOptInOn_SendsTheDeclarationWhenGiven()
    {
        var current = """{"double_opt_in":true,"soft_opt_in":false,"postal_address":"a","default_consent_wording":"b","privacy_policy_url":"c","signup_opt_out_wording":"d"}""";
        var handler = new MockHttpMessageHandler();
        var bodies = new List<string>();
        handler.When(HttpMethod.Get, $"{Org}/email-marketing/settings").Respond("application/json", current);
        handler.ReplyWith(HttpMethod.Put, $"{Org}/email-marketing/settings", current, bodies);

        await CommandRunner.RunAsync(handler, () =>
            new EmailMarketingSettingsSetCommand().ExecuteAsync(null!, new EmailMarketingSettingsSetSettings { SoftOptIn = true, SoftOptInDeclaration = true }));

        bodies.Single().Should().Contain("\"soft_opt_in\":true").And.Contain("\"soft_opt_in_declaration\":true");
    }

    [Fact]
    public async Task List_PrintsTheContacts_AndPointsToTheNextPage()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, $"{Org}/contacts*").Respond("application/json",
            """{"items":[{"id":"c1","email":"a@example.com","first_name":"Ann","is_user":false,"email_marketing_status":"subscribed"}],"total_items":40,"has_next_page":true,"page":1,"page_size":1}""");

        var (code, output) = await CommandRunner.RunAsync(handler, () =>
            new ContactsListCommand().ExecuteAsync(null!, new ContactsListSettings()));

        code.Should().Be(0);
        output.Should().Contain("a@example.com").And.Contain("--page 2");
    }
}
