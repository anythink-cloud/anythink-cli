using System.Text.Json;
using System.Text.Json.Nodes;

namespace Anythink.ProjectImporters.Supabase;

public record SupabaseImportOptions(IReadOnlyList<string>? Schemas = null);

public class SupabaseImporter : IPlatformImporter
{
    public const string IdField = "supabase_id";
    public const string ExtraField = "supabase_extra";

    internal const string NeverRead =
        "Never read, so not brought across: auth users' details (only a count is taken: no emails, password hashes or sessions), the vault and project secrets, Edge Functions, function and trigger code, cron job commands, Realtime settings and Storage bucket limits. Column defaults, check constraints, composite unique constraints and indexes are not recreated.";

    private const int ListedNames = 10;
    private const int ObjectPageSize = 1000;
    private static readonly string[] ReservedNames = [.. ReservedFieldNames.Names, IdField, ExtraField];

    private static readonly HashSet<string> SystemSchemas = new(StringComparer.OrdinalIgnoreCase)
    {
        "auth", "storage", "realtime", "extensions", "vault", "pgsodium", "pgsodium_masks", "information_schema",
        "pgbouncer", "net", "cron", "pgtle", "graphql", "graphql_public", "pg_catalog", "pg_toast", "public_system"
    };

    private readonly ISupabaseSource _source;
    private readonly SupabaseSecrets _secrets;
    private readonly string? _projectUrl;
    private readonly string? _serviceKey;
    private readonly IReadOnlyList<string> _schemas;
    private readonly Dictionary<string, TableInfo> _tables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<string?>> _cursors = new(StringComparer.OrdinalIgnoreCase);
    private readonly SortedDictionary<string, int> _mismatches = new(StringComparer.Ordinal);

    private sealed record ColumnPlan(SupabaseColumn Column, string? Field, SupabaseKind Kind, string? ForeignKeyEntity, bool KeptAsKey = false);

    private sealed record TableInfo(SupabaseTableRef Ref, string Entity, IReadOnlyList<ColumnPlan> Columns, IReadOnlyList<int> KeyIndexes);

    public string PlatformName => "Supabase";
    public string ConnectionSummary => _projectUrl is null ? _source.Description : UrlRedaction.Redact(_projectUrl);
    public string? SourceAuthToken => _serviceKey;
    public IReadOnlyDictionary<string, string>? SourceHeaders => _serviceKey is null ? null : new Dictionary<string, string> { ["apikey"] = _serviceKey };
    public bool AllowSourceRedirects => false;

    public SupabaseImporter(string connectionString, string? projectUrl = null, string? serviceKey = null, SupabaseImportOptions? options = null)
        : this(Open(connectionString), new SupabaseSecrets(connectionString, SupabaseConnection.Parse(connectionString).Password, serviceKey),
            projectUrl, serviceKey, options)
    { }

    internal SupabaseImporter(ISupabaseSource source, SupabaseSecrets secrets, string? projectUrl, string? serviceKey, SupabaseImportOptions? options = null)
    {
        _source = source;
        _secrets = secrets;
        _projectUrl = string.IsNullOrWhiteSpace(projectUrl) ? null : projectUrl.TrimEnd('/');
        _serviceKey = string.IsNullOrWhiteSpace(serviceKey) ? null : serviceKey;
        _schemas = ResolveSchemas(options?.Schemas);
    }

    private static PostgresSupabaseSource Open(string connectionString)
    {
        var connection = SupabaseConnection.Parse(connectionString);
        return new PostgresSupabaseSource(new NpgsqlSqlRunner(connection.ConnectionString), connection.Description);
    }

    internal static IReadOnlyList<string> ResolveSchemas(IReadOnlyList<string>? requested)
    {
        var schemas = (requested ?? []).Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (schemas.Count == 0) schemas.Add("public");
        if (schemas.FirstOrDefault(IsSystemSchema) is { } system)
            throw new ImportException($"Schema '{system}' belongs to Supabase itself and is never imported.");
        return schemas;
    }

