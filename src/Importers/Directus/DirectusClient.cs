using AnythinkCli.Client;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnythinkCli.Importers.Directus;

public class DirectusClient
{
    private readonly HttpClient _http;
    private readonly string     _baseUrl;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull
    };

    public DirectusClient(string baseUrl, string token, HttpClient? http = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        Token    = token;
        _http    = http ?? new HttpClient();
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<List<DirectusCollection>> GetCollectionsAsync()
        => await FetchListAsync<DirectusCollection>("/collections?limit=-1");

    public async Task<List<DirectusField>> GetFieldsAsync()
        => await FetchListAsync<DirectusField>("/fields?limit=-1");

    public async Task<List<DirectusRelation>> GetRelationsAsync()
        => await FetchListAsync<DirectusRelation>("/relations?limit=-1");

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

    // Rejects ids that could change the request path (`..`, `/`) when the source is hostile.
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

    public string Token { get; private set; }

    public async Task<List<DirectusFlow>> GetFlowsAsync()
        => await FetchListAsync<DirectusFlow>("/flows?limit=-1");

    public async Task<List<DirectusOperation>> GetOperationsAsync()
        => await FetchListAsync<DirectusOperation>("/operations?limit=-1");

    public async Task<(List<System.Text.Json.Nodes.JsonObject> Records, int? TotalCount)>
        GetItemsAsync(string collection, int page, int pageSize)
    {
        if (!DirectusImporter.IsValidCollectionName(collection))
            throw new AnythinkException($"Directus collection name rejected: {collection}", 400);
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
