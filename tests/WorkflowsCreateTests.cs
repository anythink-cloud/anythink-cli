using System.Net;
using System.Text.Json.Nodes;
using AnythinkCli.Commands;
using FluentAssertions;

namespace AnythinkCli.Tests;

[Collection("ConsoleOutput")]
public class WorkflowsCreateTests
{
    private static RecordingHandler Api() =>
        new((_, _) => (HttpStatusCode.OK, """{"id":5,"name":"w","enabled":false,"steps":[]}"""));

    private static Task<(int Code, string Output)> Create(RecordingHandler handler, WorkflowCreateSettings settings) =>
        CommandRunner.RunAsync(handler, () => new WorkflowsCreateCommand().ExecuteAsync(null!, settings));

    private static JsonObject SentBody(RecordingHandler handler) =>
        JsonNode.Parse(handler.Requests.Single(r => r.Method == HttpMethod.Post).Body)!.AsObject();

    // ── Required trigger config is checked before anything is sent ───────────

    [Fact]
    public async Task Create_ManualTriggerWithoutEntity_IsRejectedBeforeCallingTheApi()
    {
        var handler = Api();

        var (code, output) = await Create(handler, new WorkflowCreateSettings { Name = "w" });

        code.Should().Be(1);
        handler.Requests.Should().BeEmpty();
        output.Should().Contain("Manual trigger needs --entity");
    }

    [Theory]
    [InlineData("Event", "An Event trigger needs --entity")]
    [InlineData("Api", "An Api trigger needs --api-route")]
    [InlineData("Timed", "A Timed trigger needs --cron")]
    public async Task Create_TriggerMissingItsRequiredOption_IsRejectedNamingTheFlag(string trigger, string expected)
    {
        var handler = Api();

        var (code, output) = await Create(handler, new WorkflowCreateSettings { Name = "w", Trigger = trigger });

        code.Should().Be(1);
        handler.Requests.Should().BeEmpty();
        output.Should().Contain(expected);
    }

    [Fact]
    public async Task Create_UnknownTriggerType_IsRejectedListingTheValidOnes()
    {
        var handler = Api();

        var (code, output) = await Create(handler, new WorkflowCreateSettings { Name = "w", Trigger = "Webhook" });

        code.Should().Be(1);
        handler.Requests.Should().BeEmpty();
        output.Should().Contain("Unknown trigger type 'Webhook'").And.Contain("Manual, Timed, Event, Api");
    }

    [Fact]
    public async Task Create_TriggerType_IsMatchedCaseInsensitivelyAndSentInTheCanonicalSpelling()
    {
        var handler = Api();

        var (code, _) = await Create(handler, new WorkflowCreateSettings { Name = "w", Trigger = "timed", Cron = "0 9 * * *" });

        code.Should().Be(0);
        SentBody(handler)["triggers"]![0]!["type"]!.GetValue<string>().Should().Be("Timed");
    }

    // ── The bodies that are sent ─────────────────────────────────────────────

    [Fact]
    public async Task Create_ApiTrigger_SendsTheRouteInTheTriggerConfigAndNoLegacyFields()
    {
        var handler = Api();

        await Create(handler, new WorkflowCreateSettings { Name = "w", Trigger = "Api", ApiRoute = "hooks/in" });

        var body = SentBody(handler);
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
        var handler = Api();

        await Create(handler, new WorkflowCreateSettings
        {
            Name = "w", Trigger = "Event", EventEntity = "blog_posts", Event = "EntityUpdated",
            Filter = """{"field":"status","operator":"eq","value":"reviewed"}""",
        });

        var config = SentBody(handler)["triggers"]![0]!["config"]!;
        config["event"]!.GetValue<string>().Should().Be("EntityUpdated");
        config["event_entity"]!.GetValue<string>().Should().Be("blog_posts");
        config["filter"]!["field"]!.GetValue<string>().Should().Be("status");
    }

    [Fact]
    public async Task Create_ManualTrigger_SendsTheEntityInManualEntities()
    {
        var handler = Api();

        await Create(handler, new WorkflowCreateSettings { Name = "w", EventEntity = "sources" });

        SentBody(handler)["triggers"]![0]!["config"]!["manual_entities"]!.AsArray()
            .Select(e => e!.GetValue<string>()).Should().Equal("sources");
    }

    // ── Options that don't apply are called out, not silently dropped ────────

    [Fact]
    public async Task Create_EntityGivenForATimedTrigger_IsIgnoredWithAWarning()
    {
        var handler = Api();

        var (code, output) = await Create(handler, new WorkflowCreateSettings
        {
            Name = "w", Trigger = "Timed", Cron = "0 9 * * *", EventEntity = "sources",
        });

        code.Should().Be(0);
        output.Should().Contain("--entity is ignored for a Timed trigger");
        SentBody(handler)["triggers"]![0]!["config"]!.AsObject().Select(p => p.Key).Should().Equal("cron_expression");
    }
}
