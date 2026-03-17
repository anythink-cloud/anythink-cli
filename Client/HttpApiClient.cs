using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AnythinkCli.Client;

/// <summary>API error returned by the server (HTTP 4xx/5xx).</summary>
public class AnythinkException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>
/// User-facing CLI error that carries Spectre markup.
/// HandleError displays it directly without double-printing.
/// </summary>
public class CliException(string markupMessage) : Exception(markupMessage) { }

/// <summary>
/// Shared HTTP infrastructure for AnythinkClient and BillingClient.
/// Handles auth headers, JSON serialization, and uniform error handling.
/// </summary>
public abstract class HttpApiClient
{
    protected readonly HttpClient Http;

    protected static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull
    };

    protected HttpApiClient(string? token, string? apiKey)
    {
        Http = new HttpClient();
        if (!string.IsNullOrEmpty(apiKey))
            Http.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        else if (!string.IsNullOrEmpty(token))
            Http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
    }

    protected async Task<T?> GetAsync<T>(string url) =>
        await DeserializeAsync<T>(await Http.GetAsync(url));

    protected async Task<T> PostAsync<T>(string url, object? body = null)
    {
        var r = await Http.PostAsync(url, Serialize(body ?? new { }));
        return await DeserializeAsync<T>(r)
               ?? throw new AnythinkException("Empty response.", (int)r.StatusCode);
    }

    protected async Task<T> PutAsync<T>(string url, object body)
    {
        var r = await Http.PutAsync(url, Serialize(body));
        return await DeserializeAsync<T>(r)
               ?? throw new AnythinkException("Empty response.", (int)r.StatusCode);
    }

    protected async Task PutVoidAsync(string url, object body)
    {
        var r = await Http.PutAsync(url, Serialize(body));
        if (!r.IsSuccessStatusCode)
            throw new AnythinkException(await r.Content.ReadAsStringAsync(), (int)r.StatusCode);
    }

    protected async Task DeleteAsync(string url)
    {
        var r = await Http.DeleteAsync(url);
        if (!r.IsSuccessStatusCode)
            throw new AnythinkException(await r.Content.ReadAsStringAsync(), (int)r.StatusCode);
    }

    private static StringContent Serialize(object body)
    {
        var json = body is JsonNode n ? n.ToJsonString() : JsonSerializer.Serialize(body, JsonOpts);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage r)
    {
        var raw = await r.Content.ReadAsStringAsync();
        if (!r.IsSuccessStatusCode) throw new AnythinkException(raw, (int)r.StatusCode);
        if (string.IsNullOrWhiteSpace(raw)) return default;
        try   { return JsonSerializer.Deserialize<T>(raw, JsonOpts); }
        catch (JsonException ex)
              { throw new AnythinkException($"Parse error: {ex.Message}\n{raw}", (int)r.StatusCode); }
    }
}
