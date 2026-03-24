using System.ComponentModel;
using System.Text.Json;
using AnythinkCli.Models;
using ModelContextProtocol.Server;

namespace AnythinkMcp.Tools;

/// <summary>
/// MCP tools for managing workflows and their steps/jobs.
/// Wraps AnythinkClient workflow methods.
/// </summary>
[McpServerToolType]
public class WorkflowTools
{
    private readonly McpClientFactory _factory;
    public WorkflowTools(McpClientFactory factory) => _factory = factory;

    [McpServerTool(Name = "workflows_list"), Description("List all workflows in the project")]
    public async Task<string> ListWorkflows()
    {
        var workflows = await _factory.GetClient().GetWorkflowsAsync();
        return JsonSerializer.Serialize(workflows.Select(w => new
        {
            w.Id, w.Name, w.Trigger, w.Enabled, w.Description
        }));
    }

    [McpServerTool(Name = "workflows_get"), Description("Get workflow details including steps")]
    public async Task<string> GetWorkflow(
        [Description("Workflow ID")] int id)
    {
        var wf = await _factory.GetClient().GetWorkflowAsync(id);
        return JsonSerializer.Serialize(wf);
    }

    [McpServerTool(Name = "workflows_create"), Description("Create a new workflow")]
    public async Task<string> CreateWorkflow(
        [Description("Workflow name")] string name,
        [Description("Trigger type: Timed, Event, Manual")] string trigger,
        [Description("Cron expression (for Timed trigger, e.g. '0 6 * * *')")] string? cron = null,
        [Description("Event name (for Event trigger)")] string? eventName = null,
        [Description("Entity name (for Event trigger)")] string? eventEntity = null,
        [Description("Workflow description")] string? description = null)
    {
        object options = trigger switch
        {
            "Timed" => new { cron = cron ?? "0 0 * * *" },
            "Event" => new EventWorkflowOptions(eventName ?? "create", eventEntity ?? ""),
            _ => new { }
        };

        var wf = await _factory.GetClient().CreateWorkflowAsync(
            new CreateWorkflowRequest(name, description, trigger, true, options));
        return JsonSerializer.Serialize(wf);
    }

    [McpServerTool(Name = "workflows_update"), Description("Update a workflow's name or description")]
    public async Task<string> UpdateWorkflow(
        [Description("Workflow ID")] int id,
        [Description("New name")] string? name = null,
        [Description("New description")] string? description = null)
    {
        var wf = await _factory.GetClient().UpdateWorkflowAsync(id,
            new UpdateWorkflowRequest(name, description));
        return JsonSerializer.Serialize(wf);
    }

    [McpServerTool(Name = "workflows_enable"), Description("Enable a workflow")]
    public async Task<string> EnableWorkflow([Description("Workflow ID")] int id)
    {
        await _factory.GetClient().EnableWorkflowAsync(id);
        return $"Workflow {id} enabled.";
    }

    [McpServerTool(Name = "workflows_disable"), Description("Disable a workflow")]
    public async Task<string> DisableWorkflow([Description("Workflow ID")] int id)
    {
        await _factory.GetClient().DisableWorkflowAsync(id);
        return $"Workflow {id} disabled.";
    }

    [McpServerTool(Name = "workflows_trigger"), Description("Trigger a manual workflow run")]
    public async Task<string> TriggerWorkflow([Description("Workflow ID")] int id)
    {
        await _factory.GetClient().TriggerWorkflowAsync(id);
        return $"Workflow {id} triggered.";
    }

    [McpServerTool(Name = "workflows_jobs"), Description("View job history for a workflow")]
    public async Task<string> GetJobs(
        [Description("Workflow ID")] int workflowId,
        [Description("Page number")] int page = 1)
    {
        var result = await _factory.GetClient().GetWorkflowJobsAsync(workflowId, page);
        return JsonSerializer.Serialize(result);
    }

    [McpServerTool(Name = "workflows_delete"), Description("Delete a workflow")]
    public async Task<string> DeleteWorkflow([Description("Workflow ID")] int id)
    {
        await _factory.GetClient().DeleteWorkflowAsync(id);
        return $"Workflow {id} deleted.";
    }
}
