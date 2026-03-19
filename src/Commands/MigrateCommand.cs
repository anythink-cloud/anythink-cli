using AnythinkCli.Client;
using AnythinkCli.Config;
using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AnythinkCli.Commands;

// ── anythink migrate ──────────────────────────────────────────────────────────
//
//  Interactively copies selected resources from one project profile to another.
//  When no --include-* flags are passed the command prompts for what to migrate.
//
//  Usage:
//    anythink migrate --from staging --to prod
//    anythink migrate --from staging --to prod --dry-run
//    anythink migrate --from staging --to prod --include-workflows --include-roles
// ─────────────────────────────────────────────────────────────────────────────

public class MigrateSettings : CommandSettings
{
    [CommandOption("--from <PROFILE>")]
    [Description("Source project profile")]
    public string? From { get; set; }

    [CommandOption("--to <PROFILE>")]
    [Description("Target project profile")]
    public string? To { get; set; }

    [CommandOption("--dry-run")]
    [Description("Preview what would be migrated without making any changes")]
    public bool DryRun { get; set; }

    // ── Optional scope flags (skip interactive prompt when provided) ───────────

    [CommandOption("--include-workflows")]
    [Description("Include workflows")]
    public bool IncludeWorkflows { get; set; }

    [CommandOption("--include-roles")]
    [Description("Include roles and their permissions")]
    public bool IncludeRoles { get; set; }

    [CommandOption("--include-settings")]
    [Description("Include organisation settings (theme, registration settings, URLs)")]
    public bool IncludeSettings { get; set; }

    [CommandOption("--include-menus")]
    [Description("Include menu configuration")]
    public bool IncludeMenus { get; set; }

    [CommandOption("--force-menus")]
    [Description("Delete existing target menus before recreating them (fixes incorrect hrefs from a previous migration)")]
    public bool ForceMenus { get; set; }

    [CommandOption("--include-files")]
    [Description("Include uploaded files")]
    public bool IncludeFiles { get; set; }

    [CommandOption("--include-data")]
    [Description("Include entity records (data). Skips entities that already have records on the target.")]
    public bool IncludeData { get; set; }

    [CommandOption("--force-data")]
    [Description("Re-migrate data even if the target already has records. Implies --include-data.")]
    public bool ForceData { get; set; }

    [CommandOption("--force-data-entities <ENTITIES>")]
    [Description("Comma-separated entity names to force-migrate (e.g. mental_edge_questions,profiles). Implies --force-data.")]
    public string? ForceDataEntities { get; set; }
}

public class MigrateCommand : BaseCommand<MigrateSettings>
{
    // Relationship types that create a DB foreign-key column — must be migrated
    // before one-to-many/many-to-many which reference those columns.
    private static readonly HashSet<string> PrimaryRelTypes =
        new(StringComparer.OrdinalIgnoreCase) { "many-to-one", "one-to-one" };

    // Migration scope keys used for the interactive prompt
    private const string ScopeEntities  = "Entities + Fields";
    private const string ScopeWorkflows = "Workflows";
    private const string ScopeRoles     = "Roles";
    private const string ScopeSettings  = "Organisation Settings";
    private const string ScopeMenus     = "Menu Configuration";
    private const string ScopeFiles     = "Files";
    private const string ScopeData      = "Data (Entity Records)";

