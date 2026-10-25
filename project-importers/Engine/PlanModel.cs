using System.Text.Json.Nodes;

namespace Anythink.ProjectImporters.Engine;

public enum PlanAction { Create, Merge, Skip, Report }

public enum ReportCategory { Mapped, MappedWithChanges, NeedsReview, NotSupported }

public static class PlanKind
{
    public const string Entity = "entity";
    public const string Field = "field";
    public const string Role = "role";
    public const string Permission = "permission";
    public const string Workflow = "workflow";
    public const string WorkflowStep = "workflow_step";
    public const string File = "file";
    public const string RecordBatch = "record_batch";
    public const string Report = "report";

    public static int Rank(string kind) => kind switch
    {
        Entity => 0,
        Field => 1,
        Role => 2,
        Permission => 3,
        Workflow => 4,
        WorkflowStep => 5,
        File => 6,
        RecordBatch => 7,
        _ => 8
    };
}

public record PlanItem(
    string Key,
    string Kind,
    PlanAction Action,
    ReportCategory Category,
    string Reason,
    string? SourceRef = null,
    string? TargetName = null,
    string? Parent = null,
    IReadOnlyList<string>? DependsOn = null,
    JsonObject? Spec = null,
    string? TargetHash = null,
    IReadOnlyList<string>? Notes = null
);

public record PlanSource(string Platform, string Url, IReadOnlyList<string> Scope);

public record PlanTarget(string ApiUrl, string ProjectId);

public record PlanOptions(bool IncludeFlows, bool IncludeData, bool IncludeFiles, bool IncludeRoles);

public record PlanContext(PlanSource Source, PlanTarget Target, string RunId, DateTimeOffset CreatedAt);

public record ImportPlan(
    int FormatVersion,
    string RunId,
    DateTimeOffset CreatedAt,
    PlanSource Source,
    PlanTarget Target,
    PlanOptions Options,
    string TargetFingerprint,
    IReadOnlyList<PlanItem> Items
)
{
    public const int CurrentFormatVersion = 1;
}
