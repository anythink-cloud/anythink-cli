using System.Text.Json.Nodes;

namespace Anythink.ProjectImporters.Firebase;

public record FirebaseImportOptions(int SampleSize = 100, string? Bucket = null);

public class FirebaseImporter : IPlatformImporter
{
    private static readonly string[] StripeSubcollections = ["checkout_sessions", "subscriptions", "payments"];

    private readonly IFirebaseSource _source;
    private readonly Func<string, string> _redact;
    private readonly int _sampleSize;
    private readonly Dictionary<string, CollectionInfo> _collections = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _cursors = new(StringComparer.OrdinalIgnoreCase);
    private readonly SortedDictionary<string, int> _mismatches = new(StringComparer.Ordinal);

    private sealed record CollectionInfo(string CollectionId, IReadOnlyList<InferredField> Fields);

    public string PlatformName => "Firebase";
    public string ConnectionSummary => $"https://console.firebase.google.com/project/{_source.ProjectId}";
    public string? SourceAuthToken => null;

    public FirebaseImporter(FirebaseServiceAccountKey key, FirebaseImportOptions? options = null)
        : this(new GoogleFirebaseSource(key, options?.Bucket), key.Redact, options?.SampleSize ?? 100) { }

    internal FirebaseImporter(IFirebaseSource source, Func<string, string> redact, int sampleSize = 100)
    {
        _source = source;
        _redact = redact;
        _sampleSize = sampleSize;
    }

    public string GetFileDownloadUrl(string sourceFileId)
    {
        try { return _source.SignDownloadUrl(sourceFileId); }
        catch (Exception ex) { throw new ImportException(_redact(ex.Message)); }
    }

    public string RecordLabel(JsonObject record, long sourceId) =>
        record[FirebaseFieldMapping.PathField]?.GetValue<string>() ?? sourceId.ToString();

    public IReadOnlyList<string> LateWarnings => _mismatches
        .Select(m => $"{m.Key}: {m.Value} value(s) did not match the sampled type and were kept in {FirebaseFieldMapping.ExtraField}.").ToList();

    public Task<ImportSchema> FetchSchemaAsync(bool includeFlows, bool includeFiles, bool includeRoles) =>
        GuardAsync(() => FetchSchemaCoreAsync(includeFiles));

    public Task<ImportRecordPage> FetchRecordsAsync(string collectionName, int page, int pageSize) =>
        GuardAsync(async () =>
        {
            if (!_collections.TryGetValue(collectionName, out var info))
                throw new ImportException($"'{collectionName}' is not a collection found in the Firebase project.");

            var after = page > 1 ? _cursors.GetValueOrDefault(collectionName) : null;
            var documents = await _source.ReadDocumentsAsync(info.CollectionId, after, pageSize);
            if (documents.Count > 0) _cursors[collectionName] = documents[^1].Path;
            return new ImportRecordPage(documents.Select(d => ToRecord(collectionName, info, d)).ToList(), null);
        });

    private async Task<T> GuardAsync<T>(Func<Task<T>> work)
    {
        try { return await work(); }
        catch (Exception ex) { throw new ImportException(_redact(ex.Message)); }
    }

    private async Task<ImportSchema> FetchSchemaCoreAsync(bool includeFiles)
    {
        var warnings = new List<string> { "Never read, so not brought across: Firestore and Cloud Storage Security Rules (new entities are private; recreate access with roles and permissions), Realtime Database, named (non-default) Firestore databases, Cloud Functions and Firestore indexes. Subcollections are only found under sampled documents, so others may exist." };
        var errors = new List<string>();

        var entityByCollection = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in await _source.ListCollectionIdsAsync())
        {
            var name = FirebaseFieldMapping.EntityName(id);
            if (name is null || entityByCollection.ContainsValue(name))
                errors.Add($"collection '{id}' skipped: its name is not valid for Anythink or clashes with another collection once converted.");
            else
            {
                entityByCollection[id] = name;
                if (name != FirebaseFieldMapping.SnakeCase(id))
                    warnings.Add($"collection '{id}' is imported as '{name}' because its converted name is not valid for Anythink.");
            }
        }

