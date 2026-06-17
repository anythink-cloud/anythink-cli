using AnythinkCli.Client;
using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using System.Text.Json.Nodes;

namespace AnythinkCli.Importers;

public record ImportOptions(
    bool DryRun,
    bool IncludeFlows,
    bool IncludeData  = false,
    bool IncludeFiles = false,
    bool IncludeRoles = false,
    int  DataPageSize = 100
);

public record ImportResult(
    int           EntitiesCreated,
    int           EntitiesSkipped,
    int           FieldsCreated,
    int           FieldsSkipped,
    int           FieldsFailed,
    int           WorkflowsCreated,
    int           WorkflowsSkipped,
    int           WorkflowStepsCreated,
    int           WorkflowStepsFailed,
    int           RecordsCreated,
    int           RecordsSkipped,
    int           RecordsFailed,
    int           FilesUploaded,
    int           FilesSkipped,
    int           FilesFailed,
    int           RolesCreated,
    int           RolesSkipped,
    int           PermissionsAttached,
    List<string>  Errors
);

/// <summary>
/// Platform-agnostic orchestrator. Takes whatever schema an
/// <see cref="IPlatformImporter"/> produces and applies it to an Anythink
/// project — creating missing entities, merging fields into existing ones,
/// and (optionally) creating workflows.
/// </summary>
public class ImportRunner
{
    private readonly IPlatformImporter _source;
    private readonly AnythinkClient    _target;
    private readonly ImportOptions     _options;

