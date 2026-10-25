using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AnythinkCli.Commands;
using AnythinkCli.Models;
using FluentAssertions;

namespace AnythinkCli.Tests;

public class WorkflowTriggersTests
{
    private static readonly JsonSerializerOptions ClientJsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull
    };

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    // ── Flags → one trigger ──────────────────────────────────────────────────

    [Fact]
    public void FromFlags_Manual_WithoutEntity_FailsBecauseItCouldNeverRun()
    {
        var act = () => WorkflowTriggers.FromFlags("Manual", null, [], null, null, null);
        act.Should().Throw<WorkflowInputException>().WithMessage("*Manual*--entity*");
    }

    [Fact]
    public void FromFlags_Manual_MapsEveryEntityToManualEntities()
    {
        var trigger = WorkflowTriggers.FromFlags("manual", null, ["blog_posts", "pages"], null, null, null);

        trigger.Type.Should().Be("Manual");
        trigger.Enabled.Should().BeTrue();
        trigger.Config.ManualEntities.Should().Equal("blog_posts", "pages");
        trigger.Config.EventEntity.Should().BeNull();
    }

    [Fact]
    public void FromFlags_Event_MapsEventEntityAndFilter()
    {
        var filter = Json("""{"field":"status","op":"changed_to","value":"published"}""");
        var trigger = WorkflowTriggers.FromFlags("Event", "EntityUpdated", ["posts"], null, null, filter);

        trigger.Config.Event.Should().Be("EntityUpdated");
        trigger.Config.EventEntity.Should().Be("posts");
        trigger.Config.Filter!.Value.GetProperty("value").GetString().Should().Be("published");
    }

    [Theory]
    [InlineData("EntityCreated")]
    [InlineData("EntityUpdated")]
    [InlineData("EntityDeleted")]
    public void FromFlags_EntityEvent_WithoutEntity_Fails(string evt)
    {
        var act = () => WorkflowTriggers.FromFlags("Event", evt, [], null, null, null);
        act.Should().Throw<WorkflowInputException>().WithMessage("*--entity*");
    }

    [Fact]
    public void FromFlags_NonEntityEvent_DoesNotNeedAnEntity()
    {
        var trigger = WorkflowTriggers.FromFlags("Event", "UserRegistered", [], null, null, null);
        trigger.Config.Event.Should().Be("UserRegistered");
    }

    [Fact]
    public void FromFlags_Event_WithoutEvent_Fails()
    {
        var act = () => WorkflowTriggers.FromFlags("Event", null, ["posts"], null, null, null);
        act.Should().Throw<WorkflowInputException>().WithMessage("*--event*");
    }

    [Fact]
    public void FromFlags_Timed_WithoutCron_Fails()
    {
        var act = () => WorkflowTriggers.FromFlags("Timed", null, [], null, null, null);
        act.Should().Throw<WorkflowInputException>().WithMessage("*--cron*");
    }

    [Fact]
    public void FromFlags_Api_WithoutRoute_Fails()
    {
        var act = () => WorkflowTriggers.FromFlags("Api", null, [], null, null, null);
        act.Should().Throw<WorkflowInputException>().WithMessage("*--api-route*");
    }

    [Fact]
    public void FromFlags_Filter_OnlyAppliesToEventTriggers()
    {
        var act = () => WorkflowTriggers.FromFlags("Timed", null, [], "0 9 * * *", null, Json("""{"field":"a","op":"eq","value":1}"""));
        act.Should().Throw<WorkflowInputException>().WithMessage("*--filter*Event*");
    }

    [Fact]
    public void FromFlags_UnknownType_Fails()
    {
        var act = () => WorkflowTriggers.FromFlags("EntityCreated", null, ["posts"], null, null, null);
        act.Should().Throw<WorkflowInputException>().WithMessage("*unknown trigger type*");
    }

    // ── Settings → trigger list ──────────────────────────────────────────────

    [Fact]
    public void FromSettings_NoTriggerFlags_ReturnsNull_SoUpdateKeepsExistingTriggers()
        => WorkflowTriggers.FromSettings(new WorkflowTriggerFlagSettings()).Should().BeNull();

    [Fact]
    public void FromSettings_TriggerAndTriggers_AreMutuallyExclusive()
    {
        var act = () => WorkflowTriggers.FromSettings(new WorkflowTriggerFlagSettings
        {
            Trigger = "Timed", Cron = "0 9 * * *", Triggers = "[]"
        });
        act.Should().Throw<WorkflowInputException>().WithMessage("*either --trigger or --triggers*");
    }

    [Fact]
    public void FromSettings_DetailFlagsWithoutTrigger_Fail()
    {
        var act = () => WorkflowTriggers.FromSettings(new WorkflowTriggerFlagSettings { Cron = "0 9 * * *" });
        act.Should().Throw<WorkflowInputException>().WithMessage("*need --trigger*");
    }

    [Fact]
    public void FromSettings_TriggersJson_MapsEveryTrigger()
    {
        var triggers = WorkflowTriggers.FromSettings(new WorkflowTriggerFlagSettings
        {
            Triggers = """
                [
                  {"type":"Timed","enabled":false,"config":{"cron_expression":"0 9 * * *"}},
                  {"type":"Event","config":{"event":"EntityCreated","event_entity":"orders","filter":{"field":"total","op":"gt","value":100}}}
                ]
                """
        })!;

        triggers.Should().HaveCount(2);
        triggers[0].Should().Match<WorkflowTriggerRequest>(t => t.Type == "Timed" && !t.Enabled);
        triggers[1].Enabled.Should().BeTrue("enabled defaults to true when omitted");
        triggers[1].Config.Filter!.Value.GetProperty("op").GetString().Should().Be("gt");
    }

    [Fact]
    public void FromSettings_FilterFile_IsReadForEventTrigger()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """{"field":"status","op":"eq","value":"live"}""");
            var triggers = WorkflowTriggers.FromSettings(new WorkflowTriggerFlagSettings
            {
                Trigger = "Event", Event = "EntityUpdated", Entities = ["posts"], FilterFile = path
            })!;

            triggers.Single().Config.Filter!.Value.GetProperty("value").GetString().Should().Be("live");
        }
        finally { File.Delete(path); }
    }

    // ── JSON triggers (export shape) ─────────────────────────────────────────

    [Fact]
    public void FromJson_MissingType_FailsLoudly_NamingTheTrigger()
    {
        var act = () => WorkflowTriggers.FromJson(Json("""[{"type":"Timed","config":{"cron_expression":"* * * * *"}},{"config":{}}]"""), "triggers");
        act.Should().Throw<WorkflowInputException>().WithMessage("triggers[1]*type is required*");
    }

    [Fact]
    public void FromJson_EventWithoutEntity_FailsLoudly_InsteadOfSeedingANullTrigger()
    {
        var act = () => WorkflowTriggers.FromJson(Json("""[{"type":"Event","config":{"event":"EntityCreated"}}]"""), "triggers");
        act.Should().Throw<WorkflowInputException>().WithMessage("triggers[0]*config.event_entity*");
    }

    [Fact]
    public void FromJson_ReadsConfigJson_WhenConfigIsMissing()
    {
        var triggers = WorkflowTriggers.FromJson(
            Json("""[{"type":"Api","enabled":true,"config_json":"{\"api_route\":\"hook\"}"}]"""), "triggers");

        triggers.Single().Config.ApiRoute.Should().Be("hook");
    }

    [Fact]
    public void FromJson_DropsSchedulerRunState()
    {
        var triggers = WorkflowTriggers.FromJson(Json("""
            [{"type":"Timed","config":{"cron_expression":"0 9 * * *","last_run_at":"2020-01-01T09:00:00Z","next_run_at":"2020-01-02T09:00:00Z"}}]
            """), "triggers");

        triggers.Single().Config.LastRunAt.Should().BeNull();
        triggers.Single().Config.NextRunAt.Should().BeNull();
    }

    [Fact]
    public void FromJson_NotAnArray_Fails()
    {
        var act = () => WorkflowTriggers.FromJson(Json("""{"type":"Manual"}"""), "triggers");
        act.Should().Throw<WorkflowInputException>().WithMessage("*must be a JSON array*");
    }

    // ── Legacy seed shape ────────────────────────────────────────────────────

    [Fact]
    public void FromLegacy_MapsTriggerAndOptions()
    {
        var trigger = WorkflowTriggers.FromLegacy("Event",
            Json("""{"event":"EntityUpdated","event_entity":"posts","filter":{"field":"status","op":"eq","value":"live"}}"""), null);

        trigger.Type.Should().Be("Event");
        trigger.Config.Event.Should().Be("EntityUpdated");
        trigger.Config.EventEntity.Should().Be("posts");
        trigger.Config.Filter.Should().NotBeNull();
    }

    [Fact]
    public void FromLegacy_UsesTopLevelApiRoute()
        => WorkflowTriggers.FromLegacy("Api", null, "hook").Config.ApiRoute.Should().Be("hook");

    [Fact]
    public void FromLegacy_ManualWithEventEntity_BecomesManualEntities()
        => WorkflowTriggers.FromLegacy("Manual", Json("""{"event_entity":"posts"}"""), null)
            .Config.ManualEntities.Should().Equal("posts");

    // ── Seed ─────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildSeed_AcceptsLegacyTriggerAndOptions()
    {
        var (request, steps) = WorkflowsSeedCommand.BuildSeed("""
            {"name":"legacy","trigger":"Timed","options":{"cron_expression":"0 6 * * *"},"steps":[]}
            """, enabledOverride: null);

        request.Triggers.Single().Should().Match<WorkflowTriggerRequest>(t => t.Type == "Timed" && t.Config.CronExpression == "0 6 * * *");
        steps.Should().BeEmpty();
    }

    [Fact]
    public void BuildSeed_WithoutAnyTrigger_FailsLoudly()
    {
        var act = () => WorkflowsSeedCommand.BuildSeed("""{"name":"x","steps":[]}""", enabledOverride: null);
        act.Should().Throw<WorkflowInputException>().WithMessage("*no triggers*");
    }

    [Fact]
    public void BuildSeed_ExplicitEmptyTriggers_IsAllowed()
        => WorkflowsSeedCommand.BuildSeed("""{"name":"x","triggers":[],"steps":[]}""", enabledOverride: null)
            .Request.Triggers.Should().BeEmpty();

    [Fact]
    public void BuildSeed_TriggersArray_WinsOverLegacyFields()
    {
        var (request, _) = WorkflowsSeedCommand.BuildSeed("""
            {"name":"x","trigger":"Manual","triggers":[{"type":"Api","config":{"api_route":"hook"}}],"steps":[]}
            """, enabledOverride: null);

        request.Triggers.Single().Type.Should().Be("Api");
    }

    [Fact]
    public void BuildSeed_EnabledOverride_WinsOverFile()
        => WorkflowsSeedCommand.BuildSeed("""{"name":"x","enabled":false,"triggers":[],"steps":[]}""", enabledOverride: true)
            .Request.Enabled.Should().BeTrue();

    // ── Update ───────────────────────────────────────────────────────────────

    private static Workflow CurrentWorkflow() => JsonSerializer.Deserialize<Workflow>("""
        {
          "id": 31, "name": "Report", "description": "Weekly", "group": "Ops", "enabled": true,
          "editor_state": "{\"nodePositions\":{\"a\":{\"x\":1,\"y\":2}}}",
          "triggers": [
            {"id": 7, "type": "Timed", "enabled": true,
             "config": {"cron_expression": "0 9 * * 1", "last_run_at": "2026-09-07T09:00:00Z", "next_run_at": "2026-09-21T09:00:00Z"}},
            {"id": 8, "type": "Event", "enabled": false,
             "config": {"event": "EntityUpdated", "event_entity": "reports", "filter": {"field": "status", "op": "eq", "value": "ready"}}}
          ]
        }
        """, ClientJsonOpts)!;

    [Fact]
    public void UpdateBuildRequest_NameOnly_ResendsEverythingElseUnchanged()
    {
        var current = CurrentWorkflow();
        var body = JsonNode.Parse(JsonSerializer.Serialize(
            WorkflowsUpdateCommand.BuildRequest(current, "Renamed", null, null), ClientJsonOpts))!;

        body["name"]!.GetValue<string>().Should().Be("Renamed");
        body["description"]!.GetValue<string>().Should().Be("Weekly");
        body["group"]!.GetValue<string>().Should().Be("Ops");
        body["enabled"]!.GetValue<bool>().Should().BeTrue();
        body["editor_state"]!.GetValue<string>().Should().Be(current.EditorState);

        var triggers = body["triggers"]!.AsArray();
        triggers.Should().HaveCount(2, "a rename must not wipe the triggers");
        triggers[0]!["config"]!["cron_expression"]!.GetValue<string>().Should().Be("0 9 * * 1");
        triggers[0]!["config"]!["next_run_at"]!.GetValue<string>().Should().Be("2026-09-21T09:00:00Z", "an untouched schedule keeps its place");
        triggers[1]!["enabled"]!.GetValue<bool>().Should().BeFalse();
        triggers[1]!["config"]!["filter"]!["value"]!.GetValue<string>().Should().Be("ready");
    }

    [Fact]
    public void UpdateBuildRequest_WithTriggers_ReplacesThemAndKeepsTheRest()
    {
        var current = CurrentWorkflow();
        var replacement = WorkflowTriggers.FromFlags("Event", "EntityCreated", ["orders"], null, null,
            Json("""{"field":"total","op":"gt","value":100}"""));

        var body = JsonNode.Parse(JsonSerializer.Serialize(
            WorkflowsUpdateCommand.BuildRequest(current, null, null, [replacement]), ClientJsonOpts))!;

        body["name"]!.GetValue<string>().Should().Be("Report");
        body["group"]!.GetValue<string>().Should().Be("Ops");
        var trigger = body["triggers"]!.AsArray().Should().ContainSingle().Subject!;
        trigger["type"]!.GetValue<string>().Should().Be("Event");
        trigger["config"]!["event_entity"]!.GetValue<string>().Should().Be("orders");
        trigger["config"]!["filter"]!["op"]!.GetValue<string>().Should().Be("gt");
        body.AsObject().ContainsKey("trigger").Should().BeFalse();
        body.AsObject().ContainsKey("options").Should().BeFalse();
    }

    // ── Manual run request ───────────────────────────────────────────────────

    [Fact]
    public void BuildRunRequest_SendsPayloadObjectAsJsonString_WithSnakeCaseEntityFields()
    {
        var request = WorkflowTriggers.BuildRunRequest("""{ "title": "Hello", "tags": ["a"] }""", "blog_posts", 12);
        var body = JsonNode.Parse(JsonSerializer.Serialize(request, ClientJsonOpts))!.AsObject();

        body.Select(p => p.Key).Should().BeEquivalentTo("entity_name", "entity_id", "data");
        body["entity_name"]!.GetValue<string>().Should().Be("blog_posts");
        body["entity_id"]!.GetValue<int>().Should().Be(12);
        var data = JsonNode.Parse(body["data"]!.GetValue<string>())!;
        data["title"]!.GetValue<string>().Should().Be("Hello");
    }

    [Theory]
    [InlineData("[1,2]")]
    [InlineData("\"text\"")]
    [InlineData("42")]
    public void BuildRunRequest_NonObjectPayload_Fails(string payload)
    {
        var act = () => WorkflowTriggers.BuildRunRequest(payload, null, null);
        act.Should().Throw<WorkflowInputException>().WithMessage("*JSON object*");
    }

    [Fact]
    public void BuildRunRequest_InvalidJson_Fails()
    {
        var act = () => WorkflowTriggers.BuildRunRequest("{not json", null, null);
        act.Should().Throw<WorkflowInputException>().WithMessage("Invalid --payload JSON*");
    }

    [Fact]
    public void BuildRunRequest_NoArguments_SendsEmptyBody()
        => JsonSerializer.Serialize(WorkflowTriggers.BuildRunRequest(null, null, null), ClientJsonOpts).Should().Be("{}");

    // ── list ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ListRow_CurrentApiShape_WithoutLegacyTriggerField_DoesNotThrow()
    {
        var workflows = JsonSerializer.Deserialize<List<Workflow>>("""
            [
              {"id": 1, "name": "No triggers", "description": null, "enabled": false, "triggers": [], "steps": []},
              {"id": 2, "name": "Null triggers", "enabled": true},
              {"id": 3, "name": "Multi", "enabled": true, "triggers": [
                {"id": 1, "type": "Timed", "enabled": true, "config": {"cron_expression": "0 9 * * *"}},
                {"id": 2, "type": "Event", "enabled": false, "config": {"event": "EntityCreated", "event_entity": "orders"}},
                {"id": 3, "type": null, "enabled": true, "config": null}
              ]}
            ]
            """, ClientJsonOpts)!;

        var rows = workflows.Select(WorkflowsListCommand.BuildRow).ToList();

        rows[0][2].Should().Be("—");
        rows[1][2].Should().Be("—");
        rows[2][2].Should().Be("Timed (0 9 * * *), Event (EntityCreated orders) disabled, ?");
    }

    // ── jobs ─────────────────────────────────────────────────────────────────

    [Fact]
    public void JobLines_PrintEachStepWithKeyAndOutcome()
    {
        var job = JsonSerializer.Deserialize<WorkflowJob>("""
            {
              "id": 5, "status": "Failed", "created_at": "2026-03-20T10:00:00Z", "error_message": "send_email failed",
              "steps": [
                {"id": 12, "workflow_step_id": 9, "success": false, "error_message": "SMTP timeout",
                 "workflow_step": {"id": 9, "key": "send_email", "name": "Send", "action": "SendAnEmail"}},
                {"id": 11, "workflow_step_id": 8, "success": true, "log": "Fetched 42 rows",
                 "workflow_step": {"id": 8, "key": "fetch_data", "name": "Fetch", "action": "ReadData"}},
                {"id": 13, "workflow_step_id": 10, "success": true}
              ]
            }
            """, ClientJsonOpts)!;

        var lines = WorkflowsJobsCommand.BuildJobLines(job);

        lines[0].Should().Contain("Job #5").And.Contain("Failed").And.Contain("2026-03-20T10:00:00Z");
        lines.Should().ContainSingle(l => l.Contains("send_email failed"));
        var stepLines = lines.Where(l => l.Contains('●')).ToList();
        stepLines.Should().HaveCount(3);
        stepLines[0].Should().Contain("fetch_data").And.Contain("Success");
        stepLines[1].Should().Contain("send_email").And.Contain("Failed");
        stepLines[2].Should().Contain("step 10");
        lines.Should().Contain(l => l.Contains("SMTP timeout"));
        lines.Should().Contain(l => l.Contains("Fetched 42 rows"));
    }
}
