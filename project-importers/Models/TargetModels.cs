using System.Text.Json;
using System.Text.Json.Serialization;

namespace Anythink.ProjectImporters.Models;

public record Entity(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("table_name")] string TableName,
    [property: JsonPropertyName("enable_rls")] bool EnableRls,
    [property: JsonPropertyName("is_system")] bool IsSystem,
    [property: JsonPropertyName("is_junction")] bool IsJunction,
    [property: JsonPropertyName("is_public")] bool IsPublic,
    [property: JsonPropertyName("lock_new_records")] bool LockNewRecords,
    [property: JsonPropertyName("fields")] List<Field>? Fields,
    [property: JsonPropertyName("id")] int? Id
);

public record Field(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("label")] string? Label,
    [property: JsonPropertyName("database_type")] string DatabaseType,
    [property: JsonPropertyName("display_type")] string DisplayType,
    [property: JsonPropertyName("is_required")] bool IsRequired,
    [property: JsonPropertyName("is_name_field")] bool IsNameField,
    [property: JsonPropertyName("is_unique")] bool IsUnique,
    [property: JsonPropertyName("is_immutable")] bool IsImmutable,
    [property: JsonPropertyName("is_searchable")] bool IsSearchable,
    [property: JsonPropertyName("publicly_searchable")] bool PubliclySearchable,
    [property: JsonPropertyName("is_indexed")] bool IsIndexed,
    [property: JsonPropertyName("default_value")] string? DefaultValue,
    [property: JsonPropertyName("locked")] bool Locked,
    [property: JsonPropertyName("relationship")] System.Text.Json.JsonElement? Relationship = null,
    [property: JsonPropertyName("options")] FieldOptionsRequest? Options = null
);

public record FieldOptionsRequest(
    [property: JsonPropertyName("select")] SelectFieldOptions? Select = null
);

public record SelectFieldOptions(
    [property: JsonPropertyName("options")] List<SelectOption>? Options = null,
    [property: JsonPropertyName("multiple")] bool Multiple = false
);

public record SelectOption(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("value")] System.Text.Json.Nodes.JsonNode? Value
);

public record CreateEntityRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("enable_rls")] bool EnableRls = false,
    [property: JsonPropertyName("is_public")] bool IsPublic = false,
    [property: JsonPropertyName("lock_new_records")] bool LockNewRecords = false,
    [property: JsonPropertyName("is_junction")] bool IsJunction = false
);

public record CreateFieldRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("database_type")] string DatabaseType,
    [property: JsonPropertyName("display_type")] string DisplayType,
    [property: JsonPropertyName("label")] string? Label = null,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("default_value")] string? DefaultValue = null,
    [property: JsonPropertyName("is_required")] bool IsRequired = false,
    [property: JsonPropertyName("is_name_field")] bool IsNameField = false,
    [property: JsonPropertyName("is_unique")] bool IsUnique = false,
    [property: JsonPropertyName("is_searchable")] bool IsSearchable = false,
    [property: JsonPropertyName("publicly_searchable")] bool PubliclySearchable = false,
    [property: JsonPropertyName("is_indexed")] bool IsIndexed = false,
    [property: JsonPropertyName("relationship")] System.Text.Json.JsonElement? Relationship = null,
    [property: JsonPropertyName("options")] FieldOptionsRequest? Options = null
);

public record Workflow(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("trigger")] string? Trigger,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("steps")] List<WorkflowStep>? Steps,
    [property: JsonPropertyName("options")] System.Text.Json.JsonElement? Options = null,
    [property: JsonPropertyName("jobs")] List<WorkflowJob>? Jobs = null,
    [property: JsonPropertyName("triggers")] List<WorkflowTrigger>? Triggers = null,
    [property: JsonPropertyName("group")] string? Group = null,
    [property: JsonPropertyName("api_route")] string? ApiRoute = null,
    [property: JsonPropertyName("editor_state")] string? EditorState = null
);

