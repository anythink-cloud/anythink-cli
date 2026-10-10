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
        var entities = await Read("entities", target.GetEntitiesAsync);
        var workflows = options.IncludeFlows ? await Read("workflows", target.GetWorkflowsAsync) : [];
        var roles = options.IncludeRoles ? await Read("roles", target.GetRolesAsync) : [];
        var permissions = options.IncludeRoles ? await Read("permissions", target.GetPermissionsAsync) : [];

        var sourceFileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (options.IncludeFiles)
            foreach (var f in await Read("files", target.GetAllFilesAsync))
                if (ImportRunner.ReadSourceFileId(f.CustomMetadata, platformName) is { } sourceId)
                    sourceFileIds.Add(sourceId);

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (options.IncludeData)
        {
            var existing = entities.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var col in schema.Collections.Where(c => existing.Contains(c.Name) && c.DataUnsupportedReason is null))
                counts[col.Name] = await Read($"records in '{col.Name}'", () => CountRecordsAsync(target, col.Name));
        }

        return new TargetSnapshot(entities, workflows, roles, permissions, sourceFileIds, counts);
    }

    private static async Task<T> Read<T>(string what, Func<Task<T>> read)
    {
        try { return await read(); }
        catch (Exception ex) when (ex is not CliException)
        {
            throw new CliException($"Couldn't read the project's {Spectre.Console.Markup.Escape(what)} to plan the import: {Spectre.Console.Markup.Escape(ex.Message)}");
        }
    }

    internal static async Task<int> CountRecordsAsync(AnythinkClient target, string entityName)
    {
        var first = await target.ListItemsAsync(entityName, 1, 1);
        return first.TotalCount ?? first.Items.Count;
    }
}
