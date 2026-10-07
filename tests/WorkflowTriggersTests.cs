using System.Text.Json;
using System.Text.Json.Nodes;
using AnythinkCli.Commands;
using AnythinkCli.Models;
using FluentAssertions;

namespace AnythinkCli.Tests;

public class WorkflowTriggersTests
{
    private static Workflow Parse(string json) => JsonSerializer.Deserialize<Workflow>(json)!;

    // ── Reading the triggers list ───────────────────────────────────────────

    [Fact]
    public void Summary_TriggersListWithNoSingularTrigger_IsSummarisedWithoutError()
    {
        var wf = Parse("""
            {"id":77,"name":"draft_generation","enabled":true,"trigger":null,
             "triggers":[{"id":8,"type":"Event","enabled":true,
               "config":{"event":"EntityCreated","event_entity":"research_items","manual_entities":[]}}]}
            """);

        WorkflowTriggers.Summary(wf).Should().Be("Event: EntityCreated on research_items");
    }

    [Fact]
    public void Summary_SeveralTriggers_AreJoinedInOrder()
    {
        var wf = Parse("""
            {"id":1,"name":"w","enabled":true,
             "triggers":[
               {"type":"Timed","enabled":true,"config":{"cron_expression":"0 9 * * *"}},
               {"type":"Manual","enabled":true,"config":{"manual_entities":["sources","blog_posts"]}},
               {"type":"Api","enabled":false,"config":{"api_route":"/hooks/in"}}]}
            """);

        WorkflowTriggers.Summary(wf).Should().Be("Timed: 0 9 * * *; Manual on sources, blog_posts; Api: /hooks/in (disabled)");
    }

    [Fact]
    public void Summary_LegacyResponseWithOnlySingularTrigger_IsStillShown()
    {
        var wf = Parse("""{"id":1,"name":"w","enabled":true,"trigger":"Timed","options":{"cron_expression":"0 6 * * *"}}""");

        WorkflowTriggers.Summary(wf).Should().Be("Timed: 0 6 * * *");
    }

    [Fact]
    public void Summary_NoTriggerAtAll_IsADash()
    {
        WorkflowTriggers.Summary(Parse("""{"id":1,"name":"w","enabled":true}""")).Should().Be("-");
    }

    // ── API routes ──────────────────────────────────────────────────────────

    [Fact]
    public void HasEnabledApiTrigger_FindsAnApiTriggerInTheList()
    {
        var wf = Parse("""
            {"id":1,"name":"w","enabled":true,
             "triggers":[{"type":"Manual","enabled":true},{"type":"Api","enabled":true,"config":{"api_route":"/in"}}]}
            """);

        WorkflowTriggers.HasEnabledApiTrigger(wf).Should().BeTrue();
    }

    [Fact]
    public void HasEnabledApiTrigger_IgnoresADisabledApiTrigger()
    {
        var wf = Parse("""{"id":1,"name":"w","enabled":true,"triggers":[{"type":"Api","enabled":false}]}""");

        WorkflowTriggers.HasEnabledApiTrigger(wf).Should().BeFalse();
    }

    // ── Creating and seeding ────────────────────────────────────────────────

    [Fact]
    public void BuildSeedRequest_LegacyManualTrigger_KeepsManualEntitiesInTheTriggerConfig()
    {
        var spec = JsonSerializer.Deserialize<WorkflowSeedSpec>("""
            {"schema_version":1,"name":"smoke","trigger":"Manual","options":{"manual_entities":["sources"]},"steps":[]}
            """)!;

        var body = JsonNode.Parse(JsonSerializer.Serialize(WorkflowsSeedCommand.BuildSeedRequest(spec, true)))!;

        body["trigger"].Should().BeNull();
        body["options"].Should().BeNull();
        var trigger = body["triggers"]!.AsArray().Should().ContainSingle().Subject!;
        trigger["type"]!.GetValue<string>().Should().Be("Manual");
        trigger["config"]!["manual_entities"]!.AsArray().Select(e => e!.GetValue<string>()).Should().Equal("sources");
    }