        var collections = new List<ImportCollection>();
        var subcollections = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (id, entity) in entityByCollection)
        {
            var documents = await _source.SampleDocumentsAsync(id, _sampleSize);
            var (inferred, fieldWarnings) = FirebaseFieldMapping.Infer(entity, documents, c => entityByCollection.GetValueOrDefault(c));
            warnings.AddRange(fieldWarnings);
            foreach (var sub in documents.SelectMany(d => d.Subcollections).Distinct())
                subcollections.Add($"{id}/{sub}");

            _collections[entity] = new CollectionInfo(id, inferred);
            collections.Add(new ImportCollection(entity,
            [
                new ImportFieldSpec(FirebaseFieldMapping.PathField, "varchar", "input", "firebase path", false, true, true),
                .. inferred.Select(f => f.Spec),
                new ImportFieldSpec(FirebaseFieldMapping.ExtraField, "jsonb", "jsonb", "firebase extra", false, false, false)
            ]));
        }

        warnings.AddRange(subcollections.Select(s => $"{s}: subcollection not brought across (found under sampled documents)."));
        if (subcollections.Any(s => StripeSubcollections.Any(x => s.EndsWith("/" + x, StringComparison.Ordinal))))
            warnings.Add("Data from the Stripe extension looks present: it is imported as plain collections only, and payments are not brought across. Set up payments in Anythink instead.");

        var files = includeFiles ? await ListFilesAsync(errors) : [];
        await AddUserWarningAsync(warnings);

        return new ImportSchema(collections, [], files, [], warnings, errors);
    }

    private async Task<List<ImportFile>> ListFilesAsync(List<string> errors)
    {
        try
        {
            return (await _source.ListObjectNamesAsync())
                .Where(n => !n.EndsWith('/'))
                .Select(n => new ImportFile(n, n[(n.LastIndexOf('/') + 1)..], IsPublic: false))
                .ToList();
        }
        catch (Exception ex)
        {
            errors.Add($"files: could not list the Cloud Storage bucket: {_redact(ex.Message)}");
            return [];
        }
    }

    private async Task AddUserWarningAsync(List<string> warnings)
    {
        try
        {
            var users = await _source.CountUsersAsync();
            if (users.Total > 0)
                warnings.Add($"{users.Total} Authentication user(s) found ({users.Disabled} disabled) but not brought across: password hashes cannot be imported yet, and the project has no way to create users without inviting them.");
        }
        catch (Exception ex)
        {
            warnings.Add($"Authentication users could not be read, so they are not in this plan: {_redact(ex.Message)}");
        }
    }

    private JsonObject ToRecord(string entity, CollectionInfo info, FirebaseDocument document)
    {
        var record = new JsonObject
        {
            ["id"] = FirebaseFieldMapping.StableId(document.Path),
            [FirebaseFieldMapping.PathField] = document.Path
        };
        var columns = info.Fields.ToDictionary(f => f.SourceName);
        var extra = new JsonObject();

        foreach (var (name, value) in document.Fields)
        {
            if (value is null) continue;
            if (!columns.TryGetValue(name, out var column))
            {
                extra[name] = FirebaseFieldMapping.ToJson(value);
                continue;
            }

            if (!FirebaseFieldMapping.Accepts(column.Spec, value))
            {
                var key = $"{entity}.{name}";
                _mismatches[key] = _mismatches.GetValueOrDefault(key) + 1;
                extra[name] = FirebaseFieldMapping.ToJson(value);
            }
            else if (value is FirebaseReference reference && column.Spec.ForeignKeyCollection is not null)
            {
                record[column.Spec.Name] = FirebaseFieldMapping.StableId(reference.Path);
                if (column.Spec.ForeignKeyCollection == entity) extra[name] = reference.Path;
            }
            else
                record[column.Spec.Name] = FirebaseFieldMapping.ToJson(value);
        }

        if (extra.Count > 0) record[FirebaseFieldMapping.ExtraField] = extra;
        return record;
    }
}
