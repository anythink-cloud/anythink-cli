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
    [property: JsonPropertyName("value")] string Value
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
    [property: JsonPropertyName("relationship")] System.Text.Json.JsonElement? Relationship = null,
    [property: JsonPropertyName("options")] FieldOptionsRequest? Options = null
);

public record UpdateFieldRequest(
    [property: JsonPropertyName("display_type")] string DisplayType,
    [property: JsonPropertyName("label")] string? Label = null,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("default_value")] string? DefaultValue = null,
    [property: JsonPropertyName("is_required")] bool IsRequired = false,
    [property: JsonPropertyName("is_searchable")] bool IsSearchable = false,
    [property: JsonPropertyName("publicly_searchable")] bool PubliclySearchable = false,
    [property: JsonPropertyName("is_indexed")] bool IsIndexed = false,
    [property: JsonPropertyName("options")] FieldOptionsRequest? Options = null
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
    [property: JsonPropertyName("event_entity")] string EventEntity,
    [property: JsonPropertyName("filter")] System.Text.Json.JsonElement? Filter = null
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
    [property: JsonPropertyName("name")]              string    Name,
    [property: JsonPropertyName("expires_in_days")]   int       ExpiresInDays,
    [property: JsonPropertyName("permission_ids")]    List<int> PermissionIds
);

// ── Integrations ─────────────────────────────────────────────────────────────

public record IntegrationOperation(
    [property: JsonPropertyName("key")]           string                          Key,
    [property: JsonPropertyName("display_name")]  string                          DisplayName,
    [property: JsonPropertyName("description")]   string                          Description,
    [property: JsonPropertyName("input_schema")]  System.Text.Json.JsonElement?   InputSchema = null,
    [property: JsonPropertyName("output_schema")] System.Text.Json.JsonElement?   OutputSchema = null
);

public record IntegrationDefinition(
    [property: JsonPropertyName("id")]              string                     Id,
    [property: JsonPropertyName("provider")]        string                     Provider,
    [property: JsonPropertyName("parent_provider")] string?                    ParentProvider,
    [property: JsonPropertyName("display_name")]   string                     DisplayName,
    [property: JsonPropertyName("description")]     string                     Description,
    [property: JsonPropertyName("icon")]            string?                    Icon,
    [property: JsonPropertyName("category")]        string                     Category,
    [property: JsonPropertyName("operations")]      List<IntegrationOperation> Operations,
    [property: JsonPropertyName("auth_type")]      string                     AuthType,
    [property: JsonPropertyName("is_enabled")]     bool                       IsEnabled,
    [property: JsonPropertyName("can_social_sign_in")] bool                   CanSocialSignIn
);

public record IntegrationConnection(
    [property: JsonPropertyName("id")]                          string    Id,
    [property: JsonPropertyName("tenant_id")]                   int       TenantId,
    [property: JsonPropertyName("user_id")]                     int?      UserId,
    [property: JsonPropertyName("integration_definition_id")]   string    IntegrationDefinitionId,
    [property: JsonPropertyName("provider")]                    string?   Provider,
    [property: JsonPropertyName("name")]                        string    Name,
    [property: JsonPropertyName("display_name")]                string?   DisplayName,
    [property: JsonPropertyName("is_enabled")]                  bool      IsEnabled,
    [property: JsonPropertyName("connected_at")]                DateTime  ConnectedAt,
    [property: JsonPropertyName("last_used_at")]                DateTime? LastUsedAt
);

public record CreateApiKeyConnectionRequest(
    [property: JsonPropertyName("integration_definition_id")] string IntegrationDefinitionId,
    [property: JsonPropertyName("name")]                      string Name,
    [property: JsonPropertyName("api_key")]                   string ApiKey,
    [property: JsonPropertyName("is_user_connection")]        bool   IsUserConnection
);

public record UpdateConnectionRequest(
    [property: JsonPropertyName("name")]       string? Name      = null,
    [property: JsonPropertyName("is_enabled")] bool?   IsEnabled = null
);

public record TestConnectionResult(
    [property: JsonPropertyName("success")] bool   Success,
    [property: JsonPropertyName("message")] string Message
);