    [Fact]
    public void ForRequest_DropsTheSourceProjectsRunState()
    {
        var wf = Parse("""
            {"id":1,"name":"w","enabled":true,
             "triggers":[{"type":"Timed","enabled":true,
               "config":{"cron_expression":"0 9 * * *","last_run_at":"2026-10-01T09:00:00Z","next_run_at":"2026-10-07T09:00:00Z"}}]}
            """);

        var config = JsonNode.Parse(JsonSerializer.Serialize(WorkflowTriggers.ForRequest(wf).Single().Config))!.AsObject();

        config.Select(p => p.Key).Should().Equal("cron_expression");
    }

    // ── Older servers keep the API route as a top-level field ───────────────

    [Fact]
    public void Summary_LegacyApiWorkflow_ShowsTheTopLevelApiRoute()
    {
        var wf = Parse("""{"id":1,"name":"w","enabled":true,"trigger":"Api","api_route":"hooks/in"}""");

        WorkflowTriggers.Summary(wf).Should().Be("Api: /hooks/in");
    }

    [Fact]
    public void ForRequest_LegacyApiWorkflow_SendsTheTopLevelApiRouteInTheTriggerConfig()
    {
        var wf = Parse("""{"id":1,"name":"w","enabled":true,"trigger":"Api","api_route":"hooks/in"}""");

        var trigger = WorkflowTriggers.ForRequest(wf).Single();

        JsonNode.Parse(JsonSerializer.Serialize(trigger.Config))!["api_route"]!.GetValue<string>().Should().Be("hooks/in");
    }

    // ── Required trigger config ─────────────────────────────────────────────

    [Theory]
    [InlineData("Manual", """{"manual_entities":[]}""", "manual_entities")]
    [InlineData("Manual", """{}""", "manual_entities")]
    [InlineData("Manual", """{"manual_entities":["sources"]}""", null)]
    [InlineData("Api", """{"api_route":""}""", "api_route")]
    [InlineData("Api", """{"api_route":"hooks/in"}""", null)]
    [InlineData("Timed", """{}""", "cron_expression")]
    [InlineData("Timed", """{"cron_expression":"0 9 * * *"}""", null)]
    [InlineData("Event", """{"event":"EntityCreated","event_entity":""}""", "event_entity")]
    [InlineData("Event", """{"event":"EntityDeleted"}""", "event_entity")]
    [InlineData("Event", """{"event":"EntityCreated","event_entity":"blog_posts"}""", null)]
    [InlineData("Event", """{"event":"UserRegistered"}""", null)]
    [InlineData("Event", """{"event_entity":"blog_posts"}""", "event")]
    [InlineData("manual", """{}""", "manual_entities")]
    public void MissingField_NamesTheConfigTheTriggerTypeRequires(string type, string config, string? expected)
    {
        var trigger = new WorkflowTriggerRequest(type, true, JsonNode.Parse(config)!);

        WorkflowTriggers.MissingField(trigger).Should().Be(expected);
    }

    [Fact]
    public void Problems_ManualTriggerWithNoEntities_SaysSo()
    {
        var wf = Parse("""{"id":1,"name":"w","enabled":true,"triggers":[{"type":"Manual","enabled":true,"config":{"manual_entities":[]}}]}""");

        WorkflowTriggers.Problems(WorkflowTriggers.ForRequest(wf)).Should()
            .Equal("Manual trigger has no entities (manual_entities needs at least one)");
    }

    [Theory]
    [InlineData("timed", "Timed")]
    [InlineData(" API ", "Api")]
    [InlineData("EVENT", "Event")]
    [InlineData("Webhook", null)]
    public void CanonicalType_MatchesCaseInsensitively(string input, string? expected)
    {
        WorkflowTriggers.CanonicalType(input).Should().Be(expected);
    }

    [Fact]
    public void ForRequest_KeepsRunStateOnlyWhenAskedTo()
    {
        var wf = Parse("""
            {"id":1,"name":"w","enabled":true,
             "triggers":[{"type":"Timed","enabled":true,"config":{"cron_expression":"0 9 * * *","next_run_at":"2026-10-07T09:00:00Z"}}]}
            """);

        var kept = JsonNode.Parse(JsonSerializer.Serialize(WorkflowTriggers.ForRequest(wf, keepRunState: true).Single().Config))!.AsObject();

        kept.Select(p => p.Key).Should().Equal("cron_expression", "next_run_at");
    }
}
