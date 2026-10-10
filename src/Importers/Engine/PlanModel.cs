using System.Text.Json.Nodes;

namespace AnythinkCli.Importers.Engine;

public enum PlanAction { Create, Merge, Skip, Report, Conflict }

public enum ReportCategory { Mapped, MappedWithChanges, NeedsReview, NotSupported }

public static class PlanKind
{
    public const string Entity = "entity";
    public const string Field = "field";
    public const string Relationship = "relationship";
    public const string Public = "public";
    public const string Role = "role";
    public const string Permission = "permission";
    public const string Workflow = "workflow";
    public const string WorkflowStep = "workflow_step";
    public const string File = "file";
    public const string RecordBatch = "record_batch";
    public const string User = "user";
    public const string Report = "report";

    internal static int Rank(string kind) => kind switch
    {
        Entity => 0,
        Field => 1,
        Relationship => 2,
        Public => 3,
        Role => 4,
        Permission => 5,
        Workflow => 6,
        WorkflowStep => 7,
        File => 8,
        RecordBatch => 9,
        User => 10,
        _ => 11
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
    string? SuggestedFix = null,
    string? SourceSnippet = null,
    IReadOnlyList<string>? Notes = null
);

public record PlanSource(string Platform, string Url, IReadOnlyList<string> Scope)
{
    public static string SanitiseUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "";
        return new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" }.Uri.GetLeftPart(UriPartial.Path);
    }
}

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
