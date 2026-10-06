using System.Text.Json;
using System.Text.RegularExpressions;
using AnythinkCli.Config;
using AnythinkCli.Models;

namespace AnythinkCli.Client;

public sealed partial class BillingException(string message, int statusCode) : AnythinkException(message, statusCode)
{
    private const int MaxMessageLength = 300;

    public override string StatusOnlyMessage => StatusCode switch
    {
        400 when Describe(Message) is { } detail => detail,
        >= 200 and < 300 => "The billing API returned a response this command couldn't read.",
        _ => $"The billing API returned status {StatusCode}."
    };

    internal static string? Describe(string body)
    {
        var text = body.Trim();
        if (text.Length == 0 || text.StartsWith('<'))
            return null;

        if (text[0] is '{' or '"')
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                text = Detail(doc.RootElement) ?? "";
            }
            catch (JsonException)
            {
                return null;
            }
        }

        text = string.Concat(Whitespace().Replace(text, " ").Where(c => !char.IsControl(c))).Trim();
        if (text.Length == 0)
            return null;
        return text.Length <= MaxMessageLength ? text : text[..MaxMessageLength].TrimEnd() + "...";
    }

    private static string? Detail(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
            return element.GetString();
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var name in new[] { "error", "message", "detail" })
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();

        if (element.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
        {
            var messages = errors.EnumerateObject()
                .SelectMany(field => Items(field.Value))
                .Where(message => message.ValueKind == JsonValueKind.String)
                .Select(message => message.GetString());
            var joined = string.Join(" ", messages);
            if (joined.Length > 0)
                return joined;
        }

        return element.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String ? title.GetString() : null;
    }

    private static List<JsonElement> Items(JsonElement element) =>
        element.ValueKind == JsonValueKind.Array ? element.EnumerateArray().ToList() : [];

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

public class BillingClient : HttpApiClient
{
    private readonly string _billing;
    private readonly string? _auth;   // = ApiUrl + /org/{platformOrgId}
    private readonly HttpClient? _anonymous;

    public BillingClient(PlatformConfig p)
        : base(p.Token, null)
    {
        _billing = p.BillingUrl.TrimEnd('/');
        _auth    = $"{p.MyAnythinkUrl.TrimEnd('/')}/org/{p.MyAnythinkOrgId}";
    }

    /// <summary>Test-only constructor — accepts a pre-configured HttpClient (e.g. with a mock handler).</summary>
    internal BillingClient(PlatformConfig p, HttpClient http) : base(http)
    {
        _billing = p.BillingUrl.TrimEnd('/');
        _auth    = $"{p.MyAnythinkUrl.TrimEnd('/')}/org/{p.MyAnythinkOrgId}";
    }

    /// <summary>A caller's own client: the bearer is already on <paramref name="http"/>, and nothing outside <paramref name="billingUrl"/> is reachable.</summary>
    public BillingClient(string billingUrl, HttpClient http, HttpClient? anonymous = null) : base(http)
    {
        _billing   = billingUrl.TrimEnd('/');
        _anonymous = anonymous;
        ConfineTo(_billing);
    }

    public BillingClient Unauthenticated() =>
        _anonymous is null ? this : new BillingClient(_billing, _anonymous);

    protected override AnythinkException Failure(string message, int statusCode) => new BillingException(message, statusCode);

    // ── Platform Auth ─────────────────────────────────────────────────────────

    private string AuthUrl => _auth ?? throw new InvalidOperationException("This billing client has no platform sign-in.");

    public Task RegisterAsync(RegisterRequest req)
        => PostAsync<object>(AuthUrl + "/auth/v1/register", req);

    public Task<LoginResponse> LoginAsync(string email, string password)
        => PostAsync<LoginResponse>(AuthUrl + "/auth/v1/token", new LoginRequest(email, password));

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
