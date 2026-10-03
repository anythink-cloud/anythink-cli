using System.ComponentModel;
using System.Reflection;
using FluentAssertions;
using ModelContextProtocol;
using RichardSzalay.MockHttp;
using AnythinkMcp.Tools;

namespace AnythinkMcp.Tests;

public class HostedToolsTests
{
    private const string ItemsUrl = "https://42.api.anythink.cloud/org/42/entities/blog_posts/items";
    private const string EmptyPage = """{"items":[],"total_items":0,"total_pages":0,"has_next_page":false,"page":1,"page_size":20}""";

    private readonly MockHttpMessageHandler _mock = new();
    private Uri? _sent;

    private HostedTools Tools()
    {
        _mock.When($"{ItemsUrl}*").Respond(req =>
        {
            _sent = req.RequestUri;
            return new HttpResponseMessage { Content = new StringContent(EmptyPage) };
        });

        return new HostedTools(
            new McpClientFactory(null, _mock),
            new HostedCredentials { OrgId = "42", InstanceUrl = "https://42.api.anythink.cloud", Token = "t" });
    }

    private string Query => _sent!.Query;

    [Theory]
    [InlineData("../x")]
    [InlineData("a?b")]
    [InlineData("a/b")]
    [InlineData("Blog_Posts")]
    [InlineData("a b")]
    [InlineData("a\n")]
    [InlineData("")]
    public async Task RecordsQuery_InvalidEntityName_IsAToolError_AndNothingIsSent(string entity)
    {
        var tools = Tools();

        var act = () => tools.RecordsQuery(entity);

        await act.Should().ThrowAsync<McpException>();
        _sent.Should().BeNull();
    }

    [Fact]
    public async Task RecordsQuery_ValidEntityName_IsQueried()
    {
        await Tools().RecordsQuery("blog_posts");
        _sent!.AbsolutePath.Should().EndWith("/entities/blog_posts/items");
    }

    [Theory]
    [InlineData(0, 0, "page=1", "pageSize=1")]
    [InlineData(-5, -5, "page=1", "pageSize=1")]
    [InlineData(3, 5000, "page=3", "pageSize=1000")]
    [InlineData(2, 1000, "page=2", "pageSize=1000")]
    public async Task RecordsQuery_PagingIsClamped(int page, int pageSize, string expectedPage, string expectedSize)
    {
        await Tools().RecordsQuery("blog_posts", page: page, pageSize: pageSize);
        Query.Should().Contain(expectedPage).And.Contain(expectedSize);
    }

    [Fact]
    public async Task RecordsQuery_JsonFilter_BecomesPerFieldQueryParams()
    {
        await Tools().RecordsQuery("blog_posts", filter: """{"status":"draft","price":{"gte":10}}""");

        Query.Should().Contain("status=draft").And.Contain("price=GTE%3A10").And.NotContain("filter=");
    }

    [Fact]
    public async Task RecordsQuery_PairFilter_BecomesPerFieldQueryParams()
    {
        await Tools().RecordsQuery("blog_posts", filter: "status=draft&price=GT:10");

        Query.Should().Contain("status=draft").And.Contain("price=GT%3A10");
    }

    [Fact]
    public async Task RecordsQuery_MalformedFilter_IsAToolError()
    {
        var tools = Tools();

        var act = () => tools.RecordsQuery("blog_posts", filter: """{"price":{"bogus":1}}""");

        await act.Should().ThrowAsync<McpException>().WithMessage("*bogus*");
        _sent.Should().BeNull();
    }

    [Theory]
    [InlineData("""{"page":2}""")]
    [InlineData("""{"pageSize":5000}""")]
    [InlineData("""{"fields":"id"}""")]
    [InlineData("""{"sortBy":"id"}""")]
    [InlineData("""{"sortDirection":"desc"}""")]
    [InlineData("""{"group_scope":"7"}""")]
    [InlineData("""{"page":{"gte":1,"lte":9}}""")]
    [InlineData("page=2")]
    public async Task RecordsQuery_ReservedFilterKey_IsAToolError_AndNothingIsSent(string filter)
    {
        var tools = Tools();

        var act = () => tools.RecordsQuery("blog_posts", filter: filter);

        await act.Should().ThrowAsync<McpException>().WithMessage("*reserved*");
        _sent.Should().BeNull();
    }

    [Fact]
    public async Task RecordsQuery_Fields_AreEscaped()
    {
        await Tools().RecordsQuery("blog_posts", fields: "id,title&page=99");

        Query.Should().Contain("fields=id%2Ctitle%26page%3D99");
        Query.Should().NotContain("page=99");
    }

    [Theory]
    [InlineData("eq"), InlineData("ne"), InlineData("gt"), InlineData("gte"), InlineData("lt"), InlineData("lte")]
    [InlineData("contains"), InlineData("not_contains"), InlineData("starts_with"), InlineData("ends_with")]
    [InlineData("in"), InlineData("null"), InlineData("exists")]
    public void RecordsQuery_Description_DocumentsFilterOperator(string op)
    {
        var description = typeof(HostedTools).GetMethod(nameof(HostedTools.RecordsQuery))!
            .GetCustomAttribute<DescriptionAttribute>()!.Description;

        description.Should().MatchRegex($@"\b{op}\b");
    }
}