    internal static bool IsSystemSchema(string schema) =>
        SystemSchemas.Contains(schema) || schema.StartsWith("pg_", StringComparison.OrdinalIgnoreCase) ||
        schema.StartsWith("supabase_", StringComparison.OrdinalIgnoreCase) || schema.StartsWith("graphql", StringComparison.OrdinalIgnoreCase) ||
        schema.StartsWith('_');

    public string GetFileDownloadUrl(string sourceFileId)
    {
        var slash = sourceFileId.IndexOf('/');
        if (_projectUrl is null || slash < 1) throw new ImportException("Files can only be downloaded with the project URL.");
        var path = string.Join('/', sourceFileId[(slash + 1)..].Split('/').Select(Uri.EscapeDataString));
        return $"{_projectUrl}/storage/v1/object/authenticated/{Uri.EscapeDataString(sourceFileId[..slash])}/{path}";
    }

    public string RecordLabel(JsonObject record, long sourceId) =>
        record[IdField]?.GetValue<string>() ?? sourceId.ToString();

    public IReadOnlyList<string> LateWarnings => _mismatches
        .Select(m => $"{m.Key}: {m.Value} value(s) did not fit the column type and were kept in {ExtraField}.").ToList();

    public Task<ImportSchema> FetchSchemaAsync(bool includeFlows, bool includeFiles, bool includeRoles)
    {
        if (includeFiles && (_projectUrl is null || _serviceKey is null))
            throw new ImportException("Importing files needs the project URL and the service role key.");
        return GuardAsync(() => FetchSchemaCoreAsync(includeFiles));
    }

    public Task<ImportRecordPage> FetchRecordsAsync(string collectionName, int page, int pageSize) =>
        GuardAsync(async () =>
        {
            if (!_tables.TryGetValue(collectionName, out var info))
                throw new ImportException($"'{collectionName}' is not a table found in the Supabase project.");

            var after = page > 1 ? _cursors.GetValueOrDefault(collectionName) : null;
            var rows = await _source.ReadRowsAsync(info.Ref, after, pageSize);
            if (rows.Count > 0) _cursors[collectionName] = Enumerable.Range(info.Columns.Count, info.KeyIndexes.Count).Select(i => rows[^1][i]).ToList();
            return new ImportRecordPage(rows.Select(r => ToRecord(info, r)).ToList(), null);
        });

    private async Task<T> GuardAsync<T>(Func<Task<T>> work)
    {
        try { return await work(); }
        catch (Exception ex) { throw new ImportException(_secrets.Redact(ex.Message)); }
    }

    private async Task<T?> TryAsync<T>(string what, List<string> warnings, Func<Task<T>> read)
    {
        try { return await read(); }
        catch (Exception ex)
        {
            warnings.Add($"{what} could not be read, so it is not in this plan: {_secrets.Redact(ex.Message)}");
            return default;
        }
    }

    private async Task<ImportSchema> FetchSchemaCoreAsync(bool includeFiles)
    {
        var warnings = new List<string> { NeverRead };
        var errors = new List<string>();

        var columns = await _source.ListColumnsAsync(_schemas);
        var constraints = await _source.ListConstraintsAsync(_schemas);
        var enums = (await _source.ListEnumsAsync()).ToDictionary(e => (e.Schema, e.Name));

        var collections = BuildCollections(columns, constraints, enums, warnings, errors);
        foreach (var schema in _schemas.Where(s => columns.All(c => c.Schema != s)))
            warnings.Add($"schema '{schema}' has no tables to import.");

        await AddSkippedTablesAsync(warnings);
        await AddPoliciesAsync(warnings, errors);
        await AddInventoryAsync(warnings);
        var files = await AddStorageAsync(includeFiles, warnings, errors);

        return new ImportSchema(collections, [], files, [], warnings, errors);
    }

