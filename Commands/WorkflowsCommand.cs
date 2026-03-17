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
            "Api" => new { api_route = settings.ApiRoute ?? "" },
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
                        options
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
            try { payload = System.Text.Json.JsonSerializer.Deserialize<JsonObject>(settings.Payload); }
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
