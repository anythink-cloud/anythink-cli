using System.Text.Json;
using System.Text.RegularExpressions;
using AnythinkCli.Models;

namespace AnythinkCli.Importers.Directus;

public class DirectusImporter : IPlatformImporter
{
    private readonly DirectusClient _client;
    private readonly string _url;

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

    private static readonly Regex CollectionNamePattern = new(@"^[a-z0-9_]+$", RegexOptions.Compiled);

    private static readonly HashSet<string> IntegerKeyTypes =
        new(StringComparer.OrdinalIgnoreCase) { "integer", "bigInteger" };

    public string PlatformName => "Directus";
    public string ConnectionSummary => _url;
    public string? SourceAuthToken => _client.Token;
    public string GetFileDownloadUrl(string sourceFileId) => _client.GetAssetUrl(sourceFileId);

    public DirectusImporter(string url, string token)
        : this(RedactUrl(url), new DirectusClient(RedactUrl(url), token), redacted: true) { }

    internal DirectusImporter(string url, DirectusClient client, bool redacted = false)
    {
        _url = redacted ? url : RedactUrl(url);
        _client = client;
    }

    internal static bool IsValidCollectionName(string name) => CollectionNamePattern.IsMatch(name);

    // Userinfo, query and fragment can carry credentials and are never needed: auth is the bearer token.
    internal static string RedactUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "(invalid url)";
        return new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" }
            .Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    public async Task<ImportSchema> FetchSchemaAsync(bool includeFlows, bool includeFiles, bool includeRoles)
    {
        var collections = await _client.GetCollectionsAsync();
        var allFields = await _client.GetFieldsAsync();
        var relations = await _client.GetRelationsAsync();

        // Hidden collections are kept: they are usually the junction tables behind many-to-many fields.
        var candidateCollections = collections
            .Where(c => !c.Collection.StartsWith("directus_", StringComparison.OrdinalIgnoreCase))
            .Where(c => c.Schema is not null)
            .ToList();

        var errors = new List<string>();
        var userCollections = candidateCollections.Where(c => IsValidCollectionName(c.Collection)).ToList();
        foreach (var bad in candidateCollections.Except(userCollections))
            errors.Add($"collection '{bad.Collection}' skipped: names must be lowercase letters, digits and underscores.");

        // Directus sometimes reports duplicate relation rows for system tables.
        var relationsLookup = relations
            .Where(r => r.RelatedCollection is not null)
            .Where(r => !r.Collection.StartsWith("directus_", StringComparison.OrdinalIgnoreCase))
            .GroupBy(r => (r.Collection, r.Field))
            .ToDictionary(g => g.Key, g => g.First().RelatedCollection!,
                          EqualityComparer<(string, string)>.Default);

        // Hidden fields are kept: junction tables mark their FK columns hidden.
        var fieldsByCollection = allFields
            .Where(f => !f.Collection.StartsWith("directus_", StringComparison.OrdinalIgnoreCase))
            .Where(f => !SkippedSourceFieldNames.Contains(f.FieldName))
            .Where(f => !ReservedTargetFieldNames.Contains(f.FieldName))
            .Where(f => !AliasTypes.Contains(f.Type))
            .Where(f => f.Schema is not null)
            .GroupBy(f => f.Collection, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var warnings = new List<string>();
        (List<ImportRole> roles, HashSet<string> publicCollections) = includeRoles
            ? await FetchRolesAndPublicAccessAsync(warnings)
            : ([], new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var primaryKeyTypes = allFields
            .Where(f => f.Schema?.IsPrimaryKey == true)
            .GroupBy(f => f.Collection, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Type, StringComparer.OrdinalIgnoreCase);

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
                    // File refs are remapped through the uploaded-file id map, not as FKs.
                    relatedCollection = null;
                    // The file type needs relationship.on_deletion or the API validator rejects it.
                    db = "file";
                    display = "file";
                    relationship = System.Text.Json.JsonSerializer.SerializeToElement(
                        new { on_deletion = "SET NULL" });
                }

                return new ImportFieldSpec(
                    Name: f.FieldName,
                    DatabaseType: db,
                    DisplayType: display,
                    Label: f.FieldName.Replace("_", " "),
                    IsRequired: f.Meta?.Required ?? false,
                    IsUnique: f.Schema?.IsUnique ?? false,
                    IsIndexed: f.Schema?.IsIndexed ?? false,
                    ForeignKeyCollection: relatedCollection,
                    IsFileField: isFile,
                    Relationship: relationship);
            }).ToList();

