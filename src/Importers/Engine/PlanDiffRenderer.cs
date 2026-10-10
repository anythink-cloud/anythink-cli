using AnythinkCli.Output;
using Spectre.Console;

namespace AnythinkCli.Importers.Engine;

public static class PlanDiffRenderer
{
    private static readonly HashSet<string> NestedKinds = [PlanKind.Field, PlanKind.Permission, PlanKind.WorkflowStep];

    public static void Render(ImportPlan plan)
    {
        Renderer.Header("Import plan (dry run)");

        var unchanged = plan.Items.Where(IsUnchanged).ToList();
        var shown = plan.Items.Where(i => !IsUnchanged(i)).ToList();
        var shownKeys = shown.Select(i => i.Key).ToHashSet();
        var children = shown.Where(i => NestedKinds.Contains(i.Kind) && i.Parent is not null && shownKeys.Contains(i.Parent))
            .ToLookup(i => i.Parent!);
        var nested = children.SelectMany(g => g).ToHashSet();

        foreach (var item in shown.Where(i => !nested.Contains(i)))
        {
            WriteLine(item, nested: false);
            foreach (var child in children[item.Key]) WriteLine(child, nested: true);
        }

        if (unchanged.Count > 0)
        {
            var parts = unchanged.GroupBy(i => i.Kind).OrderBy(g => PlanKind.Rank(g.Key))
                .Select(g => $"{g.Count()} {g.Key.Replace('_', ' ')}(s)");
            AnsiConsole.MarkupLine($"[dim]Already in the project: {Markup.Escape(string.Join(", ", parts))}[/]");
        }

        AnsiConsole.WriteLine();
        var counts = plan.Items.GroupBy(i => i.Action).ToDictionary(g => g.Key, g => g.Count());
        AnsiConsole.MarkupLine(
            $"{counts.GetValueOrDefault(PlanAction.Create)} to create, {counts.GetValueOrDefault(PlanAction.Merge)} to merge, " +
            $"{counts.GetValueOrDefault(PlanAction.Skip)} unchanged, {counts.GetValueOrDefault(PlanAction.Report)} to review.");
    }

    public static string EntityAction(PlanItem entity) => entity.Action switch
    {
        PlanAction.Create => "create",
        PlanAction.Merge => $"merge (+{entity.Spec?["new_fields"]?.GetValue<int>() ?? 0} fields)",
        _ => "skip (up to date)"
    };

    private static bool IsUnchanged(PlanItem item) => item.Action == PlanAction.Skip && item.Category == ReportCategory.Mapped;

    private static void WriteLine(PlanItem item, bool nested)
    {
        var (mark, colour) = item.Action switch
        {
            PlanAction.Create => ("+", "green"),
            PlanAction.Merge => ("~", "yellow"),
            PlanAction.Report => ("!", item.Category == ReportCategory.NotSupported ? "red" : "yellow"),
            _ => (" ", "dim")
        };

        if (item.Kind == PlanKind.Report)
        {
            AnsiConsole.MarkupLine($"[{Markup.Escape(colour)}]{Markup.Escape(mark)}[/] [bold]{Markup.Escape(ReportLabel(item))}[/]  {Markup.Escape(item.Reason)}");
            return;
        }

        var label = Markup.Escape(item.TargetName ?? item.Key);
        var line = $"[{Markup.Escape(colour)}]{Markup.Escape(mark)}[/] {Markup.Escape(item.Kind.Replace('_', ' '))} [bold]{label}[/]  [dim]{Markup.Escape(item.Reason)}[/]";
        AnsiConsole.MarkupLine(nested ? $"    {line}" : line);
        foreach (var note in item.Notes ?? [])
        {
            var text = Markup.Escape(note);
            AnsiConsole.MarkupLine(nested ? $"        [dim]{text}[/]" : $"    [dim]{text}[/]");
        }
    }

    private static string ReportLabel(PlanItem item) => item.Key.Split(':').ElementAtOrDefault(1) ?? "note";
}
