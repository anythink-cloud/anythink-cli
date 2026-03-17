using AnythinkCli.Client;
using AnythinkCli.Config;
using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace AnythinkCli.Commands;

// ── anythink migrate ──────────────────────────────────────────────────────────
//
//  Copies user-defined entities + their custom fields from a source project
//  profile to a target project profile. Optionally creates workflow skeletons
//  (step configs must be configured in the dashboard afterward).
//
//  Usage:
//    anythink migrate --from getahead-staging --to getahead-prod
//    anythink migrate --from getahead-staging --to getahead-prod --dry-run
//    anythink migrate --from getahead-staging --to getahead-prod --include-workflows
// ─────────────────────────────────────────────────────────────────────────────

public class MigrateSettings : CommandSettings
{
    [CommandOption("--from <PROFILE>")]
    [Description("Source project profile (run 'anythink config show' to list)")]
    public string? From { get; set; }

    [CommandOption("--to <PROFILE>")]
    [Description("Target project profile")]
    public string? To { get; set; }

    [CommandOption("--dry-run")]
    [Description("Preview what would be migrated without making any changes")]
    public bool DryRun { get; set; }

    [CommandOption("--include-workflows")]
    [Description("Also create workflow skeletons on the target (step configs must be set up manually)")]
    public bool IncludeWorkflows { get; set; }
}

public class MigrateCommand : AsyncCommand<MigrateSettings>
{
    private int _entitiesCreated;
    private int _entitiesSkipped;
    private int _fieldsCreated;
    private int _fieldsSkipped;
    private int _fieldsFailed;
    private int _workflowsCreated;

