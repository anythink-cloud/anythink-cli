using AnythinkCli.Client;
using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using System.Text.Json.Nodes;

namespace AnythinkCli.Importers;

public record ImportOptions(
    bool DryRun,
    bool IncludeFlows,
    bool IncludeData = false,
    bool IncludeFiles = false,
    bool IncludeRoles = false,
    int DataPageSize = 100,
    bool RequireConfirmation = false
);

public record ImportResult(
    int EntitiesCreated,
    int EntitiesSkipped,
    int FieldsCreated,
    int FieldsSkipped,
    int FieldsFailed,
    int WorkflowsCreated,
    int WorkflowsSkipped,
    int WorkflowStepsCreated,
    int WorkflowStepsFailed,
    int RecordsCreated,
    int RecordsFailed,
    int FilesUploaded,
    int FilesSkipped,
    int FilesFailed,
    int RolesCreated,
    int RolesSkipped,
    int PermissionsAttached,
    List<string> Errors,
    List<string>? Warnings = null
);

public class ImportRunner
{
    private const long MaxFileBytes = 100L * 1024 * 1024;

    private readonly IPlatformImporter _source;
    private readonly AnythinkClient _target;
    private readonly ImportOptions _options;

    public ImportRunner(IPlatformImporter source, AnythinkClient target, ImportOptions options)
    {
        _source = source;
        _target = target;
        _options = options;
    }

    public async Task<ImportResult> RunAsync()
    {
        AnsiConsole.MarkupLine($"[dim]{_source.PlatformName}:[/]  {Markup.Escape(_source.ConnectionSummary)}");
        AnsiConsole.MarkupLine($"[dim]Anythink:[/]  org {Markup.Escape(_target.OrgId)}");
        AnsiConsole.WriteLine();

        ImportSchema schema = null!;
        await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
            .StartAsync($"Fetching {_source.PlatformName} schema...", async _ =>
                schema = await _source.FetchSchemaAsync(
                    _options.IncludeFlows, _options.IncludeFiles, _options.IncludeRoles));

        AnsiConsole.MarkupLine($"Found [bold]{schema.Collections.Count}[/] collection(s) to import.");
        if (_options.IncludeFlows)
            AnsiConsole.MarkupLine($"Found [bold]{schema.Flows.Count}[/] flow(s) to import.");
        if (_options.IncludeFiles)
            AnsiConsole.MarkupLine($"Found [bold]{schema.Files.Count}[/] file(s) to import.");
        if (_options.IncludeRoles)
            AnsiConsole.MarkupLine($"Found [bold]{schema.Roles.Count}[/] role(s) to import.");
        AnsiConsole.WriteLine();

        // Snapshot existing target state up-front. We refresh per-entity field
        // lists below as we add to them.
        List<Entity> existingEntities = [];
        await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
            .StartAsync("Fetching existing Anythink entities...", async _ =>
                existingEntities = await _target.GetEntitiesAsync());

        var entitiesByName = existingEntities
            .ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);

        PrintPlan(schema, entitiesByName);

        if (_options.DryRun)
            return new ImportResult(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                schema.Errors ?? [], schema.Warnings);

