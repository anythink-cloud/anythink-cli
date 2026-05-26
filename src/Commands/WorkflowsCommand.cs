using AnythinkCli.Client;
using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Text.Json.Nodes;

namespace AnythinkCli.Commands;

// ── workflows list ────────────────────────────────────────────────────────────

public class WorkflowsListCommand : BaseCommand<EmptySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EmptySettings settings)
    {
        try
        {
            var client = GetClient();
            List<Workflow> workflows = [];

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching workflows...", async _ =>
                {
                    workflows = await client.GetWorkflowsAsync();
                });

            Renderer.Header($"Workflows ({workflows.Count})");

            if (workflows.Count == 0)
            {
                Renderer.Info("No workflows found.");
                return 0;
            }

            var table = Renderer.BuildTable("ID", "Name", "Trigger", "Steps", "Enabled");
            foreach (var w in workflows.OrderBy(x => x.Id))
            {
                table.AddRow(
                    Markup.Escape(w.Id.ToString()),
                    Markup.Escape(w.Name),
                    Markup.Escape(w.Trigger),
                    Markup.Escape((w.Steps?.Count ?? 0).ToString()),
                    w.Enabled ? "[green]yes[/]" : "[red]no[/]"
                );
            }

            AnsiConsole.Write(table);
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── workflows get ─────────────────────────────────────────────────────────────

public class WorkflowIdSettings : CommandSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("Workflow ID")]
    public int Id { get; set; }
}

