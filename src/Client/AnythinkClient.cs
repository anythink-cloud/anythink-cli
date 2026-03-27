using AnythinkCli.Config;
using AnythinkCli.Models;
using System.Text;
using System.Text.Json;
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

    public AnythinkClient(Profile p) : this(p.OrgId, p.InstanceApiUrl, p.AccessToken, p.ApiKey) { }

    /// <summary>Test-only constructor — injects a mock HttpClient.</summary>
    internal AnythinkClient(string orgId, string baseUrl, HttpClient http) : base(http)
    {
        OrgId   = orgId;
        BaseUrl = baseUrl.TrimEnd('/');
        _org    = $"{BaseUrl}/org/{OrgId}";
    }

    // ── Raw fetch (for CLI `fetch` command) ────────────────────────────────────

    public async Task<string> FetchRawAsync(string url, string method = "GET", string? body = null)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (body != null)
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        var response = await Http.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new AnythinkException(content, (int)response.StatusCode);
        return content;
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

    public Task<Entity?> UpdateEntityAsync(string name, UpdateEntityRequest req)
        => PutAsync<Entity>(_org + $"/entities/{name}", req);

    public Task DeleteEntityAsync(string name)
        => DeleteAsync(_org + $"/entities/{name}");

    // ── Fields ────────────────────────────────────────────────────────────────
    // Uses dedicated GET /entities/{name}/fields endpoint (not the full entity fetch)

    public async Task<List<Field>> GetFieldsAsync(string entityName)
        => (await GetAsync<List<Field>>(_org + $"/entities/{entityName}/fields")) ?? [];

    public Task<Field> AddFieldAsync(string entityName, CreateFieldRequest req)
        => PostAsync<Field>(_org + $"/entities/{entityName}/fields", req);

    public async Task<Field> UpdateFieldAsync(string entityName, int fieldId, UpdateFieldRequest req)
        => (await PutAsync<Field>(_org + $"/entities/{entityName}/fields/{fieldId}", req))!;

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

    public async Task<Workflow> UpdateWorkflowAsync(int id, UpdateWorkflowRequest req)
        => (await PutAsync<Workflow>(_org + $"/workflows/{id}", req))!;

    public Task EnableWorkflowAsync(int id)  => PostAsync<JsonObject>(_org + $"/workflows/{id}/enable");
    public Task DisableWorkflowAsync(int id) => PostAsync<JsonObject>(_org + $"/workflows/{id}/disable");

    public Task TriggerWorkflowAsync(int id, object? payload = null)
        => PostAsync<JsonObject>(_org + $"/workflows/{id}/trigger", payload ?? new { });

    public Task DeleteWorkflowAsync(int id) => DeleteAsync(_org + $"/workflows/{id}");

    public async Task<PaginatedResult<WorkflowJob>> GetWorkflowJobsAsync(int workflowId, int page = 1, int pageSize = 10)
        => (await GetAsync<PaginatedResult<WorkflowJob>>(_org + $"/workflows/{workflowId}/jobs?page={page}&pageSize={pageSize}"))
           ?? new PaginatedResult<WorkflowJob>([], 0, null, false, page, pageSize);

    public async Task<WorkflowJob> GetWorkflowJobAsync(int workflowId, int jobId)
        => (await GetAsync<WorkflowJob>(_org + $"/workflows/{workflowId}/jobs/{jobId}"))
           ?? throw new AnythinkException($"Job {jobId} not found.", 404);

    public Task<WorkflowStep> AddWorkflowStepAsync(int workflowId, CreateWorkflowStepRequest req)
        => PostAsync<WorkflowStep>(_org + $"/workflows/{workflowId}/steps", req);

    public Task<WorkflowStep?> UpdateWorkflowStepAsync(int workflowId, int stepId, UpdateWorkflowStepLinksRequest req)
        => PutAsync<WorkflowStep>(_org + $"/workflows/{workflowId}/steps/{stepId}", req);

    public Task<WorkflowStep?> UpdateWorkflowStepFullAsync(int workflowId, int stepId, object body)
        => PutAsync<WorkflowStep>(_org + $"/workflows/{workflowId}/steps/{stepId}", body);

    // ── Data ──────────────────────────────────────────────────────────────────

    public async Task<PaginatedResult<JsonObject>> ListItemsAsync(
        string entityName, int page = 1, int pageSize = 20, string? filterJson = null)
    {
        var url = _org + $"/entities/{entityName}/items?limit={pageSize}&page={page}";
        if (!string.IsNullOrEmpty(filterJson)) url += $"&filter={Uri.EscapeDataString(filterJson)}";
        return (await GetAsync<PaginatedResult<JsonObject>>(url))
               ?? new PaginatedResult<JsonObject>([], 0, null, false, page, pageSize);
    }

    public async Task<JsonObject> GetItemAsync(string entityName, int id)
        => (await GetAsync<JsonObject>(_org + $"/entities/{entityName}/items/{id}"))
           ?? throw new AnythinkException($"Item {id} not found in '{entityName}'.", 404);

    public Task<JsonObject> CreateItemAsync(string entityName, JsonObject data)
        => PostAsync<JsonObject>(_org + $"/entities/{entityName}/items", data);

    public Task<JsonObject?> UpdateItemAsync(string entityName, int id, JsonObject data)
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

    public Task<UserResponse?> UpdateUserAsync(int userId, UpdateUserRequest req)
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

    /// <summary>Fetches all files across pages — use for migration where completeness matters.</summary>
    public async Task<List<FileResponse>> GetAllFilesAsync()
    {
        var all  = new List<FileResponse>();
        var page = 1;
        while (true)
        {
            var result = await GetAsync<PaginatedResult<FileResponse>>(
                _org + $"/files?page={page}&pageSize=100");
            var items = result?.Items ?? [];
            all.AddRange(items);
            if (items.Count < 100) break;
            page++;
        }
        return all;
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
        return JsonSerializer.Deserialize<FileResponse>(json, JsonOpts)!;
    }

    /// <summary>
    /// Downloads a file from <paramref name="sourceUrl"/> (using <paramref name="sourceToken"/>
    /// for auth if provided) and re-uploads it to this project.
    /// </summary>
    public async Task<FileResponse> UploadFileFromUrlAsync(
        string sourceUrl, string fileName, bool isPublic = false, string? sourceToken = null)
    {
        using var downloader = new HttpClient();
        if (!string.IsNullOrEmpty(sourceToken))
            downloader.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", sourceToken);

        var bytes = await downloader.GetByteArrayAsync(sourceUrl);

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "file", fileName);

        var url = _org + $"/files?isPublic={isPublic.ToString().ToLower()}";
        var resp = await Http.PostAsync(url, form);
        if (!resp.IsSuccessStatusCode)
            throw new AnythinkException(await resp.Content.ReadAsStringAsync(), (int)resp.StatusCode);
        var json = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<FileResponse>(json, JsonOpts)!;
    }

    // ── Roles ─────────────────────────────────────────────────────────────────

    public async Task<List<RoleResponse>> GetRolesAsync()
        => (await GetAsync<List<RoleResponse>>(_org + "/roles")) ?? [];

    public Task<RoleResponse?> GetRoleAsync(int roleId)
        => GetAsync<RoleResponse>(_org + $"/roles/{roleId}");

    public Task<RoleResponse> CreateRoleAsync(CreateRoleRequest req)
        => PostAsync<RoleResponse>(_org + "/roles", req);

    public Task DeleteRoleAsync(int roleId)
        => DeleteAsync(_org + $"/roles/{roleId}");

    public Task<Permission> CreatePermissionAsync(CreatePermissionRequest req)
        => PostAsync<Permission>(_org + "/permissions", req);

    public async Task<List<Permission>> GetPermissionsAsync()
        => (await GetAsync<List<Permission>>(_org + "/permissions")) ?? [];

    public Task<RoleResponse?> UpdateRoleWithPermissionsAsync(int roleId, UpdateRolePermissionsRequest req)
        => PutAsync<RoleResponse>(_org + $"/roles/{roleId}", req);

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

    // ── Token refresh (unauthenticated — called before building a client) ─────

    /// <summary>
    /// Exchanges a saved refresh token for a new access token.
    /// Returns null when the server rejects the token — caller should prompt re-login.
    /// </summary>
    public static async Task<LoginResponse?> RefreshTokenAsync(string baseUrl, string orgId, string refreshToken)
    {
        using var http = new HttpClient();
        return await RefreshTokenAsync(baseUrl, orgId, refreshToken, http);
    }

    /// <summary>Internal overload — accepts an injected HttpClient for unit testing.</summary>
    internal static async Task<LoginResponse?> RefreshTokenAsync(
        string baseUrl, string orgId, string refreshToken, HttpClient http)
    {
        var body    = JsonSerializer.Serialize(new { token = refreshToken }, JsonOpts);
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var url     = $"{baseUrl.TrimEnd('/')}/org/{orgId}/auth/v1/refresh";
        var r       = await http.PostAsync(url, content);
        if (!r.IsSuccessStatusCode) return null;
        var raw     = await r.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try   { return JsonSerializer.Deserialize<LoginResponse>(raw, JsonOpts); }
        catch (JsonException) { return null; }
    }

    // ── Secrets ───────────────────────────────────────────────────────────────

    public async Task<List<SecretResponse>> GetSecretsAsync()
        => (await GetAsync<List<SecretResponse>>(_org + "/secrets")) ?? [];

    public Task<SecretResponse> CreateSecretAsync(CreateSecretRequest req)
        => PostAsync<SecretResponse>(_org + "/secrets", req);

    public Task<SecretResponse?> UpdateSecretAsync(string key, UpdateSecretRequest req)
        => PutAsync<SecretResponse>(_org + $"/secrets/{key}", req);

    public Task DeleteSecretAsync(string key)
        => DeleteAsync(_org + $"/secrets/{key}");

    // ── OAuth / Social Auth ───────────────────────────────────────────────────

    public Task<GoogleOAuthSettings?> GetGoogleOAuthAsync()
        => GetAsync<GoogleOAuthSettings>(_org + "/integrations/oauth/google");

    public Task PutGoogleOAuthAsync(UpdateGoogleOAuthRequest req)
        => PutVoidAsync(_org + "/integrations/oauth/google", req);

    // ── Menus ─────────────────────────────────────────────────────────────────

    public async Task<List<MenuResponse>> GetMenusAsync()
        => (await GetAsync<List<MenuResponse>>(_org + "/menus")) ?? [];

    public Task<MenuResponse?> GetMenuAsync(int menuId)
        => GetAsync<MenuResponse>(_org + $"/menus/{menuId}");

    public Task<MenuResponse> CreateMenuAsync(CreateMenuRequest req)
        => PostAsync<MenuResponse>(_org + "/menus", req);

    public Task<MenuItemResponse> CreateMenuItemAsync(int menuId, CreateMenuItemRequest req)
        => PostAsync<MenuItemResponse>(_org + $"/menus/{menuId}/items", req);

    public Task DeleteMenuAsync(int menuId)
        => DeleteAsync(_org + $"/menus/{menuId}");

    // ── Tenant / Organisation Settings ────────────────────────────────────────

    public Task<TenantResponse?> GetTenantAsync()
        => GetAsync<TenantResponse>(BaseUrl + $"/org/{OrgId}");

    public Task<TenantResponse?> UpdateTenantAsync(UpdateTenantRequest req)
        => PutAsync<TenantResponse>(BaseUrl + $"/org/{OrgId}", req);
}
