using System.Text.Json;

namespace Anythink.ProjectImporters.N8n;

public class N8nClient
{
    private const int PageSize = 100;
    private const int MaxPages = 1000;

    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _apiKey;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public N8nClient(string baseUrl, string apiKey, HttpMessageHandler? handler = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey = apiKey;
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false });
    }

    public async Task<List<N8nWorkflow>> GetWorkflowsAsync()
    {
        var workflows = new List<N8nWorkflow>();
        var seen = new HashSet<string>();
        string? cursor = null;

        for (var page = 0; page < MaxPages; page++)
        {
            var url = $"{_baseUrl}/api/v1/workflows?limit={PageSize}" +
                      (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-N8N-API-KEY", _apiKey);

            using var response = await _http.SendAsync(request);
            var raw = await response.Content.ReadAsStringAsync();
            if ((int)response.StatusCode is >= 300 and < 400)
                throw new AnythinkException(
                    $"n8n redirected the request to {response.Headers.Location?.Host ?? "another host"}. Redirects are not followed so the API key stays with the instance you named; use the instance's final URL.",
                    (int)response.StatusCode);
            if (!response.IsSuccessStatusCode)
                throw new AnythinkException(Scrub(raw), (int)response.StatusCode);

            var result = JsonSerializer.Deserialize<N8nWorkflowPage>(raw, JsonOpts);
            workflows.AddRange(result?.Data ?? []);

            cursor = result?.NextCursor;
            if (string.IsNullOrEmpty(cursor) || !seen.Add(cursor)) break;
        }

        return workflows;
    }

    private string Scrub(string text)
    {
        var shown = text.Length > 300 ? text[..300] : text;
        return string.IsNullOrEmpty(_apiKey) ? shown : shown.Replace(_apiKey, "***");
    }
}
