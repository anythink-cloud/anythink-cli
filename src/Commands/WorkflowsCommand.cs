using AnythinkCli.Client;
using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Text.Json.Nodes;

namespace AnythinkCli.Commands;

// ── workflows list ────────────────────────────────────────────────────────────

public class WorkflowListSettings : CommandSettings
{
    [CommandOption("--json")]
    [Description("Output the workflows as raw JSON instead of a table")]
    public bool Json { get; set; }
}

public class WorkflowsListCommand : BaseCommand<WorkflowListSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, WorkflowListSettings settings)
    {
        try
        {
            var client = GetClient();

            if (settings.Json)
            {
                Console.WriteLine(WorkflowJson.ForList(await client.GetWorkflowsAsync()));
                return 0;
            }

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
                    Markup.Escape(WorkflowTriggers.Summary(w)),
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

public class WorkflowGetSettings : WorkflowIdSettings
{
    [CommandOption("--json")]
    [Description("Output the workflow as raw JSON instead of a summary")]
    public bool Json { get; set; }
}

public class WorkflowsGetCommand : BaseCommand<WorkflowGetSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, WorkflowGetSettings settings)
    {
        try
        {
            var client = GetClient();

            if (settings.Json)
            {
                Console.WriteLine(WorkflowJson.ForGet(await client.GetWorkflowRawAsync(settings.Id)));
                return 0;
            }

            Workflow? wf = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync(Renderer.Status($"Fetching workflow {settings.Id}..."), async _ =>
                {
                    wf = await client.GetWorkflowAsync(settings.Id);
                });

            Renderer.Header($"Workflow: {wf!.Name}");
            Renderer.KeyValue("ID", wf.Id.ToString());
            Renderer.KeyValue("Trigger", WorkflowTriggers.Summary(wf));
            Renderer.KeyValue("Enabled", wf.Enabled ? "yes" : "no", wf.Enabled ? "green" : "red");
            if (!string.IsNullOrEmpty(wf.Description))
                Renderer.KeyValue("Description", wf.Description);

            var triggers = WorkflowTriggers.Effective(wf);
            for (var i = 0; i < triggers.Count; i++)
            {
                if (WorkflowTriggers.Filter(triggers[i]) is not { } filter) continue;
                var pretty = System.Text.Json.JsonSerializer.Serialize(filter, JsonOutput.PrettyRelaxed);
                Renderer.KeyValue(triggers.Count > 1 ? $"Filter (trigger {i + 1}, {triggers[i].Type})" : "Filter", pretty);
            }

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
                .StartAsync(Renderer.Status($"Fetching jobs for workflow {settings.Id}..."), async _ =>
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
            {
                if (step.Action == "FileHandler")
                    FileHandlerParamsRenderer.Render(step.Parameters.Value);
                else
                    Renderer.PrintJson(step.Parameters.Value.GetRawText());
            }
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

static class FileHandlerParamsRenderer
{
    public static void Render(System.Text.Json.JsonElement parameters)
    {
        if (parameters.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            Renderer.PrintJson(parameters.GetRawText());
            return;
        }

        void Row(string label, string key)
        {
            if (parameters.TryGetProperty(key, out var v)
                && v.ValueKind != System.Text.Json.JsonValueKind.Null
                && v.ValueKind != System.Text.Json.JsonValueKind.Undefined)
            {
                var text = v.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.String => v.GetString() ?? "",
                    System.Text.Json.JsonValueKind.True => "true",
                    System.Text.Json.JsonValueKind.False => "false",
                    _ => v.GetRawText()
                };
                Renderer.KeyValue(label, text);
            }
        }

        Row("Operation", "operation");
        Row("Source URL", "source_url");
        Row("Entity", "entity_name");
        Row("Record ID", "record_id");
        Row("Field", "field_name");
        Row("File Name", "file_name");
        Row("Folder ID", "folder_id");
        Row("Existing File ID", "existing_file_id");
        Row("Is Public", "is_public");
        Row("Link Mode", "link_mode");
        Row("Overwrite", "overwrite_if_present");
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
                .StartAsync(Renderer.Status($"Updating step {settings.StepId}..."), async _ =>
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
    [Description("Action type: ReadData, CreateData, UpdateData, UpsertData, DeleteData, CallAnApi, RunScript, Condition, SendAnEmail, SendACommand, SendPushNotification, FileHandler, Integration (for Integration, prefer 'integration-add')")]
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
                .StartAsync(Renderer.Status($"Adding step '{settings.Key}'..."), async _ =>
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
    [Description("Trigger type: Manual (default), Timed, Event, Api. Not case-sensitive")]
    public string Trigger { get; set; } = "Manual";

    [CommandOption("--description <DESC>")]
    [Description("Workflow description")]
    public string? Description { get; set; }

    [CommandOption("--cron <CRON>")]
    [Description("Cron expression, required for a Timed trigger (e.g. '0 9 * * *')")]
    public string? Cron { get; set; }

    [CommandOption("--entity <ENTITY>")]
    [Description("Entity name, required for an Event or Manual trigger")]
    public string? EventEntity { get; set; }

    [CommandOption("--event <EVENT>")]
    [Description("Event for an Event trigger: EntityCreated (default), EntityUpdated, EntityDeleted, UserRegistered, PaymentSucceeded and others. Not case-sensitive")]
    public string? Event { get; set; }

    [CommandOption("--api-route <ROUTE>")]
    [Description("API route, required for an Api trigger")]
    public string? ApiRoute { get; set; }

    [CommandOption("--enabled")]
    [Description("Enable workflow immediately after creation")]
    public bool Enabled { get; set; }

    [CommandOption("--filter <JSON>")]
    [Description("Filter expression JSON for Event trigger (fires only when predicate matches the row)")]
    public string? Filter { get; set; }

    [CommandOption("--filter-file <PATH>")]
    [Description("Path to a file containing the filter expression JSON")]
    public string? FilterFile { get; set; }
}

public class WorkflowsCreateCommand : BaseCommand<WorkflowCreateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, WorkflowCreateSettings settings)
    {
        var requested = settings.Trigger;
        if (string.IsNullOrEmpty(requested))
        {
            requested = AnsiConsole.Prompt(
                Renderer.Prompt<string>()
                    .Title("Select [#F97316]trigger type[/]:")
                    .AddChoices("Manual", "Timed", "Event", "Api"));
        }

        var trigger = WorkflowTriggers.CanonicalType(requested);
        if (trigger is null)
        {
            Renderer.Error($"Unknown trigger type '{requested}'. Use {string.Join(", ", WorkflowTriggers.Types)}.");
            return 1;
        }

        System.Text.Json.JsonElement? filter = null;
        var filterJson = settings.Filter;
        if (string.IsNullOrEmpty(filterJson) && !string.IsNullOrEmpty(settings.FilterFile))
        {
            try { filterJson = await File.ReadAllTextAsync(settings.FilterFile); }
            catch (Exception ex)
            {
                Renderer.Error($"Could not read --filter-file '{settings.FilterFile}': {ex.Message}");
                return 1;
            }
        }
        if (!string.IsNullOrEmpty(filterJson))
        {
            try { filter = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(filterJson); }
            catch (System.Text.Json.JsonException ex)
            {
                Renderer.Error($"Invalid filter JSON: {ex.Message}");
                return 1;
            }
        }

        if (trigger == "Event" && !string.IsNullOrEmpty(settings.Event) && WorkflowTriggers.CanonicalEvent(settings.Event) is null)
        {
            Renderer.Error($"Unknown event '{settings.Event}'. Use one of: {string.Join(", ", WorkflowTriggers.Events)}.");
            return 1;
        }

        var request = BuildTrigger(trigger, settings, filter);
        if (WorkflowTriggers.MissingField(request) is { } missing)
        {
            Renderer.Error(MissingOptionMessage(trigger, missing));
            return 1;
        }

        foreach (var ignored in IgnoredOptions(trigger, settings))
            Renderer.Warn(Markup.Escape(ignored));

        try
        {
            var client = GetClient();
            Workflow? wf = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync(Renderer.Status($"Creating workflow '{settings.Name}'..."), async _ =>
                {
                    wf = await client.CreateWorkflowAsync(new CreateWorkflowRequest(
                        settings.Name,
                        settings.Description,
                        settings.Enabled,
                        [request]));
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

    internal static WorkflowTriggerRequest BuildTrigger(
        string trigger, WorkflowCreateSettings settings, System.Text.Json.JsonElement? filter)
    {
        object config = trigger switch
        {
            "Timed" => new { cron_expression = settings.Cron },
            "Event" => new EventWorkflowOptions(
                WorkflowTriggers.CanonicalEvent(settings.Event) ?? settings.Event ?? "EntityCreated",
                settings.EventEntity ?? "",
                filter),
            "Api" => new { api_route = settings.ApiRoute ?? "" },
            "Manual" => new
            {
                manual_entities = string.IsNullOrEmpty(settings.EventEntity) ? Array.Empty<string>() : [settings.EventEntity]
            },
            _ => new { }
        };

        return new WorkflowTriggerRequest(trigger, true, config);
    }

    internal static string MissingOptionMessage(string trigger, string field) => field switch
    {
        "manual_entities" => "A Manual trigger needs --entity <ENTITY>, the entity the workflow runs on.",
        "event_entity" => "An Event trigger needs --entity <ENTITY>, the entity whose events start the workflow.",
        "api_route" => "An Api trigger needs --api-route <ROUTE>.",
        "cron_expression" => "A Timed trigger needs --cron <CRON>, for example '0 9 * * *'.",
        _ => $"A {trigger} trigger needs {field}.",
    };

    internal static List<string> IgnoredOptions(string trigger, WorkflowCreateSettings settings)
    {
        var ignored = new List<string>();

        void Check(bool given, string option, params string[] usedBy)
        {
            if (given && !usedBy.Contains(trigger))
                ignored.Add($"{option} is ignored for a {trigger} trigger.");
        }

        Check(!string.IsNullOrEmpty(settings.EventEntity), "--entity", "Event", "Manual");
        Check(!string.IsNullOrEmpty(settings.Cron), "--cron", "Timed");
        Check(!string.IsNullOrEmpty(settings.ApiRoute), "--api-route", "Api");
        Check(!string.IsNullOrEmpty(settings.Event), "--event", "Event");
        Check(!string.IsNullOrEmpty(settings.Filter) || !string.IsNullOrEmpty(settings.FilterFile), "--filter", "Event");
        return ignored;
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
            Workflow? existing = null;
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync(Renderer.Status($"Reading workflow {settings.Id}..."), async _ =>
                {
                    existing = await client.GetWorkflowAsync(settings.Id);
                });

            var request = BuildUpdateRequest(existing!, settings.Name, settings.Description);
            if (IncompleteTriggers(existing!.Name, request.Triggers!) is { } blocked)
            {
                Renderer.Error(blocked);
                return 1;
            }

            Workflow? wf = null;
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync(Renderer.Status($"Updating workflow {settings.Id}..."), async _ =>
                {
                    wf = await client.UpdateWorkflowAsync(settings.Id, request);
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

    internal static string? IncompleteTriggers(string workflowName, IReadOnlyList<WorkflowTriggerRequest> triggers)
    {
        var problems = triggers
            .Select((trigger, index) => (Number: index + 1, Problems: WorkflowTriggers.Problems([trigger])))
            .SelectMany(t => t.Problems.Select(p => $"Trigger {t.Number}: {p}"))
            .ToList();

        return problems.Count == 0
            ? null
            : $"Workflow '{workflowName}' can't be updated until its triggers are complete. {string.Join("; ", problems)}. " +
              "Fix the trigger in the dashboard first, then run this again.";
    }

    // The API replaces the whole workflow on PUT, so everything not being changed is sent back as it was.
    internal static UpdateWorkflowRequest BuildUpdateRequest(Workflow existing, string? name, string? description) => new(
        name ?? existing.Name,
        description ?? existing.Description,
        existing.Group,
        existing.Enabled,
        existing.EditorState,
        WorkflowTriggers.ForRequest(existing, keepLastRun: true));
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
                .StartAsync(Renderer.Status($"Enabling workflow {settings.Id}..."), async _ =>
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
                .StartAsync(Renderer.Status($"Disabling workflow {settings.Id}..."), async _ =>
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
    [Description("Optional JSON record to pass to the workflow (available as $anythink.trigger.data)")]
    public string? Payload { get; set; }

    [CommandOption("--entity <NAME>")]
    [Description("Entity name for the trigger (required for Manual-trigger workflows)")]
    public string? Entity { get; set; }

    [CommandOption("--entity-id <ID>")]
    [Description("Optional entity/record id to associate with the trigger")]
    public int? EntityId { get; set; }
}

public class WorkflowsTriggerCommand : BaseCommand<WorkflowTriggerSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, WorkflowTriggerSettings settings)
    {
        JsonObject? payload;
        try { payload = BuildPayload(settings.Payload, settings.Entity, settings.EntityId); }
        catch (System.Text.Json.JsonException) { Renderer.Error("Invalid JSON payload."); return 1; }

        try
        {
            var client = GetClient();

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync(Renderer.Status($"Triggering workflow {settings.Id}..."), async _ =>
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

    // The endpoint binds snake_case keys and takes data as a string it re-parses into $anythink.trigger.data.
    internal static JsonObject? BuildPayload(string? payloadJson, string? entity, int? entityId)
    {
        if (string.IsNullOrEmpty(payloadJson) && string.IsNullOrEmpty(entity) && !entityId.HasValue)
            return null;

        var body = new JsonObject();
        if (!string.IsNullOrEmpty(entity)) body["entity_name"] = entity;
        if (entityId.HasValue) body["entity_id"] = entityId.Value;
        if (!string.IsNullOrEmpty(payloadJson))
            body["data"] = (JsonNode.Parse(payloadJson) ?? throw new System.Text.Json.JsonException()).ToJsonString();
        return body;
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
                .StartAsync(Renderer.Status($"Deleting workflow {settings.Id}..."), async _ =>
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
            Renderer.Error("Workflow JSON must include name, trigger (or triggers), and steps[].");
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

        var request = BuildSeedRequest(spec, settings.Enabled ?? spec.Enabled);
        if (WorkflowTriggers.Problems(request.Triggers) is { Count: > 0 } problems)
        {
            Renderer.Error($"Workflow '{spec.Name}': {string.Join("; ", problems)}.");
            return 1;
        }

        try
        {
            var client = GetClient();

            Workflow? wf = null;
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync(Renderer.Status($"Creating workflow '{spec.Name}'..."), async _ =>
                {
                    wf = await client.CreateWorkflowAsync(request);
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
                    .StartAsync(Renderer.Status($"Adding step '{s.Key}'..."), async _ =>
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

    internal static CreateWorkflowRequest BuildSeedRequest(WorkflowSeedSpec spec, bool enabled)
    {
        List<WorkflowTriggerRequest> triggers = spec.Triggers is { Count: > 0 }
            ? WorkflowTriggers.ForRequest(spec.Triggers)
            : [WorkflowTriggers.FromLegacy(spec.Trigger ?? "Manual", spec.Options, spec.ApiRoute)];

        return new CreateWorkflowRequest(spec.Name, spec.Description, enabled, triggers, spec.Group);
    }
}

internal class WorkflowSeedSpec
{
    [System.Text.Json.Serialization.JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = 1;
    [System.Text.Json.Serialization.JsonPropertyName("name")] public string Name { get; set; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("description")] public string? Description { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("group")] public string? Group { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("trigger")] public string? Trigger { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("options")] public System.Text.Json.JsonElement? Options { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("triggers")] public List<WorkflowTrigger>? Triggers { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("api_route")] public string? ApiRoute { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("steps")] public List<WorkflowSeedStep>? Steps { get; set; }
}

class WorkflowSeedStep
{
    [System.Text.Json.Serialization.JsonPropertyName("key")] public string? Key { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("name")] public string? Name { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("description")] public string? Description { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("action")] public string? Action { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [System.Text.Json.Serialization.JsonPropertyName("is_start_step")] public bool IsStartStep { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("parameters")] public object? Parameters { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("on_success")] public string? OnSuccess { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("on_failure")] public string? OnFailure { get; set; }
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
    public override async Task<int> ExecuteAsync(CommandContext context, WorkflowsExportSettings settings)
    {
        try
        {
            var client = GetClient();
            string raw = "";
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync(Renderer.Status($"Fetching workflow {settings.Id}..."), async _ =>
                {
                    raw = await client.GetWorkflowRawAsync(settings.Id);
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

    public static string TransformExport(string rawWorkflowJson) => WorkflowJson.ForExport(rawWorkflowJson);
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
                .StartAsync(Renderer.Status($"Linking step {settings.StepId}..."), async _ =>
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

            Renderer.Success($"Step [#F97316]{settings.StepId}[/] linked: {Markup.Escape(string.Join(", ", parts))}.");
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
                Renderer.Header($"Delete step {settings.StepId} ({step.Key})");
                var summary = Renderer.BuildTable("Property", "Value");
                Renderer.AddRow(summary, "Action", step.Action);
                Renderer.AddRow(summary, "Inbound links", inboundLinks.Count.ToString());
                if (step.IsStartStep) Renderer.AddRow(summary, "Start step", "yes — deleting will detach the workflow's entry point");
                AnsiConsole.Write(summary);

                if (inboundLinks.Count > 0)
                {
                    AnsiConsole.MarkupLine("[yellow]Warning:[/] these steps link to this one and the API will reject the delete until they are re-linked:");
                    foreach (var link in inboundLinks)
                        AnsiConsole.MarkupLine($"  [yellow]•[/] step [bold]{link.Id}[/] ({Markup.Escape(link.Key)}) — {Markup.Escape(DescribeLink(link, settings.StepId))}");
                }

                if (!AnsiConsole.Confirm("[red]Delete this step?[/]", defaultValue: false))
                {
                    Renderer.Info("Cancelled.");
                    return 0;
                }
            }

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync(Renderer.Status($"Deleting step {settings.StepId}..."), async _ =>
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

// ── workflows file-handler-add ────────────────────────────────────────────────

public class WorkflowFileHandlerAddSettings : CommandSettings
{
    [CommandArgument(0, "<WORKFLOW_ID>")]
    [Description("Workflow ID")]
    public int WorkflowId { get; set; }

    [CommandArgument(1, "<STEP_KEY>")]
    [Description("Step key (unique identifier, snake_case)")]
    public string Key { get; set; } = "";

    [CommandOption("--operation <OP>")]
    [Description("FileHandler operation: fetch_and_link, link_existing, detach")]
    public string Operation { get; set; } = "fetch_and_link";

    [CommandOption("--source-url <URL>")]
    [Description("URL to fetch the file from (required for fetch_and_link; supports templating)")]
    public string? SourceUrl { get; set; }

    [CommandOption("--file-name <NAME>")]
    [Description("Filename to store the uploaded file as (templatable)")]
    public string? FileName { get; set; }

    [CommandOption("--entity <NAME>")]
    [Description("Target entity name (required)")]
    public string? Entity { get; set; }

    [CommandOption("--record-id <TEMPLATE>")]
    [Description("Target record ID, typically '{{ $anythink.trigger.id }}' (required)")]
    public string? RecordId { get; set; }

    [CommandOption("--field <NAME>")]
    [Description("Field name on the record to link the file to (required, snake_case)")]
    public string? Field { get; set; }

    [CommandOption("--folder-id <TEMPLATE>")]
    [Description("Folder ID to upload the file into (optional, templatable)")]
    public string? FolderId { get; set; }

    [CommandOption("--is-public")]
    [Description("Mark the uploaded file as public")]
    public bool IsPublic { get; set; }

    [CommandOption("--link-mode <MODE>")]
    [Description("How to link to the field: set (single value) or add (append to multi-value). Default: set")]
    public string LinkMode { get; set; } = "set";

    [CommandOption("--existing-file-id <TEMPLATE>")]
    [Description("Existing file ID (required for link_existing operation; templatable)")]
    public string? ExistingFileId { get; set; }

    [CommandOption("--overwrite-if-present")]
    [Description("Re-process even if the field already has a value (idempotency override)")]
    public bool OverwriteIfPresent { get; set; }

    [CommandOption("--name <NAME>")]
    [Description("Step display name (defaults to the step key)")]
    public string? Name { get; set; }

    [CommandOption("--start")]
    [Description("Set as start step")]
    public bool IsStartStep { get; set; }

    [CommandOption("--enabled")]
    [Description("Enable step immediately")]
    public bool Enabled { get; set; }

    [CommandOption("--on-success <STEP_ID>")]
    [Description("Step ID to execute on success")]
    public int? OnSuccessStepId { get; set; }

    [CommandOption("--on-failure <STEP_ID>")]
    [Description("Step ID to execute on failure")]
    public int? OnFailureStepId { get; set; }
}

public class WorkflowsFileHandlerAddCommand : BaseCommand<WorkflowFileHandlerAddSettings>
{
    static readonly HashSet<string> ValidOperations = new(StringComparer.Ordinal)
    {
        "fetch_and_link", "link_existing", "detach"
    };

    static readonly HashSet<string> ValidLinkModes = new(StringComparer.Ordinal)
    {
        "set", "add"
    };

    public override async Task<int> ExecuteAsync(CommandContext context, WorkflowFileHandlerAddSettings settings)
    {
        if (!ValidOperations.Contains(settings.Operation))
        {
            Renderer.Error($"--operation must be one of: fetch_and_link, link_existing, detach (got '{settings.Operation}').");
            return 1;
        }

        if (!ValidLinkModes.Contains(settings.LinkMode))
        {
            Renderer.Error($"--link-mode must be 'set' or 'add' (got '{settings.LinkMode}').");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(settings.Entity))
        {
            Renderer.Error("--entity is required.");
            return 1;
        }
        if (string.IsNullOrWhiteSpace(settings.RecordId))
        {
            Renderer.Error("--record-id is required.");
            return 1;
        }
        if (string.IsNullOrWhiteSpace(settings.Field))
        {
            Renderer.Error("--field is required.");
            return 1;
        }

        if (settings.Operation == "fetch_and_link" && string.IsNullOrWhiteSpace(settings.SourceUrl))
        {
            Renderer.Error("--source-url is required when --operation is fetch_and_link.");
            return 1;
        }
        if (settings.Operation == "link_existing" && string.IsNullOrWhiteSpace(settings.ExistingFileId))
        {
            Renderer.Error("--existing-file-id is required when --operation is link_existing.");
            return 1;
        }

        var paramsDict = new Dictionary<string, object?>
        {
            ["operation"] = settings.Operation,
            ["entity_name"] = settings.Entity,
            ["record_id"] = settings.RecordId,
            ["field_name"] = settings.Field,
            ["link_mode"] = settings.LinkMode,
        };

        if (!string.IsNullOrEmpty(settings.SourceUrl))
            paramsDict["source_url"] = settings.SourceUrl;
        if (!string.IsNullOrEmpty(settings.FileName))
            paramsDict["file_name"] = settings.FileName;
        if (!string.IsNullOrEmpty(settings.FolderId))
            paramsDict["folder_id"] = settings.FolderId;
        if (!string.IsNullOrEmpty(settings.ExistingFileId))
            paramsDict["existing_file_id"] = settings.ExistingFileId;
        if (settings.IsPublic)
            paramsDict["is_public"] = true;
        if (settings.OverwriteIfPresent)
            paramsDict["overwrite_if_present"] = true;

        var paramsJson = System.Text.Json.JsonSerializer.Serialize(paramsDict);
        var parameters = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(paramsJson);

        try
        {
            var client = GetClient();
            WorkflowStep? step = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync(Renderer.Status($"Adding FileHandler step '{settings.Key}'..."), async _ =>
                {
                    step = await client.AddWorkflowStepAsync(settings.WorkflowId,
                        new CreateWorkflowStepRequest(
                            settings.Key,
                            settings.Name ?? settings.Key,
                            "FileHandler",
                            settings.Enabled,
                            settings.IsStartStep,
                            null,
                            parameters
                        ));
                });

            if (settings.OnSuccessStepId.HasValue || settings.OnFailureStepId.HasValue)
            {
                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync(Renderer.Status($"Linking step {step!.Id}..."), async _ =>
                    {
                        var body = new Dictionary<string, object?>
                        {
                            ["name"] = step.Name,
                            ["action"] = step.Action,
                            ["enabled"] = step.Enabled,
                            ["is_start_step"] = step.IsStartStep,
                            ["on_success_step_id"] = settings.OnSuccessStepId,
                            ["on_failure_step_id"] = settings.OnFailureStepId,
                            ["parameters"] = parameters,
                        };
                        await client.UpdateWorkflowStepFullAsync(settings.WorkflowId, step.Id, body);
                    });
            }

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

// ── workflows integration-add ─────────────────────────────────────────────────

public class WorkflowIntegrationAddSettings : CommandSettings
{
    [CommandArgument(0, "<WORKFLOW_ID>")]
    [Description("Workflow ID")]
    public int WorkflowId { get; set; }

    [CommandArgument(1, "<STEP_KEY>")]
    [Description("Step key (unique identifier, snake_case)")]
    public string Key { get; set; } = "";

    [CommandOption("--provider <PROVIDER>")]
    [Description("Integration provider key (e.g. claude, openai, slack). Required.")]
    public string? Provider { get; set; }

    [CommandOption("--operation <OP>")]
    [Description("Operation key (see 'integrations get <provider>'). Required.")]
    public string? Operation { get; set; }

    [CommandOption("--credential-source <SOURCE>")]
    [Description("system | current_user | connection | entity_field (default: system)")]
    public string CredentialSource { get; set; } = "system";

    [CommandOption("--connection-id <ID>")]
    [Description("Connection ID (required when --credential-source is 'connection'; templatable)")]
    public string? ConnectionId { get; set; }

    [CommandOption("--credential-field <TEMPLATE>")]
    [Description("Credential field path (required when --credential-source is 'entity_field')")]
    public string? CredentialField { get; set; }

    [CommandOption("--input <KEY=VALUE>")]
    [Description("Operation input as key=value (repeatable; values support {{templating}})")]
    public string[] Inputs { get; set; } = [];

    [CommandOption("--inputs <JSON>")]
    [Description("Operation inputs as a JSON object (merged with --input flags)")]
    public string? InputsJson { get; set; }

    [CommandOption("--model <MODEL>")]
    [Description("Convenience shortcut for the 'model' input (AI providers)")]
    public string? Model { get; set; }

    [CommandOption("--name <NAME>")]
    [Description("Step display name (defaults to the step key)")]
    public string? Name { get; set; }

    [CommandOption("--start")]
    [Description("Set as start step")]
    public bool IsStartStep { get; set; }

    [CommandOption("--enabled")]
    [Description("Enable step immediately")]
    public bool Enabled { get; set; }

    [CommandOption("--on-success <STEP_ID>")]
    [Description("Step ID to execute on success")]
    public int? OnSuccessStepId { get; set; }

    [CommandOption("--on-failure <STEP_ID>")]
    [Description("Step ID to execute on failure")]
    public int? OnFailureStepId { get; set; }
}

public class WorkflowsIntegrationAddCommand : BaseCommand<WorkflowIntegrationAddSettings>
{
    static readonly HashSet<string> ValidSources = new(StringComparer.Ordinal)
    {
        "system", "current_user", "connection", "entity_field"
    };

    public override async Task<int> ExecuteAsync(CommandContext context, WorkflowIntegrationAddSettings settings)
    {
        System.Text.Json.JsonElement parameters;
        try { parameters = BuildParameters(settings); }
        catch (ArgumentException ex) { Renderer.Error(ex.Message); return 1; }

        try
        {
            var client = GetClient();
            WorkflowStep? step = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync(Renderer.Status($"Adding Integration step '{settings.Key}'..."), async _ =>
                {
                    step = await client.AddWorkflowStepAsync(settings.WorkflowId,
                        new CreateWorkflowStepRequest(
                            settings.Key,
                            settings.Name ?? settings.Key,
                            "Integration",
                            settings.Enabled,
                            settings.IsStartStep,
                            null,
                            parameters
                        ));
                });

            if (settings.OnSuccessStepId.HasValue || settings.OnFailureStepId.HasValue)
            {
                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync(Renderer.Status($"Linking step {step!.Id}..."), async _ =>
                    {
                        var body = new Dictionary<string, object?>
                        {
                            ["name"] = step.Name,
                            ["action"] = step.Action,
                            ["enabled"] = step.Enabled,
                            ["is_start_step"] = step.IsStartStep,
                            ["on_success_step_id"] = settings.OnSuccessStepId,
                            ["on_failure_step_id"] = settings.OnFailureStepId,
                            ["parameters"] = parameters,
                        };
                        await client.UpdateWorkflowStepFullAsync(settings.WorkflowId, step.Id, body);
                    });
            }

            Renderer.Success($"Step [#F97316]{Markup.Escape(step!.Key)}[/] (id: {step.Id}) added to workflow {settings.WorkflowId}.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }

    internal static System.Text.Json.JsonElement BuildParameters(WorkflowIntegrationAddSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Provider))
            throw new ArgumentException("--provider is required.");
        if (string.IsNullOrWhiteSpace(settings.Operation))
            throw new ArgumentException("--operation is required.");
        if (!ValidSources.Contains(settings.CredentialSource))
            throw new ArgumentException("--credential-source must be one of: system, current_user, connection, entity_field.");
        if (settings.CredentialSource == "connection" && string.IsNullOrWhiteSpace(settings.ConnectionId))
            throw new ArgumentException("--connection-id is required when --credential-source is 'connection'.");
        if (settings.CredentialSource == "entity_field" && string.IsNullOrWhiteSpace(settings.CredentialField))
            throw new ArgumentException("--credential-field is required when --credential-source is 'entity_field'.");

        var inputs = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(settings.InputsJson))
        {
            JsonNode? node;
            try { node = JsonNode.Parse(settings.InputsJson); }
            catch (System.Text.Json.JsonException) { throw new ArgumentException("--inputs is not valid JSON."); }
            if (node is not JsonObject parsed)
                throw new ArgumentException("--inputs must be a JSON object.");
            foreach (var kv in parsed)
                inputs[kv.Key] = kv.Value?.DeepClone();
        }
        foreach (var pair in settings.Inputs)
        {
            var idx = pair.IndexOf('=');
            if (idx < 1)
                throw new ArgumentException($"--input '{pair}' is not key=value.");
            inputs[pair[..idx]] = pair[(idx + 1)..];
        }
        if (!string.IsNullOrWhiteSpace(settings.Model))
            inputs["model"] = settings.Model;

        var paramsDict = new Dictionary<string, object?>
        {
            ["provider"] = settings.Provider,
            ["operation"] = settings.Operation,
            ["credential_source"] = settings.CredentialSource,
            ["inputs"] = inputs,
        };
        if (!string.IsNullOrEmpty(settings.ConnectionId))
            paramsDict["connection_id"] = settings.ConnectionId;
        // The server only reads credential_field_path; any other key is silently dropped.
        if (!string.IsNullOrEmpty(settings.CredentialField))
            paramsDict["credential_field_path"] = settings.CredentialField;

        return System.Text.Json.JsonSerializer.SerializeToElement(paramsDict);
    }
}

// ── workflows file-handler-example ────────────────────────────────────────────

public class WorkflowsFileHandlerExampleCommand : BaseCommand<EmptySettings>
{
    public override Task<int> ExecuteAsync(CommandContext context, EmptySettings settings)
    {
        var example = new Dictionary<string, object?>
        {
            ["operation"] = "fetch_and_link",
            ["source_url"] = "{{ $anythink.steps.fetch_detail.data.images[0].uri }}",
            ["entity_name"] = "artists",
            ["record_id"] = "{{ $anythink.trigger.id }}",
            ["field_name"] = "primary_image",
            ["file_name"] = "{{ $anythink.trigger.data.name }}",
            ["folder_id"] = "{{ $anythink.secrets.artist_photos_folder }}",
            ["is_public"] = true,
            ["link_mode"] = "set",
            ["overwrite_if_present"] = true,
        };
        var json = System.Text.Json.JsonSerializer.Serialize(example,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        Renderer.PrintJson(json);
        return Task.FromResult(0);
    }
}