    public override async Task<int> ExecuteAsync(CommandContext context, MigrateSettings settings)
    {
        // ── Resolve profiles ──────────────────────────────────────────────────
        var fromKey = settings.From ?? AnsiConsole.Ask<string>("[#F97316]Source profile:[/]");
        var toKey   = settings.To   ?? AnsiConsole.Ask<string>("[#F97316]Target profile:[/]");

        var fromProfile = ConfigService.GetProfile(fromKey);
        var toProfile   = ConfigService.GetProfile(toKey);

        if (fromProfile == null)
        {
            Renderer.Error($"Source profile '{fromKey}' not found.");
            AnsiConsole.MarkupLine("Run [bold #F97316]anythink config show[/] to list saved profiles.");
            return 1;
        }
        if (toProfile == null)
        {
            Renderer.Error($"Target profile '{toKey}' not found.");
            AnsiConsole.MarkupLine("Run [bold #F97316]anythink projects use <id>[/] to save a target profile first.");
            return 1;
        }

        // Refresh expired tokens before starting — saves the new token back to the correct
        // named profile key so subsequent runs don't need to refresh again.
        AnythinkClient srcClient, dstClient;
        try
        {
            srcClient = GetClientForProfile(fromKey);
            dstClient = GetClientForProfile(toKey);
        }
        catch (CliException ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {ex.Message}");
            return 1;
        }

        // ── Determine migration scope (flags or interactive) ──────────────────
        // --force-data / --force-data-entities imply --include-data
        bool anyFlagSet = settings.IncludeWorkflows || settings.IncludeRoles ||
                          settings.IncludeSettings  || settings.IncludeMenus ||
                          settings.IncludeFiles      || settings.IncludeData  ||
                          settings.ForceData         || settings.ForceDataEntities != null;

        HashSet<string> scope;
        if (anyFlagSet)
        {
            scope = new HashSet<string> { ScopeEntities };
            if (settings.IncludeWorkflows) scope.Add(ScopeWorkflows);
            if (settings.IncludeRoles)     scope.Add(ScopeRoles);
            if (settings.IncludeSettings)  scope.Add(ScopeSettings);
            if (settings.IncludeMenus)     scope.Add(ScopeMenus);
            if (settings.IncludeFiles)     scope.Add(ScopeFiles);
            if (settings.IncludeData || settings.ForceData || settings.ForceDataEntities != null) scope.Add(ScopeData);
        }
        else
        {
            var selected = AnsiConsole.Prompt(
                new MultiSelectionPrompt<string>()
                    .Title("\nWhat would you like to migrate?")
                    .PageSize(10)
                    .HighlightStyle(new Style(foreground: new Color(249, 115, 22)))
                    .InstructionsText("[grey](Press [grey]<space>[/] to toggle, [grey]<enter>[/] to confirm)[/]")
                    .AddChoices([ScopeEntities, ScopeWorkflows, ScopeRoles,
                                 ScopeSettings, ScopeMenus, ScopeFiles, ScopeData])
                    .Select(ScopeEntities));
            scope = [.. selected];
        }

        AnsiConsole.WriteLine();
        if (settings.DryRun)
            AnsiConsole.MarkupLine("[yellow]── DRY RUN — no changes will be made ──[/]");

        AnsiConsole.MarkupLine(
            $"[bold]From:[/] [#F97316]{Markup.Escape(fromKey)}[/]  " +
            $"→  [bold]To:[/] [#F97316]{Markup.Escape(toKey)}[/]\n");

        // ── Fetch source data ─────────────────────────────────────────────────
        List<Entity>       srcEntities  = [];
        List<Workflow>     srcWorkflows = [];
        List<RoleResponse> srcRoles     = [];
        List<FileResponse> srcFiles     = [];
        TenantResponse?    srcTenant    = null;
        List<MenuResponse> srcMenus     = [];
        var srcRecordCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
            .StartAsync("Reading source project...", async _ =>
            {
                srcEntities = await srcClient.GetEntitiesAsync();
                if (scope.Contains(ScopeWorkflows)) srcWorkflows = await srcClient.GetWorkflowsAsync();
                // Always fetch roles — needed for menu role-ID remapping even when ScopeRoles not selected
                srcRoles    = await srcClient.GetRolesAsync();
                if (scope.Contains(ScopeFiles) || scope.Contains(ScopeData))
                    srcFiles  = await srcClient.GetAllFilesAsync();
                if (scope.Contains(ScopeSettings)) srcTenant = await srcClient.GetTenantAsync();
                if (scope.Contains(ScopeMenus))    srcMenus  = await srcClient.GetMenusAsync();
                if (scope.Contains(ScopeData))
                {
                    // Count records per entity — paginate fully if TotalCount is unavailable
                    foreach (var e in srcEntities.Where(e => !e.IsSystem))
                        srcRecordCounts[e.Name] = await CountRecordsAsync(srcClient, e.Name);
                }
            });

        var migratable = srcEntities.Where(e => !e.IsSystem).OrderBy(e => e.Name).ToList();

        // Pre-count fields for accurate progress maxValue
        var totalFields = migratable.Sum(e => (e.Fields ?? []).Count(f => !f.Locked));

        // Build source entity ID → name map for relationship remapping
        var srcIdToName = srcEntities
            .Where(e => e.Id.HasValue)
            .ToDictionary(e => e.Id!.Value, e => e.Name);

        // ── Fetch existing target data ────────────────────────────────────────
        List<Entity>       dstEntities    = [];
        List<Workflow>     dstWorkflows   = [];
        List<RoleResponse> dstRoles       = [];
        List<Permission>   dstPermissions = [];
        List<FileResponse> dstFiles       = [];
        TenantResponse?    dstTenant      = null;
        List<MenuResponse> dstMenus       = [];

        await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
            .StartAsync("Reading target project...", async _ =>
            {
                dstEntities = await dstClient.GetEntitiesAsync();
                if (scope.Contains(ScopeWorkflows)) dstWorkflows   = await dstClient.GetWorkflowsAsync();
                // Always fetch target roles — needed for menu + settings role-ID remapping
                dstRoles    = await dstClient.GetRolesAsync();
                if (scope.Contains(ScopeRoles))     dstPermissions = await dstClient.GetPermissionsAsync();
                if (scope.Contains(ScopeFiles) || scope.Contains(ScopeData))
                    dstFiles       = await dstClient.GetAllFilesAsync();
                if (scope.Contains(ScopeSettings))  dstTenant      = await dstClient.GetTenantAsync();
                if (scope.Contains(ScopeMenus))     dstMenus       = await dstClient.GetMenusAsync();
            });

        // Target: entity name → internal ID (populated as entities are created)
        var dstNameToId = dstEntities
            .Where(e => e.Id.HasValue)
            .ToDictionary(e => e.Name, e => e.Id!.Value, StringComparer.OrdinalIgnoreCase);

        // Source role ID → name, target role name → ID (populated as roles are created)
        var srcRoleIdToName = srcRoles.ToDictionary(r => r.Id, r => r.Name);
        var dstRoleNameToId = dstRoles.ToDictionary(r => r.Name, r => r.Id, StringComparer.OrdinalIgnoreCase);

        // ── Progress bars ─────────────────────────────────────────────────────
        // Counters — written by progress tasks, read for summary
        var entitiesCreated  = new Counter(); var entitiesSkipped = new Counter();
        var fieldsCreated    = new Counter(); var fieldsSkipped   = new Counter(); var fieldsFailed = new Counter();
        var workflowsCreated = new Counter();
        var rolesCreated     = new Counter(); var rolesSkipped    = new Counter();
        var settingsDone     = new Counter();
        var menusCreated     = new Counter(); var menuItemsCreated = new Counter();
        var filesCreated     = new Counter(); var filesFailed      = new Counter();
        var recordsCreated   = new Counter(); var recordsSkipped   = new Counter();
        var recordsFailed    = new Counter();
        var dataDetail       = new List<(string Entity, int WouldCreate, int Skipped)>();
        // Populated by Files section; used by Data section for file field remapping
        var fileIdMap        = new Dictionary<int, int>();

        var errors = new List<string>();

        await AnsiConsole.Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Columns([
                new TaskDescriptionColumn { Alignment = Justify.Left },
                new ProgressBarColumn
                {
                    CompletedStyle = new Style(foreground: new Color(249, 115, 22)),
                    FinishedStyle  = new Style(foreground: new Color(249, 115, 22)),
                    RemainingStyle = new Style(foreground: Color.Grey23),
                },
                new PercentageColumn(),
                new SpinnerColumn { Style = new Style(foreground: new Color(249, 115, 22)) },
            ])
            .StartAsync(async ctx =>
            {
                // Add a task for every scope item; hidden tasks stay at 0 and complete immediately
                var entityTask   = scope.Contains(ScopeEntities)  ? ctx.AddTask($"[bold]{ScopeEntities}[/]",  maxValue: Math.Max(1, migratable.Count)) : ctx.AddTask(ScopeEntities,  maxValue: 1);
                var fieldTask    = scope.Contains(ScopeEntities)  ? ctx.AddTask($"[bold]Fields[/]",           maxValue: Math.Max(1, totalFields))       : ctx.AddTask("Fields",        maxValue: 1);
                var wfTask       = scope.Contains(ScopeWorkflows) ? ctx.AddTask($"[bold]{ScopeWorkflows}[/]", maxValue: Math.Max(1, srcWorkflows.Count)) : ctx.AddTask(ScopeWorkflows, maxValue: 1);
                var roleTask     = scope.Contains(ScopeRoles)     ? ctx.AddTask($"[bold]{ScopeRoles}[/]",     maxValue: Math.Max(1, srcRoles.Count(r => !r.Name.Equals("Admin User", StringComparison.OrdinalIgnoreCase)))) : ctx.AddTask(ScopeRoles, maxValue: 1);
                var settingsTask = scope.Contains(ScopeSettings)  ? ctx.AddTask($"[bold]{ScopeSettings}[/]",  maxValue: 1) : ctx.AddTask(ScopeSettings,  maxValue: 1);
                var menuTask     = scope.Contains(ScopeMenus)     ? ctx.AddTask($"[bold]{ScopeMenus}[/]",     maxValue: Math.Max(1, srcMenus.Count))    : ctx.AddTask(ScopeMenus,     maxValue: 1);
                var fileTask     = scope.Contains(ScopeFiles)     ? ctx.AddTask($"[bold]{ScopeFiles}[/]",     maxValue: Math.Max(1, srcFiles.Count))    : ctx.AddTask(ScopeFiles,     maxValue: 1);
                var totalSrcRecords = srcRecordCounts.Values.Sum();
                var dataTask     = scope.Contains(ScopeData)      ? ctx.AddTask($"[bold]{ScopeData}[/]",      maxValue: Math.Max(1, totalSrcRecords))   : ctx.AddTask(ScopeData,      maxValue: 1);

                // Complete tasks that are out of scope immediately
                if (!scope.Contains(ScopeEntities))  { entityTask.Value  = 1; fieldTask.Value    = 1; }
                if (!scope.Contains(ScopeWorkflows))   wfTask.Value       = 1;
                if (!scope.Contains(ScopeRoles))       roleTask.Value     = 1;
                if (!scope.Contains(ScopeSettings))    settingsTask.Value = 1;
                if (!scope.Contains(ScopeMenus))       menuTask.Value     = 1;
                if (!scope.Contains(ScopeFiles))       fileTask.Value     = 1;
                if (!scope.Contains(ScopeData))        dataTask.Value     = 1;

                // ── Entities + Fields ─────────────────────────────────────────
                if (scope.Contains(ScopeEntities))
                {
                    var dstEntityNames = dstEntities
                        .Select(e => e.Name)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    var deferredFields = new List<(Entity entity, Field field, HashSet<string> existingNames)>();

                    foreach (var entity in migratable)
                    {
                        var exists = dstEntityNames.Contains(entity.Name);
                        if (exists)
                        {
                            entitiesSkipped.Value++;
                        }
                        else if (!settings.DryRun)
                        {
                            try
                            {
                                var created = await dstClient.CreateEntityAsync(new CreateEntityRequest(
                                    entity.Name, entity.EnableRls, entity.IsPublic,
                                    entity.LockNewRecords, entity.IsJunction));
                                dstEntityNames.Add(entity.Name);
                                if (created.Id.HasValue)
                                    dstNameToId[entity.Name] = created.Id.Value;
                                entitiesCreated.Value++;
                            }
                            catch (AnythinkException ex)
                            {
                                errors.Add($"Entity '{entity.Name}': {ex.Message}");
                                entityTask.Increment(1);
                                continue;
                            }
                        }
                        else
                        {
                            entitiesCreated.Value++;
                        }

                        entityTask.Increment(1);

                        // ── Fields (pass 1) ───────────────────────────────────
                        var customFields = (entity.Fields ?? []).Where(f => !f.Locked).ToList();
                        var existingFieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                        if (dstEntityNames.Contains(entity.Name))
                        {
                            var dstEnt = dstEntities.FirstOrDefault(e =>
                                e.Name.Equals(entity.Name, StringComparison.OrdinalIgnoreCase));
                            foreach (var f in dstEnt?.Fields ?? [])
                                existingFieldNames.Add(f.Name);
                        }

                        foreach (var field in customFields)
                        {
                            if (existingFieldNames.Contains(field.Name))
                            {
                                fieldsSkipped.Value++;
                                fieldTask.Increment(1);
                                continue;
                            }

                            // Defer one-to-many / many-to-many until FK columns exist
                            if (!PrimaryRelTypes.Contains(field.DatabaseType) && field.Relationship.HasValue)
                            {
                                deferredFields.Add((entity, field, existingFieldNames));
                                continue;
                            }

                            await MigrateField(dstClient, entity.Name, field, settings.DryRun,
                                srcIdToName, dstNameToId, errors,
                                fieldsCreated, fieldsSkipped, fieldsFailed);
                            fieldTask.Increment(1);
                        }
                    }

                    // ── Fields pass 2: deferred one-to-many / many-to-many ────
                    foreach (var (entity, field, existingNames) in deferredFields)
                    {
                        if (existingNames.Contains(field.Name))
                        {
                            fieldsSkipped.Value++;
                            fieldTask.Increment(1);
                            continue;
                        }
                        await MigrateField(dstClient, entity.Name, field, settings.DryRun,
                            srcIdToName, dstNameToId, errors,
                            fieldsCreated, fieldsSkipped, fieldsFailed);
                        fieldTask.Increment(1);
                    }

                    entityTask.Value = entityTask.MaxValue;
                    fieldTask.Value  = fieldTask.MaxValue;
                }

                // ── Workflows ─────────────────────────────────────────────────
                if (scope.Contains(ScopeWorkflows) && srcWorkflows.Count > 0)
                {
                    List<Workflow> srcFull = [];
                    foreach (var wf in srcWorkflows)
                        srcFull.Add(await srcClient.GetWorkflowAsync(wf.Id));

                    var dstWfNames = dstWorkflows
                        .Select(w => w.Name)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    foreach (var wf in srcFull.OrderBy(w => w.Name))
                    {
                        if (dstWfNames.Contains(wf.Name) || settings.DryRun)
                        {
                            workflowsCreated.Value++;
                            wfTask.Increment(1);
                            continue;
                        }

                        Workflow? created = null;
                        try
                        {
                            created = await dstClient.CreateWorkflowAsync(new CreateWorkflowRequest(
                                wf.Name, wf.Description, wf.Trigger, false,
                                wf.Options.HasValue ? (object)wf.Options.Value : new { }));
                        }
                        catch (AnythinkException ex)
                        {
                            errors.Add($"Workflow '{wf.Name}': {ex.Message}");
                            wfTask.Increment(1);
                            continue;
                        }

                        workflowsCreated.Value++;

                        var steps = wf.Steps ?? [];
                        if (steps.Count > 0)
                        {
                            var stepIdMap = new Dictionary<int, int>();
                            var ordered   = steps.OrderByDescending(s => s.IsStartStep).ToList();

                            foreach (var step in ordered)
                            {
                                try
                                {
                                    var cs = await dstClient.AddWorkflowStepAsync(created.Id,
                                        new CreateWorkflowStepRequest(step.Key, step.Name, step.Action,
                                            step.Enabled, step.IsStartStep, step.Description, step.Parameters));
                                    stepIdMap[step.Id] = cs.Id;
                                }
                                catch (AnythinkException ex)
                                {
                                    errors.Add($"Step '{step.Name}' in workflow '{wf.Name}': {ex.Message}");
                                }
                            }

                            foreach (var step in ordered)
                            {
                                if (step.OnSuccessStepId == null && step.OnFailureStepId == null) continue;
                                if (!stepIdMap.TryGetValue(step.Id, out var dstStepId)) continue;
                                int? dstSuccess = step.OnSuccessStepId.HasValue && stepIdMap.TryGetValue(step.OnSuccessStepId.Value, out var s) ? s : null;
                                int? dstFailure = step.OnFailureStepId.HasValue && stepIdMap.TryGetValue(step.OnFailureStepId.Value, out var f) ? f : null;
                                try
                                {
                                    await dstClient.UpdateWorkflowStepAsync(created.Id, dstStepId,
                                        new UpdateWorkflowStepLinksRequest(step.Name, step.Action, dstSuccess, dstFailure));
                                }
                                catch (AnythinkException ex)
                                {
                                    errors.Add($"Step link '{step.Name}': {ex.Message}");
                                }
                            }
                        }

                        wfTask.Increment(1);
                    }

                    wfTask.Value = wfTask.MaxValue;
                }

                // ── Roles ─────────────────────────────────────────────────────
                if (scope.Contains(ScopeRoles))
                {
                    var dstPermsByName = dstPermissions
                        .ToDictionary(p => p.Name, p => p.Id, StringComparer.OrdinalIgnoreCase);

                    var migratableRoles = srcRoles
                        .Where(r => !r.Name.Equals("Admin User", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(r => r.Name)
                        .ToList();

                    foreach (var role in migratableRoles)
                    {
                        var existingDst = dstRoles.FirstOrDefault(r =>
                            r.Name.Equals(role.Name, StringComparison.OrdinalIgnoreCase));

                        int targetRoleId;
                        if (existingDst != null)
                        {
                            targetRoleId = existingDst.Id;
                            rolesSkipped.Value++;
                        }
                        else if (settings.DryRun)
                        {
                            rolesCreated.Value++;
                            roleTask.Increment(1);
                            continue;
                        }
                        else
                        {
                            try
                            {
                                var created = await dstClient.CreateRoleAsync(
                                    new CreateRoleRequest(role.Name, role.Description));
                                targetRoleId = created.Id;
                                dstRoleNameToId[role.Name] = targetRoleId;
                                rolesCreated.Value++;
                            }
                            catch (AnythinkException ex)
                            {
                                errors.Add($"Role '{role.Name}': {ex.Message}");
                                roleTask.Increment(1);
                                continue;
                            }
                        }

                        // Collect existing target role permission IDs (don't remove them)
                        var targetPermIds = (existingDst?.Permissions ?? [])
                            .Select(p => p.Id)
                            .ToHashSet();

                        foreach (var perm in role.Permissions ?? [])
                        {
                            // Remap entity-scoped permissions to use target entity names
                            var permName = perm.Name;
                            if (perm.EntityId.HasValue &&
                                srcIdToName.TryGetValue(perm.EntityId.Value, out var entityName))
                            {
                                var action = perm.Name.Contains(':')
                                    ? perm.Name.Split(':')[1] : perm.Name;
                                permName = $"{entityName}:{action}";
                            }

                            if (dstPermsByName.TryGetValue(permName, out var existingId))
                            {
                                targetPermIds.Add(existingId);
                            }
                            else
                            {
                                try
                                {
                                    var cp = await dstClient.CreatePermissionAsync(
                                        new CreatePermissionRequest(permName, perm.Description, true));
                                    dstPermsByName[permName] = cp.Id;
                                    targetPermIds.Add(cp.Id);
                                }
                                catch (AnythinkException ex)
                                {
                                    errors.Add($"Permission '{permName}': {ex.Message}");
                                }
                            }
                        }

                        try
                        {
                            await dstClient.UpdateRoleWithPermissionsAsync(targetRoleId,
                                new UpdateRolePermissionsRequest(
                                    role.Name, role.Description, role.IsActive,
                                    true, [.. targetPermIds]));
                        }
                        catch (AnythinkException ex)
                        {
                            errors.Add($"Assigning permissions to '{role.Name}': {ex.Message}");
                        }

                        roleTask.Increment(1);
                    }

                    roleTask.Value = roleTask.MaxValue;
                }

                // ── Organisation Settings ─────────────────────────────────────
                if (scope.Contains(ScopeSettings) && srcTenant != null && dstTenant != null)
                {
                    if (!settings.DryRun)
                    {
                        // Remap defaultRoleId: find the role name on source, look it up on target
                        int? remappedDefaultRoleId = null;
                        if (srcTenant.TenantSettings?.DefaultRoleId.HasValue == true)
                        {
                            var roleName = srcRoles
                                .FirstOrDefault(r => r.Id == srcTenant.TenantSettings.DefaultRoleId)?.Name;
                            if (roleName != null && dstRoleNameToId.TryGetValue(roleName, out var dstRoleId))
                                remappedDefaultRoleId = dstRoleId;
                        }

                        var newSettings = srcTenant.TenantSettings == null ? null
                            : new TenantSettingsDto(
                                srcTenant.TenantSettings.AllowRegistrations,
                                remappedDefaultRoleId,
                                srcTenant.TenantSettings.AllowedApplicationUrls,
                                srcTenant.TenantSettings.PaymentSuccessUrl,
                                srcTenant.TenantSettings.PaymentCancelUrl);

                        try
                        {
                            await dstClient.UpdateTenantAsync(new UpdateTenantRequest(
                                dstTenant.Name,          // keep target's own name
                                dstTenant.Description,   // keep target's own description
                                srcTenant.GoogleMapsKey,
                                dstTenant.LogoSquare?.Id,
                                dstTenant.LogoStandard?.Id,
                                newSettings,
                                srcTenant.ThemeSettings));
                            settingsDone.Value = 1;
                        }
                        catch (AnythinkException ex)
                        {
                            errors.Add($"Organisation settings: {ex.Message}");
                        }
                    }
                    else
                    {
                        settingsDone.Value = 1;
                    }

                    settingsTask.Value = settingsTask.MaxValue;
                }

                // ── Menu Configuration ────────────────────────────────────────
                if (scope.Contains(ScopeMenus) && srcMenus.Count > 0)
                {
                    foreach (var menu in srcMenus.OrderBy(m => m.Name))
                    {
                        // Fetch full source menu with nested items
                        var fullSrcMenu = settings.DryRun ? menu
                            : (await srcClient.GetMenuAsync(menu.Id) ?? menu);

                        var existingDstMenu = dstMenus.FirstOrDefault(m =>
                            m.Name.Equals(menu.Name, StringComparison.OrdinalIgnoreCase));

                        if (settings.DryRun)
                        {
                            menusCreated.Value++;
                            menuItemsCreated.Value += CountItems(fullSrcMenu.Items);
                            menuTask.Increment(1);
                            continue;
                        }

                        // --force-menus: delete the existing menu so it is fully recreated
                        if (existingDstMenu != null && settings.ForceMenus)
                        {
                            try
                            {
                                await dstClient.DeleteMenuAsync(existingDstMenu.Id);
                                existingDstMenu = null;
                            }
                            catch (AnythinkException ex)
                            {
                                errors.Add($"Deleting menu '{menu.Name}': {ex.Message}");
                                menuTask.Increment(1);
                                continue;
                            }
                        }

                        int targetMenuId;
                        if (existingDstMenu != null)
                        {
                            // Menu already exists — merge items in rather than skip
                            targetMenuId = existingDstMenu.Id;
                        }
                        else
                        {
                            // Remap roleId to target
                            if (!srcRoleIdToName.TryGetValue(menu.RoleId, out var menuRoleName) ||
                                !dstRoleNameToId.TryGetValue(menuRoleName, out var dstMenuRoleId))
                            {
                                errors.Add($"Menu '{menu.Name}': could not remap role ID {menu.RoleId} — skipped");
                                menuTask.Increment(1);
                                continue;
                            }

                            try
                            {
                                var created = await dstClient.CreateMenuAsync(
                                    new CreateMenuRequest(menu.Name, dstMenuRoleId));
                                targetMenuId = created.Id;
                                menusCreated.Value++;
                            }
                            catch (AnythinkException ex)
                            {
                                errors.Add($"Menu '{menu.Name}': {ex.Message}");
                                menuTask.Increment(1);
                                continue;
                            }
                        }

                        // Fetch current target menu items so we can skip ones that already exist
                        var fullDstMenu = await dstClient.GetMenuAsync(targetMenuId);
                        var existingHrefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        CollectHrefs(fullDstMenu?.Items ?? [], existingHrefs);

                        // Build a displayName → target item ID map for parent remapping
                        var dstItemsByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        FlattenItems(fullDstMenu?.Items ?? [], dstItemsByName);

                        await MergeMenuItems(dstClient, targetMenuId, fullSrcMenu.Items, 0,
                            existingHrefs, dstItemsByName, errors, menuItemsCreated,
                            srcClient.OrgId, dstClient.OrgId);

                        menuTask.Increment(1);
                    }

                    menuTask.Value = menuTask.MaxValue;
                }

                // ── Files ─────────────────────────────────────────────────────
                if (scope.Contains(ScopeFiles) && srcFiles.Count > 0)
                {
                    var dstFileNames = dstFiles
                        .Select(f => f.OriginalFileName)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    var srcBaseUrl = fromProfile.InstanceApiUrl.TrimEnd('/');

                    // fileIdMap is declared outside — populated here for logo remapping + data migration

                    foreach (var file in srcFiles.OrderBy(f => f.OriginalFileName))
                    {
                        if (dstFileNames.Contains(file.OriginalFileName))
                        {
                            fileTask.Increment(1);
                            continue;
                        }

                        if (!settings.DryRun)
                        {
                            try
                            {
                                var fileUrl = $"{srcBaseUrl}/org/{fromProfile.OrgId}/files/{file.Id}/get";
                                var uploaded = await dstClient.UploadFileFromUrlAsync(
                                    fileUrl, file.OriginalFileName, file.IsPublic, fromProfile.AccessToken);
                                fileIdMap[file.Id] = uploaded.Id;
                                filesCreated.Value++;
                            }
                            catch (AnythinkException ex)
                            {
                                errors.Add($"File '{file.OriginalFileName}': {ex.Message}");
                                filesFailed.Value++;
                                fileTask.Increment(1);
                                continue;
                            }
                        }
                        else
                        {
                            filesCreated.Value++;
                        }

                        fileTask.Increment(1);
                    }

                    // ── Link logo files to org settings if both scopes are active ─
                    if (scope.Contains(ScopeSettings) && srcTenant != null && dstTenant != null
                        && !settings.DryRun && (srcTenant.LogoSquare != null || srcTenant.LogoStandard != null))
                    {
                        // Build filename → dst file ID for re-run fallback (file already uploaded).
                        // Use a loop rather than ToDictionary so duplicate filenames don't throw.
                        var dstFilesByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        foreach (var f in dstFiles) dstFilesByName[f.OriginalFileName] = f.Id;

                        int? ResolveLogoId(FileResponse? srcLogo)
                        {
                            if (srcLogo == null) return null;
                            if (fileIdMap.TryGetValue(srcLogo.Id, out var newId)) return newId;
                            if (dstFilesByName.TryGetValue(srcLogo.OriginalFileName, out var existId)) return existId;
                            return null;
                        }

                        int? newSquareId   = ResolveLogoId(srcTenant.LogoSquare);
                        int? newStandardId = ResolveLogoId(srcTenant.LogoStandard);

                        try
                        {
                            var current = await dstClient.GetTenantAsync();
                            if (current != null)
                            {
                                await dstClient.UpdateTenantAsync(new UpdateTenantRequest(
                                    current.Name, current.Description, current.GoogleMapsKey,
                                    newSquareId, newStandardId,
                                    current.TenantSettings, current.ThemeSettings));
                            }
                        }
                        catch (AnythinkException ex)
                        {
                            errors.Add($"Linking logos to org settings: {ex.Message}");
                        }
                    }

                    fileTask.Value = fileTask.MaxValue;
                }

                // ── Data (Entity Records) ─────────────────────────────────────
                if (scope.Contains(ScopeData) && migratable.Count > 0)
                {
                    // Build FK field map: entity name → list of (jsonFieldName, targetEntityName)
                    // Only many-to-one / one-to-one fields create FK columns we need to remap.
                    var fkMap = new Dictionary<string, List<(string FieldName, string TargetEntity)>>(
                        StringComparer.OrdinalIgnoreCase);

                    foreach (var entity in migratable)
                    {
                        var fks = new List<(string, string)>();
                        foreach (var field in entity.Fields ?? [])
                        {
                            if (!PrimaryRelTypes.Contains(field.DatabaseType)) continue;
                            if (!field.Relationship.HasValue) continue;
                            var rel = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                                field.Relationship.Value.GetRawText())!;
                            if (rel.TryGetValue("target_entity_id", out var tEl) &&
                                tEl.ValueKind == JsonValueKind.Number &&
                                srcIdToName.TryGetValue(tEl.GetInt32(), out var targetName))
                                fks.Add((field.Name, targetName));
                        }
                        fkMap[entity.Name] = fks;
                    }

                    // Supplement fileIdMap with dst files that already existed before this run
                    // (the Files section only adds files it uploaded; previously-existing files are missing)
                    if (srcFiles.Count > 0)
                    {
                        var dstFilesByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        foreach (var f in dstFiles) dstFilesByName[f.OriginalFileName] = f.Id;
                        foreach (var srcFile in srcFiles)
                            if (!fileIdMap.ContainsKey(srcFile.Id) &&
                                dstFilesByName.TryGetValue(srcFile.OriginalFileName, out var dstFileId))
                                fileIdMap[srcFile.Id] = dstFileId;
                    }

                    // Build per-entity set of file-type field names
                    var fileFieldNames = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                    foreach (var entity in migratable)
                    {
                        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var field in entity.Fields ?? [])
                            if (field.DatabaseType.Equals("file", StringComparison.OrdinalIgnoreCase) ||
                                field.DisplayType.Equals("file", StringComparison.OrdinalIgnoreCase))
                                names.Add(field.Name);
                        if (names.Count > 0) fileFieldNames[entity.Name] = names;
                    }

                    // Per-entity source ID → destination ID (built as records are created)
                    var dataIdMap = new Dictionary<string, Dictionary<int, int>>(StringComparer.OrdinalIgnoreCase);
                    foreach (var entity in migratable)
                        dataIdMap[entity.Name] = [];

                    // Entities to force-migrate regardless of existing target records.
                    // null = force all; empty set = force none
                    HashSet<string>? forceEntities;
                    if (settings.ForceData)
                        forceEntities = null; // force all
                    else if (!string.IsNullOrEmpty(settings.ForceDataEntities))
                        forceEntities = new HashSet<string>(
                            settings.ForceDataEntities.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                            StringComparer.OrdinalIgnoreCase);
                    else
                        forceEntities = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // force none

                    // Non-junction entities first so FK maps are populated before junction records
                    var dataOrder = migratable
                        .OrderBy(e => e.IsJunction ? 1 : 0)
                        .ThenBy(e => e.Name)
                        .ToList();

                    foreach (var entity in dataOrder)
                    {
                        var srcCount = srcRecordCounts.GetValueOrDefault(entity.Name, 0);
                        if (srcCount == 0) continue;

                        if (settings.DryRun)
                        {
                            // Dry run: use pre-fetched counts, no pagination needed
                            recordsCreated.Value += srcCount;
                            dataDetail.Add((entity.Name, srcCount, 0));
                            dataTask.Increment(srcCount);
                            continue;
                        }

                        // Check if destination already has records for this entity
                        // Wrap in try-catch: Anythink JOINs anythink_files even on SELECT
                        // and that table may not exist yet on a new project.
                        int dstCount;
                        try   { dstCount = await CountRecordsAsync(dstClient, entity.Name); }
                        catch { dstCount = 0; } // table missing — assume empty, proceed

                        var isForced = forceEntities == null || forceEntities.Contains(entity.Name);
                        if (dstCount > 0 && !isForced)
                        {
                            recordsSkipped.Value += srcCount;
                            dataDetail.Add((entity.Name, 0, srcCount));
                            dataTask.Increment(srcCount);
                            continue;
                        }

                        const int pageSize = 100;
                        var page = 1;
                        var entityCreated = 0;

                        while (true)
                        {
                            PaginatedResult<JsonObject> result;
                            try   { result = await srcClient.ListItemsAsync(entity.Name, page, pageSize); }
                            catch (AnythinkException ex)
                            {
                                errors.Add($"{entity.Name} page {page}: {ex.Message}");
                                break;
                            }

                            foreach (var item in result.Items)
                            {
                                var srcIdNode = item["id"];
                                if (srcIdNode == null) { dataTask.Increment(1); continue; }
                                var srcId = srcIdNode.GetValue<int>();

                                // Build payload — strip server-managed fields
                                var payload = new JsonObject();
                                foreach (var prop in item)
                                {
                                    if (prop.Key is "id" or "tenant_id") continue;
                                    payload[prop.Key] = prop.Value?.DeepClone();
                                }

                                // Remap FK fields using maps built so far
                                foreach (var (fieldName, targetEntity) in fkMap[entity.Name])
                                {
                                    if (payload[fieldName] is not JsonNode fkNode) continue;

                                    int srcFkId;
                                    if (fkNode is JsonValue fkVal && fkVal.TryGetValue<int>(out var directId))
                                        srcFkId = directId;
                                    else if (fkNode is JsonObject fkObj &&
                                             fkObj["id"] is JsonValue idVal &&
                                             idVal.TryGetValue<int>(out var objId))
                                        srcFkId = objId;
                                    else continue;

                                    if (dataIdMap.TryGetValue(targetEntity, out var targetMap) &&
                                        targetMap.TryGetValue(srcFkId, out var dstFkId))
                                        payload[fieldName] = JsonValue.Create(dstFkId);
                                    else
                                        payload.Remove(fieldName); // can't remap — omit rather than send a stale ID
                                }

                                // Remap file fields: source file IDs → destination file IDs
                                if (fileFieldNames.TryGetValue(entity.Name, out var fileFields))
                                {
                                    foreach (var ffName in fileFields)
                                    {
                                        if (!payload.ContainsKey(ffName)) continue;
                                        var ffNode = payload[ffName];
                                        if (ffNode == null) continue;

                                        if (ffNode is JsonValue ffVal && ffVal.TryGetValue<int>(out var singleFileId))
                                        {
                                            if (fileIdMap.TryGetValue(singleFileId, out var dstSingle))
                                                payload[ffName] = JsonValue.Create(dstSingle);
                                            else
                                                payload.Remove(ffName);
                                        }
                                        else if (ffNode is JsonArray ffArr)
                                        {
                                            var remapped = new JsonArray();
                                            foreach (var el in ffArr)
                                                if (el is JsonValue elv && elv.TryGetValue<int>(out var arrFileId) &&
                                                    fileIdMap.TryGetValue(arrFileId, out var dstArrId))
                                                    remapped.Add(JsonValue.Create(dstArrId));
                                            payload[ffName] = remapped;
                                        }
                                        else if (ffNode is JsonObject ffObj &&
                                                 ffObj["id"] is JsonValue objIdV &&
                                                 objIdV.TryGetValue<int>(out var objFileId))
                                        {
                                            if (fileIdMap.TryGetValue(objFileId, out var dstObjId))
                                                payload[ffName] = JsonValue.Create(dstObjId);
                                            else
                                                payload.Remove(ffName);
                                        }
                                    }
                                }

                                try
                                {
                                    var created = await dstClient.CreateItemAsync(entity.Name, payload);
                                    if (created["id"]?.GetValue<int>() is int dstId)
                                        dataIdMap[entity.Name][srcId] = dstId;
                                    recordsCreated.Value++;
                                    entityCreated++;
                                }
                                catch (AnythinkException ex) when (ex.Message.Contains("anythink_files"))
                                {
                                    // Strip all detected file fields by name
                                    if (fileFieldNames.TryGetValue(entity.Name, out var ffStrip))
                                        foreach (var ffn in ffStrip) payload.Remove(ffn);

                                    // Deep-clean all remaining values: recursively strip embedded file
                                    // objects (Anythink scans JSON columns for file refs internally)
                                    foreach (var key in payload.Select(p => p.Key).ToList())
                                        payload[key] = StripEmbeddedFileRefs(payload[key]);

                                    try
                                    {
                                        var created = await dstClient.CreateItemAsync(entity.Name, payload);
                                        if (created["id"]?.GetValue<int>() is int dstId2)
                                            dataIdMap[entity.Name][srcId] = dstId2;
                                        recordsCreated.Value++;
                                        entityCreated++;
                                    }
                                    catch (AnythinkException ex2) when (ex2.Message.Contains("anythink_files"))
                                    {
                                        errors.Add($"{entity.Name}#{srcId}: target schema missing 'anythink_files' table — " +
                                            "go to the target project in Anythink, create and save a record with a file field attached, " +
                                            "then re-run with --force-data-entities " + entity.Name);
                                        recordsFailed.Value++;
                                    }
                                    catch (AnythinkException ex2)
                                    {
                                        errors.Add($"{entity.Name}#{srcId}: {ex2.Message}");
                                        recordsFailed.Value++;
                                    }
                                }
                                catch (AnythinkException ex)
                                {
                                    errors.Add($"{entity.Name}#{srcId}: {ex.Message}");
                                    recordsFailed.Value++;
                                }

                                dataTask.Increment(1);
                            }

                            if (result.Items.Count < pageSize) break;
                            page++;
                        }

                        dataDetail.Add((entity.Name, entityCreated, 0));
                    }

                    dataTask.Value = dataTask.MaxValue;
                }
            });

        // ── Summary ───────────────────────────────────────────────────────────
        AnsiConsole.WriteLine();
        Renderer.Header("Summary");

        if (settings.DryRun)
            AnsiConsole.MarkupLine("[yellow]DRY RUN — no changes were made.[/]\n");

        if (scope.Contains(ScopeEntities))
        {
            AnsiConsole.MarkupLine($"  Entities              [green]+{entitiesCreated.Value}[/]  skipped [dim]{entitiesSkipped.Value}[/]");
            AnsiConsole.MarkupLine($"  Fields                [green]+{fieldsCreated.Value}[/]  skipped [dim]{fieldsSkipped.Value}[/]" +
                                   (fieldsFailed.Value > 0 ? $"  [red]failed {fieldsFailed.Value}[/]" : ""));
        }
        if (scope.Contains(ScopeWorkflows))
            AnsiConsole.MarkupLine($"  Workflows             [green]+{workflowsCreated.Value}[/]");
        if (scope.Contains(ScopeRoles))
            AnsiConsole.MarkupLine($"  Roles                 [green]+{rolesCreated.Value}[/]  skipped [dim]{rolesSkipped.Value}[/]");
        if (scope.Contains(ScopeSettings))
            AnsiConsole.MarkupLine($"  Organisation Settings [green]{(settingsDone.Value > 0 ? "applied" : "skipped")}[/]");
        if (scope.Contains(ScopeMenus))
            AnsiConsole.MarkupLine($"  Menus                 [green]+{menusCreated.Value}[/]  items [green]+{menuItemsCreated.Value}[/]");
        if (scope.Contains(ScopeFiles))
            AnsiConsole.MarkupLine($"  Files                 [green]+{filesCreated.Value}[/]" +
                                   (filesFailed.Value > 0 ? $"  [red]failed {filesFailed.Value}[/]" : ""));
        if (scope.Contains(ScopeData))
        {
            var verb = settings.DryRun ? "would create" : "created";
            AnsiConsole.MarkupLine($"  Records               [green]+{recordsCreated.Value}[/]  skipped [dim]{recordsSkipped.Value}[/]" +
                                   (recordsFailed.Value > 0 ? $"  [red]failed {recordsFailed.Value}[/]" : ""));
            foreach (var (entity, wouldCreate, skipped) in dataDetail.OrderBy(d => d.Entity))
            {
                if (wouldCreate > 0)
                    AnsiConsole.MarkupLine($"    [dim]·[/] {Markup.Escape(entity),-30} {verb} [green]{wouldCreate}[/]");
                else if (skipped > 0)
                    AnsiConsole.MarkupLine($"    [dim]·[/] {Markup.Escape(entity),-30} [dim]skipped — already has data[/]");
            }
        }

        if (errors.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[red]{errors.Count} error(s):[/]");
            foreach (var e in errors)
                AnsiConsole.MarkupLine($"  [dim]·[/] {Markup.Escape(e)}");
        }

        AnsiConsole.WriteLine();
        bool anyCreated = entitiesCreated.Value + fieldsCreated.Value + workflowsCreated.Value +
                          rolesCreated.Value + settingsDone.Value + menusCreated.Value +
                          filesCreated.Value + recordsCreated.Value > 0;
        if (anyCreated || settings.DryRun)
            Renderer.Success(settings.DryRun ? "Dry run complete." : "Migration complete.");
        else
            Renderer.Info("Nothing new — target is already up to date.");

        return (fieldsFailed.Value > 0 || filesFailed.Value > 0 || recordsFailed.Value > 0 || errors.Count > 0) ? 2 : 0;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class Counter { public int Value; }

    /// <summary>
    /// Returns the total record count for an entity.
    /// Uses TotalCount from the first page if available; otherwise paginates to count.
    /// </summary>
    private static async Task<int> CountRecordsAsync(AnythinkClient client, string entityName)
    {
        var first = await client.ListItemsAsync(entityName, 1, 100);
        if (first.TotalCount.HasValue) return first.TotalCount.Value;

        // TotalCount not in response — paginate to count
        var total = first.Items.Count;
        var page  = 2;
        while (first.Items.Count == 100)
        {
            var next = await client.ListItemsAsync(entityName, page++, 100);
            total += next.Items.Count;
            first  = next;
        }
        return total;
    }

    private static async Task MigrateField(
        AnythinkClient client, string entityName, Field field, bool dryRun,
        Dictionary<int, string> srcIdToName, Dictionary<string, int> dstNameToId,
        List<string> errors, Counter cCreated, Counter cSkipped, Counter cFailed)
    {
        if (!dryRun)
        {
            try
            {
                var remapped = RemapRelationship(field.Relationship, srcIdToName, dstNameToId);
                await client.AddFieldAsync(entityName, new CreateFieldRequest(
                    field.Name, field.DatabaseType, field.DisplayType, field.Label,
                    null, field.DefaultValue, field.IsRequired, field.IsNameField, field.IsUnique,
                    field.IsSearchable, field.PubliclySearchable, field.IsIndexed, remapped));
            }
            catch (AnythinkException ex)
            {
                errors.Add($"Field '{entityName}.{field.Name}': {ex.Message}");
                cFailed.Value++;
                return;
            }
        }
        cCreated.Value++;
    }

    /// <summary>
    /// Remaps the org ID in an href from source to destination.
    /// "/org/54925003/entities/categories" → "/org/37523255/entities/categories"
    /// Hrefs without an org prefix are returned unchanged.
    /// </summary>
    private static string RemapHref(string href, string srcOrgId, string dstOrgId) =>
        href.Replace($"/org/{srcOrgId}/", $"/org/{dstOrgId}/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Collects all hrefs (including nested children) into a flat set.</summary>
    private static void CollectHrefs(List<MenuItemResponse> items, HashSet<string> hrefs)
    {
        foreach (var item in items)
        {
            if (!string.IsNullOrEmpty(item.Href)) hrefs.Add(item.Href);
            if (item.Items.Count > 0) CollectHrefs(item.Items, hrefs);
        }
    }

    /// <summary>Builds a displayName → dst item ID map (flat, for parent-ID remapping).</summary>
    private static void FlattenItems(List<MenuItemResponse> items, Dictionary<string, int> nameToId)
    {
        foreach (var item in items)
        {
            nameToId.TryAdd(item.DisplayName, item.Id);
            if (item.Items.Count > 0) FlattenItems(item.Items, nameToId);
        }
    }

    /// <summary>
    /// Recursively merges source menu items into the target menu, skipping any that already
    /// exist (matched by href). Items with no href are always created.
    /// <paramref name="dstParentId"/> is the destination parent item ID (0 = top-level).
    /// </summary>
    private static async Task MergeMenuItems(
        AnythinkClient client, int menuId,
        List<MenuItemResponse> items, int dstParentId,
        HashSet<string> existingHrefs,
        Dictionary<string, int> dstItemsByName,
        List<string> errors, Counter count,
        string srcOrgId, string dstOrgId)
    {
        foreach (var item in items.OrderBy(i => i.SortOrder))
        {
            int dstItemId;
            var remappedHref = string.IsNullOrEmpty(item.Href)
                ? item.Href
                : RemapHref(item.Href, srcOrgId, dstOrgId);

            if (!string.IsNullOrEmpty(remappedHref) && existingHrefs.Contains(remappedHref))
            {
                // Item already exists — resolve dst ID so children can use it as parent
                dstItemsByName.TryGetValue(item.DisplayName, out dstItemId);
            }
            else
            {
                try
                {
                    var created = await client.CreateMenuItemAsync(menuId,
                        new CreateMenuItemRequest(item.DisplayName, item.Icon, remappedHref, dstParentId));
                    dstItemId = created.Id;
                    dstItemsByName.TryAdd(item.DisplayName, dstItemId);
                    if (!string.IsNullOrEmpty(remappedHref)) existingHrefs.Add(remappedHref);
                    count.Value++;
                }
                catch (AnythinkException ex)
                {
                    errors.Add($"Menu item '{item.DisplayName}': {ex.Message}");
                    continue;
                }
            }

            if (item.Items.Count > 0 && dstItemId != 0)
                await MergeMenuItems(client, menuId, item.Items, dstItemId,
                    existingHrefs, dstItemsByName, errors, count, srcOrgId, dstOrgId);
        }
    }

    /// <summary>
    /// Recursively removes file-like objects from a JSON value.
    /// An object is considered a file reference if it contains "original_file_name", "file_name",
    /// or "file_type". Null is returned for such objects so callers can omit the field entirely.
    /// Arrays have file-like elements removed. All other values are passed through unchanged.
    /// </summary>
    private static JsonNode? StripEmbeddedFileRefs(JsonNode? node)
    {
        if (node == null) return null;
        if (node is JsonObject obj)
        {
            if (obj.ContainsKey("original_file_name") || obj.ContainsKey("file_name") || obj.ContainsKey("file_type"))
                return null; // this object IS a file — remove it
            var cleaned = new JsonObject();
            foreach (var (k, v) in obj)
            {
                var cv = StripEmbeddedFileRefs(v);
                cleaned[k] = cv;
            }
            return cleaned;
        }
        if (node is JsonArray arr)
        {
            var cleaned = new JsonArray();
            foreach (var item in arr)
            {
                var cv = StripEmbeddedFileRefs(item);
                if (cv != null) cleaned.Add(cv);
            }
            return cleaned;
        }
        return node.DeepClone();
    }

    private static int CountItems(List<MenuItemResponse> items) =>
        items.Sum(i => 1 + CountItems(i.Items));

    private static JsonElement? RemapRelationship(
        JsonElement? rel,
        Dictionary<int, string> srcIdToName,
        Dictionary<string, int> dstNameToId)
    {
        if (!rel.HasValue || rel.Value.ValueKind == JsonValueKind.Null) return rel;

        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(rel.Value.GetRawText())!;

        RemapEntityId(dict, "source_entity_id", srcIdToName, dstNameToId);
        RemapEntityId(dict, "target_entity_id", srcIdToName, dstNameToId);
        RemapEntityId(dict, "junction_entity_id", srcIdToName, dstNameToId);

        foreach (var key in new[] { "id", "tenant_id", "locked", "created_at", "created_by", "updated_at", "updated_by" })
            dict.Remove(key);

        return JsonSerializer.SerializeToElement(dict);
    }

    private static void RemapEntityId(
        Dictionary<string, JsonElement> dict, string key,
        Dictionary<int, string> srcIdToName, Dictionary<string, int> dstNameToId)
    {
        if (!dict.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.Number) return;
        if (!srcIdToName.TryGetValue(el.GetInt32(), out var name)) return;
        if (!dstNameToId.TryGetValue(name, out var dstId)) return;
        dict[key] = JsonSerializer.SerializeToElement(dstId);
    }
}
