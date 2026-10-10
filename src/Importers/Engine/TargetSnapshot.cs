using AnythinkCli.Client;
using AnythinkCli.Models;

namespace AnythinkCli.Importers.Engine;

public record TargetSnapshot(
    IReadOnlyList<Entity> Entities,
    IReadOnlyList<Workflow> Workflows,
    IReadOnlyList<RoleResponse> Roles,
    IReadOnlyList<Permission> Permissions,
    IReadOnlySet<string> ImportedSourceFileIds,
    IReadOnlyDictionary<string, int> RecordCounts
)
{
    public static TargetSnapshot Empty { get; } = new([], [], [], [], new HashSet<string>(), new Dictionary<string, int>());
}

public static class TargetSnapshotReader
{
    public static async Task<TargetSnapshot> ReadAsync(
        AnythinkClient target, PlanOptions options, string platformName, ImportSchema schema)
    {
        var entities = await target.GetEntitiesAsync();
        var workflows = options.IncludeFlows ? await target.GetWorkflowsAsync() : [];
        var roles = options.IncludeRoles ? await target.GetRolesAsync() : [];
        var permissions = options.IncludeRoles ? await target.GetPermissionsAsync() : [];

        var sourceFileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (options.IncludeFiles)
            foreach (var f in await target.GetAllFilesAsync())
                if (ImportRunner.ReadSourceFileId(f.CustomMetadata, platformName) is { } sourceId)
                    sourceFileIds.Add(sourceId);

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (options.IncludeData)
        {
            var existing = entities.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var col in schema.Collections.Where(c => existing.Contains(c.Name) && c.DataUnsupportedReason is null))
                counts[col.Name] = await CountRecordsAsync(target, col.Name);
        }

        return new TargetSnapshot(entities, workflows, roles, permissions, sourceFileIds, counts);
    }

    internal static async Task<int> CountRecordsAsync(AnythinkClient target, string entityName)
    {
        var first = await target.ListItemsAsync(entityName, 1, 1);
        return first.TotalCount ?? first.Items.Count;
    }
}
