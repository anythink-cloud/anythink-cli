namespace AnythinkCli.Importers.Directus;

public class DirectusImporter : IPlatformImporter
{
    private readonly DirectusClient _client;
    private readonly string         _url;

    // Field names Anythink reserves on every entity — never try to import these.
    private static readonly HashSet<string> ReservedTargetFieldNames =
        new(StringComparer.OrdinalIgnoreCase)
        { "id", "tenant_id", "locked", "created_at", "updated_at" };

    // Directus built-in field names that Anythink provides automatically
    // and which would never be useful to copy across.
    private static readonly HashSet<string> SkippedSourceFieldNames =
        new(StringComparer.OrdinalIgnoreCase)
        { "id", "user_created", "user_updated", "date_created", "date_updated", "sort" };

    // Directus virtual/relational types that don't map to a DB column. Skip.
    private static readonly HashSet<string> AliasTypes =
        new(StringComparer.OrdinalIgnoreCase)
        { "alias", "o2m", "m2m", "m2a", "translations", "presentation", "group" };

    public string PlatformName     => "Directus";
    public string ConnectionSummary => _url;

    public DirectusImporter(string url, string token)
    {
        _url    = url;
        _client = new DirectusClient(url, token);
    }

    public async Task<ImportSchema> FetchSchemaAsync(bool includeFlows)
    {
        var collections = await _client.GetCollectionsAsync();
        var allFields   = await _client.GetFieldsAsync();

        // Skip Directus system collections (directus_*) and virtual collections
        // (no schema). Keep user-hidden collections — those are typically junction
        // tables and DO matter for many-to-many relationships in Anythink.
        var userCollections = collections
            .Where(c => !c.Collection.StartsWith("directus_", StringComparison.OrdinalIgnoreCase))
            .Where(c => c.Schema is not null)
            .ToList();

        // Don't filter hidden fields — junction tables (which Anythink needs to
        // back many-to-many relationships) have their FK columns marked
        // hidden in Directus, and we still want them.
        var fieldsByCollection = allFields
            .Where(f => !f.Collection.StartsWith("directus_", StringComparison.OrdinalIgnoreCase))
            .Where(f => !SkippedSourceFieldNames.Contains(f.FieldName))
            .Where(f => !ReservedTargetFieldNames.Contains(f.FieldName))
            .Where(f => !AliasTypes.Contains(f.Type))
            .Where(f => f.Schema is not null)
            .GroupBy(f => f.Collection, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var importCollections = userCollections.Select(col =>
        {
            var fields = (fieldsByCollection.GetValueOrDefault(col.Collection) ?? [])
                .Select(f =>
                {
                    var (db, display) = DirectusFieldMapping.Map(f);
                    return new ImportFieldSpec(
                        Name:         f.FieldName,
                        DatabaseType: db,
                        DisplayType:  display,
                        Label:        f.FieldName.Replace("_", " "),
                        IsRequired:   f.Meta?.Required ?? false,
                        IsUnique:     f.Schema?.IsUnique ?? false,
                        IsIndexed:    f.Schema?.IsIndexed ?? false);
                })
                .ToList();
            return new ImportCollection(col.Collection, fields);
        }).ToList();

        var flows = includeFlows
            ? await FetchFlowsAsync()
            : new List<ImportFlow>();

        return new ImportSchema(importCollections, flows);
    }

    private async Task<List<ImportFlow>> FetchFlowsAsync()
    {
        var flows      = await _client.GetFlowsAsync();
        var operations = await _client.GetOperationsAsync();

        var opsByFlow = operations
            .GroupBy(o => o.Flow, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        return flows
            .Where(f => f.Status == "active")
            .Select(flow =>
            {
                var (trigger, opts) = DirectusFlowMapping.MapTrigger(flow);
                var ops = opsByFlow.GetValueOrDefault(flow.Id) ?? [];

                var steps = ops.Select(op => new ImportStep(
                    SourceId:         op.Id,
                    Key:              op.Key,
                    Name:             op.Name,
                    Action:           DirectusFlowMapping.MapAction(op.Type),
                    IsStartStep:      string.Equals(op.Id, flow.FirstOperation, StringComparison.OrdinalIgnoreCase),
                    Description:      $"Imported from Directus ({op.Type})",
                    Parameters:       op.Options,
                    OnSuccessSourceId: op.Resolve,
                    OnFailureSourceId: op.Reject)).ToList();

                return new ImportFlow(flow.Name, trigger, opts, steps);
            }).ToList();
    }
}
