using System.Text.Json;
using System.Text.Json.Nodes;
using AnythinkCli.Client;
using AnythinkCli.Commands;
using AnythinkCli.Models;
using FluentAssertions;

namespace AnythinkCli.Tests;

public class MigrateWorkflowTests
{
    private const string Org = "/org/99999";

    private static Workflow Source(string? legacyTrigger = null, JsonElement? options = null,
        List<WorkflowTrigger>? triggers = null, List<WorkflowStep>? steps = null) =>
        new(1, "wf", "d", legacyTrigger!, false, steps, options, null, triggers);

    // ── Rule: triggers[] on the source workflow are copied across as they are ──

    [Fact]
    public void Source_Triggers_Are_Copied_With_Type_Enabled_Flag_And_Config()
    {
        var wf = Source(triggers:
        [
            new("Timed", true,  new WorkflowTriggerConfig(CronExpression: "0 6 * * *")),
            new("Event", false, new WorkflowTriggerConfig(Event: "EntityCreated", EventEntity: "orders")),
        ]);

        var built = MigrateCommand.BuildTriggers(wf);

        built.Select(t => (t.Type, t.Enabled)).Should().Equal(("Timed", true), ("Event", false));
        built[0].Config.CronExpression.Should().Be("0 6 * * *");
        built[1].Config.EventEntity.Should().Be("orders");
    }

    [Fact]
    public void Trigger_Type_Is_Never_Null_For_A_Source_That_Reports_Triggers()
    {
        var wf = Source(triggers: [new("Api", true, new WorkflowTriggerConfig(ApiRoute: "hook"))]);

        MigrateCommand.BuildTriggers(wf).Single().Type.Should().Be("Api");
    }

    [Fact]
    public void Older_Sources_Fall_Back_To_Their_Single_Trigger_And_Options()
    {
        var options = JsonSerializer.SerializeToElement(new { cron_expression = "*/5 * * * *" });

        var built = MigrateCommand.BuildTriggers(Source(legacyTrigger: "Timed", options: options));

        built.Single().Type.Should().Be("Timed");
        built.Single().Config.CronExpression.Should().Be("*/5 * * * *");
    }

    [Fact]
    public void Source_With_No_Trigger_Information_Becomes_Manual()
    {
        MigrateCommand.BuildTriggers(Source()).Single().Type.Should().Be("Manual");
    }

    [Fact]
    public void Triggers_Deserialise_From_The_Api_Response_Shape()
    {
        var json = """{"id":1,"name":"wf","enabled":true,"steps":[],"triggers":[{"id":2,"type":"Timed","enabled":true,"config":{"cron_expression":"0 1 * * *"}}]}""";

        var wf = JsonSerializer.Deserialize<Workflow>(json)!;

        MigrateCommand.BuildTriggers(wf).Single().Config.CronExpression.Should().Be("0 1 * * *");
    }

    // ── Rule: wiring step links must not wipe the step's parameters ──

    [Fact]
    public async Task Linking_Steps_Re_Sends_The_Full_Step_So_Parameters_Survive()
    {
        var stepJson = """{"id":{0},"key":"k","name":"n","action":"RunScript","enabled":true,"is_start_step":true}""";
        var handler = new StubHttpHandler()
            .On(HttpMethod.Post, $"{Org}/workflows/40/steps", stepJson.Replace("{0}", "100"))
            .On(HttpMethod.Put, $"{Org}/workflows/40/steps/100", stepJson.Replace("{0}", "100"));
        var client = new AnythinkClient("99999", "https://api.example.com", new HttpClient(handler));
        var parameters = JsonSerializer.SerializeToElement(new { script = "keep()" });
        var src = Source(steps:
        [
            new(5, "k", "n", "desc", "RunScript", true, true, 5, null, parameters),
        ]);
        var errors = new List<string>();

        await MigrateCommand.CopyWorkflowStepsAsync(client, 40, src, errors);

        var put = JsonNode.Parse(handler.Matching(HttpMethod.Put, "/steps/100").Single().Body)!.AsObject();
        put["parameters"]!["script"]!.GetValue<string>().Should().Be("keep()");
        put["description"]!.GetValue<string>().Should().Be("desc");
        put["enabled"]!.GetValue<bool>().Should().BeTrue();
        put["is_start_step"]!.GetValue<bool>().Should().BeTrue();
        put["on_success_step_id"]!.GetValue<int>().Should().Be(100);
        errors.Should().BeEmpty();
    }
}
