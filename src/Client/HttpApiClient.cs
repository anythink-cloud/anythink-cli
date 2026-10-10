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

    public string StatusOnlyMessage => StatusCode is >= 200 and < 300
        ? "The project API returned a response this command couldn't read."
        : $"The project API returned status {StatusCode}.";
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

    private static readonly SocketsHttpHandler SharedHandler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        UseCookies = false
    };

    protected static HttpClient NewClient() => new(SharedHandler, disposeHandler: false);

    protected static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    protected HttpApiClient(string? token, string? apiKey)
    {
        Http = NewClient();
        if (!string.IsNullOrEmpty(apiKey))
            Http.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        else if (!string.IsNullOrEmpty(token))
            Http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>Test-only constructor — accepts a pre-configured HttpClient (e.g. with a mock handler).</summary>
    internal HttpApiClient(HttpClient http) { Http = http; }

    private string? _confinedTo;

    protected void ConfineTo(string root) => _confinedTo = new Uri(root).AbsoluteUri.TrimEnd('/');

    // Uri collapses ".." segments, so the check has to run on the normalised form.
    protected Uri Target(string url)
    {
        var uri = new Uri(url);
        if (_confinedTo is not null)
        {
            var path = uri.GetLeftPart(UriPartial.Path);
            if (path != _confinedTo && !path.StartsWith(_confinedTo + "/", StringComparison.Ordinal))
                throw new InvalidOperationException("The request path leaves the project.");
        }
        return uri;
    }

    protected async Task<T?> GetAsync<T>(string url) =>
        await DeserializeAsync<T>(await Http.GetAsync(Target(url), ClientContext.Cancellation));

    protected async Task<T> PostAsync<T>(string url, object? body = null)
    {
        var r = await Http.PostAsync(Target(url), Serialize(body ?? new { }), ClientContext.Cancellation);
        return await DeserializeAsync<T>(r)
               ?? throw new AnythinkException("Empty response.", (int)r.StatusCode);
    }

    protected async Task<T?> PutAsync<T>(string url, object body)
    {
        var r = await Http.PutAsync(Target(url), Serialize(body), ClientContext.Cancellation);
        // 204 No Content = success with no body (e.g. entity item updates)
        if (r.StatusCode == System.Net.HttpStatusCode.NoContent) return default;
        return await DeserializeAsync<T>(r)
               ?? throw new AnythinkException("Empty response.", (int)r.StatusCode);
    }

    protected async Task PostVoidAsync(string url, object? body = null)
    {
        var r = await Http.PostAsync(Target(url), Serialize(body ?? new { }), ClientContext.Cancellation);
        if (!r.IsSuccessStatusCode)
            throw new AnythinkException(await r.Content.ReadAsStringAsync(), (int)r.StatusCode);
    }

    protected async Task PutVoidAsync(string url, object body)
    {
        var r = await Http.PutAsync(Target(url), Serialize(body), ClientContext.Cancellation);
        if (!r.IsSuccessStatusCode)
            throw new AnythinkException(await r.Content.ReadAsStringAsync(), (int)r.StatusCode);
    }

    protected async Task DeleteAsync(string url)
    {
        var r = await Http.DeleteAsync(Target(url), ClientContext.Cancellation);
        if (!r.IsSuccessStatusCode)
            throw new AnythinkException(await r.Content.ReadAsStringAsync(), (int)r.StatusCode);
    }

    protected async Task<T?> DeleteAsync<T>(string url)
    {
        var r = await Http.DeleteAsync(url);
        return await DeserializeAsync<T>(r);
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
        try { return JsonSerializer.Deserialize<T>(raw, JsonOpts); }
        catch (JsonException ex)
        { throw new AnythinkException($"Parse error: {ex.Message}\n{raw}", (int)r.StatusCode); }
    }
}
