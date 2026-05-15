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

// ── workflows seed ───────────────────────────────────────────────────────────

public class WorkflowsSeedSettings : CommandSettings
{
    [CommandArgument(0, "<FILE>")]
    [Description("Path to a workflow JSON file (use `workflows export` to produce one)")]
    public string File { get; set; } = "";

    [CommandOption("--var <KEY=VALUE>")]
    [Description("Substitute {{KEY}} placeholders in the JSON. Repeatable.")]
    public string[] Vars { get; set; } = [];

    [CommandOption("--enabled")]
    [Description("Override the workflow's enabled flag to true")]
    public bool? Enabled { get; set; }
}

public class WorkflowsSeedCommand : BaseCommand<WorkflowsSeedSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, WorkflowsSeedSettings settings)
    {
        if (!System.IO.File.Exists(settings.File))
        {
            Renderer.Error($"File not found: {settings.File}");
            return 1;
        }

        var raw = await System.IO.File.ReadAllTextAsync(settings.File);

        // {{var_name}} placeholder substitution. Leaves {{ $anythink... }} workflow
        // template vars alone because they don't match the bare-word pattern.
        var vars = new Dictionary<string, string>();
        foreach (var v in settings.Vars)
        {
            var eq = v.IndexOf('=');
            if (eq <= 0) { Renderer.Error($"Invalid --var '{v}', expected KEY=VALUE"); return 1; }
            vars[v[..eq]] = v[(eq + 1)..];
        }
        var substituted = System.Text.RegularExpressions.Regex.Replace(
            raw,
            @"\{\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*\}\}",
            m =>
            {
                var key = m.Groups[1].Value;
                return vars.TryGetValue(key, out var value) ? value : m.Value;
            });

        WorkflowSeedSpec? spec;
        try
        {
            spec = System.Text.Json.JsonSerializer.Deserialize<WorkflowSeedSpec>(substituted,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            Renderer.Error($"Invalid workflow JSON: {ex.Message}");
            return 1;
        }
        if (spec is null || string.IsNullOrEmpty(spec.Name) || spec.Steps is null)
        {
            Renderer.Error("Workflow JSON must include name, trigger, and steps[].");
            return 1;
        }

        // Warn on unresolved placeholders so the user knows to pass --var
        var leftovers = System.Text.RegularExpressions.Regex.Matches(substituted, @"\{\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*\}\}")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();
        if (leftovers.Count > 0)
        {
            Renderer.Error($"Unresolved placeholders: {string.Join(", ", leftovers)}. Pass --var {leftovers[0]}=...");
            return 1;
        }

        var enabled = settings.Enabled ?? spec.Enabled;
        var trigger = spec.Trigger ?? "Manual";
        var options = spec.Options ?? (object)new { };

        try
        {
            var client = GetClient();

            Workflow? wf = null;
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Creating workflow '{spec.Name}'...", async _ =>
                {
                    wf = await client.CreateWorkflowAsync(new CreateWorkflowRequest(
                        spec.Name,
                        spec.Description,
                        trigger,
                        enabled,
                        options,
                        trigger == "Api" ? spec.ApiRoute : null));
                });
            Renderer.Success($"Workflow [#F97316]{Markup.Escape(wf!.Name)}[/] created (id: {wf.Id}).");

            // Add steps in order, recording the key→id mapping for link resolution.
            var keyToId = new Dictionary<string, int>();
            foreach (var s in spec.Steps)
            {
                System.Text.Json.JsonElement? parameters = null;
                if (s.Parameters is not null)
                {
                    var paramJson = System.Text.Json.JsonSerializer.Serialize(s.Parameters);
                    parameters = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(paramJson);
                    parameters = WorkflowStepParameterNormalizer.Normalize(s.Action ?? "", parameters.Value);
                }

                WorkflowStep? step = null;
                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync($"Adding step '{s.Key}'...", async _ =>
                    {
                        step = await client.AddWorkflowStepAsync(wf!.Id,
                            new CreateWorkflowStepRequest(
                                s.Key ?? "",
                                s.Name ?? s.Key ?? "",
                                s.Action ?? "RunScript",
                                s.Enabled,
                                s.IsStartStep,
                                s.Description,
                                parameters));
                    });
                keyToId[s.Key ?? ""] = step!.Id;
                Renderer.Success($"  Step [#F97316]{Markup.Escape(step.Key)}[/] (id: {step.Id}) added.");
            }

            // Resolve key→id for on_success / on_failure and patch each step.
            foreach (var s in spec.Steps)
            {
                if (string.IsNullOrEmpty(s.OnSuccess) && string.IsNullOrEmpty(s.OnFailure)) continue;
                if (!keyToId.TryGetValue(s.Key ?? "", out var stepId)) continue;

                int? successId = null, failureId = null;
                if (!string.IsNullOrEmpty(s.OnSuccess))
                {
                    if (!keyToId.TryGetValue(s.OnSuccess, out var sid))
                    {
                        Renderer.Error($"Step '{s.Key}' references unknown on_success step '{s.OnSuccess}'");
                        return 1;
                    }
                    successId = sid;
                }
                if (!string.IsNullOrEmpty(s.OnFailure))
                {
                    if (!keyToId.TryGetValue(s.OnFailure, out var fid))
                    {
                        Renderer.Error($"Step '{s.Key}' references unknown on_failure step '{s.OnFailure}'");
                        return 1;
                    }
                    failureId = fid;
                }

                // Fetch the freshly-created step so we can preserve its parameters
                // through the update (the API rejects updates with missing fields).
                var fresh = await client.GetWorkflowAsync(wf!.Id);
                var freshStep = fresh.Steps!.First(x => x.Id == stepId);
                var body = new Dictionary<string, object?>
                {
                    ["name"] = freshStep.Name,
                    ["action"] = freshStep.Action,
                    ["enabled"] = freshStep.Enabled,
                    ["is_start_step"] = freshStep.IsStartStep,
                    ["on_success_step_id"] = successId ?? freshStep.OnSuccessStepId,
                    ["on_failure_step_id"] = failureId ?? freshStep.OnFailureStepId,
                };
                if (freshStep.Parameters.HasValue)
                    body["parameters"] = freshStep.Parameters.Value;

                await client.UpdateWorkflowStepFullAsync(wf.Id, stepId, body);
            }

            Renderer.Success($"\nWorkflow seeded. Trigger with: anythink workflows trigger {wf.Id}");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

class WorkflowSeedSpec
{
    [System.Text.Json.Serialization.JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = 1;
    [System.Text.Json.Serialization.JsonPropertyName("name")]           public string Name { get; set; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("description")]    public string? Description { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("trigger")]        public string? Trigger { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("enabled")]        public bool Enabled { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("options")]        public object? Options { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("api_route")]      public string? ApiRoute { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("steps")]          public List<WorkflowSeedStep>? Steps { get; set; }
}

class WorkflowSeedStep
{
    [System.Text.Json.Serialization.JsonPropertyName("key")]           public string? Key { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("name")]          public string? Name { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("description")]   public string? Description { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("action")]        public string? Action { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("enabled")]       public bool Enabled { get; set; } = true;
    [System.Text.Json.Serialization.JsonPropertyName("is_start_step")] public bool IsStartStep { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("parameters")]    public object? Parameters { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("on_success")]    public string? OnSuccess { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("on_failure")]    public string? OnFailure { get; set; }
}

// ── workflows export ─────────────────────────────────────────────────────────

public class WorkflowsExportSettings : CommandSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("Workflow ID to export")]
    public int Id { get; set; }

    [CommandOption("-o|--output <FILE>")]
    [Description("Write to FILE instead of stdout")]
    public string? Output { get; set; }
}

public class WorkflowsExportCommand : BaseCommand<WorkflowsExportSettings>
{
    // Workflow + step fields that are storage- or run-only and shouldn't
    // round-trip. The server populates these; the seed side ignores them.
    private static readonly HashSet<string> StripWorkflowFields = new()
    {
        "id", "tenant_id", "created_at", "updated_at", "editor_state",
        "jobs", "last_run_at", "last_run_status",
        "options_json",                              // stringified duplicate of options
        "locked", "created_by", "updated_by",        // audit metadata, not definition
    };
    private static readonly HashSet<string> StripStepFields = new()
    {
        "id", "workflow_id", "tenant_id", "created_at", "updated_at",
        "on_success_step_id", "on_failure_step_id",
        "on_success_step", "on_failure_step",        // server-side nested expansion
        "parameters_json",                            // re-emitted as parsed `parameters`
        "locked", "created_by", "updated_by",        // audit metadata, not definition
    };

    public override async Task<int> ExecuteAsync(CommandContext context, WorkflowsExportSettings settings)
    {
        try
        {
            var client = GetClient();
            string raw = "";
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync($"Fetching workflow {settings.Id}...", async _ =>
                {
                    raw = await client.FetchRawAsync($"{client.BaseUrl}/org/{client.OrgId}/workflows/{settings.Id}");
                });

            var json = TransformExport(raw);

            if (string.IsNullOrEmpty(settings.Output))
            {
                Console.WriteLine(json);
            }
            else
            {
                await System.IO.File.WriteAllTextAsync(settings.Output, json);
                Renderer.Success($"Wrote {Markup.Escape(settings.Output)} ({json.Length} bytes).");
                AnsiConsole.MarkupLine($"Re-import with: [bold #F97316]anythink workflows seed {Markup.Escape(settings.Output)}[/]");
            }
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }

    /// <summary>
    /// Pure transform: server workflow JSON → exportable spec JSON.
    /// Strips run/storage-only fields, parses parameters_json into parameters,
    /// and replaces on_success/failure step IDs with their step keys.
    /// </summary>
    public static string TransformExport(string rawWorkflowJson)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(rawWorkflowJson);
        var root = doc.RootElement;

        var idToKey = new Dictionary<int, string>();
        if (root.TryGetProperty("steps", out var stepsArr) && stepsArr.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var s in stepsArr.EnumerateArray())
            {
                if (s.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var id)
                    && s.TryGetProperty("key", out var keyEl) && keyEl.GetString() is { } k)
                {
                    idToKey[id] = k;
                }
            }
        }

        var exported = new Dictionary<string, object?> { ["schema_version"] = 1 };
        foreach (var prop in root.EnumerateObject())
        {
            if (StripWorkflowFields.Contains(prop.Name)) continue;
            if (prop.Name == "steps") continue;
            exported[prop.Name] = JsonValueOf(prop.Value);
        }

        var exportedSteps = new List<Dictionary<string, object?>>();
        if (root.TryGetProperty("steps", out var steps2) && steps2.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var s in steps2.EnumerateArray())
            {
                var step = new Dictionary<string, object?>();
                foreach (var prop in s.EnumerateObject())
                {
                    if (StripStepFields.Contains(prop.Name)) continue;
                    step[prop.Name] = JsonValueOf(prop.Value);
                }

                if (s.TryGetProperty("parameters_json", out var pj) && pj.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var pjStr = pj.GetString();
                    if (!string.IsNullOrEmpty(pjStr))
                    {
                        using var pdoc = System.Text.Json.JsonDocument.Parse(pjStr);
                        step["parameters"] = JsonValueOf(pdoc.RootElement);
                    }
                }

                if (s.TryGetProperty("on_success_step_id", out var ss)
                    && ss.ValueKind == System.Text.Json.JsonValueKind.Number
                    && ss.TryGetInt32(out var ssi)
                    && idToKey.TryGetValue(ssi, out var ssKey))
                    step["on_success"] = ssKey;
                if (s.TryGetProperty("on_failure_step_id", out var fs)
                    && fs.ValueKind == System.Text.Json.JsonValueKind.Number
                    && fs.TryGetInt32(out var fsi)
                    && idToKey.TryGetValue(fsi, out var fsKey))
                    step["on_failure"] = fsKey;

                exportedSteps.Add(step);
            }
        }
        exported["steps"] = exportedSteps;

        return System.Text.Json.JsonSerializer.Serialize(exported, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented          = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
    }

    // Convert a JsonElement into a plain object tree (Dictionary / List / primitives)
    // so the outer Serializer emits proper JSON instead of escaped raw text.
    private static object? JsonValueOf(System.Text.Json.JsonElement e) => e.ValueKind switch
    {
        System.Text.Json.JsonValueKind.Object => e.EnumerateObject().ToDictionary(p => p.Name, p => JsonValueOf(p.Value)),
        System.Text.Json.JsonValueKind.Array  => e.EnumerateArray().Select(JsonValueOf).ToList(),
        System.Text.Json.JsonValueKind.String => e.GetString(),
        System.Text.Json.JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDouble(),
        System.Text.Json.JsonValueKind.True   => true,
        System.Text.Json.JsonValueKind.False  => false,
        _                                      => null,
    };
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
