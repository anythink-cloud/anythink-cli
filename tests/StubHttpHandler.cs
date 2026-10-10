using System.Net;
using System.Text;

namespace AnythinkCli.Tests;

internal sealed record RecordedRequest(HttpMethod Method, string PathAndQuery, string Body);

internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly List<(HttpMethod? Method, string Path, HttpStatusCode Status, string Body)> _routes = [];

    public List<RecordedRequest> Requests { get; } = [];

    public StubHttpHandler On(string pathAndQuery, string json, HttpStatusCode status = HttpStatusCode.OK)
        => On(null, pathAndQuery, json, status);

    public StubHttpHandler On(HttpMethod? method, string pathAndQuery, string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        _routes.Add((method, pathAndQuery, status, json));
        return this;
    }

    public IEnumerable<RecordedRequest> Writes =>
        Requests.Where(r => r.Method != HttpMethod.Get);

    public IEnumerable<RecordedRequest> Matching(HttpMethod method, string pathContains) =>
        Requests.Where(r => r.Method == method && r.PathAndQuery.Contains(pathContains));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        var path = request.RequestUri!.PathAndQuery;
        Requests.Add(new RecordedRequest(request.Method, path, body));

        var route = _routes.LastOrDefault(r => r.Path == path && (r.Method is null || r.Method == request.Method));
        if (route.Path is null)
            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("no stub", Encoding.UTF8) };

        return new HttpResponseMessage(route.Status) { Content = new StringContent(route.Body, Encoding.UTF8, "application/json") };
    }
}
