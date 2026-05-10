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
        _http    = new HttpClient();
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<List<DirectusCollection>> GetCollectionsAsync()
        => await FetchListAsync<DirectusCollection>("/collections");

    public async Task<List<DirectusField>> GetFieldsAsync()
        => await FetchListAsync<DirectusField>("/fields");

    public async Task<List<DirectusFlow>> GetFlowsAsync()
        => await FetchListAsync<DirectusFlow>("/flows");

    public async Task<List<DirectusOperation>> GetOperationsAsync()
        => await FetchListAsync<DirectusOperation>("/operations");

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
