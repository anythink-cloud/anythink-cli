using Anythink.ProjectImporters.Engine;

namespace Anythink.ProjectImporters;

public record ReviewStep(string Where, string Action, string? Note);

public record CollectionPlanRow(string Collection, int Fields, string Action);

public interface IImportReporter
{
    void Connected(string platform, string connection, string orgId);
    void SchemaFound(int collections, int? flows, int? files, int? roles);
    Task<T> WorkingAsync<T>(string message, Func<Task<T>> work);
    void CollectionPlan(IReadOnlyList<CollectionPlanRow> rows, IReadOnlyList<string> notes);
    bool Confirm(string question);
    void Cancelled();
    void Section(string title);
    void Skipped(string subject, string reason);
    void Warn(string message);
    void Error(string message);
    void CollectionApplied(string name, bool isNew, int created, int skipped, int failed);
    void FlowCreated(string name, string triggers, string summary, bool needsAttention);
    void FilesImported(int uploaded, int alreadyPresent, int failed);
    void RecordsImported(string collection, int created, int failed);
    void RoleCreated(string name, int permissions);
    void PlanJson(string json);
    void DryRunPlan(ImportPlan plan, string? savedTo);
    void Complete(ImportResult result, ImportOptions options, IReadOnlyList<ReviewStep> stepsNeedingReview);
}

public sealed class SilentImportReporter : IImportReporter
{
    public static readonly SilentImportReporter Instance = new();

    public void Connected(string platform, string connection, string orgId) { }
    public void SchemaFound(int collections, int? flows, int? files, int? roles) { }
    public Task<T> WorkingAsync<T>(string message, Func<Task<T>> work) => work();
    public void CollectionPlan(IReadOnlyList<CollectionPlanRow> rows, IReadOnlyList<string> notes) { }
    public bool Confirm(string question) => true;
    public void Cancelled() { }
    public void Section(string title) { }
    public void Skipped(string subject, string reason) { }
    public void Warn(string message) { }
    public void Error(string message) { }
    public void CollectionApplied(string name, bool isNew, int created, int skipped, int failed) { }
    public void FlowCreated(string name, string triggers, string summary, bool needsAttention) { }
    public void FilesImported(int uploaded, int alreadyPresent, int failed) { }
    public void RecordsImported(string collection, int created, int failed) { }
    public void RoleCreated(string name, int permissions) { }
    public void PlanJson(string json) { }
    public void DryRunPlan(ImportPlan plan, string? savedTo) { }
    public void Complete(ImportResult result, ImportOptions options, IReadOnlyList<ReviewStep> stepsNeedingReview) { }
}
