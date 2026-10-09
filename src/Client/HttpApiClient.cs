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
    public TimeSpan? RetryAfter { get; init; }
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

    /// <summary>Test-only constructor — accepts a pre-configured HttpClient (e.g. with a mock handler).</summary>
    internal HttpApiClient(HttpClient http) { Http = http; }

    protected async Task<T?> GetAsync<T>(string url) =>
        await DeserializeAsync<T>(await Http.GetAsync(url));

    protected async Task<T> PostAsync<T>(string url, object? body = null)
    {
        var r = await Http.PostAsync(url, Serialize(body ?? new { }));
        return await DeserializeAsync<T>(r)
               ?? throw new AnythinkException("Empty response.", (int)r.StatusCode);
    }

    protected async Task<T?> PutAsync<T>(string url, object body)
    {
        var r = await Http.PutAsync(url, Serialize(body));
        // 204 No Content = success with no body (e.g. entity item updates)
        if (r.StatusCode == System.Net.HttpStatusCode.NoContent) return default;
        return await DeserializeAsync<T>(r)
               ?? throw new AnythinkException("Empty response.", (int)r.StatusCode);
    }

    protected async Task PostVoidAsync(string url, object? body = null)
    {
        var r = await Http.PostAsync(url, Serialize(body ?? new { }));
        if (!r.IsSuccessStatusCode)
            throw Failure(await r.Content.ReadAsStringAsync(), r);
    }

    protected async Task PutVoidAsync(string url, object body)
    {
        var r = await Http.PutAsync(url, Serialize(body));
        if (!r.IsSuccessStatusCode)
            throw Failure(await r.Content.ReadAsStringAsync(), r);
    }

    protected async Task DeleteAsync(string url)
    {
        var r = await Http.DeleteAsync(url);
        if (!r.IsSuccessStatusCode)
            throw Failure(await r.Content.ReadAsStringAsync(), r);
    }

    private static StringContent Serialize(object body)
    {
        var json = body is JsonNode n ? n.ToJsonString() : JsonSerializer.Serialize(body, JsonOpts);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static AnythinkException Failure(string raw, HttpResponseMessage r)
    {
        var wait = r.Headers.RetryAfter?.Delta
                   ?? (r.Headers.RetryAfter?.Date is { } at ? (TimeSpan?)(at - DateTimeOffset.UtcNow) : null)
                   ?? RateLimitWait(raw, r);
        return new AnythinkException(raw, (int)r.StatusCode) { RetryAfter = wait is { } w && w > TimeSpan.Zero ? w : null };
    }

    // The rate limiter reports its wait as retry_after seconds in the body or an X-RateLimit-Reset header, not Retry-After.
    private static TimeSpan? RateLimitWait(string raw, HttpResponseMessage r)
    {
        if (r.StatusCode != (System.Net.HttpStatusCode)429) return null;
        try
        {
            if (JsonNode.Parse(raw) is JsonObject o && o["retry_after"] is JsonValue v && v.TryGetValue<double>(out var secs))
                return TimeSpan.FromSeconds(secs);
        }
        catch (JsonException) { }
        if (r.Headers.TryGetValues("X-RateLimit-Reset", out var values) && long.TryParse(values.FirstOrDefault(), out var reset))
            return reset > 1_000_000_000 ? DateTimeOffset.FromUnixTimeSeconds(reset) - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(reset);
        return null;
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage r)
    {
        var raw = await r.Content.ReadAsStringAsync();
        if (!r.IsSuccessStatusCode) throw Failure(raw, r);
        if (string.IsNullOrWhiteSpace(raw)) return default;
        try   { return JsonSerializer.Deserialize<T>(raw, JsonOpts); }
        catch (JsonException ex)
              { throw new AnythinkException($"Parse error: {ex.Message}\n{raw}", (int)r.StatusCode); }
    }
}
