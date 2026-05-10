using AnythinkCli.Client;
using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;

namespace AnythinkCli.Importers;

public record ImportOptions(
    bool DryRun,
    bool IncludeFlows
);

public record ImportResult(
    int           EntitiesCreated,
    int           EntitiesSkipped,
    int           FieldsCreated,
    int           FieldsSkipped,
    int           FieldsFailed,
    int           WorkflowsCreated,
    int           WorkflowsSkipped,
    int           WorkflowStepsCreated,
    int           WorkflowStepsFailed,
    List<string>  Errors
);

/// <summary>
/// Platform-agnostic orchestrator. Takes whatever schema an
/// <see cref="IPlatformImporter"/> produces and applies it to an Anythink
/// project — creating missing entities, merging fields into existing ones,
/// and (optionally) creating workflows.
/// </summary>
public class ImportRunner
{
    private readonly IPlatformImporter _source;
    private readonly AnythinkClient    _target;
    private readonly ImportOptions     _options;

    public ImportRunner(IPlatformImporter source, AnythinkClient target, ImportOptions options)
    {
        _source  = source;
        _target  = target;
        _options = options;
    }

    public async Task<ImportResult> RunAsync()
    {
        AnsiConsole.MarkupLine($"[dim]{_source.PlatformName}:[/]  {Markup.Escape(_source.ConnectionSummary)}");
        AnsiConsole.MarkupLine($"[dim]Anythink:[/]  org {Markup.Escape(_target.OrgId)}");
        AnsiConsole.WriteLine();

        ImportSchema schema = null!;
        await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
            .StartAsync($"Fetching {_source.PlatformName} schema...", async _ =>
                schema = await _source.FetchSchemaAsync(_options.IncludeFlows));

        AnsiConsole.MarkupLine($"Found [bold]{schema.Collections.Count}[/] collection(s) to import.");
        if (_options.IncludeFlows)
            AnsiConsole.MarkupLine($"Found [bold]{schema.Flows.Count}[/] flow(s) to import.");
        AnsiConsole.WriteLine();

        // Snapshot existing target state up-front. We refresh per-entity field
        // lists below as we add to them.
        List<Entity> existingEntities = [];
        await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
            .StartAsync("Fetching existing Anythink entities...", async _ =>
                existingEntities = await _target.GetEntitiesAsync());

        var entitiesByName = existingEntities
            .ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);

        if (_options.DryRun)
        {
            PrintDryRunPlan(schema, entitiesByName);
            return new ImportResult(0, 0, 0, 0, 0, 0, 0, 0, 0, []);
        }

        // ── Apply collections / fields ────────────────────────────────────────
        var result = new ResultAccumulator();

        foreach (var col in schema.Collections)
        {
            await ApplyCollectionAsync(col, entitiesByName, result);
        }

        // ── Apply flows ───────────────────────────────────────────────────────
        if (_options.IncludeFlows && schema.Flows.Count > 0)
        {
            AnsiConsole.WriteLine();
            Renderer.Header("Importing flows");
            var existingWorkflows = await _target.GetWorkflowsAsync();
            var existingWfNames   = existingWorkflows
                .Select(w => w.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var flow in schema.Flows)
            {
                await ApplyFlowAsync(flow, existingWfNames, result);
            }
        }