    private List<ImportCollection> BuildCollections(
        IReadOnlyList<SupabaseColumn> columns, IReadOnlyList<SupabaseConstraint> constraints,
        Dictionary<(string, string), SupabaseEnum> enums, List<string> warnings, List<string> errors)
    {
        _tables.Clear();
        var tables = columns.GroupBy(c => (c.Schema, c.Table)).ToList();
        var entityByTable = new Dictionary<(string Schema, string Table), string>();
        var usedEntities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in tables)
        {
            var (schema, name) = table.Key;
            var source = schema == _schemas[0] ? name : $"{schema}_{name}";
            var entity = ImportNames.Entity(source, "supabase");
            if (entity is null || !usedEntities.Add(entity))
                errors.Add($"table '{schema}.{name}' skipped: its name is not valid for Anythink or clashes with another table once converted.");
            else
            {
                entityByTable[table.Key] = entity;
                if (entity != name) warnings.Add($"table '{schema}.{name}' is imported as '{entity}' because that is a valid Anythink name.");
            }
        }

        var keys = constraints.Where(c => c.Kind == 'p').ToDictionary(c => (c.Schema, c.Table), c => c.Columns);
        var foreignKeys = constraints.Where(c => c.Kind == 'f').ToList();
        var collections = new List<ImportCollection>();

        foreach (var table in tables.Where(t => entityByTable.ContainsKey(t.Key)))
        {
            var entity = entityByTable[table.Key];
            var key = keys.GetValueOrDefault(table.Key) ?? [];
            var own = constraints.Where(c => (c.Schema, c.Table) == table.Key).ToList();
            var collection = BuildCollection(table.Key.Schema, table.Key.Table, entity, table.ToList(), key, own, foreignKeys, keys, entityByTable, enums, warnings);
            collections.Add(collection);
        }

