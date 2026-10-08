using System.Text.Json.Nodes;
using AnythinkCli.Commands;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkCli.Tests;

[Collection("ConsoleOutput")]
public class WorkflowsCreateTests
{
    private static MockHttpMessageHandler Api(List<string> sent)
    {
        var mock = new MockHttpMessageHandler();
        mock.ReplyWith(HttpMethod.Post, $"{CommandRunner.Org}/workflows", """{"id":5,"name":"w","enabled":false,"steps":[]}""", sent);
        return mock;
    }

    private static Task<(int Code, string Output)> Create(MockHttpMessageHandler mock, WorkflowCreateSettings settings) =>
        CommandRunner.RunAsync(mock, () => new WorkflowsCreateCommand().ExecuteAsync(null!, settings));

    private static JsonObject SentBody(List<string> sent) => JsonNode.Parse(sent.Single())!.AsObject();

    // ── Required trigger config is checked before anything is sent ───────────

    [Fact]
    public async Task Create_ManualTriggerWithoutEntity_IsRejectedBeforeCallingTheApi()
    {
        var sent = new List<string>();
        var mock = Api(sent);

        var (code, output) = await Create(mock, new WorkflowCreateSettings { Name = "w" });

        code.Should().Be(1);
        sent.Should().BeEmpty();
        output.Should().Contain("Manual trigger needs --entity");
    }

    [Theory]
    [InlineData("Event", "An Event trigger needs --entity")]
    [InlineData("Api", "An Api trigger needs --api-route")]
    [InlineData("Timed", "A Timed trigger needs --cron")]
    public async Task Create_TriggerMissingItsRequiredOption_IsRejectedNamingTheFlag(string trigger, string expected)
    {
        var sent = new List<string>();
        var mock = Api(sent);

        var (code, output) = await Create(mock, new WorkflowCreateSettings { Name = "w", Trigger = trigger });

        code.Should().Be(1);
        sent.Should().BeEmpty();
        output.Should().Contain(expected);
    }

    [Fact]
    public async Task Create_UnknownTriggerType_IsRejectedListingTheValidOnes()
    {
        var sent = new List<string>();
        var mock = Api(sent);

        var (code, output) = await Create(mock, new WorkflowCreateSettings { Name = "w", Trigger = "Webhook" });

        code.Should().Be(1);
        sent.Should().BeEmpty();
        output.Should().Contain("Unknown trigger type 'Webhook'").And.Contain("Manual, Timed, Event, Api");
    }

    [Fact]
    public async Task Create_TriggerType_IsMatchedCaseInsensitivelyAndSentInTheCanonicalSpelling()
    {
        var sent = new List<string>();
        var mock = Api(sent);

        var (code, _) = await Create(mock, new WorkflowCreateSettings { Name = "w", Trigger = "timed", Cron = "0 9 * * *" });

        code.Should().Be(0);
        SentBody(sent)["triggers"]![0]!["type"]!.GetValue<string>().Should().Be("Timed");
    }

    // ── The bodies that are sent ─────────────────────────────────────────────

    [Fact]
    public async Task Create_ApiTrigger_SendsTheRouteInTheTriggerConfigAndNoLegacyFields()
    {
        var sent = new List<string>();
        var mock = Api(sent);

        await Create(mock, new WorkflowCreateSettings { Name = "w", Trigger = "Api", ApiRoute = "hooks/in" });

        var body = SentBody(sent);
        body["trigger"].Should().BeNull();
        body["api_route"].Should().BeNull();
        var trigger = body["triggers"]!.AsArray().Should().ContainSingle().Subject!;
        trigger["type"]!.GetValue<string>().Should().Be("Api");
        trigger["enabled"]!.GetValue<bool>().Should().BeTrue();
        trigger["config"]!["api_route"]!.GetValue<string>().Should().Be("hooks/in");
    }

    [Fact]
    public async Task Create_EventTrigger_SendsEventEntityAndFilterInTheTriggerConfig()
    {
        var sent = new List<string>();
        var mock = Api(sent);

        await Create(mock, new WorkflowCreateSettings
        {
            Name = "w",
            Trigger = "Event",
            EventEntity = "blog_posts",
            Event = "EntityUpdated",
            Filter = """{"field":"status","operator":"eq","value":"reviewed"}""",
        });

        var config = SentBody(sent)["triggers"]![0]!["config"]!;
        config["event"]!.GetValue<string>().Should().Be("EntityUpdated");
        config["event_entity"]!.GetValue<string>().Should().Be("blog_posts");
        config["filter"]!["field"]!.GetValue<string>().Should().Be("status");
    }

    [Fact]
    public async Task Create_ManualTrigger_SendsTheEntityInManualEntities()
    {
        var sent = new List<string>();
        var mock = Api(sent);

        await Create(mock, new WorkflowCreateSettings { Name = "w", EventEntity = "sources" });

        SentBody(sent)["triggers"]![0]!["config"]!["manual_entities"]!.AsArray()
            .Select(e => e!.GetValue<string>()).Should().Equal("sources");
    }

    // ── --event is checked against the events the API knows ──────────────────

    [Fact]
    public async Task Create_UnknownEvent_IsRejectedListingTheKnownEvents()
    {
        var sent = new List<string>();
        var mock = Api(sent);

        var (code, output) = await Create(mock, new WorkflowCreateSettings { Name = "w", Trigger = "Event", EventEntity = "posts", Event = "EntityCreatd" });

        code.Should().Be(1);
        sent.Should().BeEmpty();
        output.Should().Contain("Unknown event 'EntityCreatd'").And.Contain("EntityUpdated").And.Contain("PaymentSucceeded");
    }

    [Fact]
    public async Task Create_EventName_IsMatchedCaseInsensitivelyAndSentInTheCanonicalSpelling()
    {
        var sent = new List<string>();
        var mock = Api(sent);

        var (code, _) = await Create(mock, new WorkflowCreateSettings { Name = "w", Trigger = "Event", EventEntity = "posts", Event = "entityupdated" });

        code.Should().Be(0);
        SentBody(sent)["triggers"]![0]!["config"]!["event"]!.GetValue<string>().Should().Be("EntityUpdated");
    }

    [Fact]
    public async Task Create_EventThatIsNotAboutAnEntity_NeedsNoEntity()
    {
        var sent = new List<string>();
        var mock = Api(sent);

        var (code, _) = await Create(mock, new WorkflowCreateSettings { Name = "w", Trigger = "Event", Event = "UserRegistered" });

        code.Should().Be(0);
        SentBody(sent)["triggers"]![0]!["config"]!["event"]!.GetValue<string>().Should().Be("UserRegistered");
    }

    // ── Options that don't apply are called out, not silently dropped ────────

    [Fact]
    public async Task Create_EntityGivenForATimedTrigger_IsIgnoredWithAWarning()
    {
        var sent = new List<string>();
        var mock = Api(sent);

        var (code, output) = await Create(mock, new WorkflowCreateSettings
        {
            Name = "w",
            Trigger = "Timed",
            Cron = "0 9 * * *",
            EventEntity = "sources",
        });

        code.Should().Be(0);
        output.Should().Contain("--entity is ignored for a Timed trigger");
        SentBody(sent)["triggers"]![0]!["config"]!.AsObject().Select(p => p.Key).Should().Equal("cron_expression");
    }
}