    public ImportRunner(IPlatformImporter source, AnythinkClient target, ImportOptions options)
    {
        _source  = source;
        _target  = target;
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

        if (_options.DryRun)
        {
            PrintDryRunPlan(schema, entitiesByName);
            return new ImportResult(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, []);
        }

        // ── Apply collections / fields ────────────────────────────────────────
        var result = new ResultAccumulator();

        foreach (var col in schema.Collections)
        {
            await ApplyCollectionAsync(col, entitiesByName, result);
        }

        // ── Apply flows ───────────────────────────────────────────────────────
        if (_options.IncludeFlows && schema.Flows.Count > 0)
        {
            AnsiConsole.WriteLine();
            Renderer.Header("Importing flows");
            var existingWorkflows = await _target.GetWorkflowsAsync();
            var existingByName    = existingWorkflows
                .ToDictionary(w => w.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var flow in schema.Flows)
            {
                await ApplyFlowAsync(flow, existingByName, result);
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
            await ApplyDataAsync(schema, fileIdMap, result);
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

    /// <summary>
    /// For each source role: create-or-find the matching Anythink role, ensure
    /// each "&lt;collection&gt;:&lt;action&gt;" permission exists, then attach the
    /// permission set to the role. We preserve any permissions the role already
    /// has on the target rather than wiping them.
    /// </summary>
    private async Task ApplyRolesAsync(List<ImportRole> roles, ResultAccumulator result)
    {
        // Snapshot target state once.
        var existingRoles      = await _target.GetRolesAsync();
        var existingByName     = existingRoles.ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);
        var existingPermissions = await _target.GetPermissionsAsync();
        var permsByName         = existingPermissions.ToDictionary(p => p.Name,
                                      StringComparer.OrdinalIgnoreCase);

        foreach (var role in roles)
        {
            // Skip Anythink's auto-created Admin User role — it isn't user-managed.
            if (role.Name.Equals("Admin User", StringComparison.OrdinalIgnoreCase))
                continue;

            int roleId;
            HashSet<int> existingPermIds;
            if (existingByName.TryGetValue(role.Name, out var existing))
            {
                roleId = existing.Id;
                existingPermIds = (existing.Permissions ?? new List<Permission>())
                    .Select(p => p.Id).ToHashSet();
                result.RolesSkipped++;
            }
            else
            {
                try
                {
                    var created = await _target.CreateRoleAsync(
                        new CreateRoleRequest(role.Name, role.Description));
                    roleId = created.Id;
                    existingPermIds = [];
                    result.RolesCreated++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"role '{role.Name}': {ex.Message}");
                    Renderer.Error($"create role failed — {Markup.Escape(role.Name)}: {Markup.Escape(ex.Message)}");
                    continue;
                }
            }

            // Ensure each permission "<collection>:<action>" exists, then collect ids.
            var permIds = new HashSet<int>(existingPermIds);
            int newAttached = 0;
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
                if (permIds.Add(perm.Id)) newAttached++;
            }

            try
            {
                await _target.UpdateRoleWithPermissionsAsync(roleId,
                    new UpdateRolePermissionsRequest(role.Name, role.Description,
                        true, true, permIds.ToList()));
                result.PermissionsAttached += newAttached;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"attaching permissions to '{role.Name}': {ex.Message}");
                continue;
            }

            var verb = existing is null ? "created" : "merged";
            Renderer.Success($"[bold]{Markup.Escape(role.Name)}[/] {verb} — " +
                $"{role.CollectionPermissions.Count} permission(s) " +
                $"({newAttached} newly attached)");
        }
    }

    // ── File application ──────────────────────────────────────────────────────

    private async Task ApplyFilesAsync(
        List<ImportFile> files,
        Dictionary<string, long> fileIdMap,
        ResultAccumulator result)
    {
        // Snapshot existing target files by filename so we can detect previously-imported ones.
        var existingByName = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var existing = await _target.GetAllFilesAsync();
            foreach (var f in existing)
                existingByName[f.OriginalFileName] = f.Id;
        }
        catch { /* best effort — proceed without de-dup */ }

        foreach (var file in files)
        {
            if (existingByName.TryGetValue(file.FileName, out var existingId))
            {
                fileIdMap[file.SourceId] = existingId;
                result.FilesSkipped++;
                continue;
            }

            try
            {
                var url = _source.GetFileDownloadUrl(file.SourceId);
                var uploaded = await _target.UploadFileFromUrlAsync(
                    url, file.FileName, file.IsPublic, _source.SourceAuthToken);
                fileIdMap[file.SourceId] = uploaded.Id;
                result.FilesUploaded++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"file {file.FileName}: {ex.Message}");
                result.FilesFailed++;
            }
        }

        Renderer.Success(
            $"Files — [green]{result.FilesUploaded} uploaded[/]" +
            (result.FilesSkipped > 0 ? $", [dim]{result.FilesSkipped} already present[/]" : "") +
            (result.FilesFailed > 0 ? $", [yellow]{result.FilesFailed} failed[/]" : ""));
    }

    // ── Collection / field application ────────────────────────────────────────

    private async Task ApplyCollectionAsync(
        ImportCollection col,
        Dictionary<string, Entity> entitiesByName,
        ResultAccumulator result)
    {
        Entity entity;
        bool   isNewEntity = !entitiesByName.TryGetValue(col.Name, out entity!);

        if (isNewEntity)
        {
            try
            {
                entity = await _target.CreateEntityAsync(new CreateEntityRequest(
                    Name:     col.Name,
                    IsPublic: col.IsPublic));
                entitiesByName[col.Name] = entity;
                result.EntitiesCreated++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Create entity '{col.Name}': {ex.Message}");
                Renderer.Error($"create entity failed — {Markup.Escape(col.Name)}: {Markup.Escape(ex.Message)}");
                return;
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
                    Name:         field.Name,
                    DatabaseType: field.DatabaseType,
                    DisplayType:  field.DisplayType,
                    Label:        field.Label,
                    IsRequired:   field.IsRequired,
                    IsUnique:     field.IsUnique,
                    IsIndexed:    field.IsIndexed,
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
            (0, 0)         => $"{created} field(s)",
            (var s, 0)     => $"{created} new, [dim]{s} already present[/]",
            (0, var f)     => $"{created} field(s), [yellow]{f} failed[/]",
            (var s, var f) => $"{created} new, [dim]{s} already present[/], [yellow]{f} failed[/]"
        };
        Renderer.Success($"[bold]{Markup.Escape(col.Name)}[/] {verb} — {summary}");
    }

    // ── Flow application ──────────────────────────────────────────────────────

    private async Task ApplyFlowAsync(
        ImportFlow flow,
        Dictionary<string, Workflow> existingByName,
        ResultAccumulator result)
    {
        // Create-or-merge: if a workflow with this name exists, we add only
        // the steps it doesn't already have (matched by Key) and re-wire the
        // success/failure links so they cover both new and old steps.
        Workflow wf;
        bool merged;
        if (existingByName.TryGetValue(flow.Name, out var existing))
        {
            // Re-fetch with steps populated — list view may not include them.
            wf = await _target.GetWorkflowAsync(existing.Id) ?? existing;
            merged = true;
            result.WorkflowsSkipped++;
        }
        else
        {
            try
            {
                wf = await _target.CreateWorkflowAsync(new CreateWorkflowRequest(
                    Name:        flow.Name,
                    Description: null,
                    Enabled:     false,
                    Triggers:    flow.Triggers));
                result.WorkflowsCreated++;
                merged = false;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Workflow '{flow.Name}': {ex.Message}");
                Renderer.Error($"create workflow failed — {Markup.Escape(flow.Name)}: {Markup.Escape(ex.Message)}");
                return;
            }
        }

        // Build sourceId → dst step id map. For existing steps on the
        // workflow we don't know the original sourceId, so match by Key.
        var stepIdMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var existingByKey = (wf.Steps ?? new List<WorkflowStep>())
            .Where(s => !string.IsNullOrEmpty(s.Key))
            .ToDictionary(s => s.Key!, s => s.Id, StringComparer.OrdinalIgnoreCase);

        int created = 0, skipped = 0, failed = 0, reviewSteps = 0;

        var ordered = flow.Steps
            .OrderByDescending(s => s.IsStartStep)
            .ToList();

        // Pass 1: ensure every source step exists on the target.
        foreach (var step in ordered)
        {
            if (existingByKey.TryGetValue(step.Key, out var existingStepId))
            {
                stepIdMap[step.SourceId] = existingStepId;
                skipped++;
                if (step.NeedsManualReview) reviewSteps++;
                result.ReviewSteps += step.NeedsManualReview ? 1 : 0;
                if (step.NeedsManualReview)
                    result.StepsNeedingReview.Add(($"{flow.Name} / {step.Name}", step.Action, step.ReviewNote));
                continue;
            }

            try
            {
                var c = await _target.AddWorkflowStepAsync(wf.Id,
                    new CreateWorkflowStepRequest(
                        Key:         step.Key,
                        Name:        step.Name,
                        Action:      step.Action,
                        Enabled:     true,
                        IsStartStep: step.IsStartStep,
                        Description: step.Description,
                        Parameters:  step.Parameters));
                stepIdMap[step.SourceId] = c.Id;
                existingByKey[step.Key]  = c.Id;
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

            int? dstSuccess = step.OnSuccessSourceId != null && stepIdMap.TryGetValue(step.OnSuccessSourceId, out var rs) ? rs : null;
            int? dstFailure = step.OnFailureSourceId != null && stepIdMap.TryGetValue(step.OnFailureSourceId, out var rf) ? rf : null;
            if (dstSuccess == null && dstFailure == null) continue;

            try
            {
                // AnyAPI's UpdateWorkflowStepRequest is a full-replace: any
                // field we omit becomes null on the stored row. Send the full
                // shape so parameters/description/enabled survive the link wire.
                await _target.UpdateWorkflowStepFullAsync(wf.Id, dstStepId, new
                {
                    name              = step.Name,
                    description       = step.Description,
                    enabled           = true,
                    action            = step.Action,
                    parameters        = step.Parameters,
                    is_start_step     = step.IsStartStep,
                    on_success_step_id = dstSuccess,
                    on_failure_step_id = dstFailure,
                });
            }
            catch
            {
                // Link failures are non-fatal — best effort.
            }
        }

        var verb = merged ? "merged" : "created";
        var pieces = new List<string>();
        if (created > 0) pieces.Add($"{created} new step(s)");
        if (skipped > 0) pieces.Add($"[dim]{skipped} already present[/]");
        if (failed > 0)  pieces.Add($"[yellow]{failed} failed[/]");
        if (reviewSteps > 0) pieces.Add($"[yellow]{reviewSteps} need review[/]");
        var summary = pieces.Count == 0 ? "no changes" : string.Join(", ", pieces);
        var triggerLabel = string.Join("/", flow.Triggers.Select(t => t.Type));
        Renderer.Success($"[bold]{Markup.Escape(flow.Name)}[/] ({Markup.Escape(triggerLabel)}) {verb} — {summary}");
    }

    // ── Data application ──────────────────────────────────────────────────────

    /// <summary>
    /// Paginates source records per collection, remaps FK + file fields, and
    /// inserts into the Anythink target. Non-junction collections first so FK
    /// maps are populated before junction rows reference them. Records whose
    /// FK can't be remapped (target row missing) have that field dropped — we
    /// don't fail the whole row.
    /// </summary>
    private async Task ApplyDataAsync(
        ImportSchema schema,
        Dictionary<string, long> fileIdMap,
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

        // Topologically order by FK dependencies so referenced rows always
        // land before referrers. Junctions naturally fall out at the end
        // because they're pure FKs. Cycles (rare — typically self-references
        // or A→B→A) get inserted in arrival order with the cycle-breaking
        // FK dropped on the first record; a second pass to repair them is
        // future work.
        var ordered = TopoSortCollections(schema.Collections);

        foreach (var col in ordered)
        {
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
        // Skip if target already has records — we don't currently overwrite.
        // (--force-data-entities flag would override this; future work.)
        int existingCount;
        try { existingCount = await CountTargetRecordsAsync(col.Name); }
        catch { existingCount = 0; }

        if (existingCount > 0)
        {
            Renderer.Info($"[dim]skip[/]  {Markup.Escape(col.Name)} — target already has {existingCount} record(s)");
            return;
        }

        var page = 1;
        int created = 0, failed = 0;

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
                    failed++; result.RecordsFailed++;
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
                        // Target row missing — drop the FK rather than send a stale id.
                        payload.Remove(name);
                    }
                }

                // File-field remap: source uses platform-native file ids
                // (UUIDs for Directus); Anythink uses integer file ids.
                // Anythink stores file references in a junction table to
                // anythink_files, so the value must be an array of ids — even
                // for a "single file" field.
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
                            payload.Remove(fieldName); // file wasn't imported — drop ref
                    }
                }

                try
                {
                    var createdNode = await _target.CreateItemAsync(col.Name, payload);
                    if (createdNode?["id"] is JsonValue idVal && idVal.TryGetValue<long>(out var dstId))
                        idMap[col.Name][srcId] = dstId;
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
        if (v.TryGetValue<long>(out var l))  { id = l; return true; }
        if (v.TryGetValue<int>(out var i))   { id = i; return true; }
        return false;
    }

    private static long? ExtractFkId(JsonNode node)
    {
        if (node is JsonValue v)
        {
            if (v.TryGetValue<long>(out var l)) return l;
            if (v.TryGetValue<int>(out var i))  return i;
        }
        else if (node is JsonObject obj && obj["id"] is JsonValue idVal)
        {
            if (idVal.TryGetValue<long>(out var l)) return l;
            if (idVal.TryGetValue<int>(out var i))  return i;
        }
        return null;
    }

    // ── Output ────────────────────────────────────────────────────────────────

    private void PrintDryRunPlan(ImportSchema schema, Dictionary<string, Entity> entitiesByName)
    {
        Renderer.Header("Import plan (dry run)");

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

        AnsiConsole.WriteLine();
        Renderer.Info("Re-run without [bold]--dry-run[/] to apply.");
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
            if (r.RecordsSkipped > 0)
                AnsiConsole.MarkupLine($"  Records skipped  : [dim]{r.RecordsSkipped}[/]");
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
                AnsiConsole.MarkupLine($"  Roles merged     : [dim]{r.RolesSkipped}[/]");
            AnsiConsole.MarkupLine($"  Perms attached   : [green]{r.PermissionsAttached}[/]");
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
        public int RecordsCreated, RecordsSkipped, RecordsFailed;
        public int FilesUploaded, FilesSkipped, FilesFailed;
        public int RolesCreated, RolesSkipped, PermissionsAttached;
        public int ReviewSteps;
        public List<(string Where, string Action, string? Note)> StepsNeedingReview = [];
        public List<string> Errors = [];

        public ImportResult Build() => new(
            EntitiesCreated, EntitiesSkipped,
            FieldsCreated, FieldsSkipped, FieldsFailed,
            WorkflowsCreated, WorkflowsSkipped,
            WorkflowStepsCreated, WorkflowStepsFailed,
            RecordsCreated, RecordsSkipped, RecordsFailed,
            FilesUploaded, FilesSkipped, FilesFailed,
            RolesCreated, RolesSkipped, PermissionsAttached,
            Errors);
    }
}