        PrintSummary(result);
        return result.Build();
    }

    // ── Collection / field application ────────────────────────────────────────

    private async Task ApplyCollectionAsync(
        ImportCollection col,
        Dictionary<string, Entity> entitiesByName,
        ResultAccumulator result)
    {
        Entity entity;
        bool   isNewEntity = !entitiesByName.TryGetValue(col.Name, out entity!);

        if (isNewEntity)
        {
            try
            {
                entity = await _target.CreateEntityAsync(new CreateEntityRequest(col.Name));
                entitiesByName[col.Name] = entity;
                result.EntitiesCreated++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Create entity '{col.Name}': {ex.Message}");
                Renderer.Error($"create entity failed — {Markup.Escape(col.Name)}: {Markup.Escape(ex.Message)}");
                return;
            }
        }
        else
        {
            result.EntitiesSkipped++;
        }

        // Merge fields: only add ones that don't already exist on the target entity.
        var existingFieldNames = (entity.Fields ?? new List<Field>())
            .Select(f => f.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int created = 0, skipped = 0, failed = 0;

        foreach (var field in col.Fields)
        {
            if (existingFieldNames.Contains(field.Name))
            {
                skipped++;
                result.FieldsSkipped++;
                continue;
            }

            try
            {
                await _target.AddFieldAsync(col.Name, new CreateFieldRequest(
                    Name:         field.Name,
                    DatabaseType: field.DatabaseType,
                    DisplayType:  field.DisplayType,
                    Label:        field.Label,
                    IsRequired:   field.IsRequired,
                    IsUnique:     field.IsUnique,
                    IsIndexed:    field.IsIndexed));
                existingFieldNames.Add(field.Name);
                created++;
                result.FieldsCreated++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{col.Name}.{field.Name}: {ex.Message}");
                failed++;
                result.FieldsFailed++;
            }
        }

        var verb = isNewEntity ? "created" : "merged";
        var summary = (skipped, failed) switch
        {
            (0, 0)         => $"{created} field(s)",
            (var s, 0)     => $"{created} new, [dim]{s} already present[/]",
            (0, var f)     => $"{created} field(s), [yellow]{f} failed[/]",
            (var s, var f) => $"{created} new, [dim]{s} already present[/], [yellow]{f} failed[/]"
        };
        Renderer.Success($"[bold]{Markup.Escape(col.Name)}[/] {verb} — {summary}");
    }

    // ── Flow application ──────────────────────────────────────────────────────

    private async Task ApplyFlowAsync(
        ImportFlow flow,
        HashSet<string> existingWfNames,
        ResultAccumulator result)
    {
        if (existingWfNames.Contains(flow.Name))
        {
            Renderer.Info($"[dim]skip[/]  {Markup.Escape(flow.Name)} (workflow already exists)");
            result.WorkflowsSkipped++;
            return;
        }

        Workflow? wf;
        try
        {
            wf = await _target.CreateWorkflowAsync(new CreateWorkflowRequest(
                flow.Name, null, flow.Trigger, false, flow.TriggerOptions));
            result.WorkflowsCreated++;
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Workflow '{flow.Name}': {ex.Message}");
            Renderer.Error($"create workflow failed — {Markup.Escape(flow.Name)}: {Markup.Escape(ex.Message)}");
            return;
        }

        // Pass 1: create steps, build a sourceId → dst step id map.
        var stepIdMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int stepOk = 0, stepErr = 0;

        var ordered = flow.Steps
            .OrderByDescending(s => s.IsStartStep)
            .ToList();

        foreach (var step in ordered)
        {
            try
            {
                var created = await _target.AddWorkflowStepAsync(wf.Id,
                    new CreateWorkflowStepRequest(
                        Key:         step.Key,
                        Name:        step.Name,
                        Action:      step.Action,
                        Enabled:     true,
                        IsStartStep: step.IsStartStep,
                        Description: step.Description,
                        Parameters:  step.Parameters));
                stepIdMap[step.SourceId] = created.Id;
                stepOk++;
                result.WorkflowStepsCreated++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{flow.Name}/{step.Name}: {ex.Message}");
                stepErr++;
                result.WorkflowStepsFailed++;
            }
        }

        // Pass 2: wire success / failure links.
        foreach (var step in ordered)
        {
            if (step.OnSuccessSourceId == null && step.OnFailureSourceId == null) continue;
            if (!stepIdMap.TryGetValue(step.SourceId, out var dstStepId)) continue;

            int? dstSuccess = step.OnSuccessSourceId != null && stepIdMap.TryGetValue(step.OnSuccessSourceId, out var rs) ? rs : null;
            int? dstFailure = step.OnFailureSourceId != null && stepIdMap.TryGetValue(step.OnFailureSourceId, out var rf) ? rf : null;
            if (dstSuccess == null && dstFailure == null) continue;

            try
            {
                await _target.UpdateWorkflowStepAsync(wf.Id, dstStepId,
                    new UpdateWorkflowStepLinksRequest(step.Name, step.Action, dstSuccess, dstFailure));
            }
            catch
            {
                // Link failures are non-fatal — best effort.
            }
        }

        var stepSummary = stepErr > 0
            ? $"{stepOk} step(s), [yellow]{stepErr} failed[/]"
            : $"{stepOk} step(s)";
        Renderer.Success($"[bold]{Markup.Escape(flow.Name)}[/] ({Markup.Escape(flow.Trigger)}) — {stepSummary}");
    }

    // ── Output ────────────────────────────────────────────────────────────────

    private void PrintDryRunPlan(ImportSchema schema, Dictionary<string, Entity> entitiesByName)
    {
        Renderer.Header("Import plan (dry run)");

        var table = Renderer.BuildTable("Collection", "Fields", "Action");
        foreach (var col in schema.Collections)
        {
            string action;
            if (entitiesByName.TryGetValue(col.Name, out var existing))
            {
                var existingFields = (existing.Fields ?? new List<Field>())
                    .Select(f => f.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var newFields = col.Fields.Count(f => !existingFields.Contains(f.Name));
                action = newFields == 0 ? "skip (up to date)" : $"merge (+{newFields} fields)";
            }
            else
            {
                action = "create";
            }
            Renderer.AddRow(table, col.Name, col.Fields.Count.ToString(), action);
        }
        AnsiConsole.Write(table);

        AnsiConsole.WriteLine();
        Renderer.Info("Re-run without [bold]--dry-run[/] to apply.");
    }

    private void PrintSummary(ResultAccumulator r)
    {
        AnsiConsole.WriteLine();
        Renderer.Header("Import complete");
        AnsiConsole.MarkupLine($"  Entities created : [green]{r.EntitiesCreated}[/]");
        AnsiConsole.MarkupLine($"  Entities skipped : [dim]{r.EntitiesSkipped}[/]");
        AnsiConsole.MarkupLine($"  Fields created   : [green]{r.FieldsCreated}[/]");
        if (r.FieldsSkipped > 0)
            AnsiConsole.MarkupLine($"  Fields skipped   : [dim]{r.FieldsSkipped}[/]");
        if (r.FieldsFailed > 0)
            AnsiConsole.MarkupLine($"  Fields failed    : [red]{r.FieldsFailed}[/]");
        if (_options.IncludeFlows)
        {
            AnsiConsole.MarkupLine($"  Workflows created: [green]{r.WorkflowsCreated}[/]");
            AnsiConsole.MarkupLine($"  Workflows skipped: [dim]{r.WorkflowsSkipped}[/]");
            AnsiConsole.MarkupLine($"  Steps created    : [green]{r.WorkflowStepsCreated}[/]");
            if (r.WorkflowStepsFailed > 0)
                AnsiConsole.MarkupLine($"  Steps failed     : [red]{r.WorkflowStepsFailed}[/]");
        }

        if (r.Errors.Count > 0)
        {
            AnsiConsole.WriteLine();
            Renderer.Warn($"{r.Errors.Count} error(s):");
            foreach (var e in r.Errors)
                AnsiConsole.MarkupLine($"  [red]·[/] {Markup.Escape(e)}");
        }
    }

    private sealed class ResultAccumulator
    {
        public int EntitiesCreated, EntitiesSkipped;
        public int FieldsCreated, FieldsSkipped, FieldsFailed;
        public int WorkflowsCreated, WorkflowsSkipped;
        public int WorkflowStepsCreated, WorkflowStepsFailed;
        public List<string> Errors = [];

        public ImportResult Build() => new(
            EntitiesCreated, EntitiesSkipped,
            FieldsCreated, FieldsSkipped, FieldsFailed,
            WorkflowsCreated, WorkflowsSkipped,
            WorkflowStepsCreated, WorkflowStepsFailed,
            Errors);
    }
}
