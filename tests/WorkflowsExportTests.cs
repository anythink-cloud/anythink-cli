using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AnythinkCli.Commands;
using FluentAssertions;

namespace AnythinkCli.Tests;

public class WorkflowsExportTests
{
    private const string SampleWorkflowJson = """
    {
      "id": 93,
      "tenant_id": 1234,
      "name": "AI Social Post Generator",
      "description": "When a blog post is reviewed, generate a tweet",
      "group": "Marketing",
      "enabled": true,
      "editor_state": "{\"nodePositions\":{}}",
      "created_at": "2026-03-26T23:08:27.891Z",
      "updated_at": "2026-04-01T10:00:00.000Z",
      "triggers": [
        {
          "id": 501, "tenant_id": 1234, "workflow_id": 93, "locked": false,
          "created_at": "2026-03-26T23:08:27.891Z", "updated_at": "2026-03-26T23:08:27.891Z",
          "created_by": 1, "updated_by": 1, "workflow": null,
          "type": "Event",
          "enabled": true,
          "config_json": "{\"event\":\"EntityUpdated\",\"event_entity\":\"blog_posts\"}",
          "config": {
            "event": "EntityUpdated",
            "event_entity": "blog_posts",
            "manual_entities": null,
            "cron_expression": null,
            "last_run_at": null,
            "next_run_at": null,
            "api_route": null,
            "filter": {
              "and": [
                { "field": "status", "op": "changed_to", "value": "reviewed" },
                { "not": { "field": "word_count", "op": "lt", "value": 100 } }
              ]
            }
          }
        },
        {
          "id": 502, "workflow_id": 93,
          "type": "Timed",
          "enabled": false,
          "config": {
            "cron_expression": "0 9 * * 1",
            "last_run_at": "2026-04-06T09:00:00Z",
            "next_run_at": "2026-04-13T09:00:00Z",
            "timezone": "Europe/London"
          }
        },
        {
          "id": 503, "workflow_id": 93,
          "type": "Manual",
          "enabled": true,
          "config": { "manual_entities": ["blog_posts", "pages"] }
        },
        {
          "id": 504, "workflow_id": 93,
          "type": "Api",
          "enabled": true,
          "config": { "api_route": "generate-tweet" }
        }
      ],
      "steps": [
        {
          "id": 234,
          "workflow_id": 93,
          "key": "check_reviewed",
          "name": "Check Status is Reviewed",
          "description": null,
          "enabled": true,
          "action": "Condition",
          "parameters_json": "{\"logical_operator\":\"AND\",\"filter_conditions\":[]}",
          "is_start_step": true,
          "on_success_step_id": 235,
          "on_failure_step_id": null,
          "on_success_step": { "id": 235, "key": "read_blog_post" }
        },
        {
          "id": 235,
          "workflow_id": 93,
          "key": "read_blog_post",
          "name": "Read Blog Post",
          "enabled": true,
          "action": "ReadData",
          "parameters_json": "{\"entity\":\"blog_posts\"}",
          "is_start_step": false,
          "on_success_step_id": null,
          "on_failure_step_id": null
        }
      ]
    }
    """;

    [Fact]
    public void TransformExport_Strips_Run_And_Storage_Fields()
    {
        var result = WorkflowsExportCommand.TransformExport(SampleWorkflowJson);
        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        root.TryGetProperty("id", out _).Should().BeFalse();
        root.TryGetProperty("tenant_id", out _).Should().BeFalse();
        root.TryGetProperty("editor_state", out _).Should().BeFalse();
        root.TryGetProperty("created_at", out _).Should().BeFalse();
        root.TryGetProperty("updated_at", out _).Should().BeFalse();
    }

    [Fact]
    public void TransformExport_Includes_Schema_Version_And_Definition_Fields()
    {
        var result = WorkflowsExportCommand.TransformExport(SampleWorkflowJson);
        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        root.GetProperty("schema_version").GetInt32().Should().Be(1);
        root.GetProperty("name").GetString().Should().Be("AI Social Post Generator");
        root.GetProperty("group").GetString().Should().Be("Marketing");
        root.GetProperty("enabled").GetBoolean().Should().BeTrue();
        root.GetProperty("triggers").GetArrayLength().Should().Be(4);
        root.GetProperty("triggers")[0].GetProperty("type").GetString().Should().Be("Event");
    }

    // ── Trigger export ───────────────────────────────────────────────────────

    [Fact]
    public void TransformExport_Triggers_DropStorageFields_AndConfigJsonDuplicate()
    {
        var result = WorkflowsExportCommand.TransformExport(SampleWorkflowJson);
        using var doc = JsonDocument.Parse(result);

        foreach (var trigger in doc.RootElement.GetProperty("triggers").EnumerateArray())
        {
            foreach (var stripped in new[] { "id", "tenant_id", "workflow_id", "created_at", "updated_at",
                                             "locked", "created_by", "updated_by", "workflow", "config_json" })
                trigger.TryGetProperty(stripped, out _).Should().BeFalse($"trigger {stripped} should be stripped");
            trigger.TryGetProperty("type", out _).Should().BeTrue();
            trigger.TryGetProperty("enabled", out _).Should().BeTrue();
            trigger.TryGetProperty("config", out _).Should().BeTrue();
        }
    }

    [Fact]
    public void TransformExport_Triggers_DropSchedulerRunState()
    {
        var result = WorkflowsExportCommand.TransformExport(SampleWorkflowJson);
        using var doc = JsonDocument.Parse(result);
        var timed = doc.RootElement.GetProperty("triggers")[1].GetProperty("config");

        timed.GetProperty("cron_expression").GetString().Should().Be("0 9 * * 1");
        timed.TryGetProperty("last_run_at", out _).Should().BeFalse();
        timed.TryGetProperty("next_run_at", out _).Should().BeFalse();
    }

