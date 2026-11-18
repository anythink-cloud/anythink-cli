using System.Net;
using AnythinkCli.Client;
using AnythinkCli.Commands;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkCli.Tests;

[Collection("ConsoleOutput")]
public class ContactsCommandPathTests
{
    private const string Org = "https://api.example.com/org/99999";
    private const string ContactJson = """{"id":3,"email":"a@example.com","first_name":"Ann","last_name":"Lee","is_user":false,"email_marketing_status":"unsubscribed","source":"web"}""";

    // ── Rule: get shows the consent history, and --json prints the contact as the API sent it ──

    [Fact]
    public async Task Get_ShowsTheConsentHistory_WhenTheContactHasEvents()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, $"{Org}/contacts/3").Respond("application/json",
            """{"id":3,"email":"a@example.com","is_user":false,"consent_events":[{"action":"subscribed","basis":"consent","source":"signup-form","wording":"Yes please","occurred_at":"2026-03-01T10:00:00Z"}]}""");

        var (code, output) = await CommandRunner.RunAsync(handler, () =>
            new ContactsGetCommand().ExecuteAsync(null!, new ContactIdSettings { Id = 3 }));

        code.Should().Be(0);
        output.Should().Contain("Consent history (1)").And.Contain("signup-form").And.Contain("Yes please").And.Contain("2026-03-01 10:00");
    }

    [Fact]
    public async Task Get_Json_PrintsTheContact()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, $"{Org}/contacts/3").Respond("application/json", ContactJson);

        var (code, output) = await CommandRunner.RunAsync(handler, () =>
            new ContactsGetCommand().ExecuteAsync(null!, new ContactIdSettings { Id = 3, Json = true }));

        code.Should().Be(0);
        output.Should().Contain("\"email\": \"a@example.com\"");
    }

    [Fact]
    public async Task List_Json_PrintsTheResult_AndAnEmptyListSaysSo()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, $"{Org}/contacts*").Respond("application/json", """{"items":[],"total_items":0,"has_next_page":false,"page":1,"page_size":25}""");

        var (_, json) = await CommandRunner.RunAsync(handler, () =>
            new ContactsListCommand().ExecuteAsync(null!, new ContactsListSettings { Json = true }));
        var (code, text) = await CommandRunner.RunAsync(handler, () =>
            new ContactsListCommand().ExecuteAsync(null!, new ContactsListSettings()));

        json.Should().Contain("\"items\"");
        code.Should().Be(0);
        text.Should().Contain("No contacts found.");
    }

    // ── Rule: create reports the new contact, and a rejected create shows the API's reason ──

    [Fact]
    public async Task Create_ReportsTheNewContact_AndJsonPrintsIt()
    {
        var handler = new MockHttpMessageHandler();
        var bodies = new List<string>();
        handler.ReplyWith(HttpMethod.Post, $"{Org}/contacts", ContactJson, bodies);

        var (code, output) = await CommandRunner.RunAsync(handler, () =>
            new ContactsCreateCommand().ExecuteAsync(null!, new ContactsCreateSettings { Email = "a@example.com", Consent = true, Wording = "Yes" }));
        var (_, json) = await CommandRunner.RunAsync(handler, () =>
            new ContactsCreateCommand().ExecuteAsync(null!, new ContactsCreateSettings { Email = "a@example.com", Json = true }));

        code.Should().Be(0);
        output.Should().Contain("created").And.Contain("a@example.com");
        bodies[0].Should().Contain("\"consent\":{\"wording\":\"Yes\"");
        json.Should().Contain("\"id\": 3");
    }

    [Fact]
    public async Task Create_Rejected4xx_ShowsTheApisErrorText()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"{Org}/contacts").Respond(HttpStatusCode.BadRequest, "application/json", """{"error":"Email is not valid"}""");

        var (code, output) = await CommandRunner.RunAsync(handler, () =>
            new ContactsCreateCommand().ExecuteAsync(null!, new ContactsCreateSettings { Email = "nope" }));

        code.Should().Be(1);
        output.Should().Contain("API error (400)").And.Contain("Email is not valid");
    }

    // ── Rule: update sends only the fields given ──

    [Fact]
    public async Task Update_PatchesOnlyTheFieldsGiven_AndReportsTheContact()
    {
        var handler = new MockHttpMessageHandler();
        var bodies = new List<string>();
        handler.ReplyWith(HttpMethod.Patch, $"{Org}/contacts/3", ContactJson, bodies);

        var (code, output) = await CommandRunner.RunAsync(handler, () =>
            new ContactsUpdateCommand().ExecuteAsync(null!, new ContactsUpdateSettings { Id = 3, Source = "web" }));

        code.Should().Be(0);
        output.Should().Contain("updated");
        bodies.Single().Should().Contain("\"source\":\"web\"").And.NotContain("\"first_name\":\"");
    }

    [Fact]
    public async Task Update_Json_PrintsTheContact()
    {
        var handler = new MockHttpMessageHandler();
        handler.ReplyWith(HttpMethod.Patch, $"{Org}/contacts/3", ContactJson);

        var (_, output) = await CommandRunner.RunAsync(handler, () =>
            new ContactsUpdateCommand().ExecuteAsync(null!, new ContactsUpdateSettings { Id = 3, FirstName = "Ann", Json = true }));

        output.Should().Contain("\"first_name\": \"Ann\"");
    }

    [Fact]
    public async Task Update_OfAMissingContact_ShowsTheApisError()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Patch, $"{Org}/contacts/9").Respond(HttpStatusCode.NotFound, "application/json", """{"error":"Contact not found"}""");

        var (code, output) = await CommandRunner.RunAsync(handler, () =>
            new ContactsUpdateCommand().ExecuteAsync(null!, new ContactsUpdateSettings { Id = 9, Source = "web" }));

        code.Should().Be(1);
        output.Should().Contain("Contact not found");
    }

    // ── Rule: unsubscribe reports the contact's new status ──

    [Fact]
    public async Task Unsubscribe_ReportsTheNewStatus_AndJsonPrintsTheContact()
    {
        var handler = new MockHttpMessageHandler();
        handler.ReplyWith(HttpMethod.Post, $"{Org}/contacts/3/unsubscribe", ContactJson);

        var (code, output) = await CommandRunner.RunAsync(handler, () =>
            new ContactsUnsubscribeCommand().ExecuteAsync(null!, new ContactIdSettings { Id = 3 }));
        var (_, json) = await CommandRunner.RunAsync(handler, () =>
            new ContactsUnsubscribeCommand().ExecuteAsync(null!, new ContactIdSettings { Id = 3, Json = true }));

        code.Should().Be(0);
        output.Should().Contain("is now unsubscribed");
        json.Should().Contain("\"email_marketing_status\": \"unsubscribed\"");
    }

    [Fact]
    public async Task Unsubscribe_WhenTheApiFails_ExitsNonZero()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"{Org}/contacts/3/unsubscribe").Respond(HttpStatusCode.BadRequest, "application/json", """{"error":"Already unsubscribed"}""");

        var (code, output) = await CommandRunner.RunAsync(handler, () =>
            new ContactsUnsubscribeCommand().ExecuteAsync(null!, new ContactIdSettings { Id = 3 }));

        code.Should().Be(1);
        output.Should().Contain("Already unsubscribed");
    }

    // ── Rule: export prints the contact's data as JSON ──

    [Fact]
    public async Task Export_PrintsTheContactsDataAsJson()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, $"{Org}/contacts/3/export").Respond("application/json", """{"contact":{"email":"a@example.com"},"consent_events":[]}""");

        var (code, output) = await CommandRunner.RunAsync(handler, () =>
            new ContactsExportCommand().ExecuteAsync(null!, new ContactIdSettings { Id = 3 }));

        code.Should().Be(0);
        output.Should().Contain("\"consent_events\"").And.Contain("a@example.com");
    }

    [Fact]
    public async Task Export_OfAMissingContact_ShowsTheApisError()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, $"{Org}/contacts/9/export").Respond(HttpStatusCode.NotFound, "application/json", """{"error":"Contact not found"}""");

        var (code, output) = await CommandRunner.RunAsync(handler, () =>
            new ContactsExportCommand().ExecuteAsync(null!, new ContactIdSettings { Id = 9 }));

        code.Should().Be(1);
        output.Should().Contain("Contact not found");
    }

    // ── Rule: erase and subscribe failures are reported, not thrown ──

    [Fact]
    public async Task Erase_WhenTheApiFails_ExitsNonZeroWithTheApisReason()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"{Org}/contacts/3/erase").Respond(HttpStatusCode.Forbidden, "application/json", """{"error":"Not allowed"}""");

        var (code, output) = await CommandRunner.RunAsync(handler, () =>
            new ContactsEraseCommand().ExecuteAsync(null!, new ContactsEraseSettings { Id = 3, Yes = true }));

        code.Should().Be(1);
        output.Should().Contain("Not allowed");
    }

    [Fact]
    public async Task Subscribe_WhenTheApiFails_ExitsNonZero()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"{Org}/contacts/subscribe").Respond(HttpStatusCode.BadRequest, "application/json", """{"error":"Invalid email"}""");

        var (code, output) = await CommandRunner.RunAsync(handler, () =>
            new ContactsSubscribeCommand().ExecuteAsync(null!, new ContactsSubscribeSettings { Email = "x" }));

        code.Should().Be(1);
        output.Should().Contain("Invalid email");
    }

    // ── Rule: on a remote (hosted) run a server error shows the status only, never the response body ──

    [Fact]
    public async Task ContactsCall_On5xx_WhenRemote_ShowsTheStatusOnly()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, $"{Org}/contacts/3").Respond(HttpStatusCode.InternalServerError, "text/plain", "boom a@example.com");

        var (code, output) = await CommandRunner.RunAsync(handler, () =>
        {
            ClientContext.Remote = true;
            try { return new ContactsGetCommand().ExecuteAsync(null!, new ContactIdSettings { Id = 3 }); }
            finally { ClientContext.Remote = false; }
        });

        code.Should().Be(1);
        output.Should().Contain("returned status 500").And.NotContain("a@example.com");
    }

    // ── Rule: email-marketing settings need at least one setting; failures are reported ──

    [Fact]
    public void SettingsSet_WithNothingToChange_IsRejected()
    {
        new EmailMarketingSettingsSetSettings().Validate().Successful.Should().BeFalse();
        new EmailMarketingSettingsSetSettings { DoubleOptIn = false }.Validate().Successful.Should().BeTrue();
    }

    [Fact]
    public async Task SettingsSet_WhenTheApiRejectsIt_ShowsTheReason()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Put, $"{Org}/email-marketing/settings").Respond(HttpStatusCode.BadRequest, "application/json", """{"error":"Privacy policy URL must be https"}""");

        var (code, output) = await CommandRunner.RunAsync(handler, () =>
            new EmailMarketingSettingsSetCommand().ExecuteAsync(null!, new EmailMarketingSettingsSetSettings { PrivacyPolicyUrl = "http://x" }));

        code.Should().Be(1);
        output.Should().Contain("Privacy policy URL must be https");
    }

    // ── Rule: options shows what the public signup form is told, tolerating missing sections ──

    [Fact]
    public async Task Options_ShowsThePublicSignupOptions()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, $"{Org}/email-marketing/options").Respond("application/json",
            """{"double_opt_in":true,"newsletter":{"consent_wording":"Send me news","privacy_policy_url":"https://x.test/p"},"signup":{"marketing_opt_out_offered":true,"opt_out_wording":"No marketing"}}""");

        var (code, output) = await CommandRunner.RunAsync(handler, () =>
            new EmailMarketingOptionsCommand().ExecuteAsync(null!, new JsonSettings()));

        code.Should().Be(0);
        output.Should().Contain("Public signup options").And.Contain("Send me news").And.Contain("https://x.test/p").And.Contain("No marketing");
    }

    [Fact]
    public async Task Options_WithoutNewsletterOrSignupSections_StillRenders()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, $"{Org}/email-marketing/options").Respond("application/json", """{"double_opt_in":false}""");

        var (code, output) = await CommandRunner.RunAsync(handler, () =>
            new EmailMarketingOptionsCommand().ExecuteAsync(null!, new JsonSettings()));

        code.Should().Be(0);
        output.Should().Contain("Double opt-in");
    }

    [Fact]
    public async Task Options_Json_PrintsThem_AndAFailureIsReported()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, $"{Org}/email-marketing/options").Respond("application/json", """{"double_opt_in":true}""");

        var (code, json) = await CommandRunner.RunAsync(handler, () =>
            new EmailMarketingOptionsCommand().ExecuteAsync(null!, new JsonSettings { Json = true }));

        var failing = new MockHttpMessageHandler();
        failing.When(HttpMethod.Get, $"{Org}/email-marketing/options").Respond(HttpStatusCode.NotFound);
        var (failCode, failOutput) = await CommandRunner.RunAsync(failing, () =>
            new EmailMarketingOptionsCommand().ExecuteAsync(null!, new JsonSettings()));

        code.Should().Be(0);
        json.Should().Contain("\"double_opt_in\": true");
        failCode.Should().Be(1);
        failOutput.Should().Contain("Contacts aren't available on this project yet");
    }

    [Fact]
    public async Task SettingsGet_WhenTheApiFails_ExitsNonZero()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, $"{Org}/email-marketing/settings").Respond(HttpStatusCode.BadRequest, "application/json", """{"error":"Bad"}""");

        var (code, _) = await CommandRunner.RunAsync(handler, () =>
            new EmailMarketingSettingsGetCommand().ExecuteAsync(null!, new JsonSettings()));

        code.Should().Be(1);
    }

    // ── Rule: import tells you about ignored columns, how many problem rows were left out, and unreadable files ──

    [Fact]
    public async Task Import_ReportsIgnoredColumns_AndCapsTheProblemRowsShown()
    {
        var rows = string.Concat(Enumerable.Range(0, 27).Select(i => $"bad{i},subscribed,x\n"));
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"{Org}/contacts/import").Respond("application/json", """{"created":1,"updated":0,"suppressed":0,"invalid":0}""");

        var (code, output) = await CommandRunner.RunAsync(handler, () =>
            new ContactsImportCommand().ExecuteAsync(null!, new ContactsImportSettings { CsvText = "email,status,favourite_colour\nok@example.com,subscribed,red\n" + rows, SkipInvalid = true }));

        code.Should().Be(0);
        output.Should().Contain("Ignoring columns that aren't contact fields: favourite_colour").And.Contain("and 2 more.");
    }

    [Fact]
    public async Task Import_OfAMissingFile_SaysTheFileWasNotFound()
    {
        var (code, output) = await CommandRunner.RunAsync(new MockHttpMessageHandler(), () =>
            new ContactsImportCommand().ExecuteAsync(null!, new ContactsImportSettings { File = "/no/such/contacts.csv" }));

        code.Should().Be(1);
        output.Should().Contain("File not found");
    }

    [Fact]
    public async Task Import_FromAFile_SendsItsRows()
    {
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, "email,status\nok@example.com,subscribed\n");
        try
        {
            var handler = new MockHttpMessageHandler();
            handler.When(HttpMethod.Post, $"{Org}/contacts/import").Respond("application/json", """{"created":1,"updated":0,"suppressed":0,"invalid":0}""");

            var (code, output) = await CommandRunner.RunAsync(handler, () =>
                new ContactsImportCommand().ExecuteAsync(null!, new ContactsImportSettings { File = path }));

            code.Should().Be(0);
            output.Should().Contain("Import complete.");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Import_OnARemoteRun_RefusesAFilePath()
    {
        var (code, output) = await CommandRunner.RunAsync(new MockHttpMessageHandler(), () =>
        {
            ClientContext.Remote = true;
            try { return new ContactsImportCommand().ExecuteAsync(null!, new ContactsImportSettings { File = "contacts.csv" }); }
            finally { ClientContext.Remote = false; }
        });

        code.Should().Be(1);
        output.Should().Contain("can't be used remotely");
    }

    [Fact]
    public async Task Import_WithNoValidRows_SendsNothing()
    {
        var (code, output) = await CommandRunner.RunAsync(new MockHttpMessageHandler(), () =>
            new ContactsImportCommand().ExecuteAsync(null!, new ContactsImportSettings { CsvText = "email,status\nbad,subscribed\n", SkipInvalid = true }));

        code.Should().Be(1);
        output.Should().Contain("There are no valid rows to import.");
    }

    [Fact]
    public async Task Import_Json_PrintsTheSummary()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"{Org}/contacts/import").Respond("application/json", """{"created":1,"updated":0,"suppressed":0,"invalid":0}""");

        var (code, output) = await CommandRunner.RunAsync(handler, () =>
            new ContactsImportCommand().ExecuteAsync(null!, new ContactsImportSettings { CsvText = "email,status\nok@example.com,subscribed\n", Json = true }));

        code.Should().Be(0);
        output.Should().Contain("\"Created\": 1");
    }

    [Fact]
    public async Task Import_FailedBatchWithANonJsonBody_ReportsTheStatusOnly()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"{Org}/contacts/import").Respond(HttpStatusCode.BadRequest, "text/plain", "not json");

        var (code, output) = await CommandRunner.RunAsync(handler, () =>
            new ContactsImportCommand().ExecuteAsync(null!, new ContactsImportSettings { CsvText = "email,status\nok@example.com,subscribed\n" }));

        code.Should().Be(1);
        output.Should().Contain("status 400").And.NotContain("not json");
    }
}