            return new ImportCollection(
                Name: col.Collection,
                Fields: fields,
                IsJunction: col.Meta?.Hidden == true,
                IsPublic: publicCollections.Contains(col.Collection),
                DataUnsupportedReason: primaryKeyTypes.TryGetValue(col.Collection, out var pkType) &&
                                       !IntegerKeyTypes.Contains(pkType)
                    ? $"primary key type '{pkType}' is not supported; only integer ids can be imported"
                    : null);
        }).ToList();

        var flows = includeFlows
            ? await FetchFlowsAsync()
            : new List<ImportFlow>();

        var files = includeFiles
            ? await FetchFilesAsync()
            : new List<ImportFile>();

        return new ImportSchema(importCollections, flows, files, roles, warnings, errors);
    }

    private async Task<(List<ImportRole> Roles, HashSet<string> Public)> FetchRolesAndPublicAccessAsync(
        List<string> warnings)
    {
        var roles = await _client.GetRolesAsync();
        var policies = await _client.GetPoliciesAsync();
        var accessRows = await _client.GetAccessAsync();
        var permissions = await _client.GetPermissionsAsync();

        var adminPolicyIds = policies
            .Where(p => p.AdminAccess == true)
            .Select(p => p.Id)
            .ToHashSet();
        var policyNames = policies.ToDictionary(p => p.Id, p => p.Name);

        // Only the attachment with neither role nor user is Public; app_access=false alone just means API-only.
        var publicPolicyIds = accessRows
            .Where(a => a.Role is null && a.User is null)
            .Select(a => a.Policy)
            .Where(id => !adminPolicyIds.Contains(id))
            .ToHashSet();

        var candidate = permissions
            .Where(p => p.Policy is not null)
            .Where(p => !p.Collection.StartsWith("directus_", StringComparison.OrdinalIgnoreCase))
            .Where(p => IsKnownAction(p.Action))
            .ToList();

        // Permissions with row filters, validation, presets or field limits can't be expressed as a
        // plain "<collection>:<action>" grant; importing them whole would widen access, so skip and report.
        var unrestricted = new List<DirectusPermission>();
        foreach (var p in candidate)
        {
            if (IsUnrestricted(p)) { unrestricted.Add(p); continue; }
            var policyName = policyNames.GetValueOrDefault(p.Policy!, p.Policy!);
            warnings.Add($"policy '{policyName}': {NormalizeAction(p.Action)} on '{p.Collection}' has row filters or field limits — not imported.");
        }

        var permsByPolicy = unrestricted
            .GroupBy(p => p.Policy!)
            .ToDictionary(g => g.Key,
                          g => g.Select(p => (p.Collection, Action: NormalizeAction(p.Action))).ToList());

        var publicCollections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var policyId in publicPolicyIds)
        {
            if (!permsByPolicy.TryGetValue(policyId, out var perms)) continue;
            foreach (var (collection, action) in perms)
                if (action == "read" && IsValidCollectionName(collection))
                    publicCollections.Add(collection);
        }

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

            var collPerms = new HashSet<(string, string)>();
            foreach (var pid in policyIds)
            {
                if (!permsByPolicy.TryGetValue(pid, out var perms)) continue;
                foreach (var p in perms) collPerms.Add(p);
            }
            if (collPerms.Count == 0)
            {
                warnings.Add($"role '{role.Name}': no importable permissions, so it was not imported.");
                continue;
            }

            importRoles.Add(new ImportRole(
                Name: role.Name,
                Description: role.Description,
                CollectionPermissions: collPerms.OrderBy(p => p.Item1).ThenBy(p => p.Item2).ToList()));
        }

        return (importRoles, publicCollections);
    }

    private static bool IsUnrestricted(DirectusPermission p)
    {
        static bool Empty(JsonElement? e) =>
            e is null or { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } ||
            (e.Value.ValueKind == JsonValueKind.Object && !e.Value.EnumerateObject().Any()) ||
            (e.Value.ValueKind == JsonValueKind.Array && e.Value.GetArrayLength() == 0);

        if (!Empty(p.Filter) || !Empty(p.Validation) || !Empty(p.Presets)) return false;
        if (p.Action.Equals("delete", StringComparison.OrdinalIgnoreCase)) return true;

        return p.Fields switch
        {
            { ValueKind: JsonValueKind.Array } f =>
                f.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && x.GetString() == "*"),
            { ValueKind: JsonValueKind.String } f => f.GetString()!.Split(',').Select(x => x.Trim()).Contains("*"),
            _ => false
        };
    }

    private static bool IsKnownAction(string action) =>
        action.Equals("read", StringComparison.OrdinalIgnoreCase) ||
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
        var flows = await _client.GetFlowsAsync();
        var operations = await _client.GetOperationsAsync();

        var opsByFlow = operations
            .GroupBy(o => o.Flow, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        return flows
            .Where(f => f.Status == "active")
            .Select(flow =>
            {
                var trigger = DirectusFlowMapping.MapTrigger(flow);
                var ops = opsByFlow.GetValueOrDefault(flow.Id) ?? [];

                // Directus links forward, so {{ $last }} needs the reverse edge to find its predecessor.
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

                    var prevKey = predecessorByOpId.GetValueOrDefault(op.Id);
                    bool unresolved = false;
                    var adaptedParams = t.Parameters;
                    if (t.Parameters.ValueKind != JsonValueKind.Undefined &&
                        t.Parameters.ValueKind != JsonValueKind.Null)
                    {
                        var (rewritten, hadUnresolved) = DirectusTemplateAdapter.AdaptElement(
                            t.Parameters, prevKey, knownStepKeys);
                        adaptedParams = rewritten;
                        unresolved = hadUnresolved;
                    }

                    var needsReview = t.NeedsManualReview || unresolved;
                    var reviewNote = t.ReviewNote ??
                        (unresolved ? "Template expression couldn't be auto-translated to Anythink syntax — review the script/payload." : null);

                    return new ImportStep(
                        SourceId: op.Id,
                        Key: op.Key,
                        Name: op.Name,
                        Action: t.Action,
                        IsStartStep: string.Equals(op.Id, flow.FirstOperation, StringComparison.OrdinalIgnoreCase),
                        Description: $"Imported from Directus ({op.Type})",
                        Parameters: adaptedParams,
                        OnSuccessSourceId: op.Resolve,
                        OnFailureSourceId: op.Reject,
                        NeedsManualReview: needsReview,
                        ReviewNote: reviewNote,
                        Enabled: t.Enabled);
                }).ToList();

                return new ImportFlow(flow.Name, new List<WorkflowTriggerRequest> { trigger }, steps);
            }).ToList();
    }
}
