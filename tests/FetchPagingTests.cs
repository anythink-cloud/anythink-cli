using AnythinkCli.Client;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkCli.Tests;

public class FetchPagingTests
{
    private const string BaseUrl = "https://api.example.com";
    private const string OrgId = "99999";
    private const string JobsUrl = $"{BaseUrl}/org/{OrgId}/workflows/43/jobs";

    private static string Page(int page, bool hasNext, int items = 1)
        => $$"""{"items":[{{string.Join(",", Enumerable.Repeat("{\"id\":1}", items))}}],"page":{{page}},"has_next_page":{{(hasNext ? "true" : "false")}}}""";

    private static async Task<(List<string> Bodies, List<Uri> Sent)> FetchAll(string url, Func<int, string> respond)
    {
        var sent = new List<Uri>();
        var handler = new MockHttpMessageHandler();
        handler.When($"{JobsUrl}*").Respond(req =>
        {
            sent.Add(req.RequestUri!);
            var page = FetchPaging.QueryInt(req.RequestUri!.AbsoluteUri, "page") ?? 0;
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(respond(page), System.Text.Encoding.UTF8, "application/json"),
            };
        });

        var bodies = new List<string>();
        await foreach (var body in new AnythinkClient(OrgId, BaseUrl, new HttpClient(handler)).FetchPagesAsync(url))
            bodies.Add(body);
        return (bodies, sent);
    }

    [Fact]
    public async Task FetchPagesAsync_FollowsPagesUntilTheLast()
    {
        var (bodies, sent) = await FetchAll(JobsUrl, page => Page(page, hasNext: page < 3));

        bodies.Should().HaveCount(3);
        sent.Select(u => FetchPaging.QueryInt(u.AbsoluteUri, "page")).Should().Equal(1, 2, 3);
        sent.Should().OnlyContain(u => FetchPaging.QueryInt(u.AbsoluteUri, "pageSize") == FetchPaging.MaxPageSize);
    }

    [Fact]
    public async Task FetchPagesAsync_CapsOversizedPageSize()
    {
        var (_, sent) = await FetchAll($"{JobsUrl}?pageSize=4000", page => Page(page, hasNext: false));

        FetchPaging.QueryInt(sent.Single().AbsoluteUri, "pageSize").Should().Be(FetchPaging.MaxPageSize);
    }

    [Fact]
    public async Task FetchPagesAsync_KeepsSmallerPageSizeStartPageAndOtherParams()
    {
        var (_, sent) = await FetchAll($"{JobsUrl}?pageSize=20&page=4&status=Failed", page => Page(page, hasNext: page < 5));

        sent.Select(u => FetchPaging.QueryInt(u.AbsoluteUri, "page")).Should().Equal(4, 5);
        sent.Should().OnlyContain(u => FetchPaging.QueryInt(u.AbsoluteUri, "pageSize") == 20 && u.Query.Contains("status=Failed"));
    }

    [Fact]
    public async Task FetchPagesAsync_StopsOnAnEmptyPageEvenIfMoreIsClaimed()
    {
        var (bodies, _) = await FetchAll(JobsUrl, page => Page(page, hasNext: true, items: page < 2 ? 1 : 0));

        bodies.Should().HaveCount(2);
    }

    [Theory]
    [InlineData($"{JobsUrl}?pageSize=4000&page=2", $"{JobsUrl}?pageSize=100&page=2")]
    [InlineData($"{JobsUrl}?pageSize=100", $"{JobsUrl}?pageSize=100")]
    [InlineData(JobsUrl, JobsUrl)]
    [InlineData("workflows/43/jobs?pageSize=4000", "workflows/43/jobs?pageSize=4000")]
    public void CapPageSize_CapsOnlyOversizedAbsoluteUrls(string url, string expected)
        => FetchPaging.CapPageSize(url).Should().Be(expected);

    [Theory]
    [InlineData("""{"id":1}""")]
    [InlineData("""[{"id":1}]""")]
    [InlineData("not json")]
    public async Task FetchPagesAsync_UnpagedResponse_ReturnsOnce(string body)
    {
        var (bodies, _) = await FetchAll(JobsUrl, _ => body);

        bodies.Should().Equal(body);
    }
}
