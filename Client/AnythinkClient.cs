using AnythinkCli.Config;
using AnythinkCli.Models;
using System.Text.Json.Nodes;

namespace AnythinkCli.Client;

public class AnythinkClient : HttpApiClient
{
    public  string OrgId   { get; }
    public  string BaseUrl { get; }
    private string _org;

    public AnythinkClient(string orgId, string baseUrl, string? token = null, string? apiKey = null)
        : base(token, apiKey)
    {
        OrgId   = orgId;
        BaseUrl = baseUrl.TrimEnd('/');
        _org    = $"{BaseUrl}/org/{OrgId}";
    }

    public AnythinkClient(Profile p) : this(p.OrgId, p.BaseUrl, p.AccessToken, p.ApiKey) { }

    /// <summary>Test-only constructor — injects a mock HttpClient.</summary>
    internal AnythinkClient(string orgId, string baseUrl, HttpClient http) : base(http)
    {
        OrgId   = orgId;
        BaseUrl = baseUrl.TrimEnd('/');
        _org    = $"{BaseUrl}/org/{OrgId}";
    }

    // ── Project Auth ──────────────────────────────────────────────────────────

    public Task<LoginResponse> ExchangeTransferTokenAsync(string transferToken)
        => PostAsync<LoginResponse>(_org + "/auth/v1/exchange-transfer-token",
            new { transfer_token = transferToken });

    // ── Entities ──────────────────────────────────────────────────────────────

    public async Task<List<Entity>> GetEntitiesAsync()
        => (await GetAsync<List<Entity>>(_org + "/entities")) ?? [];

    public async Task<Entity> GetEntityAsync(string name)
        => (await GetAsync<Entity>(_org + $"/entities/{name}"))
           ?? throw new AnythinkException($"Entity '{name}' not found.", 404);

    public Task<Entity> CreateEntityAsync(CreateEntityRequest req)
        => PostAsync<Entity>(_org + "/entities", req);

    public Task<Entity> UpdateEntityAsync(string name, UpdateEntityRequest req)
        => PutAsync<Entity>(_org + $"/entities/{name}", req);

    public Task DeleteEntityAsync(string name)
        => DeleteAsync(_org + $"/entities/{name}");

    // ── Fields ────────────────────────────────────────────────────────────────
    // Uses dedicated GET /entities/{name}/fields endpoint (not the full entity fetch)

    public async Task<List<Field>> GetFieldsAsync(string entityName)
        => (await GetAsync<List<Field>>(_org + $"/entities/{entityName}/fields")) ?? [];

    public Task<Field> AddFieldAsync(string entityName, CreateFieldRequest req)
        => PostAsync<Field>(_org + $"/entities/{entityName}/fields", req);

    public Task DeleteFieldAsync(string entityName, int fieldId)
        => DeleteAsync(_org + $"/entities/{entityName}/fields/{fieldId}");

    // ── Workflows ─────────────────────────────────────────────────────────────

    public async Task<List<Workflow>> GetWorkflowsAsync()
        => (await GetAsync<List<Workflow>>(_org + "/workflows")) ?? [];

    public async Task<Workflow> GetWorkflowAsync(int id)
        => (await GetAsync<Workflow>(_org + $"/workflows/{id}"))
           ?? throw new AnythinkException($"Workflow {id} not found.", 404);

    public Task<Workflow> CreateWorkflowAsync(CreateWorkflowRequest req)
        => PostAsync<Workflow>(_org + "/workflows", req);

    public Task EnableWorkflowAsync(int id)  => PostAsync<JsonObject>(_org + $"/workflows/{id}/enable");
    public Task DisableWorkflowAsync(int id) => PostAsync<JsonObject>(_org + $"/workflows/{id}/disable");

    public Task TriggerWorkflowAsync(int id, object? payload = null)
        => PostAsync<JsonObject>(_org + $"/workflows/{id}/trigger", payload ?? new { });

    public Task DeleteWorkflowAsync(int id) => DeleteAsync(_org + $"/workflows/{id}");

    // ── Data ──────────────────────────────────────────────────────────────────

    public async Task<PaginatedResult<JsonObject>> ListItemsAsync(
        string entityName, int page = 1, int pageSize = 20, string? filterJson = null)
    {
        var url = _org + $"/entities/{entityName}/items?page={page}&page_size={pageSize}";
        if (!string.IsNullOrEmpty(filterJson)) url += $"&filter={Uri.EscapeDataString(filterJson)}";
        return (await GetAsync<PaginatedResult<JsonObject>>(url))
               ?? new PaginatedResult<JsonObject>([], 0, page, pageSize);
    }

    public async Task<JsonObject> GetItemAsync(string entityName, int id)
        => (await GetAsync<JsonObject>(_org + $"/entities/{entityName}/items/{id}"))
           ?? throw new AnythinkException($"Item {id} not found in '{entityName}'.", 404);

    public Task<JsonObject> CreateItemAsync(string entityName, JsonObject data)
        => PostAsync<JsonObject>(_org + $"/entities/{entityName}/items", data);

