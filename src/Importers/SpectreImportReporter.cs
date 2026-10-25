using Anythink.ProjectImporters;
using Anythink.ProjectImporters.Engine;
using Anythink.ProjectImporters.Engine;
using AnythinkCli.Output;
using Spectre.Console;

namespace AnythinkCli.Importers;

public sealed class SpectreImportReporter : IImportReporter
{
    public void Connected(string platform, string connection, string orgId)
    {
        AnsiConsole.MarkupLine($"[dim]{Markup.Escape(platform)}:[/]  {Markup.Escape(connection)}");
        AnsiConsole.MarkupLine($"[dim]Anythink:[/]  org {Markup.Escape(orgId)}");
        AnsiConsole.WriteLine();
    }

    public void SchemaFound(int collections, int? flows, int? files, int? roles)
    {
        AnsiConsole.MarkupLine($"Found [bold]{collections}[/] collection(s) to import.");
        if (flows is { } f)
            AnsiConsole.MarkupLine($"Found [bold]{f}[/] flow(s) to import.");
        if (files is { } n)
            AnsiConsole.MarkupLine($"Found [bold]{n}[/] file(s) to import.");
        if (roles is { } r)
            AnsiConsole.MarkupLine($"Found [bold]{r}[/] role(s) to import.");
        AnsiConsole.WriteLine();
    }

    public async Task<T> WorkingAsync<T>(string message, Func<Task<T>> work)
    {
        T result = default!;
        await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
            .StartAsync(Renderer.Status(message), async _ => result = await work());
        return result;
    }

    public void CollectionPlan(IReadOnlyList<CollectionPlanRow> rows, IReadOnlyList<string> notes)
    {
        Renderer.Header("Import plan");
        var table = Renderer.BuildTable("Collection", "Fields", "Action");
        foreach (var row in rows)
            Renderer.AddRow(table, row.Collection, row.Fields.ToString(), row.Action);
        AnsiConsole.Write(table);
        foreach (var note in notes)
            Renderer.Info(Markup.Escape(note));
    }

    public bool Confirm(string question) => AnsiConsole.Confirm(Markup.Escape(question), defaultValue: false);

    public void Cancelled() => Renderer.Info("Cancelled — nothing was changed.");

    public void Section(string title)
    {
        AnsiConsole.WriteLine();
        Renderer.Header(title);
    }

    public void Skipped(string subject, string reason) =>
        Renderer.Info($"[dim]skip[/]  {Markup.Escape(subject)} — {Markup.Escape(reason)}");

    public void Warn(string message) => Renderer.Warn(Markup.Escape(message));

    public void Error(string message) => Renderer.Error(message);

    public void CollectionApplied(string name, bool isNew, int created, int skipped, int failed)
    {
        var summary = (skipped, failed) switch
        {
            (0, 0) => $"{created} field(s)",
            (var s, 0) => $"{created} new, [dim]{s} already present[/]",
            (0, var f) => $"{created} field(s), [yellow]{f} failed[/]",
            (var s, var f) => $"{created} new, [dim]{s} already present[/], [yellow]{f} failed[/]"
        };
        Renderer.Success($"[bold]{Markup.Escape(name)}[/] {(isNew ? "created" : "merged")} — {summary}");
    }

    public void FlowCreated(string name, string triggers, string summary, bool needsAttention)
    {
        var tone = needsAttention ? "yellow" : "default";
        Renderer.Success($"[bold]{Markup.Escape(name)}[/] ({Markup.Escape(triggers)}) created — [{tone}]{Markup.Escape(summary)}[/]");
    }

    public void FilesImported(int uploaded, int alreadyPresent, int failed) =>
        Renderer.Success(
            $"Files — [green]{uploaded} uploaded[/]" +
            (alreadyPresent > 0 ? $", [dim]{alreadyPresent} already present[/]" : "") +
            (failed > 0 ? $", [yellow]{failed} failed[/]" : ""));

