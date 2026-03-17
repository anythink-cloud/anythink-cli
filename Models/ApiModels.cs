using System.Text.Json.Serialization;

namespace AnythinkCli.Models;

public record LoginRequest(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("password")] string Password
);

public record LoginResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("expires_in")] int? ExpiresIn
);

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
    [property: JsonPropertyName("is_unique")] bool IsUnique,
    [property: JsonPropertyName("is_immutable")] bool IsImmutable,
    [property: JsonPropertyName("is_searchable")] bool IsSearchable,
    [property: JsonPropertyName("is_indexed")] bool IsIndexed,
    [property: JsonPropertyName("default_value")] string? DefaultValue,
    [property: JsonPropertyName("locked")] bool Locked
);

public record CreateEntityRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("enable_rls")] bool EnableRls = false,
    [property: JsonPropertyName("is_public")] bool IsPublic = false,
    [property: JsonPropertyName("lock_new_records")] bool LockNewRecords = false,
    [property: JsonPropertyName("is_junction")] bool IsJunction = false
);

public record UpdateEntityRequest(
    [property: JsonPropertyName("enable_rls")] bool EnableRls,
    [property: JsonPropertyName("is_public")] bool IsPublic,
    [property: JsonPropertyName("lock_new_records")] bool LockNewRecords
);

public record CreateFieldRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("database_type")] string DatabaseType,
    [property: JsonPropertyName("display_type")] string DisplayType,
    [property: JsonPropertyName("label")] string? Label = null,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("default_value")] string? DefaultValue = null,
    [property: JsonPropertyName("is_required")] bool IsRequired = false,
    [property: JsonPropertyName("is_unique")] bool IsUnique = false,
    [property: JsonPropertyName("is_searchable")] bool IsSearchable = false,
    [property: JsonPropertyName("is_indexed")] bool IsIndexed = false
);

public record Workflow(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("trigger")] string Trigger,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("steps")] List<WorkflowStep>? Steps
);

public record WorkflowStep(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("is_start_step")] bool IsStartStep,
    [property: JsonPropertyName("on_success_step_id")] int? OnSuccessStepId,
    [property: JsonPropertyName("on_failure_step_id")] int? OnFailureStepId
);

public record CreateWorkflowRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("trigger")] string Trigger,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("options")] object Options
);

// Used internally to avoid C# 'event' keyword conflict in anonymous types
public record EventWorkflowOptions(
    [property: JsonPropertyName("event")] string Event,
    [property: JsonPropertyName("event_entity")] string EventEntity
);

public record ApiKey(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("key_prefix")] string? KeyPrefix,
    [property: JsonPropertyName("created_at")] DateTime? CreatedAt
);

public record PaginatedResult<T>(
    [property: JsonPropertyName("items")] List<T> Items,
    [property: JsonPropertyName("total_count")] int? TotalCount,
    [property: JsonPropertyName("page")] int? Page,
    [property: JsonPropertyName("page_size")] int? PageSize
);

// ── Users ────────────────────────────────────────────────────────────────────

public record UserResponse(
    [property: JsonPropertyName("id")]           int     Id,
    [property: JsonPropertyName("first_name")]   string  FirstName,
    [property: JsonPropertyName("last_name")]    string  LastName,
    [property: JsonPropertyName("email")]        string  Email,
    [property: JsonPropertyName("role_id")]      int?    RoleId,
    [property: JsonPropertyName("role_name")]    string? RoleName,
    [property: JsonPropertyName("is_confirmed")] bool    IsConfirmed,
    [property: JsonPropertyName("created_at")]   DateTime CreatedAt
);

public record CreateUserRequest(
    [property: JsonPropertyName("first_name")]          string  FirstName,
    [property: JsonPropertyName("last_name")]           string  LastName,
    [property: JsonPropertyName("email")]               string  Email,
    [property: JsonPropertyName("role_id")]             int?    RoleId,
    [property: JsonPropertyName("require_confirmation")] bool   RequireConfirmation = true
);