    public Task<JsonObject> UpdateItemAsync(string entityName, int id, JsonObject data)
        => PutAsync<JsonObject>(_org + $"/entities/{entityName}/items/{id}", data);

    public Task DeleteItemAsync(string entityName, int id)
        => DeleteAsync(_org + $"/entities/{entityName}/items/{id}");

    // ── Users ─────────────────────────────────────────────────────────────────

    public async Task<List<UserResponse>> GetUsersAsync()
        => (await GetAsync<List<UserResponse>>(_org + "/users")) ?? [];

    public Task<UserResponse?> GetMeAsync()
        => GetAsync<UserResponse>(_org + "/users/me");

    public Task<UserResponse?> GetUserAsync(int userId)
        => GetAsync<UserResponse>(_org + $"/users/{userId}");

    public Task<UserResponse> CreateUserAsync(CreateUserRequest req)
        => PostAsync<UserResponse>(_org + "/users", req);

    public Task<UserResponse> UpdateUserAsync(int userId, UpdateUserRequest req)
        => PutAsync<UserResponse>(_org + $"/users/{userId}", req);

    public Task DeleteUserAsync(int userId)
        => DeleteAsync(_org + $"/users/{userId}");

    public Task SendInvitationAsync(int userId)
        => PostAsync<object>(_org + $"/users/{userId}/send-invitation-email", new { });

    // ── Files ─────────────────────────────────────────────────────────────────

    public async Task<List<FileResponse>> GetFilesAsync(int page = 1, int pageSize = 25)
    {
        var result = await GetAsync<PaginatedResult<FileResponse>>(_org + $"/files?page={page}&pageSize={pageSize}");
        return result?.Items ?? [];
    }

    public Task<FileResponse?> GetFileAsync(int id)
        => GetAsync<FileResponse>(_org + $"/files/{id}");

    public Task DeleteFileAsync(int id)
        => DeleteAsync(_org + $"/files/{id}");

    public async Task<FileResponse> UploadFileAsync(string filePath, bool isPublic = false)
    {
        using var form = new MultipartFormDataContent();
        var fileBytes = await File.ReadAllBytesAsync(filePath);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "file", Path.GetFileName(filePath));
        var url = _org + $"/files?isPublic={isPublic.ToString().ToLower()}";
        var resp = await Http.PostAsync(url, form);
        if (!resp.IsSuccessStatusCode)
            throw new AnythinkException(await resp.Content.ReadAsStringAsync(), (int)resp.StatusCode);
        var json = await resp.Content.ReadAsStringAsync();
        return System.Text.Json.JsonSerializer.Deserialize<FileResponse>(json, JsonOpts)!;
    }

    // ── Roles ─────────────────────────────────────────────────────────────────

    public async Task<List<RoleResponse>> GetRolesAsync()
        => (await GetAsync<List<RoleResponse>>(_org + "/roles")) ?? [];

    public Task<RoleResponse> CreateRoleAsync(CreateRoleRequest req)
        => PostAsync<RoleResponse>(_org + "/roles", req);

    public Task DeleteRoleAsync(int roleId)
        => DeleteAsync(_org + $"/roles/{roleId}");

    // ── Pay ───────────────────────────────────────────────────────────────────

    private string _pay => _org + "/integrations/anythinkpay";

    public Task<StripeConnectStatus?> GetStripeConnectAsync()
        => GetAsync<StripeConnectStatus>(_pay + "/stripe-connect");

    public Task<StripeConnectStatus> CreateStripeConnectAsync(CreateStripeConnectRequest req)
        => PostAsync<StripeConnectStatus>(_pay + "/stripe-connect", req);

    public Task<OnboardingLinkResponse> CreateOnboardingLinkAsync(string refreshUrl, string returnUrl)
        => PostAsync<OnboardingLinkResponse>(_pay + "/stripe-connect/onboarding",
            new CreateOnboardingLinkRequest(refreshUrl, returnUrl));

    public async Task<List<PaymentResponse>> GetPaymentsAsync(int page = 1, int pageSize = 25)
    {
        var result = await GetAsync<PaginatedResult<PaymentResponse>>(_pay + $"/payments?page={page}&pageSize={pageSize}");
        return result?.Items ?? [];
    }

    public Task<PaymentResponse?> GetPaymentAsync(string id)
        => GetAsync<PaymentResponse>(_pay + $"/payments/{id}");

    public async Task<List<PaymentMethodResponse>> GetPaymentMethodsAsync()
        => (await GetAsync<List<PaymentMethodResponse>>(_pay + "/payment-methods")) ?? [];

    // ── OAuth / Social Auth ───────────────────────────────────────────────────

    public Task<GoogleOAuthSettings?> GetGoogleOAuthAsync()
        => GetAsync<GoogleOAuthSettings>(_org + "/integrations/oauth/google");

    public Task PutGoogleOAuthAsync(UpdateGoogleOAuthRequest req)
        => PutVoidAsync(_org + "/integrations/oauth/google", req);
}
