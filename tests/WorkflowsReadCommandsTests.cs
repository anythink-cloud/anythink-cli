using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using AnythinkCli.Commands;
using FluentAssertions;

namespace AnythinkCli.Tests;

internal static class WorkflowFixtures
{
    public const string Script = "if (a < b && name == 'x') { return items.map(i => i.id + ' <ok>'); } " +
                                 "// padding to make the script about one and a half kilobytes long ";

    public static string LongScript(int length = 1500) =>
        string.Concat(Enumerable.Repeat(Script, length / Script.Length + 1))[..length];

    // The API expands each step's on_success_step in full, so a linear chain repeats itself once per step.
    public static JsonObject Step(int index, int count, string script)
    {
        var parameters = new JsonObject { ["script"] = script };
        return new JsonObject
        {
            ["id"] = 100 + index,
            ["workflow_id"] = 7,
            ["tenant_id"] = 1,
            ["key"] = $"step_{index}",
            ["name"] = $"Step {index}",
            ["action"] = "RunScript",
            ["enabled"] = true,
            ["is_start_step"] = index == 0,
            ["parameters"] = parameters.DeepClone(),
            ["parameters_json"] = parameters.ToJsonString(),
            ["on_success_step_id"] = index < count - 1 ? 100 + index + 1 : null,
            ["on_failure_step_id"] = null,
            ["on_success_step"] = index < count - 1 ? Step(index + 1, count, script) : null,
            ["on_failure_step"] = null,
            ["created_at"] = "2026-10-01T09:00:00Z",
        };
    }

    public static JsonObject Workflow(int id, int stepCount, string script)
    {
        var steps = new JsonArray();
        for (var i = 0; i < stepCount; i++)
            steps.Add(Step(i, stepCount, script));

        return new JsonObject
        {
            ["id"] = id,
            ["tenant_id"] = 1,
            ["name"] = $"workflow_{id}",
            ["description"] = "A long chain",
            ["group"] = "content",
            ["enabled"] = true,
            ["editor_state"] = """{"nodePositions":{}}""",
            ["trigger"] = null,
            ["triggers"] = new JsonArray(new JsonObject
            {
                ["id"] = 3,
                ["tenant_id"] = 1,
                ["workflow_id"] = id,
                ["type"] = "Event",
                ["enabled"] = true,
                ["config_json"] = """{"event":"EntityCreated","event_entity":"research_items"}""",
                ["config"] = new JsonObject
                {
                    ["event"] = "EntityCreated",
                    ["event_entity"] = "research_items",
                    ["last_run_at"] = "2026-10-06T08:00:00Z",
                },
            }),
            ["steps"] = steps,
        };
    }
}

[Collection("ConsoleOutput")]
public class WorkflowsReadCommandsTests
{
    private static RecordingHandler ReturningJson(string json) => new((_, _) => (HttpStatusCode.OK, json));

    private static JsonObject FifteenStepWorkflow() => WorkflowFixtures.Workflow(7, 15, WorkflowFixtures.LongScript());

    // ── list ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListJson_FifteenStepWorkflow_PrintsASmallSummaryThatParses()
    {
        var raw = new JsonArray(FifteenStepWorkflow()).ToJsonString();
        raw.Length.Should().BeGreaterThan(200_000, "the fixture has to be as large as the real response that was cut off");

        var (code, output) = await CommandRunner.RunAsync(ReturningJson(raw),
            () => new WorkflowsListCommand().ExecuteAsync(null!, new WorkflowListSettings { Json = true }));

        code.Should().Be(0);
        output.Length.Should().BeLessThan(2_000);
        var summary = JsonNode.Parse(output)!.AsArray().Should().ContainSingle().Subject!.AsObject();
        summary.Select(p => p.Key).Should().Equal("id", "name", "description", "group", "enabled", "triggers", "step_count");
        summary["step_count"]!.GetValue<int>().Should().Be(15);
        summary["group"]!.GetValue<string>().Should().Be("content");
    }

    [Fact]
    public async Task ListJson_Triggers_ShowTypeEnabledAndConfigWithoutRunState()
    {
        var raw = new JsonArray(FifteenStepWorkflow()).ToJsonString();

        var (_, output) = await CommandRunner.RunAsync(ReturningJson(raw),
            () => new WorkflowsListCommand().ExecuteAsync(null!, new WorkflowListSettings { Json = true }));

        var trigger = JsonNode.Parse(output)![0]!["triggers"]!.AsArray().Should().ContainSingle().Subject!.AsObject();
        trigger["type"]!.GetValue<string>().Should().Be("Event");
        trigger["enabled"]!.GetValue<bool>().Should().BeTrue();
        trigger["config"]!["event_entity"]!.GetValue<string>().Should().Be("research_items");
        trigger["config"]!.AsObject().ContainsKey("last_run_at").Should().BeFalse();
    }

