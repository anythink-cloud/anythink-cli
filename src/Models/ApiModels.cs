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
    [property: JsonPropertyName("is_name_field")] bool IsNameField,
    [property: JsonPropertyName("is_unique")] bool IsUnique,
    [property: JsonPropertyName("is_immutable")] bool IsImmutable,
    [property: JsonPropertyName("is_searchable")] bool IsSearchable,
    [property: JsonPropertyName("publicly_searchable")] bool PubliclySearchable,
    [property: JsonPropertyName("is_indexed")] bool IsIndexed,
    [property: JsonPropertyName("default_value")] string? DefaultValue,
    [property: JsonPropertyName("locked")] bool Locked,
    [property: JsonPropertyName("relationship")] System.Text.Json.JsonElement? Relationship = null
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
    [property: JsonPropertyName("is_name_field")] bool IsNameField = false,
    [property: JsonPropertyName("is_unique")] bool IsUnique = false,
    [property: JsonPropertyName("is_searchable")] bool IsSearchable = false,
    [property: JsonPropertyName("publicly_searchable")] bool PubliclySearchable = false,
    [property: JsonPropertyName("is_indexed")] bool IsIndexed = false,
    [property: JsonPropertyName("relationship")] System.Text.Json.JsonElement? Relationship = null
);

public record UpdateFieldRequest(
    [property: JsonPropertyName("display_type")] string DisplayType,
    [property: JsonPropertyName("label")] string? Label = null,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("default_value")] string? DefaultValue = null,
    [property: JsonPropertyName("is_required")] bool IsRequired = false,
    [property: JsonPropertyName("is_searchable")] bool IsSearchable = false,
    [property: JsonPropertyName("publicly_searchable")] bool PubliclySearchable = false,
    [property: JsonPropertyName("is_indexed")] bool IsIndexed = false
);

public record Workflow(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("trigger")] string Trigger,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("steps")] List<WorkflowStep>? Steps,
    [property: JsonPropertyName("options")] System.Text.Json.JsonElement? Options = null,
    [property: JsonPropertyName("jobs")] List<WorkflowJob>? Jobs = null
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
    [property: JsonPropertyName("trigger")] string Trigger,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("options")] object Options,
    [property: JsonPropertyName("api_route")] string? ApiRoute = null
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

public record UpdateWorkflowRequest(
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("description")] string? Description = null
);

public record UpdateWorkflowStepLinksRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("on_success_step_id")] int? OnSuccessStepId,
    [property: JsonPropertyName("on_failure_step_id")] int? OnFailureStepId
);

// Used internally to avoid C# 'event' keyword conflict in anonymous types
public record EventWorkflowOptions(
    [property: JsonPropertyName("event")] string Event,
    [property: JsonPropertyName("event_entity")] string EventEntity
);

// ── API Keys ─────────────────────────────────────────────────────────────────

public record ApiKeyResponse(
    [property: JsonPropertyName("id")]          int              Id,
    [property: JsonPropertyName("user_id")]     int              UserId,
    [property: JsonPropertyName("name")]        string           Name,
    [property: JsonPropertyName("key")]         string           Key,
    [property: JsonPropertyName("expires_at")]  DateTime         ExpiresAt,
    [property: JsonPropertyName("revoked")]     bool             Revoked,
    [property: JsonPropertyName("permissions")] List<Permission> Permissions,
    [property: JsonPropertyName("created_at")]  DateTime?        CreatedAt = null
);

public record CreateApiKeyRequest(
    [property: JsonPropertyName("name")]            string    Name,
    [property: JsonPropertyName("expiresInDays")]   int       ExpiresInDays,
    [property: JsonPropertyName("permissionIds")]   List<int> PermissionIds
);

public record PaginatedResult<T>(
    [property: JsonPropertyName("items")] List<T> Items,
    [property: JsonPropertyName("total_items")] int? TotalCount,
    [property: JsonPropertyName("total_pages")] int? TotalPages,
    [property: JsonPropertyName("has_next_page")] bool HasNextPage,
    [property: JsonPropertyName("page")] int? Page,
    [property: JsonPropertyName("page_size")] int? PageSize
);

// ── Search ───────────────────────────────────────────────────────────────────

public record SearchResult(
    [property: JsonPropertyName("items")]               List<System.Text.Json.Nodes.JsonObject> Items,
    [property: JsonPropertyName("page")]                int                                     Page,
    [property: JsonPropertyName("page_size")]           int                                     PageSize,
    [property: JsonPropertyName("total_items")]         int                                     TotalItems,
    [property: JsonPropertyName("total_pages")]         int                                     TotalPages,
    [property: JsonPropertyName("has_next_page")]       bool                                    HasNextPage,
    [property: JsonPropertyName("has_previous_page")]   bool                                    HasPreviousPage,
    [property: JsonPropertyName("retrieval_time")]      int?                                    RetrievalTime,
    [property: JsonPropertyName("facet_distribution")]  System.Text.Json.JsonElement?           FacetDistribution
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

public record Permission(
    [property: JsonPropertyName("id")]          int     Id,
    [property: JsonPropertyName("name")]        string  Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("entity_id")]   int?    EntityId,
    [property: JsonPropertyName("is_active")]   bool    IsActive
);

