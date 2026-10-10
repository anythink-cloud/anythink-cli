using AnythinkCli.Output;
using Spectre.Console;

namespace AnythinkCli.Importers.Engine;

public static class PlanDiffRenderer
{
    public static void Render(ImportPlan plan)
    {
        Renderer.Header("Import plan (dry run)");

        foreach (var item in plan.Items)
        {
            var (mark, colour) = item.Action switch
            {
                PlanAction.Create => ("+", "green"),
                PlanAction.Merge => ("~", "yellow"),
                PlanAction.Report or PlanAction.Conflict => ("!", item.Category == ReportCategory.NotSupported ? "red" : "yellow"),
                _ => (" ", "dim")
            };
            AnsiConsole.MarkupLine($"[{Markup.Escape(colour)}]{Markup.Escape(mark)}[/] {Markup.Escape(item.Kind)} [bold]{Markup.Escape(Label(item))}[/]  [dim]{Markup.Escape(item.Reason)}[/]");
            foreach (var note in item.Notes ?? [])
                AnsiConsole.MarkupLine($"    [dim]{Markup.Escape(note)}[/]");
        }

        AnsiConsole.WriteLine();
        var counts = plan.Items.GroupBy(i => i.Action).ToDictionary(g => g.Key, g => g.Count());
        AnsiConsole.MarkupLine(
            $"{counts.GetValueOrDefault(PlanAction.Create)} to create, {counts.GetValueOrDefault(PlanAction.Merge)} to merge, " +
            $"{counts.GetValueOrDefault(PlanAction.Skip)} unchanged, {counts.GetValueOrDefault(PlanAction.Report) + counts.GetValueOrDefault(PlanAction.Conflict)} to review.");
    }

    public static string EntityAction(PlanItem entity) => entity.Action switch
    {
        PlanAction.Create => "create",
        PlanAction.Merge => $"merge (+{entity.Spec?["new_fields"]?.GetValue<int>() ?? 0} fields)",
        _ => "skip (up to date)"
    };

    private static string Label(PlanItem item) => item.Kind is PlanKind.Report ? item.Key : item.Key[(item.Key.IndexOf(':') + 1)..];
}