public class WorkflowsGetCommand : BaseCommand<WorkflowIdSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, WorkflowIdSettings settings)
    {
        try
        {
            var client = GetClient();
            Workflow? wf = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Fetching workflow {settings.Id}...", async _ =>
                {
                    wf = await client.GetWorkflowAsync(settings.Id);
                });

            Renderer.Header($"Workflow: {wf!.Name}");
            Renderer.KeyValue("ID", wf.Id.ToString());
            Renderer.KeyValue("Trigger", wf.Trigger);
            Renderer.KeyValue("Enabled", wf.Enabled ? "yes" : "no", wf.Enabled ? "green" : "red");
            if (!string.IsNullOrEmpty(wf.Description))
                Renderer.KeyValue("Description", wf.Description);

            var steps = wf.Steps ?? [];
            if (steps.Count > 0)
            {
                AnsiConsole.WriteLine();
                Renderer.Header($"Steps ({steps.Count})");
                var table = Renderer.BuildTable("ID", "Key", "Name", "Action", "Start", "Next", "Fail");
                foreach (var s in steps)
                {
                    Renderer.AddRow(table,
                        s.Id.ToString(),
                        s.Key,
                        s.Name,
                        s.Action,
                        s.IsStartStep ? "●" : "",
                        s.OnSuccessStepId?.ToString() ?? "",
                        s.OnFailureStepId?.ToString() ?? ""
                    );
                }
                AnsiConsole.Write(table);
            }

            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── workflows jobs ────────────────────────────────────────────────────────────

public class WorkflowsJobsCommand : BaseCommand<WorkflowIdSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, WorkflowIdSettings settings)
    {
        try
        {
            var client = GetClient();
            PaginatedResult<WorkflowJob>? result = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Fetching jobs for workflow {settings.Id}...", async _ =>
                {
                    result = await client.GetWorkflowJobsAsync(settings.Id);
                });

            var jobs = result!.Items ?? [];
            Renderer.Header($"Jobs ({result.TotalCount})");

            if (jobs.Count == 0)
            {
                Renderer.Info("No jobs found.");
                return 0;
            }

            foreach (var job in jobs.OrderByDescending(j => j.Id))
            {
                var statusColor = job.Status switch
                {
                    "Completed" => "green",
                    "Failed" => "red",
                    "Running" => "yellow",
                    _ => "dim"
                };
                AnsiConsole.MarkupLine($"\n  [bold]Job #{job.Id}[/] [{statusColor}]{Markup.Escape(job.Status ?? "unknown")}[/] — {Markup.Escape(job.StartedAt ?? "?")}");

                if (!string.IsNullOrEmpty(job.ErrorMessage))
                    AnsiConsole.MarkupLine($"  [red]Error:[/] {Markup.Escape(job.ErrorMessage)}");

                foreach (var step in job.JobSteps ?? [])
                {
                    var stepColor = step.Status switch
                    {
                        "Completed" => "green",
                        "Failed" => "red",
                        "Running" => "yellow",
                        _ => "dim"
                    };
                    AnsiConsole.MarkupLine($"    [{stepColor}]●[/] {Markup.Escape(step.StepKey ?? "?")}: [{stepColor}]{Markup.Escape(step.Status ?? "?")}[/]");
                    if (!string.IsNullOrEmpty(step.ErrorMessage))
                        AnsiConsole.MarkupLine($"      [red]{Markup.Escape(step.ErrorMessage)}[/]");
                    if (!string.IsNullOrEmpty(step.Log))
                        AnsiConsole.MarkupLine($"      [dim]{Markup.Escape(step.Log)}[/]");
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── workflows step-get ───────────────────────────────────────────────────────

public class WorkflowStepGetSettings : CommandSettings
{
    [CommandArgument(0, "<WORKFLOW_ID>")]
    [Description("Workflow ID")]
    public int WorkflowId { get; set; }

    [CommandArgument(1, "<STEP_ID>")]
    [Description("Step ID")]
    public int StepId { get; set; }
}

public class WorkflowsStepGetCommand : BaseCommand<WorkflowStepGetSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, WorkflowStepGetSettings settings)
    {
        try
        {
            var client = GetClient();
            Workflow? wf = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching workflow...", async _ =>
                {
                    wf = await client.GetWorkflowAsync(settings.WorkflowId);
                });

            var step = wf!.Steps?.FirstOrDefault(s => s.Id == settings.StepId);
            if (step == null)
            {
                Renderer.Error($"Step {settings.StepId} not found in workflow {settings.WorkflowId}.");
                return 1;
            }

            Renderer.Header($"Step: {step.Name}");
            Renderer.KeyValue("ID", step.Id.ToString());
            Renderer.KeyValue("Key", step.Key);
            Renderer.KeyValue("Action", step.Action);
            Renderer.KeyValue("Enabled", step.Enabled ? "yes" : "no", step.Enabled ? "green" : "red");
            Renderer.KeyValue("Start Step", step.IsStartStep ? "yes" : "no");
            Renderer.KeyValue("On Success", step.OnSuccessStepId?.ToString() ?? "—");
            Renderer.KeyValue("On Failure", step.OnFailureStepId?.ToString() ?? "—");

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Parameters:[/]");
            if (step.Parameters.HasValue)
                Renderer.PrintJson(step.Parameters.Value.GetRawText());
            else
                AnsiConsole.MarkupLine("[dim]  (none)[/]");

            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── workflows step-update ─────────────────────────────────────────────────────

public class WorkflowStepUpdateSettings : CommandSettings
{
    [CommandArgument(0, "<WORKFLOW_ID>")]
    [Description("Workflow ID")]
    public int WorkflowId { get; set; }

    [CommandArgument(1, "<STEP_ID>")]
    [Description("Step ID")]
    public int StepId { get; set; }

    [CommandOption("--params <JSON>")]
    [Description("Step parameters as JSON")]
    public string? Params { get; set; }

    [CommandOption("--name <NAME>")]
    [Description("Step name")]
    public string? Name { get; set; }

    [CommandOption("--enabled")]
    [Description("Enable this step")]
    public bool? Enabled { get; set; }
}

public class WorkflowsStepUpdateCommand : BaseCommand<WorkflowStepUpdateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, WorkflowStepUpdateSettings settings)
    {
        try
        {
            var client = GetClient();

            // Fetch current step to preserve existing values
            Workflow? wf = null;
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching workflow...", async _ =>
                {
                    wf = await client.GetWorkflowAsync(settings.WorkflowId);
                });

            var step = wf!.Steps?.FirstOrDefault(s => s.Id == settings.StepId);
            if (step == null)
            {
                Renderer.Error($"Step {settings.StepId} not found in workflow {settings.WorkflowId}.");
                return 1;
            }

            // Build the full update body preserving existing values
            var body = new Dictionary<string, object?>
            {
                ["name"] = settings.Name ?? step.Name,
                ["action"] = step.Action,
                ["enabled"] = settings.Enabled ?? step.Enabled,
                ["is_start_step"] = step.IsStartStep,
                ["on_success_step_id"] = step.OnSuccessStepId,
                ["on_failure_step_id"] = step.OnFailureStepId,
            };

            if (!string.IsNullOrEmpty(settings.Params))
            {
                var parsed = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(settings.Params);
                var actionType = step.Action ?? "";
                body["parameters"] = WorkflowStepParameterNormalizer.Normalize(actionType, parsed);
            }
            else if (step.Parameters.HasValue)
            {
                body["parameters"] = step.Parameters.Value;
            }

            WorkflowStep? updated = null;
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Updating step {settings.StepId}...", async _ =>
                {
                    updated = await client.UpdateWorkflowStepFullAsync(settings.WorkflowId, settings.StepId, body);
                });

            Renderer.Success($"Step [#F97316]{Markup.Escape(updated!.Name)}[/] updated.");
            if (updated.Parameters.HasValue)
            {
                AnsiConsole.MarkupLine("[bold]Parameters:[/]");
                Renderer.PrintJson(updated.Parameters.Value.GetRawText());
            }

            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── workflows step-add ────────────────────────────────────────────────────────

public class WorkflowStepAddSettings : CommandSettings
{
    [CommandArgument(0, "<WORKFLOW_ID>")]
    [Description("Workflow ID")]
    public int WorkflowId { get; set; }

    [CommandArgument(1, "<KEY>")]
    [Description("Step key (unique identifier)")]
    public string Key { get; set; } = "";

    [CommandOption("--name <NAME>")]
    [Description("Step display name")]
    public string? Name { get; set; }

    [CommandOption("--action <ACTION>")]
    [Description("Action type: ReadData, CreateData, UpdateData, DeleteData, CallAnApi, RunScript, Condition, SendAnEmail")]
    public string Action { get; set; } = "RunScript";

    [CommandOption("--params <JSON>")]
    [Description("Step parameters as JSON")]
    public string? Params { get; set; }

    [CommandOption("--start")]
    [Description("Set as start step")]
    public bool IsStartStep { get; set; }

    [CommandOption("--enabled")]
    [Description("Enable step immediately")]
    public bool Enabled { get; set; } = true;
}

public class WorkflowsStepAddCommand : BaseCommand<WorkflowStepAddSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, WorkflowStepAddSettings settings)
    {
        try
        {
            var client = GetClient();

            System.Text.Json.JsonElement? parameters = null;
            if (!string.IsNullOrEmpty(settings.Params))
            {
                parameters = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(settings.Params);
                parameters = WorkflowStepParameterNormalizer.Normalize(settings.Action, parameters.Value);
            }

            WorkflowStep? step = null;
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Adding step '{settings.Key}'...", async _ =>
                {
                    step = await client.AddWorkflowStepAsync(settings.WorkflowId,
                        new CreateWorkflowStepRequest(
                            settings.Key,
                            settings.Name ?? settings.Key,
                            settings.Action,
                            settings.Enabled,
                            settings.IsStartStep,
                            null,
                            parameters
                        ));
                });

            Renderer.Success($"Step [#F97316]{Markup.Escape(step!.Key)}[/] (id: {step.Id}) added to workflow {settings.WorkflowId}.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

/// Normalizes workflow step parameters to the format the platform expects.
/// Users can pass the intuitive "entity"/"data" format; this converts to
/// "entity_name"/"payload" (JSON string) for CreateData/UpdateData actions.
static class WorkflowStepParameterNormalizer
{
    public static System.Text.Json.JsonElement Normalize(string action, System.Text.Json.JsonElement parameters)
    {
        if (parameters.ValueKind != System.Text.Json.JsonValueKind.Object)
            return parameters;

        var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(parameters.GetRawText());
        if (dict == null) return parameters;

        bool changed = false;

        // Normalize "entity" → "entity_name" for all data actions
        if (dict.ContainsKey("entity") && !dict.ContainsKey("entity_name"))
        {
            dict["entity_name"] = dict["entity"];
            dict.Remove("entity");
            changed = true;
        }

        // For CreateData/UpdateData: normalize "data" → "payload" (as JSON string)
        if ((action == "CreateData" || action == "UpdateData")
            && dict.ContainsKey("data") && !dict.ContainsKey("payload"))
        {
            var dataElement = dict["data"];
            var payloadStr = dataElement.GetRawText();
            dict["payload"] = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                System.Text.Json.JsonSerializer.Serialize(payloadStr));
            dict.Remove("data");
            changed = true;
        }

        if (!changed) return parameters;

        var normalized = System.Text.Json.JsonSerializer.Serialize(dict);
        return System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(normalized);
    }
}

// ── workflows create ──────────────────────────────────────────────────────────

public class WorkflowCreateSettings : CommandSettings
{
    [CommandArgument(0, "<NAME>")]
    [Description("Workflow name")]
    public string Name { get; set; } = "";

    [CommandOption("--trigger <TRIGGER>")]
    [Description("Trigger type: Manual, Timed, Event, Api")]
    public string Trigger { get; set; } = "Manual";

    [CommandOption("--description <DESC>")]
    [Description("Workflow description")]
    public string? Description { get; set; }

    [CommandOption("--cron <CRON>")]
    [Description("Cron expression (for Timed trigger, e.g. '0 9 * * *')")]
    public string? Cron { get; set; }

    [CommandOption("--entity <ENTITY>")]
    [Description("Entity name for Event trigger")]
    public string? EventEntity { get; set; }

    [CommandOption("--event <EVENT>")]
    [Description("Event type: EntityCreated, EntityUpdated, EntityDeleted")]
    public string? Event { get; set; }

    [CommandOption("--api-route <ROUTE>")]
    [Description("Custom API route (for Api trigger)")]
    public string? ApiRoute { get; set; }

    [CommandOption("--enabled")]
    [Description("Enable workflow immediately after creation")]
    public bool Enabled { get; set; }
}

public class WorkflowsCreateCommand : BaseCommand<WorkflowCreateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, WorkflowCreateSettings settings)
    {
        var trigger = settings.Trigger;
        if (string.IsNullOrEmpty(trigger))
        {
            trigger = AnsiConsole.Prompt(
                Renderer.Prompt<string>()
                    .Title("Select [#F97316]trigger type[/]:")
                    .AddChoices("Manual", "Timed", "Event", "Api"));
        }

        object options = trigger switch
        {
            "Timed" => new
            {
                cron_expression = settings.Cron ?? "0 9 * * *",
                event_entity = ""
            },
            "Event" => (object)new EventWorkflowOptions(
                settings.Event ?? "EntityCreated",
                settings.EventEntity ?? ""
            ),
            "Api" => new { api_route = settings.ApiRoute ?? "", event_entity = settings.EventEntity ?? "" },
            _ => new { }
        };

        try
        {
            var client = GetClient();
            Workflow? wf = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Creating workflow '{settings.Name}'...", async _ =>
                {
                    wf = await client.CreateWorkflowAsync(new CreateWorkflowRequest(
                        settings.Name,
                        settings.Description,
                        trigger,
                        settings.Enabled,
                        options,
                        trigger == "Api" ? settings.ApiRoute : null
                    ));
                });

            Renderer.Success($"Workflow [#F97316]{Markup.Escape(wf!.Name)}[/] created (id: {Markup.Escape(wf.Id.ToString())}).");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── workflows update ──────────────────────────────────────────────────────────

public class WorkflowUpdateSettings : CommandSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("Workflow ID")]
    public int Id { get; set; }

    [CommandOption("--name <NAME>")]
    [Description("New workflow name")]
    public string? Name { get; set; }

    [CommandOption("--description <DESC>")]
    [Description("New workflow description")]
    public string? Description { get; set; }
}

