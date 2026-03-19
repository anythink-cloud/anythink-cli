using AnythinkCli.Config;
using AnythinkCli.Models;

namespace AnythinkCli.Client;

public class BillingClient : HttpApiClient
{
    private readonly string _billing;
    private readonly string _auth;   // = ApiUrl + /org/{platformOrgId}

    public BillingClient(PlatformConfig p)
        : base(p.Token, null)
    {
        _billing = p.BillingUrl.TrimEnd('/');
        _auth    = $"{p.MyAnythinkUrl.TrimEnd('/')}/org/{p.MyAnythinkOrgId}";
    }

    // ── Platform Auth ─────────────────────────────────────────────────────────

    public Task RegisterAsync(RegisterRequest req)
        => PostAsync<object>(_auth + "/auth/v1/register", req);

    public Task<LoginResponse> LoginAsync(string email, string password)
        => PostAsync<LoginResponse>(_auth + "/auth/v1/token", new LoginRequest(email, password));

    // ── Plans ─────────────────────────────────────────────────────────────────

    public async Task<List<BillingPlan>> GetPlansAsync()
        => (await GetAsync<List<BillingPlan>>(_billing + "/v1/plans")) ?? [];

    // ── Accounts ──────────────────────────────────────────────────────────────

    public async Task<List<BillingAccount>> GetAccountsAsync()
        => (await GetAsync<List<BillingAccount>>(_billing + "/v1/accounts")) ?? [];

    public Task<BillingAccount> CreateAccountAsync(CreateBillingAccountRequest req)
        => PostAsync<BillingAccount>(_billing + "/v1/accounts", req);

    // ── Projects (Shared Tenants) ──────────────────────────────────────────────

    public async Task<List<SharedTenant>> GetProjectsAsync(Guid accountId)
        => (await GetAsync<List<SharedTenant>>(_billing + $"/v1/accounts/{accountId}/shared-tenants")) ?? [];

    public Task<SharedTenant> CreateProjectAsync(Guid accountId, CreateSharedTenantRequest req)
        => PostAsync<SharedTenant>(_billing + $"/v1/accounts/{accountId}/shared-tenants", req);

    public Task DeleteProjectAsync(Guid accountId, Guid projectId)
        => DeleteAsync(_billing + $"/v1/accounts/{accountId}/shared-tenants/{projectId}");

    /// <summary>
    /// Generates a short-lived (60s) single-use transfer token that can be exchanged
    /// for a project-scoped JWT via POST /org/{orgId}/auth/v1/exchange-transfer-token.
    /// </summary>
    public Task<GenerateTransferTokenResponse> GetTransferTokenAsync(Guid accountId, Guid projectId)
        => PostAsync<GenerateTransferTokenResponse>(
            _billing + $"/v1/accounts/{accountId}/shared-tenants/{projectId}/transfer-token");
}
