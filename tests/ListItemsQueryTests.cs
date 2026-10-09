using AnythinkCli.Client;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkCli.Tests;

public class ListItemsQueryTests
{
    private const string BaseUrl = "https://api.example.com";
    private const string OrgId = "99999";
    private const string ItemsUrl = $"{BaseUrl}/org/{OrgId}/entities/blog_posts/items";
    private const string EmptyPage = """{"items":[],"total_items":0,"total_pages":0,"has_next_page":false,"page":1,"page_size":20}""";

    private static async Task<List<KeyValuePair<string, string>>> SentQuery(int page = 1, int pageSize = 20, string? filter = null)
    {
        Uri? sent = null;
        var handler = new MockHttpMessageHandler();
        handler.When($"{ItemsUrl}*").Respond(req =>
        {
            sent = req.RequestUri;
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(EmptyPage, System.Text.Encoding.UTF8, "application/json"),
            };
        });

        var client = new AnythinkClient(OrgId, BaseUrl, new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) });
        await client.ListItemsAsync("blog_posts", page, pageSize, filter);

        return sent!.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Select(kv => new KeyValuePair<string, string>(Uri.UnescapeDataString(kv[0]), Uri.UnescapeDataString(kv[1])))
            .ToList();
    }

    private static KeyValuePair<string, string> P(string key, string value) => new(key, value);

    private static IEnumerable<string> Parsed(string? filter)
        => ItemFilterQuery.Parse(filter).Select(p => $"{p.Key}={p.Value}");

    // ── Pagination: the API only reads `page` and `pageSize` ─────────────────

    [Fact]
    public async Task ListItems_PageSize_SentAsPageSizeParam()
    {
        var query = await SentQuery(pageSize: 75);

        query.Should().Contain(P("pageSize", "75"));
    }

    [Fact]
    public async Task ListItems_Page_SentAsPageParam()
    {
        var query = await SentQuery(page: 3);

        query.Should().Contain(P("page", "3"));
    }

    [Fact]
    public async Task ListItems_NeverSendsLimitOrFilterParams()
    {
        var query = await SentQuery(filter: """{"status":"draft"}""");

        query.Select(p => p.Key).Should().NotContain(["limit", "filter"]);
    }

    // ── Filters become one query param per field ─────────────────────────────

    [Fact]
    public async Task ListItems_JsonEquality_SentAsFieldParam()
    {
        var query = await SentQuery(filter: """{"status":"draft","views":5,"featured":true}""");

        query.Should().Contain([P("status", "draft"), P("views", "5"), P("featured", "true")]);
    }

    [Fact]
    public async Task ListItems_FilterValues_AreUrlEncoded()
    {
        var query = await SentQuery(filter: """{"title":"fish & chips=great"}""");

        query.Should().Contain(P("title", "fish & chips=great"));
    }

    [Fact]
    public async Task ListItems_NoFilter_SendsOnlyPagination()
    {
        var query = await SentQuery();

        query.Select(p => p.Key).Should().BeEquivalentTo(["page", "pageSize"]);
    }

    // ── JSON operator objects map to the API's operator prefixes ─────────────

    [Theory]
    [InlineData("eq", "draft", "draft")]
    [InlineData("ne", "draft", "!draft")]
    [InlineData("neq", "draft", "!draft")]
    [InlineData("gt", "5", "GT:5")]
    [InlineData("gte", "5", "GTE:5")]
    [InlineData("lt", "5", "LT:5")]
    [InlineData("lte", "5", "LTE:5")]
    [InlineData("contains", "foo", "C:foo")]
    [InlineData("not_contains", "foo", "NC:foo")]
    [InlineData("ncontains", "foo", "NC:foo")]
    [InlineData("starts_with", "foo", "SW:foo")]
    [InlineData("ends_with", "foo", "EW:foo")]
    [InlineData("in", "a,b", "IN:a,b")]
    public void Parse_SingleOperator_MapsToPrefixOnFieldKey(string op, string value, string expected)
    {
        Parsed($$$"""{"title":{"{{{op}}}":"{{{value}}}"}}""")
            .Should().Equal($"title={expected}");
    }

    [Fact]
    public void Parse_OperatorNames_AreCaseInsensitive()
    {
        Parsed("""{"views":{"GTE":10}}""").Should().Equal("views=GTE:10");
    }

    [Fact]
    public void Parse_NumericOperand_UsesInvariantFormatting()
    {
        Parsed("""{"price":{"lt":9.99}}""").Should().Equal("price=LT:9.99");
    }

    [Fact]
    public void Parse_MultipleOperatorsOnOneField_UseSuffixedKeys()
    {
        Parsed("""{"price":{"gte":10,"lt":100}}""")
            .Should().Equal("price__gte=GTE:10", "price__lt=LT:100");
    }

    [Fact]
    public void Parse_ArrayValue_MapsToIn()
    {
        Parsed("""{"status":["draft","review"],"id":{"in":[1,2,3]}}""")
            .Should().Equal("status=IN:draft,review", "id=IN:1,2,3");
    }

    [Fact]
    public void Parse_NullValue_MapsToIsNull()
    {
        Parsed("""{"published_at":null}""").Should().Equal("published_at=NULL:");
    }

    [Fact]
    public void Parse_NeNull_MapsToIsNotNull()
    {
        Parsed("""{"published_at":{"ne":null}}""").Should().Equal("published_at=NNULL:");
    }

    [Fact]
    public void Parse_NeNullAlongsideOtherOperators_UsesNullSuffixKey()
    {
        Parsed("""{"published_at":{"ne":null,"gt":"2026-01-01"}}""")
            .Should().Equal("published_at__null=NNULL:", "published_at__gt=GT:2026-01-01");
    }

    [Theory]
    [InlineData("null", true, "NULL:")]
    [InlineData("null", false, "NNULL:")]
    [InlineData("exists", true, "EXISTS:")]
    [InlineData("exists", false, "NOT_EXISTS:")]
    public void Parse_BooleanSentinelOperators_MapToSentinels(string op, bool flag, string expected)
    {
        Parsed($$$"""{"author":{"{{{op}}}":{{{(flag ? "true" : "false")}}}}}""")
            .Should().Equal($"author={expected}");
    }

    [Fact]
    public void Parse_RawOperatorStrings_PassThrough()
    {
        Parsed("""{"price":"GT:5"}""").Should().Equal("price=GT:5");
    }

    [Fact]
    public void Parse_DottedRelationalField_KeptAsKey()
    {
        Parsed("""{"author.name":{"contains":"ann"}}""").Should().Equal("author.name=C:ann");
    }

    // ── field=value pair form ────────────────────────────────────────────────

    [Fact]
    public void Parse_PairForm_SplitsOnAmpersandAndFirstEquals()
    {
        Parsed("status=draft & price=GTE:10&note=a=b")
            .Should().Equal("status=draft", "price=GTE:10", "note=a=b");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_Blank_ReturnsNoParams(string? filter)
    {
        Parsed(filter).Should().BeEmpty();
    }

    // ── Bad input fails loudly instead of being silently ignored ─────────────

    [Theory]
    [InlineData("""{"status":"draft" """)]
    [InlineData("""{"price":{"between":[1,2]}}""")]
    [InlineData("""{"price":{"gt":[1,2]}}""")]
    [InlineData("""{"price":{"gt":null}}""")]
    [InlineData("""{"author":{"exists":"yes"}}""")]
    [InlineData("""{"meta":{"eq":{"a":1}}}""")]
    [InlineData("status")]
    [InlineData("=draft")]
    public void Parse_InvalidFilter_Throws(string filter)
    {
        var act = () => ItemFilterQuery.Parse(filter);

        act.Should().Throw<ArgumentException>();
    }
}