public class WorkflowsUpdateCommand : BaseCommand<WorkflowUpdateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, WorkflowUpdateSettings settings)
    {
        if (settings.Name is null && settings.Description is null)
        {
            Renderer.Error("Provide at least --name or --description to update.");
            return 1;
        }

        try
        {
            var client = GetClient();
            Workflow? wf = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Updating workflow {settings.Id}...", async _ =>
                {
                    wf = await client.UpdateWorkflowAsync(settings.Id, new UpdateWorkflowRequest(
                        settings.Name,
                        settings.Description
                    ));
                });

            Renderer.Success($"Workflow [#F97316]{Markup.Escape(wf!.Name)}[/] updated.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── workflows enable / disable ────────────────────────────────────────────────

public class WorkflowsEnableCommand : BaseCommand<WorkflowIdSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, WorkflowIdSettings settings)
    {
        try
        {
            var client = GetClient();

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Enabling workflow {settings.Id}...", async _ =>
                {
                    await client.EnableWorkflowAsync(settings.Id);
                });

            Renderer.Success($"Workflow [#F97316]{Markup.Escape(settings.Id.ToString())}[/] enabled.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

public class WorkflowsDisableCommand : BaseCommand<WorkflowIdSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, WorkflowIdSettings settings)
    {
        try
        {
            var client = GetClient();

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Disabling workflow {settings.Id}...", async _ =>
                {
                    await client.DisableWorkflowAsync(settings.Id);
                });

            Renderer.Success($"Workflow [#F97316]{Markup.Escape(settings.Id.ToString())}[/] disabled.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── workflows trigger ─────────────────────────────────────────────────────────

public class WorkflowTriggerSettings : CommandSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("Workflow ID")]
    public int Id { get; set; }

    [CommandOption("--payload <JSON>")]
    [Description("Optional JSON payload to pass to the workflow")]
    public string? Payload { get; set; }
}

public class WorkflowsTriggerCommand : BaseCommand<WorkflowTriggerSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, WorkflowTriggerSettings settings)
    {
        object? payload = null;
        if (!string.IsNullOrEmpty(settings.Payload))
        {
            try
            {
                var parsed = System.Text.Json.JsonSerializer.Deserialize<JsonObject>(settings.Payload);
                payload = new { data = parsed };
            }
            catch { Renderer.Error("Invalid JSON payload."); return 1; }
        }

        try
        {
            var client = GetClient();

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Triggering workflow {settings.Id}...", async _ =>
                {
                    await client.TriggerWorkflowAsync(settings.Id, payload);
                });

            Renderer.Success($"Workflow [#F97316]{Markup.Escape(settings.Id.ToString())}[/] triggered.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── workflows delete ──────────────────────────────────────────────────────────

public class WorkflowDeleteSettings : CommandSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("Workflow ID to delete")]
    public int Id { get; set; }

    [CommandOption("--yes")]
    [Description("Skip confirmation")]
    public bool Yes { get; set; }
}

public class WorkflowsDeleteCommand : BaseCommand<WorkflowDeleteSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, WorkflowDeleteSettings settings)
    {
        if (!settings.Yes)
        {
            var confirm = AnsiConsole.Confirm(
                $"[yellow]Delete workflow[/] [bold red]{settings.Id}[/][yellow]?[/]",
                defaultValue: false);
            if (!confirm) { Renderer.Info("Cancelled."); return 0; }
        }

        try
        {
            var client = GetClient();

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Deleting workflow {settings.Id}...", async _ =>
                {
                    await client.DeleteWorkflowAsync(settings.Id);
                });

            Renderer.Success($"Workflow [#F97316]{Markup.Escape(settings.Id.ToString())}[/] deleted.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── workflows steps link ─────────────────────────────────────────────────────

public class WorkflowStepLinkSettings : CommandSettings
{
    [CommandArgument(0, "<WORKFLOW_ID>")]
    [Description("Workflow ID")]
    public int WorkflowId { get; set; }

    [CommandArgument(1, "<STEP_ID>")]
    [Description("Step ID to update")]
    public int StepId { get; set; }

    [CommandOption("--on-success <STEP_ID>")]
    [Description("Step ID to execute on success")]
    public int? OnSuccessStepId { get; set; }

    [CommandOption("--on-failure <STEP_ID>")]
    [Description("Step ID to execute on failure")]
    public int? OnFailureStepId { get; set; }
}

public class WorkflowsStepLinkCommand : BaseCommand<WorkflowStepLinkSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, WorkflowStepLinkSettings settings)
    {
        try
        {
            var client = GetClient();

            // First get the current step to preserve name and action
            Workflow? wf = null;
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching workflow...", async _ =>
                {
                    wf = await client.GetWorkflowAsync(settings.WorkflowId);
                });

            var step = wf!.Steps?.FirstOrDefault(s => s.Id == settings.StepId);
            if (step == null)
            {
                Renderer.Error($"Step {settings.StepId} not found in workflow {settings.WorkflowId}.");
                return 1;
            }

            WorkflowStep? updated = null;
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Linking step {settings.StepId}...", async _ =>
                {
                    var body = new Dictionary<string, object?>
                    {
                        ["name"] = step.Name,
                        ["action"] = step.Action,
                        ["enabled"] = step.Enabled,
                        ["is_start_step"] = step.IsStartStep,
                        ["on_success_step_id"] = settings.OnSuccessStepId ?? step.OnSuccessStepId,
                        ["on_failure_step_id"] = settings.OnFailureStepId ?? step.OnFailureStepId,
                    };
                    if (step.Parameters.HasValue)
                        body["parameters"] = step.Parameters.Value;

                    updated = await client.UpdateWorkflowStepFullAsync(settings.WorkflowId, settings.StepId, body);
                });

            var parts = new List<string>();
            if (settings.OnSuccessStepId.HasValue)
                parts.Add($"on_success → {settings.OnSuccessStepId}");
            if (settings.OnFailureStepId.HasValue)
                parts.Add($"on_failure → {settings.OnFailureStepId}");

            Renderer.Success($"Step [#F97316]{settings.StepId}[/] linked: {string.Join(", ", parts)}.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── workflows step delete ────────────────────────────────────────────────────

public class WorkflowStepDeleteSettings : CommandSettings
{
    [CommandArgument(0, "<WORKFLOW_ID>")]
    [Description("Workflow ID")]
    public int WorkflowId { get; set; }

    [CommandArgument(1, "<STEP_ID>")]
    [Description("Step ID to delete")]
    public int StepId { get; set; }

    [CommandOption("-y|--yes")]
    [Description("Skip confirmation")]
    public bool Yes { get; set; }
}

public class WorkflowsStepDeleteCommand : BaseCommand<WorkflowStepDeleteSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, WorkflowStepDeleteSettings settings)
    {
        var client = GetClient();
        Workflow? wf = null;
        WorkflowStep? step = null;
        var inboundLinks = new List<WorkflowStep>();

        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching workflow...", async _ =>
                {
                    wf = await client.GetWorkflowAsync(settings.WorkflowId);
                });

            step = wf!.Steps?.FirstOrDefault(s => s.Id == settings.StepId);
            if (step == null)
            {
                Renderer.Error($"Step {settings.StepId} not found in workflow {settings.WorkflowId}.");
                return 1;
            }

            // Steps that link TO the one we're deleting. The API rejects the delete with a
            // FK violation if any of these exist — re-link them (or delete them) first.
            inboundLinks = (wf.Steps ?? [])
                .Where(s => s.OnSuccessStepId == settings.StepId || s.OnFailureStepId == settings.StepId)
                .ToList();

            if (!settings.Yes)
            {
                Renderer.Header($"Delete step {settings.StepId} ({Markup.Escape(step.Key)})");
                var summary = Renderer.BuildTable("Property", "Value");
                Renderer.AddRow(summary, "Action", step.Action);
                Renderer.AddRow(summary, "Inbound links", inboundLinks.Count.ToString());
                if (step.IsStartStep) Renderer.AddRow(summary, "Start step", "yes — deleting will detach the workflow's entry point");
                AnsiConsole.Write(summary);

                if (inboundLinks.Count > 0)
                {
                    AnsiConsole.MarkupLine("[yellow]Warning:[/] these steps link to this one and the API will reject the delete until they are re-linked:");
                    foreach (var link in inboundLinks)
                        AnsiConsole.MarkupLine($"  [yellow]•[/] step [bold]{link.Id}[/] ({Markup.Escape(link.Key)}) — {DescribeLink(link, settings.StepId)}");
                }

                if (!AnsiConsole.Confirm("[red]Delete this step?[/]", defaultValue: false))
                {
                    Renderer.Info("Cancelled.");
                    return 0;
                }
            }

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Deleting step {settings.StepId}...", async _ =>
                {
                    await client.DeleteWorkflowStepAsync(settings.WorkflowId, settings.StepId);
                });

            Renderer.Success($"Step [#F97316]{settings.StepId}[/] ({Markup.Escape(step.Key)}) deleted.");
            return 0;
        }
        catch (AnythinkException ex) when (ex.StatusCode == 500 && inboundLinks.Count > 0)
        {
            // The API surfaces FK constraint failures as a generic 500. Translate it
            // into a clear list of what the user needs to fix and how.
            Renderer.Error($"Cannot delete step {settings.StepId}: still referenced by {inboundLinks.Count} other step(s).");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("Re-link or delete each of these first, then re-run:");
            foreach (var link in inboundLinks)
            {
                var which = link.OnSuccessStepId == settings.StepId ? "--on-success" : "--on-failure";
                AnsiConsole.MarkupLine($"  [dim]anythink workflows step-link {settings.WorkflowId} {link.Id} {which} <NEW_TARGET>[/]");
            }
            return 1;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }

    private static string DescribeLink(WorkflowStep link, int targetId)
    {
        var via = new List<string>();
        if (link.OnSuccessStepId == targetId) via.Add("on_success");
        if (link.OnFailureStepId == targetId) via.Add("on_failure");
        return string.Join(" + ", via);
    }
}
