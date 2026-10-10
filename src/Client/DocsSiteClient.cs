using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnythinkCli.Client;

public sealed record DocsSearchHit(string Title, string Path, string Url, string Snippet);

public sealed record DocsSearchResult(string Query, IReadOnlyList<DocsSearchHit> Results, int Total);

public sealed record DocsPage(string Title, string Path, string Url, string Markdown);

public sealed class DocsPageNotFoundException(string message) : Exception(message);

public sealed class DocsSiteClient
{
    public const string BaseUrl = "https://docs.anythink.cloud";
    private const string Host = "docs.anythink.cloud";
    private const int MaxRedirects = 5;

    private static readonly Lazy<DocsSiteClient> Shared = new(() =>
        new DocsSiteClient(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) }));

    private static readonly AsyncLocal<DocsSiteClient?> Ambient = new();

    private readonly HttpClient _http;

    // Redirects are followed by hand so they can never leave the docs site; pass a handler that doesn't follow them itself.
    public DocsSiteClient(HttpClient http) => _http = http;

    public static DocsSiteClient Current
    {
        get => Ambient.Value ?? Shared.Value;
        set => Ambient.Value = value;
    }

    public async Task<DocsSearchResult> SearchAsync(string query, int limit, CancellationToken cancellationToken = default)
    {
        var url = $"{BaseUrl}/api/search?q={Uri.EscapeDataString(query)}&maxResults={limit}";
        using var response = await SendAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"The docs search returned {(int)response.StatusCode}.", null, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var payload = JsonSerializer.Deserialize<SearchPayload>(body) ?? new SearchPayload();
        var hits = (payload.Results ?? [])
            .Where(r => !string.IsNullOrWhiteSpace(r.Url))
            .Take(limit)
            .Select(r => new DocsSearchHit(r.Title ?? r.Url!, r.Url!, r.Url!.StartsWith('/') ? BaseUrl + r.Url : r.Url!, r.Excerpt ?? ""))
            .ToList();
        return new DocsSearchResult(query, hits, hits.Count);
    }

    public async Task<DocsPage> ReadAsync(string slugOrUrl, CancellationToken cancellationToken = default)
    {
        var path = ToPath(slugOrUrl);

        var page = await MarkdownAsync(path, cancellationToken);
        if (page is null && await ResolvePageAsync(path, cancellationToken) is { } moved && moved != path)
            page = await MarkdownAsync(moved, cancellationToken);
        if (page is null && !path[1..].Contains('/') && await FindBySlugAsync(path[1..], cancellationToken) is { } listed)
            page = await MarkdownAsync(listed, cancellationToken);

        return page ?? throw new DocsPageNotFoundException(
            $"No guide found at {BaseUrl}{path}. Find the right page with 'anythink docs search <query>'.");
    }

    internal static string ToPath(string slugOrUrl)
    {
        var input = slugOrUrl.Trim();
        if (input.Length == 0)
            throw new ArgumentException("Give a guide path, slug or docs URL.");

        string path;
        if (input.Contains("://", StringComparison.Ordinal) || input.StartsWith(Host, StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(input.Contains("://", StringComparison.Ordinal) ? input : "https://" + input, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("https" or "http")
                || !uri.Host.Equals(Host, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Only pages on {BaseUrl} can be read.");
            path = uri.AbsolutePath;
        }
        else
        {
            path = input.Split('?', '#')[0];
        }

        path = "/" + path.Trim('/');
        if (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            path = path[..^3];
        if (path == "/" || path.Contains("..", StringComparison.Ordinal) || path.Any(c => char.IsWhiteSpace(c) || c == '\\'))
            throw new ArgumentException($"'{slugOrUrl}' isn't a guide path, slug or docs URL.");
        return path;
    }

    private async Task<DocsPage?> MarkdownAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync($"{BaseUrl}{path}.md", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"The docs site returned {(int)response.StatusCode}.", null, response.StatusCode);
        if (response.Content.Headers.ContentType?.MediaType is "text/html")
            return null;

        var markdown = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
        var finalPath = response.RequestMessage!.RequestUri!.AbsolutePath;
        finalPath = finalPath.EndsWith(".md", StringComparison.Ordinal) ? finalPath[..^3] : finalPath;
        return new DocsPage(TitleOf(markdown, finalPath), finalPath, BaseUrl + finalPath, markdown);
    }

    // Search returns some pages under older paths that only the page route redirects to their current home.
    private async Task<string?> ResolvePageAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(BaseUrl + path, cancellationToken);
        return response.IsSuccessStatusCode ? response.RequestMessage!.RequestUri!.AbsolutePath : null;
    }

    private async Task<string?> FindBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        using var response = await SendAsync($"{BaseUrl}/llms.txt", cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        var wanted = slug.Replace('_', '-');
        var index = await response.Content.ReadAsStringAsync(cancellationToken);
        return LinkedPaths(index).FirstOrDefault(p => p.Split('/')[^1].Replace('_', '-').Equals(wanted, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> LinkedPaths(string index)
    {
        foreach (var part in index.Split("](", StringSplitOptions.None).Skip(1))
        {
            var end = part.IndexOf(')');
            if (end > 0 && Uri.TryCreate(part[..end], UriKind.Absolute, out var uri)
                && uri.Host.Equals(Host, StringComparison.OrdinalIgnoreCase) && uri.AbsolutePath.Length > 1)
                yield return uri.AbsolutePath.TrimEnd('/');
        }
    }

    private async Task<HttpResponseMessage> SendAsync(string url, CancellationToken cancellationToken)
    {
        var target = new Uri(url);
        for (var hop = 0; ; hop++)
        {
            var response = await _http.SendAsync(new HttpRequestMessage(HttpMethod.Get, target), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.RequestMessage ??= new HttpRequestMessage(HttpMethod.Get, target);
            var status = (int)response.StatusCode;
            if (status is < 300 or >= 400 || response.Headers.Location is null || hop == MaxRedirects)
                return response;

            var next = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(target, response.Headers.Location);
            response.Dispose();
            if (!next.Host.Equals(Host, StringComparison.OrdinalIgnoreCase) || next.Scheme != "https")
                return new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = new HttpRequestMessage(HttpMethod.Get, target) };
            target = next;
        }
    }

    private static string TitleOf(string markdown, string path)
    {
        var heading = markdown.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("# ", StringComparison.Ordinal));
        return heading is not null ? heading[2..].Trim() : path.Split('/')[^1];
    }

    private sealed class SearchPayload
    {
        [JsonPropertyName("results")] public List<SearchItem>? Results { get; set; }
    }

    private sealed class SearchItem
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("excerpt")] public string? Excerpt { get; set; }
    }
}