        return collections;
    }

    private ImportCollection BuildCollection(
        string schema, string table, string entity, List<SupabaseColumn> columns, IReadOnlyList<string> key,
        List<SupabaseConstraint> own, List<SupabaseConstraint> allForeignKeys, Dictionary<(string, string), IReadOnlyList<string>> keys,
        Dictionary<(string Schema, string Table), string> entityByTable, Dictionary<(string, string), SupabaseEnum> enums, List<string> warnings)
    {
        var label = $"{schema}.{table}";
        var fields = new List<ImportFieldSpec>();
        var plans = new List<ColumnPlan>();
        var used = new HashSet<string>(ReservedNames.Where(n => n is IdField or ExtraField), StringComparer.OrdinalIgnoreCase);
        var renames = new List<string>();

        if (key.Count > 0)
            fields.Add(new ImportFieldSpec(IdField, "varchar", "input", "supabase id", false, true, true));

        var foreignKeys = own.Where(c => c.Kind == 'f').ToList();
        foreach (var composite in foreignKeys.Where(c => c.Columns.Count > 1))
            warnings.Add($"{label}: foreign key ({string.Join(", ", composite.Columns)}) to {composite.RefSchema}.{composite.RefTable} spans several columns, so it is not a relationship; the values are kept as plain fields.");
        var singleForeignKeys = foreignKeys.Where(c => c.Columns.Count == 1).GroupBy(c => c.Columns[0]).ToDictionary(g => g.Key, g => g.First());

        var uniqueColumns = own.Where(c => c.Kind == 'u' && c.Columns.Count == 1).Select(c => c.Columns[0]).ToHashSet();
        foreach (var composite in own.Where(c => c.Kind == 'u' && c.Columns.Count > 1))
            warnings.Add($"{label}: unique constraint ({string.Join(", ", composite.Columns)}) is not recreated.");
        var unenforced = own.Where(c => c.Kind is 'c' or 'x').ToList();
        if (unenforced.Count > 0)
            warnings.Add($"{label}: {unenforced.Count} check or exclusion constraint(s) are not recreated ({string.Join(", ", unenforced.Select(c => c.Name))}).");

        var soleKey = key.Count == 1 ? key[0] : null;
        foreach (var column in columns)
        {
            var type = SupabaseTypes.Map(column, enums);
            var snake = ImportNames.SnakeCase(column.Name);
            if (column.Name == soleKey && snake == "id")
            {
                if (singleForeignKeys.TryGetValue(column.Name, out var keyForeignKey))
                    warnings.Add($"{label}.{column.Name}: primary key that is also a foreign key to {keyForeignKey.RefSchema}.{keyForeignKey.RefTable}; its value is kept in {IdField} and the link is not a relationship.");
                plans.Add(new ColumnPlan(column, null, type.Kind, null, KeptAsKey: true));
                continue;
            }

            var name = snake == "id" ? "supabase_id_column" : ReservedNames.Contains(snake, StringComparer.OrdinalIgnoreCase) ? $"supabase_{snake}" : snake;
            if (!ImportNames.IsValid(name) || !used.Add(name))
            {
                warnings.Add($"{label}: column '{column.Name}' cannot be a field of its own (its name is not valid for Anythink or clashes with another field); its values are kept in {ExtraField}.");
                plans.Add(new ColumnPlan(column, null, type.Kind, null));
                continue;
            }
            if (name != column.Name) renames.Add($"{column.Name} -> {name}");
            if (type.Note is not null) warnings.Add($"{label}.{column.Name}: {type.Note}");

            string? target = null;
            var fieldType = type;
            if (singleForeignKeys.TryGetValue(column.Name, out var fk))
            {
                target = ForeignKeyTarget(fk, keys, entityByTable);
                if (target is null)
                    warnings.Add($"{label}.{column.Name}: foreign key to {fk.RefSchema}.{fk.RefTable} is not a relationship (the table is not imported, or the key is not its primary key); the values are kept in the column's own type.");
                else
                    fieldType = new SupabaseFieldType("integer", "input", SupabaseKind.Integer);
            }

            plans.Add(new ColumnPlan(column, name, fieldType.Kind, target));
            fields.Add(new ImportFieldSpec(name, fieldType.Db, fieldType.Display, name.Replace('_', ' '),
                IsRequired: false, IsUnique: uniqueColumns.Contains(column.Name) || column.Name == soleKey, IsIndexed: false, ForeignKeyCollection: target));
        }

        if (renames.Count > 0) warnings.Add($"{label}: columns renamed to valid Anythink names: {string.Join(", ", renames)}.");
        fields.Add(new ImportFieldSpec(ExtraField, "jsonb", "jsonb", "supabase extra", false, false, false));

        var keyIndexes = key.Select(k => columns.FindIndex(c => c.Name == k)).ToList();
        var keyColumns = key.Select(k => columns.First(c => c.Name == k)).Select(c => new SupabaseKeyColumn(c.Name, c.UdtSchema, c.UdtName)).ToList();
        _tables[entity] = new TableInfo(new SupabaseTableRef(schema, table, columns, keyColumns, columns[0].RelKind == 'p'), entity, plans, keyIndexes);

        return new ImportCollection(entity, fields,
            DataUnsupportedReason: key.Count == 0 ? "the table has no primary key, so its rows cannot be read in a stable order or matched on a re-run" : null);
    }

    private static string? ForeignKeyTarget(
        SupabaseConstraint fk, Dictionary<(string, string), IReadOnlyList<string>> keys, Dictionary<(string Schema, string Table), string> entityByTable)
    {
        if (fk.RefSchema is null || fk.RefTable is null || fk.RefColumns.Count != 1) return null;
        var target = (fk.RefSchema, fk.RefTable);
        return entityByTable.TryGetValue(target, out var entity) && keys.TryGetValue(target, out var targetKey) &&
               targetKey.Count == 1 && targetKey[0] == fk.RefColumns[0] ? entity : null;
    }

    internal static long RecordId(string entity, string keyText) => ImportNames.StableId($"{entity}\0{keyText}");

    private static string KeyText(IReadOnlyList<string> values) =>
        values.Count == 1 ? values[0] : JsonSerializer.Serialize(values);

    private JsonObject ToRecord(TableInfo info, string?[] row)
    {
        var record = new JsonObject();
        var extra = new JsonObject();

        if (info.KeyIndexes.Count > 0)
        {
            var key = KeyText(info.KeyIndexes.Select(i => row[i] ?? "").ToList());
            record["id"] = RecordId(info.Entity, key);
            record[IdField] = key;
        }

        for (var i = 0; i < info.Columns.Count; i++)
        {
            var plan = info.Columns[i];
            if (row[i] is not { } text) continue;

            if (plan.KeptAsKey) continue;
            if (plan.ForeignKeyEntity is { } target)
            {
                record[plan.Field!] = RecordId(target, text);
                if (target == info.Entity) extra[plan.Column.Name] = text;
            }
            else if (plan.Field is null)
                extra[plan.Column.Name] = SupabaseTypes.TryToJson(plan.Kind, text, out var node) ? node : text;
            else if (SupabaseTypes.TryToJson(plan.Kind, text, out var value))
                record[plan.Field] = value;
            else
            {
                var key = $"{info.Entity}.{plan.Field}";
                _mismatches[key] = _mismatches.GetValueOrDefault(key) + 1;
                extra[plan.Column.Name] = text;
            }
        }

        if (extra.Count > 0) record[ExtraField] = extra;
        return record;
    }

    private async Task AddSkippedTablesAsync(List<string> warnings)
    {
        var skipped = await TryAsync("foreign and extension tables", warnings, () => _source.ListSkippedTablesAsync(_schemas)) ?? [];
        if (skipped.Where(t => t.Kind == 'f').ToList() is { Count: > 0 } foreign)
            warnings.Add($"{foreign.Count} foreign table(s) not brought across ({Names(foreign.Select(t => $"{t.Schema}.{t.Name}"))}); their data lives in another system.");
        if (skipped.Where(t => t.Kind != 'f').ToList() is { Count: > 0 } owned)
            warnings.Add($"{owned.Count} table(s) owned by extensions not brought across ({Names(owned.Select(t => $"{t.Schema}.{t.Name}"))}).");
    }

    private async Task AddPoliciesAsync(List<string> warnings, List<string> errors)
    {
        try
        {
            foreach (var p in await _source.ListPoliciesAsync(_schemas))
            {
                var clauses = new List<string>();
                if (p.Using is not null) clauses.Add($"using ({Collapse(p.Using)})");
                if (p.WithCheck is not null) clauses.Add($"with check ({Collapse(p.WithCheck)})");
                warnings.Add($"row-level security policy \"{p.Name}\" on {p.Schema}.{p.Table} ({p.Command}, {p.Permissive.ToLowerInvariant()}, to {string.Join(", ", p.Roles)}): {string.Join(" ", clauses)}. Not brought across: set up roles and record access in Anythink instead.");
            }
        }
        catch (Exception ex)
        {
            errors.Add($"row-level security policies could not be read, so they are not in this plan: {_secrets.Redact(ex.Message)}");
        }
    }

    private static string Collapse(string expression) => string.Join(' ', expression.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Names(IEnumerable<string> names)
    {
        var all = names.ToList();
        return string.Join(", ", all.Take(ListedNames)) + (all.Count > ListedNames ? $" and {all.Count - ListedNames} more" : "");
    }

    private async Task AddInventoryAsync(List<string> warnings)
    {
        if (await TryAsync("views", warnings, () => _source.ListViewsAsync(_schemas)) is { Count: > 0 } views)
            warnings.Add($"{views.Count} view(s) not brought across ({Names(views)}); recreate them in Anythink if you need them.");

        if (await TryAsync("functions", warnings, () => _source.ListRoutinesAsync(_schemas)) is { Count: > 0 } routines)
            warnings.Add($"{routines.Count} database function(s) not brought across ({Names(routines.Select(r => $"{r.Schema}.{r.Name}").Distinct())}); calls to them through the API will not work.");

        if (await TryAsync("triggers", warnings, () => _source.ListTriggersAsync(_schemas)) is { Count: > 0 } triggers)
            warnings.Add($"{triggers.Count} trigger(s) not brought across ({Names(triggers.Select(t => $"{t.Schema}.{t.Table}.{t.Name}"))}); rebuild their behaviour as workflows.");

        var extensions = await TryAsync("extensions", warnings, () => _source.ListExtensionsAsync()) ?? [];
        if (extensions.Where(e => e != "plpgsql").ToList() is { Count: > 0 } used)
            warnings.Add($"{used.Count} extension(s) in use ({Names(used)}) are not brought across.");

        if (await TryAsync("publications", warnings, () => _source.ListPublicationsAsync()) is { Count: > 0 } publications)
            warnings.Add($"{publications.Count} publication(s) not brought across ({Names(publications.Select(p => $"{p.Name}: {p.Tables} table(s)"))}); realtime is not available in Anythink yet.");

        if (extensions.Contains("pg_cron") && await TryAsync("cron jobs", warnings, async () => (long?)await _source.CountCronJobsAsync()) is > 0 and var jobs)
            warnings.Add($"{jobs} cron job(s) visible to this role (a role that is not a superuser sees only its own) not brought across (their commands are not read); recreate them as scheduled workflows.");

        if (await TryAsync("auth users", warnings, async () => (long?)await _source.CountAuthUsersAsync()) is > 0 and var users)
            warnings.Add($"{users} auth user(s) found but not brought across: only the count is read, and users cannot be imported yet.");
    }

    private async Task<List<ImportFile>> AddStorageAsync(bool includeFiles, List<string> warnings, List<string> errors)
    {
        var files = new List<ImportFile>();
        try
        {
            var buckets = await _source.ListBucketsAsync();
            foreach (var bucket in buckets.Where(b => b.Public))
                warnings.Add($"bucket '{bucket.Name}' is public in Supabase; its files are imported private. Make them public in Anythink only if you want that.");
            if (buckets.Count > 0)
                warnings.Add($"{buckets.Count} Storage bucket(s) found ({Names(buckets.Select(b => b.Name))}). Files are imported flat by name, with their bucket and path kept as the source id; Storage policies and bucket settings are not brought across.");

            if (!includeFiles) return files;

            string? afterBucket = null, afterName = null;
            while (true)
            {
                var page = await _source.ListObjectsAsync(afterBucket, afterName, ObjectPageSize);
                var traversing = page.Where(o => o.Name.Split('/').Contains("..")).Select(o => $"{o.Bucket}/{o.Name}").ToList();
                if (traversing.Count > 0) warnings.Add($"{traversing.Count} object(s) not brought across because their names contain '..' path segments ({Names(traversing)}).");
                files.AddRange(page.Where(o => !o.Name.Split('/').Contains("..") && !o.Name.EndsWith('/') && !o.Name.EndsWith(".emptyFolderPlaceholder", StringComparison.Ordinal))
                    .Select(o => new ImportFile($"{o.Bucket}/{o.Name}", o.Name[(o.Name.LastIndexOf('/') + 1)..], IsPublic: false)));
                if (page.Count < ObjectPageSize) break;
                (afterBucket, afterName) = (page[^1].Bucket, page[^1].Name);
            }
        }
        catch (Exception ex)
        {
            errors.Add($"files: could not list Storage: {_secrets.Redact(ex.Message)}");
            files.Clear();
        }
        return files;
    }
}
