using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using AnythinkCli.Commands;
using AnythinkCli.Models;
using FluentAssertions;

namespace AnythinkCli.Tests;

[Collection("ConsoleOutput")]
public class WorkflowsSeedCommandTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string WriteSeed(string json)
    {
        var path = Path.Combine(_dir, "workflow.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static RecordingHandler Api() =>
        new((_, _) => (HttpStatusCode.OK, """{"id":9,"name":"smoke","enabled":false,"steps":[]}"""));

    private static Task<(int Code, string Output)> Seed(RecordingHandler handler, string path) =>
        CommandRunner.RunAsync(handler, () => new WorkflowsSeedCommand().ExecuteAsync(null!, new WorkflowsSeedSettings { File = path }));

    [Fact]
    public async Task Seed_ManualTriggerWithNoEntities_FailsNamingTheWorkflowAndTheMissingField()
    {
        var handler = Api();

        var (code, output) = await Seed(handler, WriteSeed("""{"name":"smoke","trigger":"Manual","steps":[]}"""));

        code.Should().Be(1);
        handler.Requests.Should().BeEmpty();
        output.Should().Contain("Workflow 'smoke'").And.Contain("manual_entities");
    }

    [Fact]
    public async Task Seed_TriggerInTheListMissingItsConfig_FailsNamingTheWorkflowAndTheMissingField()
    {
        var handler = Api();
        var seed = WriteSeed("""
            {"name":"smoke","steps":[
              {"key":"a","action":"RunScript"}],
             "triggers":[{"type":"Timed","enabled":true,"config":{"cron_expression":"0 9 * * *"}},
                         {"type":"Api","enabled":true,"config":{}}]}
            """);

        var (code, output) = await Seed(handler, seed);

        code.Should().Be(1);
        handler.Requests.Should().BeEmpty();
        output.Should().Contain("Workflow 'smoke'").And.Contain("Api trigger has no api_route");
    }

    [Fact]
    public async Task Seed_Group_IsSentWithTheWorkflow()
    {
        var handler = Api();
        var seed = WriteSeed("""
            {"name":"smoke","group":"content","trigger":"Manual","options":{"manual_entities":["sources"]},"steps":[]}
            """);

        var (code, _) = await Seed(handler, seed);

        code.Should().Be(0);
        JsonNode.Parse(handler.Requests.Single(r => r.Method == HttpMethod.Post).Body)!["group"]!.GetValue<string>().Should().Be("content");
    }
}

public class WorkflowsMigrateRequestTests
{
    private static Workflow Parse(string json) => JsonSerializer.Deserialize<Workflow>(json)!;

    [Fact]
    public void BuildWorkflowRequest_ManualTriggerWithNoEntities_IsSkippedNamingTheWorkflowAndTheReason()
    {
        var wf = Parse("""{"id":1,"name":"old_default","enabled":true,"trigger":"Manual","options":{"manual_entities":[]}}""");

        var (_, skipReason) = MigrateCommand.BuildWorkflowRequest(wf);

        skipReason.Should().Be("Workflow 'old_default': Manual trigger has no entities (manual_entities needs at least one)");
    }

    [Fact]
    public void BuildWorkflowRequest_CompleteWorkflow_IsCopiedWithItsGroupAndAllTriggers()
    {
        var wf = Parse("""
            {"id":1,"name":"w","description":"d","group":"content","enabled":true,
             "triggers":[{"id":4,"type":"Timed","enabled":true,"config":{"cron_expression":"0 9 * * *"}},
                         {"id":5,"type":"Manual","enabled":false,"config":{"manual_entities":["sources"]}}]}
            """);

        var (request, skipReason) = MigrateCommand.BuildWorkflowRequest(wf);

        skipReason.Should().BeNull();
        request.Group.Should().Be("content");
        request.Enabled.Should().BeFalse();
        request.Triggers.Select(t => (t.Type, t.Enabled)).Should().Equal(("Timed", true), ("Manual", false));
    }

    [Fact]
    public void BuildWorkflowRequest_LegacyApiWorkflow_SendsTheTopLevelApiRoute()
    {
        var wf = Parse("""{"id":1,"name":"w","enabled":true,"trigger":"Api","api_route":"hooks/in"}""");

        var (request, skipReason) = MigrateCommand.BuildWorkflowRequest(wf);

        skipReason.Should().BeNull();
        JsonNode.Parse(JsonSerializer.Serialize(request.Triggers.Single().Config))!["api_route"]!.GetValue<string>().Should().Be("hooks/in");
    }
}

public class WorkflowsExportRoundTripTests
{
    private const string SeveralTriggers = """
        {"id":12,"tenant_id":1,"name":"fan_out","description":"d","group":"content","enabled":true,
         "editor_state":"{}","trigger":null,
         "triggers":[
           {"id":1,"tenant_id":1,"workflow_id":12,"type":"Timed","enabled":true,"config_json":"{}",
            "config":{"cron_expression":"0 9 * * *","last_run_at":"2026-10-06T09:00:00Z","next_run_at":"2026-10-07T09:00:00Z"}},
           {"id":2,"tenant_id":1,"workflow_id":12,"type":"Manual","enabled":true,"config_json":"{}",
            "config":{"manual_entities":["sources","blog_posts"]}},
           {"id":3,"tenant_id":1,"workflow_id":12,"type":"Api","enabled":false,"config_json":"{}",
            "config":{"api_route":"hooks/in"}},
           {"id":4,"tenant_id":1,"workflow_id":12,"type":"Event","enabled":true,"config_json":"{}",
            "config":{"event":"EntityCreated","event_entity":"blog_posts","filter":{"field":"status"}}}],
         "steps":[{"id":50,"workflow_id":12,"key":"only","name":"Only","action":"RunScript","enabled":true,
                   "is_start_step":true,"parameters_json":"{\"script\":\"return 1;\"}"}]}
        """;

    [Fact]
    public void Export_Triggers_LoseTheirStorageFieldsAndRunState()
    {
        var exported = JsonNode.Parse(WorkflowsExportCommand.TransformExport(SeveralTriggers))!;

        var triggers = exported["triggers"]!.AsArray();
        triggers.Should().HaveCount(4);
        foreach (var trigger in triggers.Select(t => t!.AsObject()))
        {
            trigger.Select(p => p.Key).Should().BeEquivalentTo("type", "enabled", "config");
            trigger["config"]!.AsObject().ContainsKey("last_run_at").Should().BeFalse();
            trigger["config"]!.AsObject().ContainsKey("next_run_at").Should().BeFalse();
        }
    }

    [Fact]
    public void ExportThenSeed_WorkflowWithSeveralTriggers_KeepsEveryTriggerAndTheGroup()
    {
        var exported = WorkflowsExportCommand.TransformExport(SeveralTriggers);

        var spec = JsonSerializer.Deserialize<WorkflowSeedSpec>(exported)!;
        var request = WorkflowsSeedCommand.BuildSeedRequest(spec, enabled: true);

        request.Group.Should().Be("content");
        request.Triggers.Select(t => (t.Type, t.Enabled)).Should()
            .Equal(("Timed", true), ("Manual", true), ("Api", false), ("Event", true));
        WorkflowTriggers.Problems(request.Triggers).Should().BeEmpty();
        var config = (JsonNode.Parse(JsonSerializer.Serialize(request.Triggers))!.AsArray());
        config[1]!["config"]!["manual_entities"]!.AsArray().Select(e => e!.GetValue<string>()).Should().Equal("sources", "blog_posts");
        config[2]!["config"]!["api_route"]!.GetValue<string>().Should().Be("hooks/in");
        config[3]!["config"]!["filter"]!["field"]!.GetValue<string>().Should().Be("status");
    }
}

public class WorkflowApiRoutesTests
{
    private static Workflow Parse(string json) => JsonSerializer.Deserialize<Workflow>(json)!;

    [Fact]
    public void WorkflowApiRoutes_ListsTheRealRouteOfEachEnabledApiTrigger()
    {
        var workflows = new[]
        {
            Parse("""
                {"id":1,"name":"intake","enabled":true,
                 "triggers":[{"type":"Api","enabled":true,"config":{"api_route":"hooks/in"}},
                             {"type":"Api","enabled":true,"config":{"api_route":"/hooks/other"}},
                             {"type":"Api","enabled":false,"config":{"api_route":"hooks/off"}},
                             {"type":"Manual","enabled":true,"config":{"manual_entities":["a"]}}]}
                """),
            Parse("""{"id":2,"name":"legacy","enabled":true,"trigger":"Api","api_route":"old/route"}"""),
        };

        var rows = ApiListCommand.WorkflowApiRoutes("https://api.example.com/org/1", workflows);

        rows.Should().Equal(
            ("https://api.example.com/org/1/workflows/api/hooks/in", "intake"),
            ("https://api.example.com/org/1/workflows/api/hooks/other", "intake"),
            ("https://api.example.com/org/1/workflows/api/old/route", "legacy"));
    }
}
