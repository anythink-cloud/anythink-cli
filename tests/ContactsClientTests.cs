using System.Net;
using System.Text.Json;
using AnythinkCli.Client;
using AnythinkCli.Commands;
using AnythinkCli.Models;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkCli.Tests;

public class ContactsClientTests
{
    private const string Org = "https://api.example.com/org/99999";

    private const string ContactJson =
        """{"id":1,"email":"a@example.com","first_name":"Ann","last_name":null,"user_id":7,"is_user":false,"email_marketing_status":"subscribed","consent_basis":"consent","status_changed_at":"2026-01-01T00:00:00Z","source":"web","created_at":"2026-01-01T00:00:00Z","updated_at":"2026-01-01T00:00:00Z"}""";

    private static AnythinkClient Client(MockHttpMessageHandler handler) =>
        new("99999", "https://api.example.com", new HttpClient(handler));

    // ── Rule: each operation sends the right method, path and snake_case body ──

    [Fact]
    public async Task GetContactsAsync_SendsEveryFilterAsASnakeCaseQueryParameter()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Get, $"{Org}/contacts?page=2&page_size=10&status=subscribed&is_user=false&source=web&search=ann%20lee")
            .Respond("application/json", $$"""{"items":[{{ContactJson}}],"total_items":1,"has_next_page":false,"page":2,"page_size":10}""");

        var result = await Client(handler).GetContactsAsync("subscribed", false, "web", "ann lee", 2, 10);

        result.Items.Should().ContainSingle().Which.EmailMarketingStatus.Should().Be("subscribed");
        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task GetContactsAsync_WithNoFilters_SendsOnlyThePaging()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Get, $"{Org}/contacts?page=1&page_size=25")
            .Respond("application/json", """{"items":[],"has_next_page":false}""");

        await Client(handler).GetContactsAsync();

        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task GetContactAsync_ReadsTheConsentEvents()
    {
        var handler = new MockHttpMessageHandler();
        var withEvents = ContactJson.TrimEnd('}') + ""","consent_events":[{"id":10,"action":"subscribed","basis":"consent","source":"web","wording":"Yes please","ip_address":"203.0.113.9","campaign_id":null,"actor_user_id":null,"occurred_at":"2026-01-01T00:00:00Z","created_at":"2026-01-01T00:00:00Z"}]}""";
        handler.Expect(HttpMethod.Get, $"{Org}/contacts/1").Respond("application/json", withEvents);

        var contact = await Client(handler).GetContactAsync(1);

        contact.ConsentEvents.Should().ContainSingle().Which.Wording.Should().Be("Yes please");
    }

    [Fact]
    public async Task CreateContactAsync_PostsConsentOnlyWhenGiven()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Post, $"{Org}/contacts")
            .WithContent("""{"email":"a@example.com","first_name":"Ann","consent":{"wording":"Yes","source":"form"}}""")
            .Respond(HttpStatusCode.Created, "application/json", ContactJson);
        handler.Expect(HttpMethod.Post, $"{Org}/contacts")
            .WithContent("""{"email":"b@example.com"}""")
            .Respond(HttpStatusCode.Created, "application/json", ContactJson);

        var client = Client(handler);
        await client.CreateContactAsync(new CreateContactRequest("a@example.com", "Ann", Consent: new ContactConsentRequest("Yes", "form")));
        await client.CreateContactAsync(new CreateContactRequest("b@example.com"));

        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task UpdateContactAsync_PatchesOnlyTheFieldsGiven()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect(new HttpMethod("PATCH"), $"{Org}/contacts/1").WithContent("""{"last_name":"Lee"}""")
            .Respond("application/json", ContactJson);

        await Client(handler).UpdateContactAsync(1, new UpdateContactRequest(LastName: "Lee"));

        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task UnsubscribeContactAsync_PostsToTheUnsubscribeRoute()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Post, $"{Org}/contacts/1/unsubscribe").Respond("application/json", ContactJson);

        await Client(handler).UnsubscribeContactAsync(1);

        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task EraseContactAsync_AcceptsNoContent()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Post, $"{Org}/contacts/1/erase").Respond(HttpStatusCode.NoContent);

        await Client(handler).EraseContactAsync(1);

        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task EraseContactAsync_ApiRefusal_Throws()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"{Org}/contacts/1/erase").Respond(HttpStatusCode.Forbidden, "application/json", "{}");

        var act = () => Client(handler).EraseContactAsync(1);

        (await act.Should().ThrowAsync<AnythinkException>()).Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task ExportContactAsync_ReturnsTheRawDocument()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Get, $"{Org}/contacts/1/export")
            .Respond("application/json", """{"contact":{"id":1},"events":[]}""");

        var export = await Client(handler).ExportContactAsync(1);

        export["contact"]!["id"]!.GetValue<int>().Should().Be(1);
    }

    [Fact]
    public async Task ImportContactsAsync_PostsTheRowsAndReadsTheCounts()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Post, $"{Org}/contacts/import")
            .WithContent("""{"rows":[{"email":"a@example.com","status":"unsubscribed","first_name":"Ann"}]}""")
            .Respond("application/json", """{"created":1,"updated":2,"suppressed":3,"invalid":4}""");

        var result = await Client(handler).ImportContactsAsync([new ImportContactRow("a@example.com", "unsubscribed", "Ann")]);

        result.Should().Be(new ImportContactsResult(1, 2, 3, 4));
    }

    // ── Rule: ids are whole numbers on the wire ──────────────────────────────

    [Fact]
    public async Task GetContactAsync_ReadsNumericIdsForTheContactItsEventsAndTheirCampaign()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Get, $"{Org}/contacts/42").Respond("application/json", """
            {"id":42,"email":"a@example.com","user_id":7,"is_user":true,"email_marketing_status":"subscribed",
             "consent_events":[{"id":314,"action":"subscribed","basis":"consent","source":"web","wording":"Yes",
             "ip_address":"203.0.113.9","campaign_id":5,"actor_user_id":9,"occurred_at":"2026-01-01T00:00:00Z","created_at":"2026-01-01T00:00:00Z"}]}
            """);

        var contact = await Client(handler).GetContactAsync(42);

        contact.Id.Should().Be(42);
        contact.UserId.Should().Be(7);
        var e = contact.ConsentEvents.Should().ContainSingle().Subject;
        (e.Id, e.CampaignId, e.ActorUserId).Should().Be((314, 5, 9));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void ContactId_MustBeAPositiveWholeNumber(int id) =>
        new ContactIdSettings { Id = id }.Validate().Successful.Should().BeFalse();

    // ── Rule: the public routes send no credentials ──────────────────────────

    [Fact]
    public async Task SubscribeAsync_UsesTheAnonymousClient_AndSendsOnlyWhatWasGiven()
    {
        var signedIn = new MockHttpMessageHandler();
        var anonymous = new MockHttpMessageHandler();
        anonymous.Expect(HttpMethod.Post, $"{Org}/contacts/subscribe")
            .WithContent("""{"email":"a@example.com","source":"web"}""")
            .Respond(HttpStatusCode.Accepted, "application/json", """{"status":"accepted"}""");

        var client = new AnythinkClient("99999", "https://api.example.com", new HttpClient(signedIn), new HttpClient(anonymous));
        var result = await client.SubscribeAsync(new SubscribeRequest("a@example.com", Source: "web"));

        result!.Status.Should().Be("accepted");
        anonymous.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task GetEmailMarketingOptionsAsync_UsesTheAnonymousClient()
    {
        var signedIn = new MockHttpMessageHandler();
        var anonymous = new MockHttpMessageHandler();
        anonymous.Expect(HttpMethod.Get, $"{Org}/email-marketing/options").Respond("application/json",
            """{"double_opt_in":true,"newsletter":{"consent_wording":"Yes","privacy_policy_url":"https://x.test/p"},"signup":{"marketing_opt_out_offered":true,"opt_out_wording":"No thanks"}}""");

        var client = new AnythinkClient("99999", "https://api.example.com", new HttpClient(signedIn), new HttpClient(anonymous));
        var options = await client.GetEmailMarketingOptionsAsync();

        options.DoubleOptIn.Should().BeTrue();
        options.Signup!.OptOutWording.Should().Be("No thanks");
        anonymous.VerifyNoOutstandingExpectation();
    }

    // ── Rule: settings are written in full, and the declaration only when given ──

    [Fact]
    public async Task UpdateEmailMarketingSettingsAsync_PutsEveryField_AndTheDeclarationOnlyWhenGiven()
    {
        var handler = new MockHttpMessageHandler();
        var bodies = new List<string>();
        handler.ReplyWith(HttpMethod.Put, $"{Org}/email-marketing/settings",
            """{"double_opt_in":true,"soft_opt_in":true,"postal_address":"1 High St","default_consent_wording":"Yes","privacy_policy_url":"https://x.test/p","signup_opt_out_wording":"No"}""", bodies);

        var client = Client(handler);
        await client.UpdateEmailMarketingSettingsAsync(new UpdateEmailMarketingSettingsRequest(true, true, "1 High St", "Yes", "https://x.test/p", "No", true));
        await client.UpdateEmailMarketingSettingsAsync(new UpdateEmailMarketingSettingsRequest(true, false, "1 High St", "Yes", "https://x.test/p", "No"));

        var first = JsonDocument.Parse(bodies[0]).RootElement;
        first.GetProperty("soft_opt_in_declaration").GetBoolean().Should().BeTrue();
        first.GetProperty("postal_address").GetString().Should().Be("1 High St");
        JsonDocument.Parse(bodies[1]).RootElement.TryGetProperty("soft_opt_in_declaration", out _).Should().BeFalse();
    }
}