public record IntegrationOAuthSettings(
    [property: JsonPropertyName("has_client_id")]      bool HasClientId,
    [property: JsonPropertyName("use_social_sign_in")] bool UseSocialSignIn,
    [property: JsonPropertyName("is_enabled")]         bool IsEnabled
);

public record SetOAuthSettingsRequest(
    [property: JsonPropertyName("client_id")]          string? ClientId,
    [property: JsonPropertyName("client_secret")]      string? ClientSecret,
    [property: JsonPropertyName("is_enabled")]         bool?   IsEnabled,
    [property: JsonPropertyName("use_social_sign_in")] bool    UseSocialSignIn = false
);

public record OAuthUrlResponse(
    [property: JsonPropertyName("url")] string Url
);

public record CreateConnectionRequest(
    [property: JsonPropertyName("integration_definition_id")] string  IntegrationDefinitionId,
    [property: JsonPropertyName("name")]                      string  Name,
    [property: JsonPropertyName("auth_code")]                 string  AuthCode,
    [property: JsonPropertyName("redirect_uri")]              string  RedirectUri,
    [property: JsonPropertyName("state")]                     string? State,
    [property: JsonPropertyName("is_user_connection")]        bool    IsUserConnection
);

public record ExecuteIntegrationRequest(
    [property: JsonPropertyName("operation")] string                              Operation,
    [property: JsonPropertyName("inputs")]    Dictionary<string, object>          Inputs
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

// ── Pay › Subscription plans ─────────────────────────────────────────────────

public record SubscriptionPlanResponse(
    [property: JsonPropertyName("id")]                  int      Id,
    [property: JsonPropertyName("plan_name")]           string   PlanName,
    [property: JsonPropertyName("name")]                string   Name,
    [property: JsonPropertyName("description")]         string   Description,
    [property: JsonPropertyName("type")]                string   Type,
    [property: JsonPropertyName("amount")]              decimal  Amount,
    [property: JsonPropertyName("currency")]            string   Currency,
    [property: JsonPropertyName("billing_interval")]    string   BillingInterval,
    [property: JsonPropertyName("interval_count")]      int      IntervalCount,
    [property: JsonPropertyName("trial_period_days")]   int?     TrialPeriodDays,
    [property: JsonPropertyName("product_name")]        string?  ProductName,
    [property: JsonPropertyName("product_description")] string?  ProductDescription,
    [property: JsonPropertyName("reference")]           string?  Reference,
    [property: JsonPropertyName("is_active")]           bool     IsActive,
    [property: JsonPropertyName("apple_product_id")]            string? AppleProductId = null,
    [property: JsonPropertyName("apple_subscription_group_id")] string? AppleSubscriptionGroupId = null
);

public record CreateSubscriptionPlanRequest(
    [property: JsonPropertyName("plan_name")]           string  PlanName,
    [property: JsonPropertyName("name")]                string  Name,
    [property: JsonPropertyName("description")]         string  Description,
    [property: JsonPropertyName("type")]                string  Type,
    [property: JsonPropertyName("amount")]              decimal Amount,
    [property: JsonPropertyName("currency")]            string  Currency,
    [property: JsonPropertyName("billing_interval")]    string  BillingInterval,
    [property: JsonPropertyName("interval_count")]      int     IntervalCount,
    [property: JsonPropertyName("trial_period_days")]   int?    TrialPeriodDays,
    [property: JsonPropertyName("product_name")]        string? ProductName,
    [property: JsonPropertyName("product_description")] string? ProductDescription,
    [property: JsonPropertyName("reference")]           string? Reference,
    [property: JsonPropertyName("is_active")]           bool    IsActive,
    [property: JsonPropertyName("apple_product_id")]            string? AppleProductId = null,
    [property: JsonPropertyName("apple_subscription_group_id")] string? AppleSubscriptionGroupId = null
);

public record UpdateSubscriptionPlanRequest(
    [property: JsonPropertyName("plan_name")]           string  PlanName,
    [property: JsonPropertyName("name")]                string  Name,
    [property: JsonPropertyName("description")]         string  Description,
    [property: JsonPropertyName("type")]                string  Type,
    [property: JsonPropertyName("amount")]              decimal Amount,
    [property: JsonPropertyName("currency")]            string  Currency,
    [property: JsonPropertyName("billing_interval")]    string  BillingInterval,
    [property: JsonPropertyName("interval_count")]      int     IntervalCount,
    [property: JsonPropertyName("trial_period_days")]   int?    TrialPeriodDays,
    [property: JsonPropertyName("product_name")]        string? ProductName,
    [property: JsonPropertyName("product_description")] string? ProductDescription,
    [property: JsonPropertyName("reference")]           string? Reference,
    [property: JsonPropertyName("is_active")]           bool    IsActive,
    [property: JsonPropertyName("apple_product_id")]            string? AppleProductId = null,
    [property: JsonPropertyName("apple_subscription_group_id")] string? AppleSubscriptionGroupId = null
);

// ── Pay › Subscriptions ──────────────────────────────────────────────────────

public record SubscriptionResponse(
    [property: JsonPropertyName("id")]                       Guid      Id,
    [property: JsonPropertyName("type")]                     string    Type,
    [property: JsonPropertyName("name")]                     string    Name,
    [property: JsonPropertyName("description")]              string?   Description,
    [property: JsonPropertyName("amount")]                   decimal   Amount,
    [property: JsonPropertyName("currency")]                 string    Currency,
    [property: JsonPropertyName("billing_interval")]         string    BillingInterval,
    [property: JsonPropertyName("interval_count")]           int       IntervalCount,
    [property: JsonPropertyName("current_period_starts_at")] DateTime? CurrentPeriodStartsAt,
    [property: JsonPropertyName("current_period_ends_at")]   DateTime? CurrentPeriodEndsAt,
    [property: JsonPropertyName("trial_period_days")]        int?      TrialPeriodDays,
    [property: JsonPropertyName("trial_ends_at")]            DateTime? TrialEndsAt,
    [property: JsonPropertyName("status")]                   string    Status,
    [property: JsonPropertyName("auto_cancel_at")]           DateTime? AutoCancelAt,
    [property: JsonPropertyName("cancelled_at")]             DateTime? CancelledAt,
    [property: JsonPropertyName("reference")]                string?   Reference,
    [property: JsonPropertyName("checkout_url")]             string?   CheckoutUrl,
    [property: JsonPropertyName("last_synced_at")]           DateTime? LastSyncedAt,
    [property: JsonPropertyName("created_at")]               DateTime  CreatedAt,
    [property: JsonPropertyName("updated_at")]               DateTime  UpdatedAt,
    [property: JsonPropertyName("customer_email")]           string?   CustomerEmail,
    [property: JsonPropertyName("customer_name")]            string?   CustomerName
);

public record CreateSubscriptionRequest(
    [property: JsonPropertyName("name")]                  string  Name,
    [property: JsonPropertyName("description")]           string  Description,
    [property: JsonPropertyName("type")]                  string  Type,
    [property: JsonPropertyName("amount")]                decimal Amount,
    [property: JsonPropertyName("currency")]              string  Currency,
    [property: JsonPropertyName("billing_interval")]      string  BillingInterval,
    [property: JsonPropertyName("interval_count")]        int     IntervalCount,
    [property: JsonPropertyName("trial_period_days")]     int?    TrialPeriodDays,
    [property: JsonPropertyName("product_name")]          string? ProductName,
    [property: JsonPropertyName("product_description")]   string? ProductDescription,
    [property: JsonPropertyName("reference")]             string? Reference,
    [property: JsonPropertyName("customer_email")]        string  CustomerEmail,
    [property: JsonPropertyName("customer_name")]         string? CustomerName       = null,
    [property: JsonPropertyName("customer_phone")]        string? CustomerPhone      = null,
    [property: JsonPropertyName("subscriber_entity_id")]  long?   SubscriberEntityId = null,
    [property: JsonPropertyName("subscriber_id")]         long?   SubscriberId       = null,
    [property: JsonPropertyName("success_url")]           string? SuccessUrl         = null,
    [property: JsonPropertyName("cancel_url")]            string? CancelUrl          = null
);

public record CreateSubscriptionResponse(
    [property: JsonPropertyName("anythink_pay_id")] Guid    AnythinkPayId,
    [property: JsonPropertyName("checkout_url")]    string? CheckoutUrl,
    [property: JsonPropertyName("session_id")]      string? SessionId
);

public record CheckSubscriptionAccessResponse(
    [property: JsonPropertyName("has_access")]   bool                          HasAccess,
    [property: JsonPropertyName("subscription")] SubscriptionCheckAccessItem? Subscription
);

public record SubscriptionCheckAccessItem(
    [property: JsonPropertyName("id")]     Guid   Id,
    [property: JsonPropertyName("name")]   string Name,
    [property: JsonPropertyName("status")] string Status
);

public record SubscriptionUserResponse(
    [property: JsonPropertyName("user_id")]         int    UserId,
    [property: JsonPropertyName("subscription_id")] Guid   SubscriptionId,
    [property: JsonPropertyName("readonly")]        bool   Readonly
);

public record UpdateSubscriptionUserRequest(
    [property: JsonPropertyName("user_id")]  int  UserId,
    [property: JsonPropertyName("readonly")] bool Readonly
);

// ── Pay › Apple IAP credentials ──────────────────────────────────────────────

public record AppleIapCredentialsResponse(
    [property: JsonPropertyName("bundle_id")]        string? BundleId,
    [property: JsonPropertyName("environment")]      string? Environment,
    [property: JsonPropertyName("asc_issuer_id")]    string? AscIssuerId,
    [property: JsonPropertyName("asc_key_id")]       string? AscKeyId,
    [property: JsonPropertyName("has_private_key")]  bool    HasPrivateKey,
    [property: JsonPropertyName("is_configured")]    bool    IsConfigured,
    [property: JsonPropertyName("notification_url")] string? NotificationUrl
);

// A null asc_private_key_pem leaves the stored key untouched; an empty string clears it.
public record UpdateAppleIapCredentialsRequest(
    [property: JsonPropertyName("bundle_id")]          string? BundleId          = null,
    [property: JsonPropertyName("environment")]        string? Environment        = null,
    [property: JsonPropertyName("asc_issuer_id")]      string? AscIssuerId        = null,
    [property: JsonPropertyName("asc_key_id")]         string? AscKeyId           = null,
    [property: JsonPropertyName("asc_private_key_pem")] string? AscPrivateKeyPem  = null
);

// ── Pay › Entitlement ────────────────────────────────────────────────────────

public record SubscriptionEntitlementResponse(
    [property: JsonPropertyName("has_access")]            bool      HasAccess,
    [property: JsonPropertyName("subscription_id")]       Guid?     SubscriptionId,
    [property: JsonPropertyName("provider")]              string?   Provider,
    [property: JsonPropertyName("product_id")]            string?   ProductId,
    [property: JsonPropertyName("status")]                string?   Status,
    [property: JsonPropertyName("expires_at")]            DateTime? ExpiresAt,
    [property: JsonPropertyName("auto_cancel_at")]        DateTime? AutoCancelAt,
    [property: JsonPropertyName("cancelled_at")]          DateTime? CancelledAt,
    [property: JsonPropertyName("is_trial")]              bool      IsTrial,
    [property: JsonPropertyName("trial_ends_at")]         DateTime? TrialEndsAt,
    [property: JsonPropertyName("trial_days_remaining")]  int?      TrialDaysRemaining
);

// ── Pay › Apple receipt verify ───────────────────────────────────────────────

public record AppleVerifyRequest(
    [property: JsonPropertyName("signed_transaction")]      string? SignedTransaction     = null,
    [property: JsonPropertyName("original_transaction_id")] string? OriginalTransactionId = null
);

public record AppleVerifyResponse(
    [property: JsonPropertyName("subscription_id")]    Guid      SubscriptionId,
    [property: JsonPropertyName("bound_user_id")]      int       BoundUserId,
    [property: JsonPropertyName("provider")]           string?   Provider,
    [property: JsonPropertyName("product_id")]         string?   ProductId,
    [property: JsonPropertyName("plan_name")]          string?   PlanName,
    [property: JsonPropertyName("plan_id")]            int?      PlanId,
    [property: JsonPropertyName("status")]             string?   Status,
    [property: JsonPropertyName("expires_at")]         DateTime? ExpiresAt,
    [property: JsonPropertyName("has_access")]         bool      HasAccess,
    [property: JsonPropertyName("matched_known_plan")] bool      MatchedKnownPlan
);

// ── Pay › Subscription history + admin recovery ──────────────────────────────

public record SubscriptionEventResponse(
    [property: JsonPropertyName("id")]            Guid     Id,
    [property: JsonPropertyName("event_type")]    string   EventType,
    [property: JsonPropertyName("source")]        string?  Source,
    [property: JsonPropertyName("prior_status")]  string?  PriorStatus,
    [property: JsonPropertyName("new_status")]    string?  NewStatus,
    [property: JsonPropertyName("summary")]       string?  Summary,
    [property: JsonPropertyName("occurred_at")]   DateTime OccurredAt
);

public record AdminRelinkRequest(
    [property: JsonPropertyName("user_id")] int UserId
);

public record AdminSetStatusRequest(
    [property: JsonPropertyName("status")]       string Status,
    [property: JsonPropertyName("clear_period")] bool   ClearPeriod
);

// ── Pay › Offers (admin) ──────────────────────────────────────────────────────
// Rewards and eligibility are stored and transported as opaque JSON STRINGS — the
// reward processor lives in PayApi (a separate service), so AnyApi forwards them
// verbatim. The CLI's primary input is raw JSON; OfferRewardDto below is a
// client-only helper used to build that JSON from convenience flags and to render a
// short summary. `central_tenant_id` is NOT sent — the server sets it.

public record OfferResponse(
    [property: JsonPropertyName("id")]                      Guid              Id,
    [property: JsonPropertyName("name")]                    string            Name,
    [property: JsonPropertyName("description")]             string?           Description,
    [property: JsonPropertyName("kind")]                    string            Kind,
    [property: JsonPropertyName("redeemer_reward_json")]    string?           RedeemerRewardJson,
    [property: JsonPropertyName("referrer_reward_json")]    string?           ReferrerRewardJson,
    [property: JsonPropertyName("eligibility_json")]        string?           EligibilityJson,
    [property: JsonPropertyName("valid_from")]             DateTime?         ValidFrom,
    [property: JsonPropertyName("valid_until")]            DateTime?         ValidUntil,
    [property: JsonPropertyName("total_redemption_cap")]    int?              TotalRedemptionCap,
    [property: JsonPropertyName("per_user_redemption_cap")] int              PerUserRedemptionCap,
    [property: JsonPropertyName("status")]                  string            Status,
    [property: JsonPropertyName("stripe_coupon_id")]        string?           StripeCouponId,
    [property: JsonPropertyName("created_at")]              DateTime          CreatedAt,
    [property: JsonPropertyName("updated_at")]              DateTime          UpdatedAt,
    [property: JsonPropertyName("primary_code")]            OfferCodeResponse? PrimaryCode
);

public record OfferCodeResponse(
    [property: JsonPropertyName("id")]               Guid     Id,
    [property: JsonPropertyName("offer_id")]         Guid     OfferId,
    [property: JsonPropertyName("slug")]             string   Slug,
    [property: JsonPropertyName("owner_user_id")]    int?     OwnerUserId,
    [property: JsonPropertyName("redemption_count")] int      RedemptionCount,
    [property: JsonPropertyName("created_at")]        DateTime CreatedAt,
    [property: JsonPropertyName("owner")]             UserRef? Owner
);

public record UserRef(
    [property: JsonPropertyName("id")]         int     Id,
    [property: JsonPropertyName("first_name")] string  FirstName,
    [property: JsonPropertyName("last_name")]  string  LastName,
    [property: JsonPropertyName("email")]      string? Email
);

public record OfferRedemptionResponse(
    [property: JsonPropertyName("id")]                          Guid      Id,
    [property: JsonPropertyName("offer_id")]                    Guid      OfferId,
    [property: JsonPropertyName("code_id")]                     Guid      CodeId,
    [property: JsonPropertyName("redeemer_user_id")]            int       RedeemerUserId,
    [property: JsonPropertyName("redeemer_subscription_id")]    Guid?     RedeemerSubscriptionId,
    [property: JsonPropertyName("redeemer_reward_status")]      string    RedeemerRewardStatus,
    [property: JsonPropertyName("referrer_user_id")]            int?      ReferrerUserId,
    [property: JsonPropertyName("referrer_reward_status")]      string?   ReferrerRewardStatus,
    [property: JsonPropertyName("referrer_reward_snapshot_json")] string? ReferrerRewardSnapshotJson,
    [property: JsonPropertyName("redeemed_at")]                 DateTime  RedeemedAt,
    [property: JsonPropertyName("applied_at")]                  DateTime? AppliedAt,
    [property: JsonPropertyName("reversed_at")]                 DateTime? ReversedAt,
    [property: JsonPropertyName("redeemer")]                    UserRef?  Redeemer,
    [property: JsonPropertyName("referrer")]                    UserRef?  Referrer
);

public record PersonalCodeResponse(
    [property: JsonPropertyName("slug")]             string  Slug,
    [property: JsonPropertyName("offer_id")]         Guid    OfferId,
    [property: JsonPropertyName("redemption_count")] int     RedemptionCount,
    [property: JsonPropertyName("offer_name")]        string? OfferName
);

public record CreateOfferRequest(
    [property: JsonPropertyName("name")]                    string  Name,
    [property: JsonPropertyName("kind")]                    string  Kind,
    [property: JsonPropertyName("redeemer_reward_json")]    string  RedeemerRewardJson,
    [property: JsonPropertyName("description")]             string? Description          = null,
    [property: JsonPropertyName("referrer_reward_json")]    string? ReferrerRewardJson   = null,
    [property: JsonPropertyName("eligibility_json")]        string? EligibilityJson      = null,
    [property: JsonPropertyName("valid_from")]             DateTime? ValidFrom          = null,
    [property: JsonPropertyName("valid_until")]            DateTime? ValidUntil         = null,
    [property: JsonPropertyName("total_redemption_cap")]    int?    TotalRedemptionCap   = null,
    [property: JsonPropertyName("per_user_redemption_cap")] int     PerUserRedemptionCap = 1,
    [property: JsonPropertyName("status")]                  string  Status               = "active"
);

// Patch semantics — only non-null fields are applied server-side; `kind` is immutable.
public record UpdateOfferRequest(
    [property: JsonPropertyName("name")]                    string?   Name                 = null,
    [property: JsonPropertyName("description")]             string?   Description          = null,
    [property: JsonPropertyName("redeemer_reward_json")]    string?   RedeemerRewardJson   = null,
    [property: JsonPropertyName("referrer_reward_json")]    string?   ReferrerRewardJson   = null,
    [property: JsonPropertyName("eligibility_json")]        string?   EligibilityJson      = null,
    [property: JsonPropertyName("valid_from")]             DateTime? ValidFrom            = null,
    [property: JsonPropertyName("valid_until")]            DateTime? ValidUntil           = null,
    [property: JsonPropertyName("total_redemption_cap")]    int?      TotalRedemptionCap   = null,
    [property: JsonPropertyName("per_user_redemption_cap")] int?      PerUserRedemptionCap = null,
    [property: JsonPropertyName("status")]                  string?   Status               = null
);

public record CreateOfferCodeRequest(
    [property: JsonPropertyName("slug")]          string Slug,
    [property: JsonPropertyName("owner_user_id")] int?   OwnerUserId = null
);

// Client-only helper for the convenience flags and the display summary — never sent
// or received directly (the wire field is the *_reward_json string). The documented
// reward vocabulary is type-discriminated; unknown shapes pass through as raw JSON.
public record OfferRewardDto(
    [property: JsonPropertyName("type")]            string   Type,
    [property: JsonPropertyName("days")]            int?     Days        = null,
    [property: JsonPropertyName("percent_off")]     decimal? PercentOff  = null,
    [property: JsonPropertyName("duration")]        string?  Duration    = null,
    [property: JsonPropertyName("amount")]          decimal? Amount      = null,
    [property: JsonPropertyName("currency")]        string?  Currency    = null,
    [property: JsonPropertyName("tiers")]           List<OfferTierDto>? Tiers = null
);

public record OfferTierDto(
    [property: JsonPropertyName("at")]     int            At,
    [property: JsonPropertyName("reward")] OfferRewardDto Reward
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
    [property: JsonPropertyName("payment_cancel_url")]     string?       PaymentCancelUrl,
    [property: JsonPropertyName("app_engagement_trial_enabled")] bool    AppEngagementTrialEnabled = false
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