public record RoleResponse(
    [property: JsonPropertyName("id")]               int              Id,
    [property: JsonPropertyName("name")]             string           Name,
    [property: JsonPropertyName("description")]      string?          Description,
    [property: JsonPropertyName("is_active")]        bool             IsActive,
    [property: JsonPropertyName("anyapi_access")]    bool             AnyApiAccess = false,
    [property: JsonPropertyName("permissions")]      List<Permission>? Permissions = null
);

public record CreateRoleRequest(
    [property: JsonPropertyName("name")]        string  Name,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("is_active")]   bool    IsActive = true
);

public record CreatePermissionRequest(
    [property: JsonPropertyName("name")]        string  Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("is_active")]   bool    IsActive
);

public record UpdateRolePermissionsRequest(
    [property: JsonPropertyName("name")]            string    Name,
    [property: JsonPropertyName("description")]     string?   Description,
    [property: JsonPropertyName("is_active")]       bool      IsActive,
    [property: JsonPropertyName("anyapi_access")]   bool      AnyApiAccess,
    [property: JsonPropertyName("permission_ids")]  List<int> PermissionIds
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

// ── Secrets ──────────────────────────────────────────────────────────────────

public record SecretUserResponse(
    [property: JsonPropertyName("user_id")]    int    UserId,
    [property: JsonPropertyName("first_name")] string FirstName,
    [property: JsonPropertyName("last_name")]  string LastName,
    [property: JsonPropertyName("email")]      string Email,
    [property: JsonPropertyName("readonly")]   bool   Readonly
);

public record SecretResponse(
    [property: JsonPropertyName("id")]         int                    Id,
    [property: JsonPropertyName("key")]        string                 Key,
    [property: JsonPropertyName("created_at")] DateTime               CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTime               UpdatedAt,
    [property: JsonPropertyName("users")]      List<SecretUserResponse> Users
);

public record CreateSecretRequest(
    [property: JsonPropertyName("key")]      string    Key,
    [property: JsonPropertyName("value")]    string    Value,
    [property: JsonPropertyName("user_ids")] List<int>? UserIds = null
);

public record UpdateSecretRequest(
    [property: JsonPropertyName("value")]    string    Value,
    [property: JsonPropertyName("user_ids")] List<int>? UserIds = null
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

// ── Menu ─────────────────────────────────────────────────────────────────────

public record MenuResponse(
    [property: JsonPropertyName("id")]      int                    Id,
    [property: JsonPropertyName("name")]    string                 Name,
    [property: JsonPropertyName("role_id")] int                    RoleId,
    [property: JsonPropertyName("items")]   List<MenuItemResponse> Items
);

public record MenuItemResponse(
    [property: JsonPropertyName("id")]           int                    Id,
    [property: JsonPropertyName("menu_id")]      int                    MenuId,
    [property: JsonPropertyName("display_name")] string                 DisplayName,
    [property: JsonPropertyName("icon")]         string                 Icon,
    [property: JsonPropertyName("href")]         string                 Href,
    [property: JsonPropertyName("parent_id")]    int?                   ParentId,
    [property: JsonPropertyName("sort_order")]   int                    SortOrder,
    [property: JsonPropertyName("items")]        List<MenuItemResponse> Items
);

public record CreateMenuRequest(
    [property: JsonPropertyName("name")]    string Name,
    [property: JsonPropertyName("role_id")] int    RoleId
);

public record CreateMenuItemRequest(
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("icon")]         string Icon,
    [property: JsonPropertyName("href")]         string Href,
    [property: JsonPropertyName("parent_id")]    int    ParentId
);

// ── Organisation / Tenant Settings ───────────────────────────────────────────

public record TenantSettingsDto(
    [property: JsonPropertyName("allow_registrations")]    bool          AllowRegistrations,
    [property: JsonPropertyName("default_role_id")]        int?          DefaultRoleId,
    [property: JsonPropertyName("allowed_application_urls")] List<string> AllowedApplicationUrls,
    [property: JsonPropertyName("payment_success_url")]    string?       PaymentSuccessUrl,
    [property: JsonPropertyName("payment_cancel_url")]     string?       PaymentCancelUrl
);

public record ThemeSettingsDto(
    [property: JsonPropertyName("primary_color")] string? PrimaryColor,
    [property: JsonPropertyName("gray_color")]    string? GrayColor
);

public record TenantResponse(
    [property: JsonPropertyName("id")]              int                Id,
    [property: JsonPropertyName("name")]            string             Name,
    [property: JsonPropertyName("description")]     string?            Description,
    [property: JsonPropertyName("tenant_settings")] TenantSettingsDto? TenantSettings,
    [property: JsonPropertyName("theme_settings")]  ThemeSettingsDto?  ThemeSettings,
    [property: JsonPropertyName("logo_square")]     FileResponse?      LogoSquare,
    [property: JsonPropertyName("logo_standard")]   FileResponse?      LogoStandard,
    [property: JsonPropertyName("google_maps_key")] string?            GoogleMapsKey
);

public record UpdateTenantRequest(
    [property: JsonPropertyName("name")]              string             Name,
    [property: JsonPropertyName("description")]       string?            Description,
    [property: JsonPropertyName("google_maps_key")]   string?            GoogleMapsKey,
    [property: JsonPropertyName("logo_square_id")]    int?               LogoSquareId,
    [property: JsonPropertyName("logo_standard_id")]  int?               LogoStandardId,
    [property: JsonPropertyName("tenant_settings")]   TenantSettingsDto? TenantSettings,
    [property: JsonPropertyName("theme_settings")]    ThemeSettingsDto?  ThemeSettings
);
