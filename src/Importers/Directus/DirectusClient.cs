using AnythinkCli.Client;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnythinkCli.Importers.Directus;

/// <summary>
/// Minimal read-only client for the Directus REST API.
/// Used by the <c>anythink import directus</c> command.
/// </summary>
public class DirectusClient
{
    private readonly HttpClient _http;
    private readonly string     _baseUrl;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull
    };

    public DirectusClient(string baseUrl, string token)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        Token    = token;
        _http    = new HttpClient();
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<List<DirectusCollection>> GetCollectionsAsync()
        => await FetchListAsync<DirectusCollection>("/collections");

    public async Task<List<DirectusField>> GetFieldsAsync()
        => await FetchListAsync<DirectusField>("/fields");

    public async Task<List<DirectusRelation>> GetRelationsAsync()
        => await FetchListAsync<DirectusRelation>("/relations");

    public async Task<List<DirectusFile>> GetFilesAsync()
        => await FetchListAsync<DirectusFile>("/files?limit=-1");

    public async Task<List<DirectusRole>> GetRolesAsync()
        => await FetchListAsync<DirectusRole>("/roles?limit=-1");

    public async Task<List<DirectusPolicy>> GetPoliciesAsync()
        => await FetchListAsync<DirectusPolicy>("/policies?limit=-1");

    public async Task<List<DirectusAccess>> GetAccessAsync()
        => await FetchListAsync<DirectusAccess>("/access?limit=-1");

    public async Task<List<DirectusPermission>> GetPermissionsAsync()
        => await FetchListAsync<DirectusPermission>("/permissions?limit=-1");

    /// <summary>
    /// URL that returns the raw bytes of a file. Used by the runner to
    /// download from source and re-upload to Anythink.
    /// </summary>
    /// <summary>
    /// Build an asset download URL for a Directus file. Rejects file ids that
    /// don't look like a Directus UUID — defensive against a hostile source
    /// returning a `..`/`/`-laced id that would change the request path.
    /// </summary>
    public string GetAssetUrl(string fileId)
    {
        if (string.IsNullOrEmpty(fileId) || !IsSafeFileId(fileId))
            throw new AnythinkException($"Directus file id rejected (unsafe characters): {fileId}", 400);
        return $"{_baseUrl}/assets/{Uri.EscapeDataString(fileId)}";
    }

    private static bool IsSafeFileId(string id)
    {
        // Directus file ids are UUIDs; accept anything that's just letters,
        // digits, hyphens, and underscores.
        foreach (var c in id)
        {
            if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_'))
                return false;
        }
        return true;
    }

    /// <summary>The bearer token configured for source downloads.</summary>
    public string Token { get; private set; }

    public async Task<List<DirectusFlow>> GetFlowsAsync()
        => await FetchListAsync<DirectusFlow>("/flows");

    public async Task<List<DirectusOperation>> GetOperationsAsync()
        => await FetchListAsync<DirectusOperation>("/operations");

    /// <summary>
    /// Returns a page of records from /items/&lt;collection&gt; plus an optional
    /// total count from meta.total_count (requires ?meta=total_count).
    /// </summary>
    public async Task<(List<System.Text.Json.Nodes.JsonObject> Records, int? TotalCount)>
        GetItemsAsync(string collection, int page, int pageSize)
    {
        var url = $"{_baseUrl}/items/{Uri.EscapeDataString(collection)}" +
                  $"?limit={pageSize}&page={page}&meta=total_count";
        var response = await _http.GetAsync(url);
        var raw      = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new AnythinkException(raw, (int)response.StatusCode);

        using var doc = JsonDocument.Parse(raw);
        var records = new List<System.Text.Json.Nodes.JsonObject>();
        if (doc.RootElement.TryGetProperty("data", out var data) &&
            data.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in data.EnumerateArray())
            {
                var node = System.Text.Json.Nodes.JsonNode.Parse(el.GetRawText());
                if (node is System.Text.Json.Nodes.JsonObject obj) records.Add(obj);
            }
        }

        int? total = null;
        if (doc.RootElement.TryGetProperty("meta", out var meta) &&
            meta.TryGetProperty("total_count", out var tc) &&
            tc.ValueKind == JsonValueKind.Number)
            total = tc.GetInt32();

        return (records, total);
    }

    private async Task<List<T>> FetchListAsync<T>(string path)
    {
        var response = await _http.GetAsync(_baseUrl + path);
        var raw      = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new AnythinkException(raw, (int)response.StatusCode);

        var result = JsonSerializer.Deserialize<DirectusListResponse<T>>(raw, JsonOpts);
        return result?.Data ?? [];
    }
}
