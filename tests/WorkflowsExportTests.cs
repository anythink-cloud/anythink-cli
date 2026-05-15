using System.Text.Json;
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
      "trigger": "Event",
      "enabled": true,
      "editor_state": "{\"nodePositions\":{}}",
      "created_at": "2026-03-26T23:08:27.891Z",
      "updated_at": "2026-04-01T10:00:00.000Z",
      "options": { "entity_name": "blog_posts" },
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
        root.GetProperty("trigger").GetString().Should().Be("Event");
        root.GetProperty("enabled").GetBoolean().Should().BeTrue();
        root.GetProperty("options").GetProperty("entity_name").GetString().Should().Be("blog_posts");
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
          "id": 1, "name": "x", "trigger": "Manual", "enabled": true,
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
