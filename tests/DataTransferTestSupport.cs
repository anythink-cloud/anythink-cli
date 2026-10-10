using System.Net;
using System.Text;
using AnythinkCli.Client;

namespace AnythinkCli.Tests;

internal sealed class FakeApi : HttpMessageHandler
{
    public const string FieldsJson = """
        [{"name":"id","database_type":"integer"},{"name":"created_at","database_type":"timestamp"},
         {"name":"name","database_type":"varchar"},{"name":"zip","database_type":"varchar"},
         {"name":"age","database_type":"integer"},{"name":"price","database_type":"decimal"},
         {"name":"active","database_type":"boolean"},{"name":"meta","database_type":"jsonb"},
         {"name":"born","database_type":"date"},{"name":"status","database_type":"varchar"},
         {"name":"seen","database_type":"timestamp"},{"name":"big","database_type":"bigint"},
         {"name":"orders","database_type":"one-to-many"},{"name":"tags","database_type":"many-to-many"},
         {"name":"owner","database_type":"user"},{"name":"api_secret","database_type":"secret"}]
        """;

    public List<string> Posted { get; } = [];
    public List<string> Gets { get; } = [];
    public Func<int, HttpResponseMessage> OnPost { get; set; } = _ => Json(HttpStatusCode.Created, "{\"id\":1}");
    public Func<int, Exception?>? OnPostThrow { get; set; }
    public Func<Uri, HttpResponseMessage>? OnGetItems { get; set; }
    private int _posts, _inFlight, _maxInFlight;
    public int MaxInFlight => _maxInFlight;
    public TimeSpan PostLatency { get; set; }

    public static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Get)
        {
            Gets.Add(request.RequestUri.PathAndQuery);
            return path.EndsWith("/fields") ? Json(HttpStatusCode.OK, FieldsJson)
                : OnGetItems?.Invoke(request.RequestUri) ?? Json(HttpStatusCode.OK, "{\"items\":[],\"has_next_page\":false}");
        }

        var now = Interlocked.Increment(ref _inFlight);
        int seen;
        while (now > (seen = _maxInFlight)) Interlocked.CompareExchange(ref _maxInFlight, now, seen);
        try
        {
            if (PostLatency > TimeSpan.Zero) await Task.Delay(PostLatency, ct);
            var body = await request.Content!.ReadAsStringAsync(ct);
            lock (Posted) Posted.Add(body);
            var n = Interlocked.Increment(ref _posts);
            if (OnPostThrow?.Invoke(n) is { } boom) throw boom;
            return OnPost(n);
        }
        finally { Interlocked.Decrement(ref _inFlight); }
    }

    public AnythinkClient Client() => new("1", "https://api.example.com", new HttpClient(this));
}

internal sealed class TempDir : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("anythink-dt-").FullName;
    public string File(string name, string? content = null)
    {
        var p = System.IO.Path.Combine(Path, name);
        if (content is not null) System.IO.File.WriteAllText(p, content);
        return p;
    }
    public void Dispose() => Directory.Delete(Path, true);
}
