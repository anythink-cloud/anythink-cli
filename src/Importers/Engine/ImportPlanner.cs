using System.Text.Json.Nodes;
using AnythinkCli.Models;

namespace AnythinkCli.Importers.Engine;

public static class ImportPlanner
{
    // Legacy mode: reproduces what ImportRunner decides today, so a plan and an apply never disagree.
    private const string AdminRole = "Admin User";

    public static ImportPlan Build(ImportSchema schema, TargetSnapshot target, PlanOptions options, PlanContext context)
    {
        var items = new List<PlanItem>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        var entityNames = schema.Collections.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var col in schema.Collections) AddCollection(items, used, col, target, entityNames);
        if (options.IncludeRoles) AddRoles(items, used, schema.Roles, target, entityNames);
        if (options.IncludeFlows) AddFlows(items, used, schema.Flows, target);
        if (options.IncludeFiles) AddFiles(items, used, schema.Files, target, context.Source.Platform);
        if (options.IncludeData) AddData(items, used, schema.Collections, target);
        AddReports(items, schema);

        var ordered = items
            .OrderBy(i => PlanKind.Rank(i.Kind))
            .ThenBy(i => i.Key, StringComparer.Ordinal)
            .ToList();

        return new ImportPlan(ImportPlan.CurrentFormatVersion, context.RunId, context.CreatedAt,
            context.Source, context.Target, options, Fingerprint(ordered), ordered);
    }

    private static string Unique(HashSet<string> used, string key, string? suffix = null)
    {
        if (used.Add(key)) return key;
        if (suffix is not null && used.Add($"{key}#{suffix}")) return $"{key}#{suffix}";
        for (var n = 2; ; n++)
            if (used.Add($"{key}#{n}")) return $"{key}#{n}";
    }

    private static string Fingerprint(IEnumerable<PlanItem> items) =>
        TargetHasher.Hash(string.Join("\n", items.Where(i => i.TargetHash is not null).Select(i => $"{i.Key}={i.TargetHash}")));

    private static void AddCollection(List<PlanItem> items, HashSet<string> used, ImportCollection col, TargetSnapshot target, HashSet<string> entityNames)
    {
        var existing = target.Entities.FirstOrDefault(e => e.Name.Equals(col.Name, StringComparison.OrdinalIgnoreCase));
        var entityKey = Unique(used, $"entity:{col.Name}");
        var present = (existing?.Fields ?? []).Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var fieldItems = new List<PlanItem>();
        foreach (var field in col.Fields)
        {
            var existingField = (existing?.Fields ?? []).FirstOrDefault(f => f.Name.Equals(field.Name, StringComparison.OrdinalIgnoreCase));
            var isNew = present.Add(field.Name);
            var dependsOn = new List<string> { entityKey };
            if (field.ForeignKeyCollection is { } fk && entityNames.Contains(fk)) dependsOn.Add($"entity:{fk}");

            fieldItems.Add(new PlanItem(
                Unique(used, $"field:{col.Name}.{field.Name}"), PlanKind.Field,
                isNew ? PlanAction.Create : PlanAction.Skip, ReportCategory.Mapped,
                isNew ? "field is missing from the project" : "field already exists",
                SourceRef: $"{col.Name}.{field.Name}", TargetName: field.Name, Parent: entityKey,
                DependsOn: dependsOn, Spec: FieldSpec(field),
                TargetHash: existingField is null ? TargetHasher.Absent : TargetHasher.Hash(existingField)));
        }

        var newFields = fieldItems.Count(i => i.Action == PlanAction.Create);
        var action = existing is null ? PlanAction.Create : newFields == 0 ? PlanAction.Skip : PlanAction.Merge;
        items.Add(new PlanItem(entityKey, PlanKind.Entity, action,
            action == PlanAction.Merge ? ReportCategory.MappedWithChanges : ReportCategory.Mapped,
            action switch
            {
                PlanAction.Create => "collection does not exist in the project",
                PlanAction.Merge => $"adds {newFields} missing field(s) to the existing entity",
                _ => "already up to date"
            },
            SourceRef: col.Name, TargetName: col.Name,
            Spec: new JsonObject
            {
                ["is_public"] = col.IsPublic,
                ["is_junction"] = col.IsJunction,
                ["field_count"] = col.Fields.Count,
                ["new_fields"] = newFields
            },
            TargetHash: existing is null ? TargetHasher.Absent : TargetHasher.Hash(existing)));
        items.AddRange(fieldItems);
    }

    private static JsonObject FieldSpec(ImportFieldSpec f)
    {
        var spec = new JsonObject
        {
            ["database_type"] = f.DatabaseType,
            ["display_type"] = f.DisplayType,
            ["label"] = f.Label,
            ["is_required"] = f.IsRequired,
            ["is_unique"] = f.IsUnique,
            ["is_indexed"] = f.IsIndexed,
            ["foreign_key_collection"] = f.ForeignKeyCollection,
            ["is_file_field"] = f.IsFileField
        };
        if (f.Relationship is { } rel && JsonNode.Parse(rel.GetRawText()) is { } node) spec["relationship"] = node;
        return spec;
    }

    private static void AddRoles(List<PlanItem> items, HashSet<string> used, List<ImportRole> roles, TargetSnapshot target, HashSet<string> entityNames)
    {
        var knownPermissions = target.Permissions.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var role in roles.Where(r => !r.Name.Equals(AdminRole, StringComparison.OrdinalIgnoreCase)))
        {
            var existing = target.Roles.FirstOrDefault(r => r.Name.Equals(role.Name, StringComparison.OrdinalIgnoreCase));
            var roleKey = Unique(used, $"role:{role.Name}");
            if (existing is not null)
            {
                items.Add(new PlanItem(roleKey, PlanKind.Role, PlanAction.Skip, ReportCategory.Mapped,
                    $"role '{role.Name}' already exists in this project — left untouched, no permissions added.",
                    SourceRef: role.Name, TargetName: role.Name, TargetHash: TargetHasher.Hash(existing)));
                continue;
            }

            items.Add(new PlanItem(roleKey, PlanKind.Role, PlanAction.Create, ReportCategory.Mapped,
                "role does not exist in the project",
                SourceRef: role.Name, TargetName: role.Name,
                Spec: new JsonObject { ["description"] = role.Description, ["permission_count"] = role.CollectionPermissions.Distinct().Count() },
                TargetHash: TargetHasher.Absent));

            foreach (var (collection, action) in role.CollectionPermissions.Distinct())
            {
                var name = $"{collection}:{action}";
                var isNew = !knownPermissions.ContainsKey(name);
                var dependsOn = new List<string> { roleKey };
                if (entityNames.Contains(collection)) dependsOn.Add($"entity:{collection}");
                items.Add(new PlanItem(Unique(used, $"permission:{role.Name}/{name}"), PlanKind.Permission,
                    isNew ? PlanAction.Create : PlanAction.Skip, ReportCategory.Mapped,
                    isNew ? "permission does not exist in the project" : "permission already exists and is reused",
                    SourceRef: name, TargetName: name, Parent: roleKey, DependsOn: dependsOn,
                    TargetHash: knownPermissions.TryGetValue(name, out var p) ? TargetHasher.Hash(p) : TargetHasher.Absent));
                knownPermissions.TryAdd(name, new Permission(0, name, null, null, true));
            }
        }
    }

    private static void AddFlows(List<PlanItem> items, HashSet<string> used, List<ImportFlow> flows, TargetSnapshot target)
    {
        foreach (var flow in flows)
        {
            var key = Unique(used, $"workflow:{flow.Name}");
            var existing = target.Workflows.FirstOrDefault(w => w.Name.Equals(flow.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                items.Add(new PlanItem(key, PlanKind.Workflow, PlanAction.Skip, ReportCategory.Mapped,
                    $"workflow '{flow.Name}' already exists in this project — left untouched.",
                    SourceRef: flow.Name, TargetName: flow.Name, TargetHash: TargetHasher.Hash(existing)));
                continue;
            }

            if (WorkflowTriggers.Problems(flow.Triggers) is { Count: > 0 } problems)
            {
                items.Add(new PlanItem(key, PlanKind.Workflow, PlanAction.Skip, ReportCategory.NeedsReview,
                    $"workflow '{flow.Name}' skipped: {string.Join("; ", problems)}.",
                    SourceRef: flow.Name, TargetName: flow.Name, TargetHash: TargetHasher.Absent));
                continue;
            }

            items.Add(new PlanItem(key, PlanKind.Workflow, PlanAction.Create, ReportCategory.Mapped,
                "workflow does not exist in the project; it is created disabled",
                SourceRef: flow.Name, TargetName: flow.Name,
                Spec: new JsonObject
                {
                    ["enabled"] = false,
                    ["triggers"] = new JsonArray(flow.Triggers.Select(t => (JsonNode?)JsonValue.Create(t.Type)).ToArray()),
                    ["step_count"] = flow.Steps.Count
                },
                TargetHash: TargetHasher.Absent));

            foreach (var step in flow.Steps)
                items.Add(new PlanItem(Unique(used, $"workflow_step:{flow.Name}/{step.Key}", step.SourceId), PlanKind.WorkflowStep, PlanAction.Create,
                    step.NeedsManualReview ? ReportCategory.NeedsReview : ReportCategory.Mapped,
                    step.NeedsManualReview ? "step needs manual review after import" : "step is created with its workflow",
                    SourceRef: step.SourceId, TargetName: step.Name, Parent: key, DependsOn: [key],
                    Spec: new JsonObject { ["action"] = step.Action, ["is_start_step"] = step.IsStartStep, ["enabled"] = step.Enabled },
                    Notes: string.IsNullOrEmpty(step.ReviewNote) ? null : [step.ReviewNote]));
        }
    }

    private static void AddFiles(List<PlanItem> items, HashSet<string> used, List<ImportFile> files, TargetSnapshot target, string platform)
    {
        foreach (var file in files)
        {
            var present = target.ImportedSourceFileIds.Contains(file.SourceId);
            items.Add(new PlanItem(Unique(used, $"file:{file.SourceId}"), PlanKind.File,
                present ? PlanAction.Skip : PlanAction.Create, ReportCategory.Mapped,
                present ? $"file was already imported from {platform}" : "file is downloaded from the source and uploaded",
                SourceRef: file.SourceId, TargetName: file.FileName,
                Spec: new JsonObject { ["file_name"] = file.FileName, ["is_public"] = file.IsPublic },
                TargetHash: present ? TargetHasher.Hash(file.SourceId) : TargetHasher.Absent));
        }
    }

    private static void AddData(List<PlanItem> items, HashSet<string> used, List<ImportCollection> collections, TargetSnapshot target)
    {
        foreach (var col in collections)
        {
            var key = Unique(used, $"records:{col.Name}");
            var entityKey = Unique(used, $"entity:{col.Name}");
            var existing = target.RecordCounts.GetValueOrDefault(col.Name);
            var spec = new JsonObject { ["existing_target_records"] = existing };

            if (col.DataUnsupportedReason is not null)
                items.Add(new PlanItem(key, PlanKind.RecordBatch, PlanAction.Report, ReportCategory.NotSupported,
                    $"{col.Name}: data not imported — {col.DataUnsupportedReason}",
                    SourceRef: col.Name, TargetName: col.Name, Parent: entityKey, DependsOn: [entityKey]));
            else if (existing > 0)
                items.Add(new PlanItem(key, PlanKind.RecordBatch, PlanAction.Skip, ReportCategory.NeedsReview,
                    $"{col.Name}: data not imported because the project already has {existing} record(s); references to it from other collections were dropped.",
                    SourceRef: col.Name, TargetName: col.Name, Parent: entityKey, DependsOn: [entityKey], Spec: spec,
                    TargetHash: TargetHasher.Hash($"records:{existing}")));
            else
                items.Add(new PlanItem(key, PlanKind.RecordBatch, PlanAction.Create, ReportCategory.Mapped,
                    "records are copied into the empty collection",
                    SourceRef: col.Name, TargetName: col.Name, Parent: entityKey, DependsOn: [entityKey], Spec: spec,
                    TargetHash: TargetHasher.Hash("records:0")));
        }
    }

    private static void AddReports(List<PlanItem> items, ImportSchema schema)
    {
        var warnings = schema.Warnings ?? [];
        for (var i = 0; i < warnings.Count; i++)
            items.Add(new PlanItem($"report:review:{i:D3}", PlanKind.Report, PlanAction.Report, ReportCategory.NeedsReview, warnings[i]));

        var errors = schema.Errors ?? [];
        for (var i = 0; i < errors.Count; i++)
            items.Add(new PlanItem($"report:unsupported:{i:D3}", PlanKind.Report, PlanAction.Report, ReportCategory.NotSupported, errors[i]));
    }
}
