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