        if (_options.RequireConfirmation &&
            !AnsiConsole.Confirm("Apply this import to the project?", defaultValue: false))
        {
            Renderer.Info("Cancelled — nothing was changed.");
            return new ImportResult(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], null);
        }

        var result = new ResultAccumulator();
        result.Errors.AddRange(schema.Errors ?? []);
        result.Warnings.AddRange(schema.Warnings ?? []);

        // ── Apply collections / fields ────────────────────────────────────────
        var failedEntities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in schema.Collections)
        {
            if (!await ApplyCollectionAsync(col, entitiesByName, result))
                failedEntities.Add(col.Name);
        }

        // ── Apply flows ───────────────────────────────────────────────────────
        if (_options.IncludeFlows && schema.Flows.Count > 0)
        {
            AnsiConsole.WriteLine();
            Renderer.Header("Importing flows");
            Dictionary<string, Workflow>? existingByName = null;
            try
            {
                existingByName = (await _target.GetWorkflowsAsync())
                    .ToDictionary(w => w.Name, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"workflows: could not list existing workflows, so no flows were imported: {ex.Message}");
            }

            foreach (var flow in existingByName is null ? [] : schema.Flows)
            {
                await ApplyFlowAsync(flow, existingByName!, result);
            }
        }

        // ── Apply files (must run before data so refs resolve) ────────────────
        var fileIdMap = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        if (_options.IncludeFiles && schema.Files.Count > 0)
        {
            AnsiConsole.WriteLine();
            Renderer.Header("Importing files");
            await ApplyFilesAsync(schema.Files, fileIdMap, result);
        }

        // ── Apply data records ────────────────────────────────────────────────
        if (_options.IncludeData)
        {
            AnsiConsole.WriteLine();
            Renderer.Header("Importing data");
            await ApplyDataAsync(schema, fileIdMap, failedEntities, result);
        }

        // ── Apply roles ───────────────────────────────────────────────────────
        if (_options.IncludeRoles && schema.Roles.Count > 0)
        {
            AnsiConsole.WriteLine();
            Renderer.Header("Importing roles");
            await ApplyRolesAsync(schema.Roles, result);
        }

        PrintSummary(result);
        return result.Build();
    }

    // ── Role application ──────────────────────────────────────────────────────

    private async Task ApplyRolesAsync(List<ImportRole> roles, ResultAccumulator result)
    {
        HashSet<string> existingNames;
        Dictionary<string, Permission> permsByName;
        try
        {
            existingNames = (await _target.GetRolesAsync()).Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            permsByName = (await _target.GetPermissionsAsync()).ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"roles: could not list existing roles and permissions, so no roles were imported: {ex.Message}");
            return;
        }

        foreach (var role in roles)
        {
            // Skip Anythink's auto-created Admin User role — it isn't user-managed.
            if (role.Name.Equals("Admin User", StringComparison.OrdinalIgnoreCase))
                continue;

            if (existingNames.Contains(role.Name))
            {
                result.RolesSkipped++;
                result.Warnings.Add($"role '{role.Name}' already exists in this project — left untouched, no permissions added.");
                continue;
            }

            int roleId;
            try
            {
                var created = await _target.CreateRoleAsync(
                    new CreateRoleRequest(role.Name, role.Description));
                roleId = created.Id;
                result.RolesCreated++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"role '{role.Name}': {ex.Message}");
                Renderer.Error($"create role failed — {role.Name}: {ex.Message}");
                continue;
            }

            var permIds = new HashSet<int>();
            foreach (var (collection, action) in role.CollectionPermissions)
            {
                var permName = $"{collection}:{action}";
                if (!permsByName.TryGetValue(permName, out var perm))
                {
                    try
                    {
                        perm = await _target.CreatePermissionAsync(
                            new CreatePermissionRequest(permName, null, true));
                        permsByName[permName] = perm;
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"permission '{permName}': {ex.Message}");
                        continue;
                    }
                }
                permIds.Add(perm.Id);
            }

            try
            {
                await _target.UpdateRoleWithPermissionsAsync(roleId,
                    new UpdateRolePermissionsRequest(role.Name, role.Description,
                        true, true, permIds.ToList()));
                result.PermissionsAttached += permIds.Count;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"attaching permissions to '{role.Name}': {ex.Message}");
                continue;
            }

            Renderer.Success($"[bold]{Markup.Escape(role.Name)}[/] created — {permIds.Count} permission(s)");
        }
    }

    // ── File application ──────────────────────────────────────────────────────

    private async Task ApplyFilesAsync(
        List<ImportFile> files,
        Dictionary<string, long> fileIdMap,
        ResultAccumulator result)
    {
        var existingBySource = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var f in await _target.GetAllFilesAsync())
                if (ReadSourceFileId(f.CustomMetadata, _source.PlatformName) is { } sourceId)
                    existingBySource[sourceId] = f.Id;
        }
        catch (Exception ex)
        {
            result.Errors.Add($"files: could not list existing project files, so no files were imported: {ex.Message}");
            result.FilesFailed += files.Count;
            return;
        }

        foreach (var file in files)
        {
            if (existingBySource.TryGetValue(file.SourceId, out var existingId))
            {
                fileIdMap[file.SourceId] = existingId;
                result.FilesSkipped++;
                continue;
            }

            FileResponse uploaded;
            try
            {
                var url = _source.GetFileDownloadUrl(file.SourceId);
                uploaded = await _target.UploadFileFromUrlAsync(
                    url, file.FileName, file.IsPublic, _source.SourceAuthToken, MaxFileBytes);
                fileIdMap[file.SourceId] = uploaded.Id;
                result.FilesUploaded++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"file {file.FileName}: {ex.Message}");
                result.FilesFailed++;
                continue;
            }

            try
            {
                await _target.UpdateFileMetadataAsync(uploaded.Id,
                    new UpdateFileMetadataRequest(BuildSourceMetadata(file.SourceId)));
            }
            catch (Exception ex)
            {
                result.Errors.Add($"file {file.FileName}: uploaded but its source id could not be recorded, so a re-run would upload it again: {ex.Message}");
            }
        }

        Renderer.Success(
            $"Files — [green]{result.FilesUploaded} uploaded[/]" +
            (result.FilesSkipped > 0 ? $", [dim]{result.FilesSkipped} already present[/]" : "") +
            (result.FilesFailed > 0 ? $", [yellow]{result.FilesFailed} failed[/]" : ""));
    }

    private string BuildSourceMetadata(string sourceFileId) =>
        System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["imported_from"] = _source.PlatformName.ToLowerInvariant(),
            ["source_file_id"] = sourceFileId
        });

    internal static string? ReadSourceFileId(string? customMetadata, string platformName)
    {
        if (string.IsNullOrWhiteSpace(customMetadata)) return null;
        try
        {
            var node = JsonNode.Parse(customMetadata) as JsonObject;
            return node?["imported_from"]?.GetValue<string>() == platformName.ToLowerInvariant()
                ? node["source_file_id"]?.GetValue<string>()
                : null;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    // ── Collection / field application ────────────────────────────────────────

    private async Task<bool> ApplyCollectionAsync(
        ImportCollection col,
        Dictionary<string, Entity> entitiesByName,
        ResultAccumulator result)
    {
        Entity entity;
        bool isNewEntity = !entitiesByName.TryGetValue(col.Name, out entity!);

        if (isNewEntity)
        {
            try
            {
                entity = await _target.CreateEntityAsync(new CreateEntityRequest(
                    Name: col.Name,
                    IsPublic: col.IsPublic));
                entitiesByName[col.Name] = entity;
                result.EntitiesCreated++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Create entity '{col.Name}': {ex.Message}");
                Renderer.Error($"create entity failed — {col.Name}: {ex.Message}");
                return false;
            }
        }
        else
        {
            result.EntitiesSkipped++;
        }

        // Merge fields: only add ones that don't already exist on the target entity.
        var existingFieldNames = (entity.Fields ?? new List<Field>())
            .Select(f => f.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int created = 0, skipped = 0, failed = 0;

        foreach (var field in col.Fields)
        {
            if (existingFieldNames.Contains(field.Name))
            {
                skipped++;
                result.FieldsSkipped++;
                continue;
            }

            try
            {
                await _target.AddFieldAsync(col.Name, new CreateFieldRequest(
                    Name: field.Name,
                    DatabaseType: field.DatabaseType,
                    DisplayType: field.DisplayType,
                    Label: field.Label,
                    IsRequired: field.IsRequired,
                    IsUnique: field.IsUnique,
                    IsIndexed: field.IsIndexed,
                    Relationship: field.Relationship));
                existingFieldNames.Add(field.Name);
                created++;
                result.FieldsCreated++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{col.Name}.{field.Name}: {ex.Message}");
                failed++;
                result.FieldsFailed++;
            }
        }

        var verb = isNewEntity ? "created" : "merged";
        var summary = (skipped, failed) switch
        {
            (0, 0) => $"{created} field(s)",
            (var s, 0) => $"{created} new, [dim]{s} already present[/]",
            (0, var f) => $"{created} field(s), [yellow]{f} failed[/]",
            (var s, var f) => $"{created} new, [dim]{s} already present[/], [yellow]{f} failed[/]"
        };
        Renderer.Success($"[bold]{Markup.Escape(col.Name)}[/] {verb} — {summary}");
        return true;
    }

    // ── Flow application ──────────────────────────────────────────────────────

    private async Task ApplyFlowAsync(
        ImportFlow flow,
        Dictionary<string, Workflow> existingByName,
        ResultAccumulator result)
    {
        if (existingByName.ContainsKey(flow.Name))
        {
            result.WorkflowsSkipped++;
            result.Warnings.Add($"workflow '{flow.Name}' already exists in this project — left untouched.");
            return;
        }

        Workflow wf;
        try
        {
            wf = await _target.CreateWorkflowAsync(new CreateWorkflowRequest(
                Name: flow.Name,
                Description: null,
                Enabled: false,
                Triggers: flow.Triggers));
            result.WorkflowsCreated++;
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Workflow '{flow.Name}': {ex.Message}");
            Renderer.Error($"create workflow failed — {flow.Name}: {ex.Message}");
            return;
        }

        var stepIdMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int created = 0, failed = 0, reviewSteps = 0;

        var ordered = flow.Steps
            .OrderByDescending(s => s.IsStartStep)
            .ToList();

        // Pass 1: create every source step.
        foreach (var step in ordered)
        {
            try
            {
                var c = await _target.AddWorkflowStepAsync(wf.Id,
                    new CreateWorkflowStepRequest(
                        Key: step.Key,
                        Name: step.Name,
                        Action: step.Action,
                        Enabled: step.Enabled,
                        IsStartStep: step.IsStartStep,
                        Description: step.Description,
                        Parameters: step.Parameters));
                stepIdMap[step.SourceId] = c.Id;
                created++;
                result.WorkflowStepsCreated++;
                if (step.NeedsManualReview)
                {
                    reviewSteps++;
                    result.ReviewSteps++;
                    result.StepsNeedingReview.Add(($"{flow.Name} / {step.Name}", step.Action, step.ReviewNote));
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{flow.Name}/{step.Name}: {ex.Message}");
                failed++;
                result.WorkflowStepsFailed++;
            }
        }

        // Pass 2: wire success / failure links.
        foreach (var step in ordered)
        {
            if (step.OnSuccessSourceId == null && step.OnFailureSourceId == null) continue;
            if (!stepIdMap.TryGetValue(step.SourceId, out var dstStepId)) continue;

            int? dstSuccess = ResolveLink(step.OnSuccessSourceId);
            int? dstFailure = ResolveLink(step.OnFailureSourceId);
            if (dstSuccess == null && dstFailure == null) continue;

            try
            {
                await _target.UpdateWorkflowStepFullAsync(wf.Id, dstStepId, new UpdateWorkflowStepRequest(
                    step.Name, step.Description, step.Enabled, step.Action, step.Parameters,
                    step.IsStartStep, dstSuccess, dstFailure));
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{flow.Name}/{step.Name}: linking to the next step failed: {ex.Message}");
            }

            int? ResolveLink(string? sourceId)
            {
                if (sourceId == null) return null;
                if (stepIdMap.TryGetValue(sourceId, out var id)) return id;
                result.Errors.Add($"{flow.Name}/{step.Name}: linked step '{sourceId}' was not created, so the link was dropped.");
                return null;
            }
        }

        var pieces = new List<string>();
        if (created > 0) pieces.Add($"{created} new step(s)");
        if (failed > 0) pieces.Add($"[yellow]{failed} failed[/]");
        if (reviewSteps > 0) pieces.Add($"[yellow]{reviewSteps} need review[/]");
        var summary = pieces.Count == 0 ? "no changes" : string.Join(", ", pieces);
        var triggerLabel = string.Join("/", flow.Triggers.Select(t => t.Type));
        Renderer.Success($"[bold]{Markup.Escape(flow.Name)}[/] ({Markup.Escape(triggerLabel)}) created — {summary}");
    }

    // ── Data application ──────────────────────────────────────────────────────

    private async Task ApplyDataAsync(
        ImportSchema schema,
        Dictionary<string, long> fileIdMap,
        HashSet<string> failedEntities,
        ResultAccumulator result)
    {
        // Per-entity src-id → dst-id map, populated as records insert.
        // Junction-table FK lookups depend on these being built first.
        var idMap = schema.Collections
            .ToDictionary(c => c.Name, _ => new Dictionary<long, long>(),
                StringComparer.OrdinalIgnoreCase);

        // FK field map per entity: list of (fieldName, targetCollection).
        var fkMap = schema.Collections.ToDictionary(
            c => c.Name,
            c => c.Fields
                  .Where(f => f.ForeignKeyCollection is not null)
                  .Select(f => (f.Name, Target: f.ForeignKeyCollection!))
                  .ToList(),
            StringComparer.OrdinalIgnoreCase);

        // File field names per entity — values get remapped via fileIdMap.
        var fileFieldsMap = schema.Collections.ToDictionary(
            c => c.Name,
            c => c.Fields.Where(f => f.IsFileField).Select(f => f.Name).ToList(),
            StringComparer.OrdinalIgnoreCase);

        // Cycles are inserted in arrival order with the cycle-breaking FK dropped on the first record.
        var ordered = TopoSortCollections(schema.Collections);

        foreach (var col in ordered)
        {
            if (failedEntities.Contains(col.Name))
            {
                Renderer.Warn($"skip {col.Name} data — its entity could not be created");
                continue;
            }
            if (col.DataUnsupportedReason is not null)
            {
                result.Errors.Add($"{col.Name}: data not imported — {col.DataUnsupportedReason}");
                continue;
            }
            await ApplyCollectionDataAsync(col, idMap, fkMap[col.Name],
                fileFieldsMap[col.Name], fileIdMap, result);
        }
    }

    internal static List<ImportCollection> TopoSortCollections(List<ImportCollection> collections)
    {
        var byName = collections.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<ImportCollection>();

        void Visit(ImportCollection col)
        {
            if (visited.Contains(col.Name)) return;
            if (visiting.Contains(col.Name)) return; // cycle — bail to avoid stack overflow
            visiting.Add(col.Name);
            foreach (var f in col.Fields)
            {
                if (f.ForeignKeyCollection is null) continue;
                if (byName.TryGetValue(f.ForeignKeyCollection, out var dep))
                    Visit(dep);
            }
            visiting.Remove(col.Name);
            visited.Add(col.Name);
            ordered.Add(col);
        }

        foreach (var c in collections.OrderBy(c => c.IsJunction ? 1 : 0).ThenBy(c => c.Name))
            Visit(c);

        return ordered;
    }

    private async Task ApplyCollectionDataAsync(
        ImportCollection col,
        Dictionary<string, Dictionary<long, long>> idMap,
        List<(string Name, string Target)> fks,
        List<string> fileFields,
        Dictionary<string, long> fileIdMap,
        ResultAccumulator result)
    {
        int existingCount;
        try { existingCount = await CountTargetRecordsAsync(col.Name); }
        catch (Exception ex)
        {
            result.Errors.Add($"{col.Name}: could not check for existing records, so its data was not imported: {ex.Message}");
            return;
        }

        if (existingCount > 0)
        {
            Renderer.Info($"[dim]skip[/]  {Markup.Escape(col.Name)} — target already has {existingCount} record(s)");
            result.Warnings.Add($"{col.Name}: data not imported because the project already has {existingCount} record(s); references to it from other collections were dropped.");
            return;
        }

        var page = 1;
        int created = 0, failed = 0, missingId = 0;
        var droppedRefs = new Dictionary<string, int>();

        while (true)
        {
            ImportRecordPage pageData;
            try
            {
                pageData = await _source.FetchRecordsAsync(col.Name, page, _options.DataPageSize);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{col.Name} page {page}: {ex.Message}");
                break;
            }

            if (pageData.Records.Count == 0) break;

            foreach (var record in pageData.Records)
            {
                if (!TryReadId(record, out var srcId))
                {
                    failed++; missingId++; result.RecordsFailed++;
                    continue;
                }

                // Strip server-managed fields and remap FKs onto a clean payload.
                var payload = new JsonObject();
                foreach (var kv in record)
                {
                    if (kv.Key is "id" or "tenant_id") continue;
                    payload[kv.Key] = kv.Value?.DeepClone();
                }

                foreach (var (name, target) in fks)
                {
                    if (!payload.ContainsKey(name)) continue;
                    var node = payload[name];
                    if (node is null) continue;

                    // Directus stores FK as a flat integer or a nested {id} object.
                    long? srcFk = ExtractFkId(node);
                    if (srcFk is null)
                    {
                        // Unknown shape — leave as-is and hope server accepts it.
                        continue;
                    }

                    if (idMap.TryGetValue(target, out var targetMap) &&
                        targetMap.TryGetValue(srcFk.Value, out var dstFk))
                    {
                        payload[name] = JsonValue.Create(dstFk);
                    }
                    else
                    {
                        payload.Remove(name);
                        droppedRefs[name] = droppedRefs.GetValueOrDefault(name) + 1;
                    }
                }

                // Anythink file fields take an array of integer ids, even for a single file.
                foreach (var fieldName in fileFields)
                {
                    if (!payload.ContainsKey(fieldName)) continue;
                    var node = payload[fieldName];
                    if (node is null) continue;
                    if (node is JsonValue v && v.TryGetValue<string>(out var srcFileId) &&
                        !string.IsNullOrEmpty(srcFileId))
                    {
                        if (fileIdMap.TryGetValue(srcFileId, out var dstFileId))
                            payload[fieldName] = new JsonArray(JsonValue.Create(dstFileId));
                        else
                        {
                            payload.Remove(fieldName);
                            droppedRefs[fieldName] = droppedRefs.GetValueOrDefault(fieldName) + 1;
                        }
                    }
                }

                try
                {
                    var createdNode = await _target.CreateItemAsync(col.Name, payload);
                    if (createdNode?["id"] is JsonValue idVal && idVal.TryGetValue<long>(out var dstId))
                        idMap[col.Name][srcId] = dstId;
                    else
                        result.Errors.Add($"{col.Name}#{srcId}: created but the response had no id, so references to it cannot be remapped.");
                    created++;
                    result.RecordsCreated++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{col.Name}#{srcId}: {ex.Message}");
                    failed++;
                    result.RecordsFailed++;
                }
            }

            if (pageData.Records.Count < _options.DataPageSize) break;
            page++;
        }

        foreach (var (field, count) in droppedRefs)
            result.Warnings.Add($"{col.Name}.{field}: {count} reference(s) dropped because the referenced row or file was not imported.");

        if (missingId > 0)
            result.Errors.Add($"{col.Name}: {missingId} record(s) not imported because they have no integer 'id'.");

        var msg = failed == 0
            ? $"{created} record(s)"
            : $"{created} record(s), [yellow]{failed} failed[/]";
        Renderer.Success($"[bold]{Markup.Escape(col.Name)}[/] — {msg}");
    }

    private async Task<int> CountTargetRecordsAsync(string entityName)
    {
        var first = await _target.ListItemsAsync(entityName, 1, 1);
        return first.TotalCount ?? first.Items.Count;
    }

    private static bool TryReadId(JsonObject record, out long id)
    {
        id = 0;
        if (record["id"] is not JsonValue v) return false;
        if (v.TryGetValue<long>(out var l)) { id = l; return true; }
        if (v.TryGetValue<int>(out var i)) { id = i; return true; }
        return false;
    }

    private static long? ExtractFkId(JsonNode node)
    {
        if (node is JsonValue v)
        {
            if (v.TryGetValue<long>(out var l)) return l;
            if (v.TryGetValue<int>(out var i)) return i;
        }
        else if (node is JsonObject obj && obj["id"] is JsonValue idVal)
        {
            if (idVal.TryGetValue<long>(out var l)) return l;
            if (idVal.TryGetValue<int>(out var i)) return i;
        }
        return null;
    }

    // ── Output ────────────────────────────────────────────────────────────────

    private void PrintPlan(ImportSchema schema, Dictionary<string, Entity> entitiesByName)
    {
        Renderer.Header(_options.DryRun ? "Import plan (dry run)" : "Import plan");

        var table = Renderer.BuildTable("Collection", "Fields", "Action");
        foreach (var col in schema.Collections)
        {
            string action;
            if (entitiesByName.TryGetValue(col.Name, out var existing))
            {
                var existingFields = (existing.Fields ?? new List<Field>())
                    .Select(f => f.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var newFields = col.Fields.Count(f => !existingFields.Contains(f.Name));
                action = newFields == 0 ? "skip (up to date)" : $"merge (+{newFields} fields)";
            }
            else
            {
                action = "create";
            }
            Renderer.AddRow(table, col.Name, col.Fields.Count.ToString(), action);
        }
        AnsiConsole.Write(table);

        if (_options.IncludeFlows) Renderer.Info($"{schema.Flows.Count} flow(s) would be created (disabled); existing workflows are left untouched.");
        if (_options.IncludeFiles) Renderer.Info($"{schema.Files.Count} file(s) would be uploaded.");
        if (_options.IncludeData) Renderer.Info("Records would be imported into collections that are currently empty.");
        if (_options.IncludeRoles) Renderer.Info($"{schema.Roles.Count} role(s) would be created; existing roles are left untouched.");

        if (_options.DryRun)
        {
            foreach (var w in schema.Warnings ?? []) Renderer.Warn(Markup.Escape(w));
            foreach (var e in schema.Errors ?? []) Renderer.Error(e);
            AnsiConsole.WriteLine();
            Renderer.Info("Re-run without [bold]--dry-run[/] to apply.");
        }
    }

    private void PrintSummary(ResultAccumulator r)
    {
        AnsiConsole.WriteLine();
        Renderer.Header("Import complete");
        AnsiConsole.MarkupLine($"  Entities created : [green]{r.EntitiesCreated}[/]");
        AnsiConsole.MarkupLine($"  Entities skipped : [dim]{r.EntitiesSkipped}[/]");
        AnsiConsole.MarkupLine($"  Fields created   : [green]{r.FieldsCreated}[/]");
        if (r.FieldsSkipped > 0)
            AnsiConsole.MarkupLine($"  Fields skipped   : [dim]{r.FieldsSkipped}[/]");
        if (r.FieldsFailed > 0)
            AnsiConsole.MarkupLine($"  Fields failed    : [red]{r.FieldsFailed}[/]");
        if (_options.IncludeFlows)
        {
            AnsiConsole.MarkupLine($"  Workflows created: [green]{r.WorkflowsCreated}[/]");
            AnsiConsole.MarkupLine($"  Workflows skipped: [dim]{r.WorkflowsSkipped}[/]");
            AnsiConsole.MarkupLine($"  Steps created    : [green]{r.WorkflowStepsCreated}[/]");
            if (r.WorkflowStepsFailed > 0)
                AnsiConsole.MarkupLine($"  Steps failed     : [red]{r.WorkflowStepsFailed}[/]");
        }
        if (_options.IncludeData)
        {
            AnsiConsole.MarkupLine($"  Records created  : [green]{r.RecordsCreated}[/]");
            if (r.RecordsFailed > 0)
                AnsiConsole.MarkupLine($"  Records failed   : [red]{r.RecordsFailed}[/]");
        }
        if (_options.IncludeFiles)
        {
            AnsiConsole.MarkupLine($"  Files uploaded   : [green]{r.FilesUploaded}[/]");
            if (r.FilesSkipped > 0)
                AnsiConsole.MarkupLine($"  Files skipped    : [dim]{r.FilesSkipped}[/]");
            if (r.FilesFailed > 0)
                AnsiConsole.MarkupLine($"  Files failed     : [red]{r.FilesFailed}[/]");
        }
        if (_options.IncludeRoles)
        {
            AnsiConsole.MarkupLine($"  Roles created    : [green]{r.RolesCreated}[/]");
            if (r.RolesSkipped > 0)
                AnsiConsole.MarkupLine($"  Roles skipped    : [dim]{r.RolesSkipped}[/]");
            AnsiConsole.MarkupLine($"  Perms attached   : [green]{r.PermissionsAttached}[/]");
        }

        if (r.Warnings.Count > 0)
        {
            AnsiConsole.WriteLine();
            Renderer.Warn($"{r.Warnings.Count} warning(s):");
            foreach (var w in r.Warnings)
                AnsiConsole.MarkupLine($"  [yellow]·[/] {Markup.Escape(w)}");
        }

        if (r.Errors.Count > 0)
        {
            AnsiConsole.WriteLine();
            Renderer.Warn($"{r.Errors.Count} error(s):");
            foreach (var e in r.Errors)
                AnsiConsole.MarkupLine($"  [red]·[/] {Markup.Escape(e)}");
        }

        if (r.StepsNeedingReview.Count > 0)
        {
            AnsiConsole.WriteLine();
            Renderer.Warn($"{r.StepsNeedingReview.Count} workflow step(s) need manual review:");
            foreach (var (where, action, note) in r.StepsNeedingReview)
            {
                AnsiConsole.MarkupLine($"  [yellow]·[/] [bold]{Markup.Escape(where)}[/] → [#F97316]{Markup.Escape(action)}[/]");
                if (!string.IsNullOrEmpty(note))
                    AnsiConsole.MarkupLine($"     [dim]{Markup.Escape(note)}[/]");
            }
        }
    }

    private sealed class ResultAccumulator
    {
        public int EntitiesCreated, EntitiesSkipped;
        public int FieldsCreated, FieldsSkipped, FieldsFailed;
        public int WorkflowsCreated, WorkflowsSkipped;
        public int WorkflowStepsCreated, WorkflowStepsFailed;
        public int RecordsCreated, RecordsFailed;
        public int FilesUploaded, FilesSkipped, FilesFailed;
        public int RolesCreated, RolesSkipped, PermissionsAttached;
        public int ReviewSteps;
        public List<(string Where, string Action, string? Note)> StepsNeedingReview = [];
        public List<string> Errors = [];
        public List<string> Warnings = [];

        public ImportResult Build() => new(
            EntitiesCreated, EntitiesSkipped,
            FieldsCreated, FieldsSkipped, FieldsFailed,
            WorkflowsCreated, WorkflowsSkipped,
            WorkflowStepsCreated, WorkflowStepsFailed,
            RecordsCreated, RecordsFailed,
            FilesUploaded, FilesSkipped, FilesFailed,
            RolesCreated, RolesSkipped, PermissionsAttached,
            Errors, Warnings);
    }
}
