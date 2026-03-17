using System.Text.Json.Serialization;

namespace AnythinkCli.Models;

public record BillingPlan(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("resource")] int Resource,
    [property: JsonPropertyName("monthly_price_cents")] decimal MonthlyPriceCents,
    [property: JsonPropertyName("annual_price_cents")] decimal AnnualPriceCents,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("storage_quota_gb")] int StorageQuotaGb,
    [property: JsonPropertyName("file_quota_gb")] int FileQuotaGb,
    [property: JsonPropertyName("user_quota")] int UserQuota,
    [property: JsonPropertyName("custom_domain_enabled")] bool CustomDomainEnabled,
    [property: JsonPropertyName("backups_enabled")] bool BackupsEnabled,
    [property: JsonPropertyName("priority_support")] bool PrioritySupport,
    [property: JsonPropertyName("is_active")] bool IsActive,
    [property: JsonPropertyName("display_order")] int DisplayOrder
);

public record BillingAccount(
    [property: JsonPropertyName("account_id")]          Guid      Id,
    [property: JsonPropertyName("organization_name")]   string    OrganizationName,
    [property: JsonPropertyName("billing_email")]       string    BillingEmail,
    [property: JsonPropertyName("currency")]            string    Currency,
    [property: JsonPropertyName("status")]              int       Status,          // 0=Active 1=Suspended 2=Canceled
    [property: JsonPropertyName("access_level")]        int       AccessLevel,     // 0=Viewer 1=Admin 2=Owner
    [property: JsonPropertyName("current_balance_cents")] long    CurrentBalanceCents,
    [property: JsonPropertyName("next_invoice_date")]   DateTime? NextInvoiceDate
);

public record CreateBillingAccountRequest(
    [property: JsonPropertyName("organization_name")] string OrganizationName,
    [property: JsonPropertyName("billing_email")] string BillingEmail,
    [property: JsonPropertyName("currency")] string Currency = "gbp",
    [property: JsonPropertyName("billing_cycle_day_of_month")] int BillingCycleDayOfMonth = 1
);

public record SharedTenant(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("plan_id")] Guid? PlanId,
    [property: JsonPropertyName("region")] string? Region,
    [property: JsonPropertyName("status")] int Status,  // 0=Initializing 1=Provisioning 2=Active 3=Suspended 4=Terminated 5=Error
    [property: JsonPropertyName("api_url")] string? ApiUrl,
    [property: JsonPropertyName("frontend_url")] string? FrontendUrl,
    [property: JsonPropertyName("external_tenant_id")] int? TenantId,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt
);

public record GenerateTransferTokenResponse(
    [property: JsonPropertyName("transfer_token")] string TransferToken
);

public record CreateSharedTenantRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("plan_id")] Guid PlanId,
    [property: JsonPropertyName("region")] string Region,
    [property: JsonPropertyName("description")] string? Description = null
);

public record RegisterRequest(
    [property: JsonPropertyName("first_name")] string FirstName,
    [property: JsonPropertyName("last_name")] string LastName,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("referral_code")] string? ReferralCode = null
);