public record UpdateUserRequest(
    [property: JsonPropertyName("first_name")] string  FirstName,
    [property: JsonPropertyName("last_name")]  string  LastName,
    [property: JsonPropertyName("role_id")]    int?    RoleId
);

// ── Files ────────────────────────────────────────────────────────────────────

public record FileResponse(
    [property: JsonPropertyName("id")]                 int      Id,
    [property: JsonPropertyName("original_file_name")] string   OriginalFileName,
    [property: JsonPropertyName("file_name")]          string   FileName,
    [property: JsonPropertyName("file_type")]          string   FileType,
    [property: JsonPropertyName("file_size")]          long     FileSize,
    [property: JsonPropertyName("is_public")]          bool     IsPublic,
    [property: JsonPropertyName("created_at")]         DateTime CreatedAt
);

// ── Roles ────────────────────────────────────────────────────────────────────

public record RoleResponse(
    [property: JsonPropertyName("id")]          int     Id,
    [property: JsonPropertyName("name")]        string  Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("is_active")]   bool    IsActive
);

public record CreateRoleRequest(
    [property: JsonPropertyName("name")]        string  Name,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("is_active")]   bool    IsActive = true
);

// ── Pay ──────────────────────────────────────────────────────────────────────

public record StripeConnectStatus(
    [property: JsonPropertyName("stripe_account_id")]    string? StripeAccountId,
    [property: JsonPropertyName("onboarding_completed")] bool    OnboardingCompleted,
    [property: JsonPropertyName("charges_enabled")]      bool    ChargesEnabled,
    [property: JsonPropertyName("payouts_enabled")]      bool    PayoutsEnabled,
    [property: JsonPropertyName("details_submitted")]    bool    DetailsSubmitted
);

public record CreateStripeConnectRequest(
    [property: JsonPropertyName("business_type")] string  BusinessType,
    [property: JsonPropertyName("country")]       string  Country,
    [property: JsonPropertyName("email")]         string  Email,
    [property: JsonPropertyName("business_name")] string? BusinessName = null
);

public record CreateOnboardingLinkRequest(
    [property: JsonPropertyName("refresh_url")] string RefreshUrl,
    [property: JsonPropertyName("return_url")]  string ReturnUrl
);

public record OnboardingLinkResponse(
    [property: JsonPropertyName("url")] string Url
);

public record PaymentResponse(
    [property: JsonPropertyName("id")]          string   Id,
    [property: JsonPropertyName("amount")]      decimal  Amount,
    [property: JsonPropertyName("currency")]    string   Currency,
    [property: JsonPropertyName("status")]      string   Status,
    [property: JsonPropertyName("description")] string?  Description,
    [property: JsonPropertyName("reference")]   string?  Reference,
    [property: JsonPropertyName("created_at")]  DateTime CreatedAt
);

public record PaymentMethodResponse(
    [property: JsonPropertyName("id")]        string  Id,
    [property: JsonPropertyName("type")]      string  Type,
    [property: JsonPropertyName("brand")]     string? Brand,
    [property: JsonPropertyName("last4")]     string? Last4,
    [property: JsonPropertyName("exp_month")] int?    ExpMonth,
    [property: JsonPropertyName("exp_year")]  int?    ExpYear
);

// ── OAuth / Social Auth ────────────────────────────────────────────────────

public record GoogleOAuthSettings(
    [property: JsonPropertyName("enabled")]       bool    Enabled,
    [property: JsonPropertyName("client_id")]     string? ClientId,
    [property: JsonPropertyName("client_secret")] string? ClientSecret
);

public record UpdateGoogleOAuthRequest(
    [property: JsonPropertyName("enabled")]       bool    Enabled,
    [property: JsonPropertyName("client_id")]     string  ClientId,
    [property: JsonPropertyName("client_secret")] string  ClientSecret
);