    [Fact]
    public async Task List_ResponseWithOnlyTriggers_ShowsEachWorkflowsTriggerSummary()
    {
        var raw = new JsonArray(WorkflowFixtures.Workflow(7, 2, "return 1;")).ToJsonString();

        var (code, output) = await CommandRunner.RunAsync(ReturningJson(raw),
            () => new WorkflowsListCommand().ExecuteAsync(null!, new WorkflowListSettings()));

        code.Should().Be(0);
        output.Should().Contain("workflow_7").And.Contain("Event: EntityCreated on research_items");
    }

    // ── get ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetJson_FifteenStepWorkflow_DropsNestedStepsAndKeepsIds()
    {
        var raw = FifteenStepWorkflow().ToJsonString();

        var (code, output) = await CommandRunner.RunAsync(ReturningJson(raw),
            () => new WorkflowsGetCommand().ExecuteAsync(null!, new WorkflowGetSettings { Id = 7, Json = true }));

        code.Should().Be(0);
        output.Length.Should().BeLessThan(40_000);
        var workflow = JsonNode.Parse(output)!.AsObject();
        workflow["id"]!.GetValue<int>().Should().Be(7);
        workflow["triggers"]![0]!["id"]!.GetValue<int>().Should().Be(3);

        var steps = workflow["steps"]!.AsArray();
        steps.Should().HaveCount(15);
        steps[0]!["id"]!.GetValue<int>().Should().Be(100);
        steps[0]!["on_success_step_id"]!.GetValue<int>().Should().Be(101);
        steps.All(s => s!.AsObject().ContainsKey("on_success_step") == false
                       && s.AsObject().ContainsKey("on_failure_step") == false).Should().BeTrue();
    }

    [Fact]
    public async Task GetJson_StripsWhatExportStripsExceptIds()
    {
        var raw = FifteenStepWorkflow().ToJsonString();

        var (_, output) = await CommandRunner.RunAsync(ReturningJson(raw),
            () => new WorkflowsGetCommand().ExecuteAsync(null!, new WorkflowGetSettings { Id = 7, Json = true }));

        var workflow = JsonNode.Parse(output)!.AsObject();
        workflow.ContainsKey("tenant_id").Should().BeFalse();
        workflow.ContainsKey("editor_state").Should().BeFalse();
        var step = workflow["steps"]![0]!.AsObject();
        step.ContainsKey("parameters_json").Should().BeFalse("parameters is already there, parsed");
        step.ContainsKey("workflow_id").Should().BeFalse();
        step["parameters"]!["script"]!.GetValue<string>().Should().StartWith("if (a < b");
        var trigger = workflow["triggers"]![0]!.AsObject();
        trigger.ContainsKey("config_json").Should().BeFalse();
        trigger.ContainsKey("workflow_id").Should().BeFalse();
    }

    [Fact]
    public async Task GetJson_TriggersKeepTheirLastAndNextRunSoAnAssistantCanSayWhenItRunsNext()
    {
        var workflow = FifteenStepWorkflow();
        workflow["triggers"]![0]!["config"]!["next_run_at"] = "2026-10-08T08:00:00Z";

        var (_, output) = await CommandRunner.RunAsync(ReturningJson(workflow.ToJsonString()),
            () => new WorkflowsGetCommand().ExecuteAsync(null!, new WorkflowGetSettings { Id = 7, Json = true }));

        var config = JsonNode.Parse(output)!["triggers"]![0]!["config"]!;
        config["last_run_at"]!.GetValue<string>().Should().Be("2026-10-06T08:00:00Z");
        config["next_run_at"]!.GetValue<string>().Should().Be("2026-10-08T08:00:00Z");
    }

    [Fact]
    public async Task GetJson_ScriptsAreNotEscaped()
    {
        var raw = FifteenStepWorkflow().ToJsonString();

        var (_, output) = await CommandRunner.RunAsync(ReturningJson(raw),
            () => new WorkflowsGetCommand().ExecuteAsync(null!, new WorkflowGetSettings { Id = 7, Json = true }));

        output.Should().Contain("name == 'x'").And.Contain("<ok>").And.NotContain("\\u0027").And.NotContain("\\u003C");
    }

    [Fact]
    public async Task Get_TwoTriggersOfTheSameType_AreLabelledDistinctly()
    {
        var workflow = WorkflowFixtures.Workflow(7, 1, "return 1;");
        var second = workflow["triggers"]![0]!.DeepClone().AsObject();
        second["id"] = 4;
        workflow["triggers"]![0]!["config"]!["filter"] = JsonNode.Parse("""{"field":"a"}""");
        second["config"]!["filter"] = JsonNode.Parse("""{"field":"b"}""");
        workflow["triggers"]!.AsArray().Add(second);

        var (_, output) = await CommandRunner.RunAsync(ReturningJson(workflow.ToJsonString()),
            () => new WorkflowsGetCommand().ExecuteAsync(null!, new WorkflowGetSettings { Id = 7 }));

        output.Should().Contain("Filter (trigger 1, Event)").And.Contain("Filter (trigger 2, Event)");
    }
}
