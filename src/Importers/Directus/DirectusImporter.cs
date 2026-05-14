using System.Text.Json;
using AnythinkCli.Models;

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
    public string? SourceAuthToken  => _client.Token;
    public string GetFileDownloadUrl(string sourceFileId) => _client.GetAssetUrl(sourceFileId);

    public DirectusImporter(string url, string token)
    {
        _url    = url;
        _client = new DirectusClient(url, token);
    }

    public async Task<ImportSchema> FetchSchemaAsync(bool includeFlows, bool includeFiles, bool includeRoles)
    {
        var collections = await _client.GetCollectionsAsync();
        var allFields   = await _client.GetFieldsAsync();
        var relations   = await _client.GetRelationsAsync();

        // Skip Directus system collections (directus_*) and virtual collections
        // (no schema). Keep user-hidden collections — those are typically junction
        // tables and DO matter for many-to-many relationships in Anythink.
        var userCollections = collections
            .Where(c => !c.Collection.StartsWith("directus_", StringComparison.OrdinalIgnoreCase))
            .Where(c => c.Schema is not null)
            .ToList();

        // Build (collection, field) → related_collection map for FK detection.
        // Filter directus_* system relations and de-dup defensively — Directus
        // sometimes reports multiple rows for system tables.
        var relationsLookup = relations
            .Where(r => r.RelatedCollection is not null)
            .Where(r => !r.Collection.StartsWith("directus_", StringComparison.OrdinalIgnoreCase))
            .GroupBy(r => (r.Collection, r.Field))
            .ToDictionary(g => g.Key, g => g.First().RelatedCollection!,
                          EqualityComparer<(string, string)>.Default);

        // Don't filter hidden fields — junction tables have their FK columns
        // marked hidden in Directus, and we still want them.
        var fieldsByCollection = allFields
            .Where(f => !f.Collection.StartsWith("directus_", StringComparison.OrdinalIgnoreCase))
            .Where(f => !SkippedSourceFieldNames.Contains(f.FieldName))
            .Where(f => !ReservedTargetFieldNames.Contains(f.FieldName))
            .Where(f => !AliasTypes.Contains(f.Type))
            .Where(f => f.Schema is not null)
            .GroupBy(f => f.Collection, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        // Roles + permissions, including the special "Public" policy that
        // governs which collections an unauthenticated visitor can read.
        // Public collections are detected up-front so we can mark them
        // is_public on entity create.
        (List<ImportRole> roles, HashSet<string> publicCollections) = includeRoles
            ? await FetchRolesAndPublicAccessAsync()
            : ([], new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var importCollections = userCollections.Select(col =>
        {
            var srcFields = fieldsByCollection.GetValueOrDefault(col.Collection) ?? [];

            var fields = srcFields.Select(f =>
            {
                var (db, display) = DirectusFieldMapping.Map(f);
                relationsLookup.TryGetValue((f.Collection, f.FieldName), out var relatedCollection);

                var isFile = string.Equals(f.Meta?.Interface, "file", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(f.Meta?.Interface, "file-image", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(relatedCollection, "directus_files", StringComparison.OrdinalIgnoreCase);

                System.Text.Json.JsonElement? relationship = null;
                if (isFile)
                {
                    // directus_files refs aren't FKs to a user collection —
                    // file imports use a separate (source uuid → anythink
                    // file id) map applied during data import.
                    relatedCollection = null;
                    // Anythink's first-class `file` db type. Requires a
                    // relationship.on_deletion or the AnyAPI validator throws.
                    db = "file";
                    display = "file";
                    relationship = System.Text.Json.JsonSerializer.SerializeToElement(
                        new { on_deletion = "SET NULL" });
                }

                return new ImportFieldSpec(
                    Name:                  f.FieldName,
                    DatabaseType:          db,
                    DisplayType:           display,
                    Label:                 f.FieldName.Replace("_", " "),
                    IsRequired:            f.Meta?.Required ?? false,
                    IsUnique:              f.Schema?.IsUnique ?? false,
                    IsIndexed:             f.Schema?.IsIndexed ?? false,
                    ForeignKeyCollection:  relatedCollection,
                    IsFileField:           isFile,
                    Relationship:          relationship);
            }).ToList();

            return new ImportCollection(
                Name:       col.Collection,
                Fields:     fields,
                IsJunction: col.Meta?.Hidden == true,
                IsPublic:   publicCollections.Contains(col.Collection));
        }).ToList();

        var flows = includeFlows
            ? await FetchFlowsAsync()
            : new List<ImportFlow>();

        var files = includeFiles
            ? await FetchFilesAsync()
            : new List<ImportFile>();

        return new ImportSchema(importCollections, flows, files, roles);
    }

    /// <summary>
    /// Walks the Directus role → access → policy → permission graph and
    /// reduces it to:
    ///  - a flat list of ImportRoles with (collection, action) tuples
    ///  - a set of collections accessible via the built-in Public policy
    ///    (admin_access=false, app_access=false)
    /// Admin roles (any attached policy with admin_access=true) are dropped —
    /// the importer creates project roles only; admin access is owner-only on
    /// Anythink and shouldn't be replicated.
    /// </summary>
    private async Task<(List<ImportRole> Roles, HashSet<string> Public)> FetchRolesAndPublicAccessAsync()
    {
        var roles       = await _client.GetRolesAsync();
        var policies    = await _client.GetPoliciesAsync();
        var accessRows  = await _client.GetAccessAsync();
        var permissions = await _client.GetPermissionsAsync();

        var publicPolicyIds = policies
            .Where(p => p.AdminAccess != true && p.AppAccess != true)
            .Select(p => p.Id)
            .ToHashSet();
        var adminPolicyIds = policies
            .Where(p => p.AdminAccess == true)
            .Select(p => p.Id)
            .ToHashSet();

        // Group permissions by policy, keeping only user-collection permissions
        // (we never replicate directus_* perms — those are platform concerns).
        var permsByPolicy = permissions
            .Where(p => p.Policy is not null)
            .Where(p => !p.Collection.StartsWith("directus_", StringComparison.OrdinalIgnoreCase))
            .Where(p => IsKnownAction(p.Action))
            .GroupBy(p => p.Policy!)
            .ToDictionary(g => g.Key,
                          g => g.Select(p => (p.Collection, Action: NormalizeAction(p.Action))).ToList());

        // Public collections = those readable via the Public policy
        var publicCollections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var policyId in publicPolicyIds)
        {
            if (!permsByPolicy.TryGetValue(policyId, out var perms)) continue;
            foreach (var (collection, action) in perms)
                if (action == "read")
                    publicCollections.Add(collection);
        }

        // Build (role → set of policies). Skip "all-tenant" access rows
        // (role == null — those are the Public policy attachments) and skip
        // roles where ANY attached policy has admin_access=true.
        var rolePolicies = accessRows
            .Where(a => a.Role is not null)
            .GroupBy(a => a.Role!)
            .ToDictionary(g => g.Key, g => g.Select(a => a.Policy).ToHashSet());

        var rolesById = roles.ToDictionary(r => r.Id);

        var importRoles = new List<ImportRole>();
        foreach (var (roleId, policyIds) in rolePolicies)
        {
            if (!rolesById.TryGetValue(roleId, out var role)) continue;
            if (policyIds.Overlaps(adminPolicyIds)) continue;        // admin role — skip

            // Union permissions across attached policies, deduped.
            var collPerms = new HashSet<(string, string)>();
            foreach (var pid in policyIds)
            {
                if (!permsByPolicy.TryGetValue(pid, out var perms)) continue;
                foreach (var p in perms) collPerms.Add(p);
            }
            if (collPerms.Count == 0) continue;

            importRoles.Add(new ImportRole(
                Name:                  role.Name,
                Description:           role.Description,
                CollectionPermissions: collPerms.OrderBy(p => p.Item1).ThenBy(p => p.Item2).ToList()));
        }

        return (importRoles, publicCollections);
    }

    private static bool IsKnownAction(string action) =>
        action.Equals("read",   StringComparison.OrdinalIgnoreCase) ||
        action.Equals("create", StringComparison.OrdinalIgnoreCase) ||
        action.Equals("update", StringComparison.OrdinalIgnoreCase) ||
        action.Equals("delete", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeAction(string action) => action.ToLowerInvariant();

    private async Task<List<ImportFile>> FetchFilesAsync()
    {
        var files = await _client.GetFilesAsync();
        return files
            .Where(f => !string.IsNullOrEmpty(f.FilenameDownload))
            .Select(f => new ImportFile(
                SourceId: f.Id,
                FileName: f.FilenameDownload!,
                IsPublic: false))   // Directus access control isn't a clean fit; default to private
            .ToList();
    }

    public async Task<ImportRecordPage> FetchRecordsAsync(string collectionName, int page, int pageSize)
    {
        var (records, total) = await _client.GetItemsAsync(collectionName, page, pageSize);
        return new ImportRecordPage(records, total);
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
                var trigger = DirectusFlowMapping.MapTrigger(flow);
                var ops     = opsByFlow.GetValueOrDefault(flow.Id) ?? [];

                // Build predecessor map up front so {{ $last.X }} can resolve
                // to the step that actually preceded each operation. Directus
                // links forward (resolve/reject), so for each "B in A.resolve"
                // edge B's predecessor is A.
                var predecessorByOpId = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (var op in ops)
                {
                    if (!string.IsNullOrEmpty(op.Resolve))
                        predecessorByOpId[op.Resolve!] = op.Key;
                    if (!string.IsNullOrEmpty(op.Reject))
                        predecessorByOpId[op.Reject!] = op.Key;
                }
                var knownStepKeys = ops.Select(o => o.Key).ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

                var steps = ops.Select(op =>
                {
                    var t = DirectusFlowMapping.Translate(op);

                    // Rewrite Directus mustache → Anythink $anythink.* syntax
                    // so the imported workflow actually substitutes values
                    // at runtime instead of printing literal placeholders.
                    var prevKey = predecessorByOpId.GetValueOrDefault(op.Id);
                    bool unresolved = false;
                    var adaptedParams = t.Parameters;
                    if (t.Parameters.ValueKind != JsonValueKind.Undefined &&
                        t.Parameters.ValueKind != JsonValueKind.Null)
                    {
                        var (rewritten, hadUnresolved) = DirectusTemplateAdapter.AdaptElement(
                            t.Parameters, prevKey, knownStepKeys);
                        adaptedParams = rewritten;
                        unresolved   = hadUnresolved;
                    }

                    // A step still needs manual review if either the
                    // translator flagged it (lossy op type, etc.) OR the
                    // template adapter couldn't rewrite something.
                    var needsReview = t.NeedsManualReview || unresolved;
                    var reviewNote  = t.ReviewNote ??
                        (unresolved ? "Template expression couldn't be auto-translated to Anythink syntax — review the script/payload." : null);

                    return new ImportStep(
                        SourceId:          op.Id,
                        Key:               op.Key,
                        Name:              op.Name,
                        Action:            t.Action,
                        IsStartStep:       string.Equals(op.Id, flow.FirstOperation, StringComparison.OrdinalIgnoreCase),
                        Description:       $"Imported from Directus ({op.Type})",
                        Parameters:        adaptedParams,
                        OnSuccessSourceId: op.Resolve,
                        OnFailureSourceId: op.Reject,
                        NeedsManualReview: needsReview,
                        ReviewNote:        reviewNote);
                }).ToList();

                return new ImportFlow(flow.Name, new List<WorkflowTriggerRequest> { trigger }, steps);
            }).ToList();
    }
}