public record WorkflowTrigger(
    [property: JsonPropertyName("id")] int? Id,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("enabled")] bool Enabled = true,
    [property: JsonPropertyName("config")] System.Text.Json.JsonElement? Config = null
);

public record WorkflowTriggerRequest(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("config")] object Config
);

public record WorkflowJob(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("started_at")] string? StartedAt,
    [property: JsonPropertyName("completed_at")] string? CompletedAt,
    [property: JsonPropertyName("error_message")] string? ErrorMessage,
    [property: JsonPropertyName("payload")] System.Text.Json.JsonElement? Payload = null,
    [property: JsonPropertyName("job_steps")] List<WorkflowJobStep>? JobSteps = null
);

public record WorkflowJobStep(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("step_key")] string? StepKey,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("error_message")] string? ErrorMessage,
    [property: JsonPropertyName("log")] string? Log,
    [property: JsonPropertyName("output")] System.Text.Json.JsonElement? Output = null
);

public record WorkflowStep(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("is_start_step")] bool IsStartStep,
    [property: JsonPropertyName("on_success_step_id")] int? OnSuccessStepId,
    [property: JsonPropertyName("on_failure_step_id")] int? OnFailureStepId,
    [property: JsonPropertyName("parameters")] System.Text.Json.JsonElement? Parameters = null
);

public record CreateWorkflowRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("triggers")] List<WorkflowTriggerRequest> Triggers,
    [property: JsonPropertyName("group")] string? Group = null
);

public record CreateWorkflowStepRequest(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("is_start_step")] bool IsStartStep,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("parameters")] System.Text.Json.JsonElement? Parameters = null
);

public record UpdateWorkflowStepRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("parameters")] System.Text.Json.JsonElement? Parameters,
    [property: JsonPropertyName("is_start_step")] bool IsStartStep,
    [property: JsonPropertyName("on_success_step_id")] int? OnSuccessStepId,
    [property: JsonPropertyName("on_failure_step_id")] int? OnFailureStepId
);

public record EventWorkflowOptions(
    [property: JsonPropertyName("event")] string Event,
    [property: JsonPropertyName("event_entity")] string EventEntity,
    [property: JsonPropertyName("filter")] System.Text.Json.JsonElement? Filter = null
);

public record PaginatedResult<T>(
    [property: JsonPropertyName("items")] List<T> Items,
    [property: JsonPropertyName("total_items")] int? TotalCount,
    [property: JsonPropertyName("total_pages")] int? TotalPages,
    [property: JsonPropertyName("has_next_page")] bool HasNextPage,
    [property: JsonPropertyName("page")] int? Page,
    [property: JsonPropertyName("page_size")] int? PageSize
);

public record FileResponse(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("original_file_name")] string OriginalFileName,
    [property: JsonPropertyName("file_name")] string FileName,
    [property: JsonPropertyName("file_type")] string FileType,
    [property: JsonPropertyName("file_size")] long FileSize,
    [property: JsonPropertyName("is_public")] bool IsPublic,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    [property: JsonPropertyName("custom_metadata")] string? CustomMetadata = null
);

public record UpdateFileMetadataRequest(
    [property: JsonPropertyName("custom_metadata")] string CustomMetadata
);

public record Permission(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("entity_id")] int? EntityId,
    [property: JsonPropertyName("is_active")] bool IsActive
);

public record RoleResponse(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("is_active")] bool IsActive,
    [property: JsonPropertyName("anyapi_access")] bool AnyApiAccess = false,
    [property: JsonPropertyName("permissions")] List<Permission>? Permissions = null,
    [property: JsonPropertyName("is_administrator")] bool IsAdministrator = false
);

public record CreateRoleRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("is_active")] bool IsActive = true
);

public record CreatePermissionRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("is_active")] bool IsActive
);

public record UpdateRolePermissionsRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("is_active")] bool IsActive,
    [property: JsonPropertyName("anyapi_access")] bool AnyApiAccess,
    [property: JsonPropertyName("permission_ids")] List<int> PermissionIds
);