    public override async Task<int> ExecuteAsync(CommandContext context, MigrateSettings settings)
    {
        // ── Resolve profiles ──────────────────────────────────────────────────
        var fromKey = settings.From ?? AnsiConsole.Ask<string>("[#F97316]Source profile:[/]");
        var toKey   = settings.To   ?? AnsiConsole.Ask<string>("[#F97316]Target profile:[/]");

        var fromProfile = ConfigService.GetProfile(fromKey);
        var toProfile   = ConfigService.GetProfile(toKey);

        if (fromProfile == null)
        {
            Renderer.Error($"Source profile '{fromKey}' not found.");
            AnsiConsole.MarkupLine("Run [bold #F97316]anythink config show[/] to list saved profiles.");
            return 1;
        }

        if (toProfile == null)
        {
            Renderer.Error($"Target profile '{toKey}' not found.");
            AnsiConsole.MarkupLine("Run [bold #F97316]anythink projects use <id>[/] to save a target profile first.");
            return 1;
        }

        var srcClient = new AnythinkClient(fromProfile);
        var dstClient = new AnythinkClient(toProfile);

        if (settings.DryRun)
            AnsiConsole.MarkupLine("[yellow]── DRY RUN — no changes will be made ──[/]");

        AnsiConsole.MarkupLine(
            $"\n[bold]From:[/] [#F97316]{Markup.Escape(fromKey)}[/]  " +
            $"→  [bold]To:[/] [#F97316]{Markup.Escape(toKey)}[/]\n");

        // ── Fetch source data ─────────────────────────────────────────────────
        List<Entity>   srcEntities  = [];
        List<Workflow> srcWorkflows = [];

        await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
            .StartAsync("Reading source project...", async _ =>
            {
                srcEntities = await srcClient.GetEntitiesAsync();
                if (settings.IncludeWorkflows)
                    srcWorkflows = await srcClient.GetWorkflowsAsync();
            });

        // User-defined entities only — skip system/built-in
        var migratable = srcEntities
            .Where(e => !e.IsSystem)
            .OrderBy(e => e.Name)
            .ToList();

        AnsiConsole.MarkupLine(
            $"Found [#F97316]{migratable.Count}[/] user-defined " +
            $"{(migratable.Count == 1 ? "entity" : "entities")}" +
            (settings.IncludeWorkflows ? $" and [#F97316]{srcWorkflows.Count}[/] workflows." : "."));

        if (migratable.Count == 0 && srcWorkflows.Count == 0)
        {
            Renderer.Info("Nothing to migrate.");
            return 0;
        }

        // ── Fetch existing target entities ────────────────────────────────────
        List<Entity> dstEntities = [];
        await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
            .StartAsync("Reading target project...", async _ =>
                dstEntities = await dstClient.GetEntitiesAsync());

        var dstEntityNames = dstEntities
            .Select(e => e.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // ── Migrate entities + fields ─────────────────────────────────────────
        Renderer.Header("Entities");

        foreach (var entity in migratable)
        {
            var exists = dstEntityNames.Contains(entity.Name);

            if (exists)
            {
                AnsiConsole.MarkupLine($"  [dim]skip[/]  {Markup.Escape(entity.Name)}  [dim](exists)[/]");
                _entitiesSkipped++;
            }
            else
            {
                if (!settings.DryRun)
                {
                    try
                    {
                        await dstClient.CreateEntityAsync(new CreateEntityRequest(
                            entity.Name,
                            entity.EnableRls,
                            entity.IsPublic,
                            entity.LockNewRecords,
                            entity.IsJunction
                        ));
                        dstEntityNames.Add(entity.Name);
                    }
                    catch (AnythinkException ex)
                    {
                        AnsiConsole.MarkupLine(
                            $"  [red]fail[/]  {Markup.Escape(entity.Name)}  " +
                            $"[dim]{Markup.Escape(ex.Message)}[/]");
                        _entitiesSkipped++;
                        continue;
                    }
                }
                AnsiConsole.MarkupLine($"  [green]+[/]   {Markup.Escape(entity.Name)}");
                _entitiesCreated++;
            }

            // ── Migrate custom (non-locked) fields ────────────────────────────
            var customFields = (entity.Fields ?? []).Where(f => !f.Locked).ToList();
            if (customFields.Count == 0) continue;

            // Get existing field names on target
            var existingFieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (dstEntityNames.Contains(entity.Name))
            {
                var dstEntity = dstEntities.FirstOrDefault(e =>
                    e.Name.Equals(entity.Name, StringComparison.OrdinalIgnoreCase));
                foreach (var f in dstEntity?.Fields ?? [])
                    existingFieldNames.Add(f.Name);
            }

            foreach (var field in customFields)
            {
                if (existingFieldNames.Contains(field.Name))
                {
                    AnsiConsole.MarkupLine(
                        $"      [dim]skip[/]  .{Markup.Escape(field.Name)}  [dim](exists)[/]");
                    _fieldsSkipped++;
                    continue;
                }

                if (!settings.DryRun)
                {
                    try
                    {
                        await dstClient.AddFieldAsync(entity.Name, new CreateFieldRequest(
                            field.Name,
                            field.DatabaseType,
                            field.DisplayType,
                            field.Label,
                            null,
                            field.DefaultValue,
                            field.IsRequired,
                            field.IsUnique,
                            field.IsSearchable,
                            field.IsIndexed
                        ));
                    }
                    catch (AnythinkException ex)
                    {
                        AnsiConsole.MarkupLine(
                            $"      [red]fail[/]  .{Markup.Escape(field.Name)}  " +
                            $"[dim]{Markup.Escape(ex.Message)}[/]");
                        _fieldsFailed++;
                        continue;
                    }
                }

                AnsiConsole.MarkupLine(
                    $"      [green]+[/]   .{Markup.Escape(field.Name)}  " +
                    $"[dim]({Markup.Escape(field.DatabaseType)})[/]");
                _fieldsCreated++;
            }
        }

        // ── Migrate workflow skeletons (optional) ─────────────────────────────
        if (settings.IncludeWorkflows && srcWorkflows.Count > 0)
        {
            AnsiConsole.WriteLine();
            Renderer.Header("Workflows");
            AnsiConsole.MarkupLine(
                "[yellow]Note:[/] Only the workflow name and trigger type are created.\n" +
                "[dim]Step configurations must be set up manually in the Anythink dashboard.[/]\n");

            foreach (var wf in srcWorkflows.OrderBy(w => w.Name))
            {
                if (!settings.DryRun)
                {
                    try
                    {
                        await dstClient.CreateWorkflowAsync(new CreateWorkflowRequest(
                            wf.Name, wf.Description, wf.Trigger, false, new { }
                        ));
                    }
                    catch (AnythinkException ex)
                    {
                        AnsiConsole.MarkupLine(
                            $"  [red]fail[/]  {Markup.Escape(wf.Name)}  " +
                            $"[dim]{Markup.Escape(ex.Message)}[/]");
                        continue;
                    }
                }

                AnsiConsole.MarkupLine(
                    $"  [green]+[/]   {Markup.Escape(wf.Name)}  " +
                    $"[dim]({Markup.Escape(wf.Trigger)})[/]");
                _workflowsCreated++;
            }
        }

        // ── Summary ───────────────────────────────────────────────────────────
        AnsiConsole.WriteLine();
        Renderer.Header("Summary");

        if (settings.DryRun)
            AnsiConsole.MarkupLine("[yellow]DRY RUN — no changes were made.[/]\n");

        AnsiConsole.MarkupLine(
            $"  Entities  [green]+{_entitiesCreated}[/]  skipped [dim]{_entitiesSkipped}[/]");
        AnsiConsole.MarkupLine(
            $"  Fields    [green]+{_fieldsCreated}[/]  skipped [dim]{_fieldsSkipped}[/]" +
            (_fieldsFailed > 0 ? $"  [red]failed {_fieldsFailed}[/]" : ""));

        if (settings.IncludeWorkflows)
            AnsiConsole.MarkupLine(
                $"  Workflows [green]+{_workflowsCreated}[/]  [dim](skeletons — configure steps in dashboard)[/]");

        AnsiConsole.WriteLine();
        if (_entitiesCreated > 0 || _fieldsCreated > 0)
            Renderer.Success("Migration complete.");
        else
            Renderer.Info("Nothing new — all entities and fields already exist on target.");

        return _fieldsFailed > 0 ? 2 : 0;
    }
}