    public void RecordsImported(string collection, int created, int failed)
    {
        var msg = failed == 0
            ? $"{created} record(s)"
            : $"{created} record(s), [yellow]{failed} failed[/]";
        Renderer.Success($"[bold]{Markup.Escape(collection)}[/] — {msg}");
    }

    public void RoleCreated(string name, int permissions) =>
        Renderer.Success($"[bold]{Markup.Escape(name)}[/] created — {permissions} permission(s)");

    public void PlanJson(string json) => AnsiConsole.Profile.Out.Writer.WriteLine(json);

    public void DryRunPlan(ImportPlan plan, string? savedTo)
    {
        PlanDiffRenderer.Render(plan);
        AnsiConsole.WriteLine();
        if (savedTo is not null)
            Renderer.Info($"Plan saved to {Markup.Escape(savedTo)}. Nothing in the project was changed.");
        else
            Renderer.Info("Re-run without [bold]--dry-run[/] to apply.");
    }

    public void Complete(ImportResult r, ImportOptions options, IReadOnlyList<ReviewStep> stepsNeedingReview)
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
        if (options.IncludeFlows)
        {
            AnsiConsole.MarkupLine($"  Workflows created: [green]{r.WorkflowsCreated}[/]");
            AnsiConsole.MarkupLine($"  Workflows skipped: [dim]{r.WorkflowsSkipped}[/]");
            AnsiConsole.MarkupLine($"  Steps created    : [green]{r.WorkflowStepsCreated}[/]");
            if (r.WorkflowStepsFailed > 0)
                AnsiConsole.MarkupLine($"  Steps failed     : [red]{r.WorkflowStepsFailed}[/]");
        }
        if (options.IncludeData)
        {
            AnsiConsole.MarkupLine($"  Records created  : [green]{r.RecordsCreated}[/]");
            if (r.RecordsFailed > 0)
                AnsiConsole.MarkupLine($"  Records failed   : [red]{r.RecordsFailed}[/]");
        }
        if (options.IncludeFiles)
        {
            AnsiConsole.MarkupLine($"  Files uploaded   : [green]{r.FilesUploaded}[/]");
            if (r.FilesSkipped > 0)
                AnsiConsole.MarkupLine($"  Files skipped    : [dim]{r.FilesSkipped}[/]");
            if (r.FilesFailed > 0)
                AnsiConsole.MarkupLine($"  Files failed     : [red]{r.FilesFailed}[/]");
        }
        if (options.IncludeRoles)
        {
            AnsiConsole.MarkupLine($"  Roles created    : [green]{r.RolesCreated}[/]");
            if (r.RolesSkipped > 0)
                AnsiConsole.MarkupLine($"  Roles skipped    : [dim]{r.RolesSkipped}[/]");
            AnsiConsole.MarkupLine($"  Perms attached   : [green]{r.PermissionsAttached}[/]");
        }

        if (r.Warnings is { Count: > 0 })
        {
            AnsiConsole.WriteLine();
            Renderer.Warn($"{r.Warnings.Count} warning(s):");
            foreach (var w in r.Warnings)
                AnsiConsole.MarkupLine($"  [yellow]·[/] {Markup.Escape(w)}");
        }

        if (r.Errors.Count > 0)
        {
            AnsiConsole.WriteLine();
            Renderer.Warn($"{r.Errors.Count} error(s):");
            foreach (var e in r.Errors)
                AnsiConsole.MarkupLine($"  [red]·[/] {Markup.Escape(e)}");
        }

        if (stepsNeedingReview.Count > 0)
        {
            AnsiConsole.WriteLine();
            Renderer.Warn($"{stepsNeedingReview.Count} workflow step(s) need manual review:");
            foreach (var (where, action, note) in stepsNeedingReview)
            {
                AnsiConsole.MarkupLine($"  [yellow]·[/] [bold]{Markup.Escape(where)}[/] → [#F97316]{Markup.Escape(action)}[/]");
                if (!string.IsNullOrEmpty(note))
                    AnsiConsole.MarkupLine($"     [dim]{Markup.Escape(note)}[/]");
            }
        }
    }
}