    [Fact]
    public void TransformExport_Triggers_FallBackToConfigJson_WhenConfigIsMissing()
    {
        const string sample = """
        {
          "id": 1, "name": "x", "enabled": true, "steps": [],
          "triggers": [ { "id": 9, "type": "Timed", "enabled": true, "config_json": "{\"cron_expression\":\"*/5 * * * *\"}" } ]
        }
        """;
        var result = WorkflowsExportCommand.TransformExport(sample);
        using var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("triggers")[0].GetProperty("config")
            .GetProperty("cron_expression").GetString().Should().Be("*/5 * * * *");
    }

    // ── Export → seed round trip ─────────────────────────────────────────────

    [Fact]
    public void ExportThenSeed_PreservesEveryTriggerField()
    {
        var exported = WorkflowsExportCommand.TransformExport(SampleWorkflowJson);
        var (request, _) = WorkflowsSeedCommand.BuildSeed(exported, enabledOverride: null);
        var sent = JsonNode.Parse(JsonSerializer.Serialize(request, ClientJsonOpts))!;

        var source = JsonNode.Parse(SampleWorkflowJson)!["triggers"]!.AsArray();
        var seeded = sent["triggers"]!.AsArray();
        seeded.Count.Should().Be(source.Count);

        for (var i = 0; i < source.Count; i++)
        {
            seeded[i]!["type"]!.GetValue<string>().Should().Be(source[i]!["type"]!.GetValue<string>());
            seeded[i]!["enabled"]!.GetValue<bool>().Should().Be(source[i]!["enabled"]!.GetValue<bool>());

            var expected = DefinitionOnly(source[i]!["config"]!.AsObject());
            var actual = seeded[i]!["config"]!;
            JsonNode.DeepEquals(actual, expected).Should().BeTrue(
                $"trigger {i} config should survive export → seed. Expected {expected.ToJsonString()}, got {actual.ToJsonString()}");
        }
    }

    [Fact]
    public void ExportThenSeed_KeepsWorkflowLevelFields()
    {
        var exported = WorkflowsExportCommand.TransformExport(SampleWorkflowJson);
        var (request, steps) = WorkflowsSeedCommand.BuildSeed(exported, enabledOverride: null);

        request.Name.Should().Be("AI Social Post Generator");
        request.Description.Should().Be("When a blog post is reviewed, generate a tweet");
        request.Group.Should().Be("Marketing");
        request.Enabled.Should().BeTrue();
        steps.Select(s => s.Key).Should().Equal("check_reviewed", "read_blog_post");
        steps[0].OnSuccess.Should().Be("read_blog_post");
    }

    private static readonly JsonSerializerOptions ClientJsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull
    };

    // Scheduler state and explicit nulls aren't part of a trigger's definition.
    private static JsonObject DefinitionOnly(JsonObject config)
    {
        var copy = new JsonObject();
        foreach (var (key, value) in config)
        {
            if (value is null || key is "last_run_at" or "next_run_at") continue;
            copy[key] = value.DeepClone();
        }
        return copy;
    }

    [Fact]
    public void TransformExport_Replaces_Step_Id_References_With_Keys()
    {
        var result = WorkflowsExportCommand.TransformExport(SampleWorkflowJson);
        using var doc = JsonDocument.Parse(result);
        var steps = doc.RootElement.GetProperty("steps");

        steps[0].TryGetProperty("id", out _).Should().BeFalse();
        steps[0].TryGetProperty("workflow_id", out _).Should().BeFalse();
        steps[0].TryGetProperty("on_success_step_id", out _).Should().BeFalse();
        steps[0].TryGetProperty("on_success_step", out _).Should().BeFalse();
        steps[0].GetProperty("on_success").GetString().Should().Be("read_blog_post");
        steps[0].TryGetProperty("on_failure", out _).Should().BeFalse();
    }

    [Fact]
    public void TransformExport_Strips_Audit_Metadata_And_Options_Json_Duplicate()
    {
        const string sample = """
        {
          "id": 1, "name": "x", "enabled": true,
          "options": { "k": "v" },
          "options_json": "{\"k\":\"v\"}",
          "locked": false, "created_by": "user-1", "updated_by": "user-2",
          "steps": [
            { "id": 1, "key": "a", "name": "A", "action": "RunScript", "enabled": true,
              "is_start_step": true, "locked": false, "created_by": "u", "updated_by": "u" }
          ]
        }
        """;
        var result = WorkflowsExportCommand.TransformExport(sample);
        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        foreach (var stripped in new[] { "options_json", "locked", "created_by", "updated_by" })
            root.TryGetProperty(stripped, out _).Should().BeFalse($"top-level {stripped} should be stripped");

        var step = root.GetProperty("steps")[0];
        foreach (var stripped in new[] { "locked", "created_by", "updated_by" })
            step.TryGetProperty(stripped, out _).Should().BeFalse($"step-level {stripped} should be stripped");
    }

    [Fact]
    public void TransformExport_Parses_Parameters_Json_Into_Parameters()
    {
        var result = WorkflowsExportCommand.TransformExport(SampleWorkflowJson);
        using var doc = JsonDocument.Parse(result);
        var firstStep = doc.RootElement.GetProperty("steps")[0];

        firstStep.TryGetProperty("parameters_json", out _).Should().BeFalse();
        firstStep.GetProperty("parameters").GetProperty("logical_operator").GetString().Should().Be("AND");
    }
}
