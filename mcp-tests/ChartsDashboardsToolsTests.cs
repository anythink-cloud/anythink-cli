using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AnythinkCli.Client;
using AnythinkMcp.Cli;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkMcp.Tests;

public class ChartsDashboardsToolsTests
{
    private const string Api = "https://api.my.anythink.cloud";
    private const string Org = Api + "/org/42";

    private const string Chart12 = """
        {"id":12,"name":"Orders by status","data_source":"entity","chart_type":"pie","entity_name":"orders","x_field":"status","y_field":null,
         "y_agg":null,"timeframe_preset":"30d","limit":10,"filters":"[{\"field\":\"region\",\"op\":\"eq\",\"value\":\"uk\"}]",
         "funnel_stages":null,"config":null,"tenant_id":42,"created_by":7,"updated_by":7,"locked":false,
         "created_at":"2026-01-01T00:00:00Z","updated_at":"2026-01-02T00:00:00Z"}
        """;

    private const string Chart14 = """{"id":14,"name":"Revenue","data_source":"entity","chart_type":"stat","entity_name":"orders","tenant_id":42}""";

    private const string Dashboard3 = """
        {"id":3,"name":"Sales","description":"Weekly numbers","is_default":false,"owner_user_id":7,"is_shared":true,"allow_shared_edit":false,
         "is_home":true,"tenant_id":42,"locked":false,
         "layout_json":"[{\"widgetId\":41,\"x\":0,\"y\":0,\"w\":6,\"h\":3,\"minH\":2},{\"widgetId\":42,\"x\":6,\"y\":0,\"w\":6,\"h\":3}]",
         "widgets":[
           {"id":42,"type":"markdown","title":"Notes","config_json":"{\"body\":\"Hello\"}","sort_order":1,"dashboard_id":3,"tenant_id":42},
           {"id":41,"type":"chart","title":"Orders by status","config_json":"{\"chartId\":12}","sort_order":0,"dashboard_id":3,"tenant_id":42}]}
        """;

    private const string Dashboard3WithTheNewChart = """
        {"id":3,"name":"Sales","is_shared":true,"is_home":true,
         "layout_json":"[{\"widgetId\":41,\"x\":0,\"y\":0,\"w\":6,\"h\":3,\"minH\":2},{\"widgetId\":42,\"x\":6,\"y\":0,\"w\":6,\"h\":3}]",
         "widgets":[
           {"id":41,"type":"chart","title":"Orders by status","config_json":"{\"chartId\":12}","sort_order":0},
           {"id":42,"type":"markdown","title":"Notes","config_json":"{\"body\":\"Hello\"}","sort_order":1},
           {"id":43,"type":"chart","title":"Revenue","config_json":"{\"chartId\":14}","sort_order":2}]}
        """;

    private const string WidgetsOf3 = """
        [{"id":41,"type":"chart","title":"Orders by status","config_json":"{\"chartId\":12}","sort_order":0},
         {"id":42,"type":"markdown","title":"Notes","config_json":"{\"body\":\"Hello\"}","sort_order":1}]
        """;

    public static TheoryData<CliToolScope> RemoteScopes => new() { CliToolScope.Internal, CliToolScope.Hosted };

    public static TheoryData<CliToolScope> AllScopes => new() { CliToolScope.Local, CliToolScope.Internal, CliToolScope.Hosted };

    private static Dictionary<string, JsonElement> Args(object values) =>
        JsonSerializer.SerializeToElement(values).EnumerateObject().ToDictionary(p => p.Name, p => p.Value);

    private static AnythinkClient Client(MockHttpMessageHandler mock) => new("42", Api, new HttpClient(mock));

    private static Task<CliRunResult> Run(CliToolScope scope, string tool, object args, MockHttpMessageHandler mock) =>
        CliCommandTool.All(scope).Single(t => t.ProtocolTool.Name == tool).RunAsync(Args(args), Client(mock));

    private static Task<CliRunResult> RunCli(CliToolScope scope, MockHttpMessageHandler mock, params string[] args) =>
        CliRunner.RunAsync(args, Client(mock), scope);

    private sealed class Sent
    {
        public string? Body { get; set; }

        public JsonNode Json => JsonNode.Parse(Body ?? throw new InvalidOperationException("The request had no body."))!;
    }

    // Spinner text is parsed on a refresh thread, so only a response that outlasts the first refresh exercises it.
    private static Sent Reply(MockHttpMessageHandler mock, HttpMethod method, string path, string? json = null, bool slow = false, HttpStatusCode status = HttpStatusCode.OK)
    {
        var sent = new Sent();
        mock.Expect(method, Org + path).Respond(async request =>
        {
            sent.Body = request.Content is null ? null : await request.Content.ReadAsStringAsync();
            if (slow) await Task.Delay(TimeSpan.FromMilliseconds(250));
            return json is null
                ? new HttpResponseMessage(status == HttpStatusCode.OK ? HttpStatusCode.NoContent : status)
                : new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        });
        return sent;
    }

    private static void Matches(JsonNode? actual, string expected) =>
        JsonNode.DeepEquals(actual, JsonNode.Parse(expected)).Should().BeTrue($"expected {expected} but was {actual?.ToJsonString()}");

    private static JsonNode Parsed(CliRunResult result)
    {
        result.ExitCode.Should().Be(0, result.Output);
        return JsonNode.Parse(result.Output)!;
    }

    private static void NothingWasSent(MockHttpMessageHandler mock, MockedRequest any) => mock.GetMatchCount(any).Should().Be(0);

    // ── Rule: each chart command sends the right method, path and body ─────────

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task ChartsList_GetsTheChartsPath_AndReturnsOneSmallRowPerChart(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Get, "/charts", $"[{Chart12},{Chart14}]");

        var result = await Run(scope, "charts_list", new { }, mock);

        var rows = Parsed(result).AsArray();
        rows.Select(r => r!["id"]!.GetValue<int>()).Should().Equal(12, 14);
        rows[0]!.AsObject().ContainsKey("tenant_id").Should().BeFalse();
        rows[0]!.AsObject().ContainsKey("filters").Should().BeFalse();
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task ChartsList_ForAnEntity_AsksForThatEntityOnly(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Get, "/charts?entityName=orders", $"[{Chart12}]");

        var result = await Run(scope, "charts_list", new { entity = "orders" }, mock);

        Parsed(result).AsArray().Should().HaveCount(1);
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task ChartsGet_GetsThatChart_WithItsFiltersAsAListAndNoServerBookkeeping(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Get, "/charts/12", Chart12);

        var result = await Run(scope, "charts_get", new { id = 12 }, mock);

        var chart = Parsed(result);
        chart["id"]!.GetValue<int>().Should().Be(12);
        chart["filters"]!.AsArray()[0]!["field"]!.GetValue<string>().Should().Be("region");
        chart.AsObject().Select(p => p.Key).Should().NotContain(["tenant_id", "created_by", "updated_by", "locked", "y_field", "config"]);
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task ChartsCreate_PostsTheConfigInTheNamesTheApiAccepts_AndReturnsTheNewId(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        var sent = Reply(mock, HttpMethod.Post, "/charts", Chart12, status: HttpStatusCode.Created);

        var result = await Run(scope, "charts_create", new
        {
            name = "Orders by status", entity = "orders", type = "pie", group_by = "status", timeframe = "30d", limit = 10,
            filter = new[] { "region:eq:uk" },
        }, mock);

        Parsed(result)["id"]!.GetValue<int>().Should().Be(12);
        Matches(sent.Json,
            """
            {"dataSource":"entity","name":"Orders by status","chartType":"pie","entityName":"orders","xField":"status",
             "timeframePreset":"30d","limit":10,"filters":[{"field":"region","op":"eq","value":"uk"}]}
            """);
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task ChartsCreate_WithTheRestOfTheConfigInJson_SendsItToo(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        var sent = Reply(mock, HttpMethod.Post, "/charts", Chart12, status: HttpStatusCode.Created);

        var result = await Run(scope, "charts_create", new
        {
            name = "Signup funnel", entity = "users", type = "funnel",
            config = """{"funnelStages":[{"label":"Signed up"},{"label":"Paid","filters":[{"field":"plan","op":"ne","value":"free"}]}],"funnelStyle":"vertical"}""",
        }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        Matches(sent.Json["funnelStages"], """[{"label":"Signed up"},{"label":"Paid","filters":[{"field":"plan","op":"ne","value":"free"}]}]""");
        sent.Json["funnelStyle"]!.GetValue<string>().Should().Be("vertical");
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task ChartsUpdate_ReadsTheChart_ThenPutsItWholeWithOnlyThePassedChange(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Get, "/charts/12", Chart12);
        var sent = Reply(mock, HttpMethod.Put, "/charts/12", Chart12);

        var result = await Run(scope, "charts_update", new { id = 12, name = "Orders by state" }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        Matches(sent.Json,
            """
            {"name":"Orders by state","dataSource":"entity","chartType":"pie","entityName":"orders","xField":"status",
             "timeframePreset":"30d","limit":10,"filters":[{"field":"region","op":"eq","value":"uk"}]}
            """);
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task ChartsDelete_DeletesThatChart(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Delete, "/charts/12");

        var result = await Run(scope, "charts_delete", new { id = 12 }, mock);

        Matches(Parsed(result), """{"id":12,"deleted":true}""");
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task ChartsPreview_PostsTheConfigWithoutSavingIt_AndShowsWhatItWouldDraw(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        var sent = Reply(mock, HttpMethod.Post, "/charts/preview-config",
            """{"chart":{"id":0,"name":"Preview","chart_type":"pie"},"items":[{"status":"paid","value":7},{"status":"open","value":3}],"total":2}""");

        var result = await Run(scope, "charts_preview", new { entity = "orders", type = "pie", group_by = "status" }, mock);

        Matches(sent.Json, """{"dataSource":"entity","chartType":"pie","entityName":"orders","xField":"status"}""");
        var data = Parsed(result);
        data["items"]!.AsArray().Should().HaveCount(2);
        data.AsObject().ContainsKey("chart").Should().BeFalse();
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task ChartsPreview_OfAChartTheApiCantDraw_FailsWithItsReason(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Post, "/charts/preview-config", """{"chart":{},"error":"Unknown metric: nope"}""");

        var result = await Run(scope, "charts_preview", new { type = "line", source = "platform", platform_metric = "nope" }, mock);

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("The chart can't be drawn: Unknown metric: nope");
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task ChartsPlatformMetrics_GetsTheCatalogue(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Get, "/charts/platform-metrics",
            """[{"id":"workflow_runs","label":"Workflow runs","shape":"timeseries","supported_chart_types":["line","stat"],"requires_period":true,"stack_by_options":[{"id":"status","label":"Run status"}]}]""");

        var result = await Run(scope, "charts_platform_metrics", new { }, mock);

        Parsed(result)[0]!["id"]!.GetValue<string>().Should().Be("workflow_runs");
        mock.VerifyNoOutstandingExpectation();
    }

    // ── Rule: each dashboard command sends the right method, path and body ─────

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task DashboardsList_GetsTheDashboardsPath_AndReturnsOneSmallRowEach(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Get, "/dashboards", $"[{Dashboard3}]");

        var result = await Run(scope, "dashboards_list", new { }, mock);

        var row = Parsed(result)[0]!.AsObject();
        row.Select(p => p.Key).Should().Equal("id", "name", "description", "is_default", "is_home", "is_shared", "owner_user_id");
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task DashboardsGet_GetsThatDashboard_WithPositionsOnTheWidgets(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Get, "/dashboards/3", Dashboard3);

        var result = await Run(scope, "dashboards_get", new { id = 3 }, mock);

        var dashboard = Parsed(result);
        dashboard.AsObject().Select(p => p.Key).Should().NotContain(["layout_json", "layout", "tenant_id", "locked"]);
        var widgets = dashboard["widgets"]!.AsArray();
        widgets.Select(w => w!["id"]!.GetValue<int>()).Should().Equal(41, 42);
        widgets[0]!["chart_id"]!.GetValue<int>().Should().Be(12);
        Matches(widgets[0]!["position"], """{"x":0,"y":0,"w":6,"h":3}""");
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task DashboardsMine_GetsTheMePath(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Get, "/dashboards/me", Dashboard3);

        var result = await Run(scope, "dashboards_mine", new { }, mock);

        Parsed(result)["id"]!.GetValue<int>().Should().Be(3);
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task DashboardsCreate_PostsTheDashboardInSnakeCase_WithAChartWidgetTitledAfterEachChart(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Get, "/charts/12", Chart12);
        Reply(mock, HttpMethod.Get, "/charts/14", Chart14);
        var sent = Reply(mock, HttpMethod.Post, "/dashboards", Dashboard3, status: HttpStatusCode.Created);

        var result = await Run(scope, "dashboards_create", new { name = "Sales", description = "Weekly numbers", shared = true, chart = new[] { 12, 14 } }, mock);

        Parsed(result)["id"]!.GetValue<int>().Should().Be(3);
        Matches(sent.Json,
            """
            {"name":"Sales","description":"Weekly numbers","is_shared":true,
             "widgets":[{"type":"chart","title":"Orders by status","config_json":"{\"chartId\":12}","sort_order":0},
                        {"type":"chart","title":"Revenue","config_json":"{\"chartId\":14}","sort_order":1}]}
            """);
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task DashboardsCreate_WithAChartThatDoesntExist_CreatesNothing(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Get, "/charts/99", status: HttpStatusCode.NotFound);
        var any = mock.When(HttpMethod.Post, Org + "/dashboards").Respond("application/json", Dashboard3);

        var result = await Run(scope, "dashboards_create", new { name = "Sales", chart = new[] { 99 } }, mock);

        result.ExitCode.Should().Be(1);
        NothingWasSent(mock, any);
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task DashboardsUpdate_PutsOnlyWhatWasPassed(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        var sent = Reply(mock, HttpMethod.Put, "/dashboards/3", Dashboard3);

        var result = await Run(scope, "dashboards_update", new { id = 3, name = "Sales overview", shared = false }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        Matches(sent.Json, """{"name":"Sales overview","is_shared":false}""");
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task DashboardsDelete_DeletesThatDashboard(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Delete, "/dashboards/3");

        var result = await Run(scope, "dashboards_delete", new { id = 3 }, mock);

        Matches(Parsed(result), """{"id":3,"deleted":true}""");
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task DashboardsData_PostsTheWidgetIds_AndNamesEachWidgetsDataWithoutTheEchoedChart(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Get, "/dashboards/3", Dashboard3);
        var sent = Reply(mock, HttpMethod.Post, "/dashboards/3/data",
            """{"41":{"chart":{"id":12,"name":"Orders by status"},"items":[{"status":"paid","value":7}],"total":1},"42":null}""");

        var result = await Run(scope, "dashboards_data", new { id = 3, widget = new[] { 41, 42 } }, mock);

        Matches(sent.Json, """{"widget_ids":[41,42]}""");
        var widgets = Parsed(result).AsArray();
        widgets.Select(w => w!["title"]!.GetValue<string>()).Should().Equal("Orders by status", "Notes");
        widgets[0]!["data"]!.AsObject().ContainsKey("chart").Should().BeFalse();
        widgets[0]!["data"]!["items"]!.AsArray().Should().HaveCount(1);
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task DashboardsData_WithNoWidgetsNamed_AsksForAllOfThem(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Get, "/dashboards/3", Dashboard3);
        var sent = Reply(mock, HttpMethod.Post, "/dashboards/3/data", "{}");

        var result = await Run(scope, "dashboards_data", new { id = 3 }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        Matches(sent.Json, """{"widget_ids":[]}""");
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task DashboardsSetHome_PostsToTheSetHomePath(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Post, "/dashboards/3/set-home", Dashboard3);

        var result = await Run(scope, "dashboards_set_home", new { id = 3 }, mock);

        Parsed(result)["is_home"]!.GetValue<bool>().Should().BeTrue();
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task DashboardsPromoteToDefault_PostsToThePromotePath(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Post, "/dashboards/3/promote-to-default", Dashboard3);

        var result = await Run(scope, "dashboards_promote_to_default", new { id = 3 }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task DashboardsWidgetPreview_PostsTheTypeAndTheWidgetSettingsAsText(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        var sent = Reply(mock, HttpMethod.Post, "/dashboards/widgets/preview", """{"chart":{"id":12},"value":42,"unit":"records"}""");

        var result = await Run(scope, "dashboards_widget_preview", new { type = "chart", chart = 12 }, mock);

        Matches(sent.Json, """{"type":"chart","config_json":"{\"chartId\":12}"}""");
        Parsed(result)["value"]!.GetValue<int>().Should().Be(42);
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task DashboardsWidgetPreview_OfAWidgetWithNoData_SaysSo(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Post, "/dashboards/widgets/preview", status: HttpStatusCode.NoContent);

        var result = await Run(scope, "dashboards_widget_preview", new { type = "markdown", config = """{"body":"Hi"}""" }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Trim().Should().Be("null");
    }

    // ── Rule: adding a chart keeps the dashboard's other widgets and settings unchanged ──

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task AddChart_PutsEveryExistingWidgetBackAsItWas_PlusTheChart_AndChangesNoSetting(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Get, "/charts/14", Chart14);
        Reply(mock, HttpMethod.Get, "/dashboards/3", Dashboard3);
        var sent = Reply(mock, HttpMethod.Put, "/dashboards/3", Dashboard3WithTheNewChart);

        var result = await Run(scope, "dashboards_add_chart", new { dashboard_id = 3, chart_id = 14 }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        Matches(sent.Json,
            """
            {"widgets":[{"id":41,"type":"chart","title":"Orders by status","config_json":"{\"chartId\":12}","sort_order":0},
                        {"id":42,"type":"markdown","title":"Notes","config_json":"{\"body\":\"Hello\"}","sort_order":1},
                        {"type":"chart","title":"Revenue","config_json":"{\"chartId\":14}","sort_order":2}]}
            """);
        Parsed(result)["widgets"]!.AsArray().Should().HaveCount(3);
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task AddChart_WithAPosition_SavesTheLayoutKeepingEveryOldPositionAsItWas(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Get, "/charts/14", Chart14);
        Reply(mock, HttpMethod.Get, "/dashboards/3", Dashboard3);
        Reply(mock, HttpMethod.Put, "/dashboards/3", Dashboard3WithTheNewChart);
        var layout = Reply(mock, HttpMethod.Put, "/dashboards/3", Dashboard3WithTheNewChart);

        var result = await Run(scope, "dashboards_add_chart",
            new { dashboard_id = 3, chart_id = 14, title = "Revenue this month", column = 0, row = 3, width = 12, height = 4 }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        layout.Json.AsObject().Select(p => p.Key).Should().Equal("layout_json");
        Matches(JsonNode.Parse(layout.Json["layout_json"]!.GetValue<string>()),
            """
            [{"widgetId":41,"x":0,"y":0,"w":6,"h":3,"minH":2},{"widgetId":42,"x":6,"y":0,"w":6,"h":3},
             {"widgetId":43,"x":0,"y":3,"w":12,"h":4}]
            """);
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task AddChart_WithJustAWidth_PlacesTheChartBelowTheExistingWidgets(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Get, "/charts/14", Chart14);
        Reply(mock, HttpMethod.Get, "/dashboards/3", Dashboard3);
        Reply(mock, HttpMethod.Put, "/dashboards/3", Dashboard3WithTheNewChart);
        var layout = Reply(mock, HttpMethod.Put, "/dashboards/3", Dashboard3WithTheNewChart);

        var result = await Run(scope, "dashboards_add_chart", new { dashboard_id = 3, chart_id = 14, width = 6 }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        var added = JsonNode.Parse(layout.Json["layout_json"]!.GetValue<string>())!.AsArray().Last()!;
        Matches(added, """{"widgetId":43,"x":0,"y":3,"w":6,"h":3}""");
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task AddChart_OfAChartThatDoesntExist_ChangesNothing(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Get, "/charts/99", status: HttpStatusCode.NotFound);
        var any = mock.When(HttpMethod.Put, Org + "/dashboards/3").Respond("application/json", Dashboard3);

        var result = await Run(scope, "dashboards_add_chart", new { dashboard_id = 3, chart_id = 99 }, mock);

        result.ExitCode.Should().Be(1);
        NothingWasSent(mock, any);
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task RemoveWidget_PutsTheOtherWidgetsBack_AndDropsOnlyItsOwnPosition(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Get, "/dashboards/3", Dashboard3);
        var sent = Reply(mock, HttpMethod.Put, "/dashboards/3", Dashboard3);

        var result = await Run(scope, "dashboards_remove_widget", new { dashboard_id = 3, widget_id = 42 }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        Matches(sent.Json["widgets"], """[{"id":41,"type":"chart","title":"Orders by status","config_json":"{\"chartId\":12}","sort_order":0}]""");
        Matches(JsonNode.Parse(sent.Json["layout_json"]!.GetValue<string>()), """[{"widgetId":41,"x":0,"y":0,"w":6,"h":3,"minH":2}]""");
        sent.Json.AsObject().Select(p => p.Key).Should().Equal("widgets", "layout_json");
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task RemoveWidget_OfAWidgetNotOnTheDashboard_ListsTheOnesThatAre_AndChangesNothing(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Get, "/dashboards/3", Dashboard3);
        var any = mock.When(HttpMethod.Put, Org + "/dashboards/3").Respond("application/json", Dashboard3);

        var result = await Run(scope, "dashboards_remove_widget", new { dashboard_id = 3, widget_id = 99 }, mock);

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("Widget 99 isn't on dashboard 3").And.Contain("41 (Orders by status)").And.Contain("42 (Notes)");
        NothingWasSent(mock, any);
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task RemoveWidget_OfTheLastWidget_SendsAnEmptyList_SoTheDashboardIsLeftEmptyNotUntouched(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Get, "/dashboards/3", """{"id":3,"name":"Solo","widgets":[{"id":41,"type":"chart","title":"Only","config_json":"{\"chartId\":12}","sort_order":0}]}""");
        var sent = Reply(mock, HttpMethod.Put, "/dashboards/3", """{"id":3,"name":"Solo","widgets":[]}""");

        var result = await Run(scope, "dashboards_remove_widget", new { dashboard_id = 3, widget_id = 41 }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        Matches(sent.Json, """{"widgets":[]}""");
    }

    // ── Rule: a create with a missing or wrong field is refused locally, naming it, and sends nothing ──

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task ChartsCreate_WithoutAnEntity_IsRefusedNamingIt_AndSendsNothing(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        var any = mock.When("*").Respond("application/json", "{}");

        var result = await Run(scope, "charts_create", new { name = "Orders", type = "pie", group_by = "status" }, mock);

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("The chart can't be created").And.Contain("An entity chart needs --entity <ENTITY>");
        NothingWasSent(mock, any);
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task ChartsCreate_WithSeveralThingsMissing_NamesEveryOne(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        var any = mock.When("*").Respond("application/json", "{}");

        var result = await Run(scope, "charts_create", new { name = "Revenue" }, mock);

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("--type").And.Contain("--entity");
        NothingWasSent(mock, any);
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task ChartsCreate_WithAnUnknownChartType_ListsTheOnesThatExist(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        var any = mock.When("*").Respond("application/json", "{}");

        var result = await Run(scope, "charts_create", new { name = "X", entity = "orders", type = "scatter" }, mock);

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("'scatter' isn't a valid chart type").And.Contain("column, line, pie, area, stat, gauge, funnel");
        NothingWasSent(mock, any);
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task ChartsUpdate_WithNothingToChange_IsRefused_AndSendsNothing(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        var any = mock.When("*").Respond("application/json", Chart12);

        var result = await Run(scope, "charts_update", new { id = 12 }, mock);

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("Nothing to update");
        NothingWasSent(mock, any);
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task DashboardsCreate_WithABlankName_IsRefused_AndSendsNothing(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        var any = mock.When("*").Respond("application/json", "{}");

        var result = await Run(scope, "dashboards_create", new { name = " " }, mock);

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("A dashboard needs a name");
        NothingWasSent(mock, any);
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task DashboardsCreate_WithAWidgetMissingItsTitle_IsRefusedNamingIt_AndSendsNothing(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        var any = mock.When("*").Respond("application/json", "{}");

        var result = await Run(scope, "dashboards_create", new { name = "D", config = """{"widgets":[{"type":"markdown"}]}""" }, mock);

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("Widget 1 needs a title");
        NothingWasSent(mock, any);
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task AddChart_WithAPositionOutsideTheGrid_IsRefused_AndSendsNothing(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        var any = mock.When("*").Respond("application/json", "{}");

        var result = await Run(scope, "dashboards_add_chart", new { dashboard_id = 3, chart_id = 14, width = 20 }, mock);

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("--width can't be more than 12");
        NothingWasSent(mock, any);
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task WidgetPreview_OfAChartWithNoChart_IsRefused_AndSendsNothing(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        var any = mock.When("*").Respond("application/json", "{}");

        var result = await Run(scope, "dashboards_widget_preview", new { type = "chart" }, mock);

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("A chart widget needs --chart <ID>");
        NothingWasSent(mock, any);
    }

    // ── Rule: a chart or dashboard name with [square brackets] is shown literally and doesn't crash ──

    private const string BracketedChart = """{"id":12,"name":"[draft] Orders [Q1]","data_source":"entity","chart_type":"stat","entity_name":"orders","updated_at":"2026-01-02T00:00:00Z"}""";

    private const string BracketedDashboard = """
        {"id":3,"name":"[beta] Sales","description":"See [docs]","is_home":true,
         "widgets":[{"id":41,"type":"chart","title":"[draft] Orders [Q1]","config_json":"{\"chartId\":12}","sort_order":0}]}
        """;

    [Theory]
    [MemberData(nameof(AllScopes))]
    public async Task ChartsCreate_WithABracketedName_ConfirmsItLiterally(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Post, "/charts", BracketedChart, slow: true, status: HttpStatusCode.Created);

        var result = await RunCli(scope, mock, "charts", "create", "[draft] Orders [Q1]", "--entity", "orders", "--type", "stat");

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("[draft] Orders [Q1]").And.NotContain("[[");
    }

    [Theory]
    [MemberData(nameof(AllScopes))]
    public async Task ChartsList_AndGet_ShowABracketedNameLiterally(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Get, "/charts", $"[{BracketedChart}]", slow: true);
        Reply(mock, HttpMethod.Get, "/charts/12", BracketedChart, slow: true);

        var list = await RunCli(scope, mock, "charts", "list");
        var get = await RunCli(scope, mock, "charts", "get", "12");

        list.ExitCode.Should().Be(0, list.Output);
        list.Output.Should().Contain("[draft] Orders [Q1]");
        get.ExitCode.Should().Be(0, get.Output);
        get.Output.Should().Contain("[draft] Orders [Q1]");
    }

    [Theory]
    [MemberData(nameof(AllScopes))]
    public async Task ChartsUpdate_OfABracketedChart_ConfirmsItLiterally(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Get, "/charts/12", BracketedChart);
        Reply(mock, HttpMethod.Put, "/charts/12", BracketedChart, slow: true);

        var result = await RunCli(scope, mock, "charts", "update", "12", "--limit", "5");

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("[draft] Orders [Q1]");
    }

    [Theory]
    [MemberData(nameof(AllScopes))]
    public async Task ChartsCreate_Refused_ShowsABracketedFilterLiterally(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        var any = mock.When("*").Respond("application/json", "{}");

        var result = await RunCli(scope, mock, "charts", "create", "X", "--entity", "orders", "--type", "stat", "--filter", "[status]");

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("[status]");
        NothingWasSent(mock, any);
    }

    [Theory]
    [MemberData(nameof(AllScopes))]
    public async Task DashboardsCreate_WithABracketedName_ConfirmsItLiterally(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Post, "/dashboards", BracketedDashboard, slow: true, status: HttpStatusCode.Created);

        var result = await RunCli(scope, mock, "dashboards", "create", "[beta] Sales");

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("[beta] Sales").And.NotContain("[[");
    }

    [Theory]
    [MemberData(nameof(AllScopes))]
    public async Task DashboardsList_Get_AndMine_ShowABracketedNameAndTitlesLiterally(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Get, "/dashboards", $"[{BracketedDashboard}]", slow: true);
        Reply(mock, HttpMethod.Get, "/dashboards/3", BracketedDashboard, slow: true);
        Reply(mock, HttpMethod.Get, "/dashboards/me", BracketedDashboard, slow: true);

        var list = await RunCli(scope, mock, "dashboards", "list");
        var get = await RunCli(scope, mock, "dashboards", "get", "3");
        var mine = await RunCli(scope, mock, "dashboards", "mine");

        list.Output.Should().Contain("[beta] Sales");
        get.Output.Should().Contain("[beta] Sales").And.Contain("See [docs]").And.Contain("[draft] Orders [Q1]");
        mine.Output.Should().Contain("[beta] Sales");
        new[] { list, get, mine }.Should().OnlyContain(r => r.ExitCode == 0);
    }

    [Theory]
    [MemberData(nameof(AllScopes))]
    public async Task AddChart_OfABracketedChart_ConfirmsItLiterally(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Get, "/charts/12", BracketedChart);
        Reply(mock, HttpMethod.Get, "/dashboards/3", BracketedDashboard);
        Reply(mock, HttpMethod.Put, "/dashboards/3", BracketedDashboard, slow: true);

        var result = await RunCli(scope, mock, "dashboards", "add-chart", "3", "12");

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("[draft] Orders [Q1]").And.NotContain("[[");
    }

    [Theory]
    [MemberData(nameof(AllScopes))]
    public async Task RemoveWidget_OfABracketedWidget_ConfirmsItLiterally(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Get, "/dashboards/3", BracketedDashboard);
        Reply(mock, HttpMethod.Put, "/dashboards/3", BracketedDashboard, slow: true);

        var result = await RunCli(scope, mock, "dashboards", "remove-widget", "3", "41");

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("[draft] Orders [Q1]");
    }

    [Theory]
    [MemberData(nameof(AllScopes))]
    public async Task RemoveWidget_OfAnUnknownWidget_ShowsTheBracketedTitlesLiterally(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Get, "/dashboards/3", BracketedDashboard);

        var result = await RunCli(scope, mock, "dashboards", "remove-widget", "3", "99");

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("41 ([draft] Orders [Q1])");
    }

    [Theory]
    [MemberData(nameof(AllScopes))]
    public async Task DashboardsData_ShowsABracketedWidgetTitleAndValuesLiterally(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Get, "/dashboards/3", BracketedDashboard, slow: true);
        Reply(mock, HttpMethod.Post, "/dashboards/3/data",
            """{"41":{"items":[{"status":"[open] now","value":7}],"total":1}}""", slow: true);

        var result = await RunCli(scope, mock, "dashboards", "data", "3");

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("[draft] Orders [Q1]").And.Contain("[open] now");
    }

    [Theory]
    [MemberData(nameof(AllScopes))]
    public async Task ChartsPreview_ShowsBracketedValuesLiterally(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Post, "/charts/preview-config", """{"chart":{},"items":[{"status":"[open] now","value":7}],"total":1}""", slow: true);

        var result = await RunCli(scope, mock, "charts", "preview", "--entity", "orders", "--type", "pie", "--group-by", "status");

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("[open] now");
    }

    // ── Rule: --json output parses, stays small, and keeps quotes and accents as typed ──

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task ChartsPreview_OfThousandsOfRows_ShowsOnlyTheFirstFifty_AndSaysHowManyThereWere(CliToolScope scope)
    {
        var rows = new JsonArray(Enumerable.Range(1, 1000)
            .Select(i => (JsonNode)new JsonObject { ["id"] = i, ["status"] = $"s{i}", ["total"] = i + 0.5, ["notes"] = $"a long note about order {i}" }).ToArray());
        var payload = new JsonObject
        {
            ["chart"] = new JsonObject { ["id"] = 0, ["name"] = "Preview", ["chart_type"] = "column" },
            ["items"] = rows,
            ["total"] = 1000,
        };
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Post, "/charts/preview-config", payload.ToJsonString());

        var result = await Run(scope, "charts_preview", new { entity = "orders", type = "column", group_by = "status" }, mock);

        var data = Parsed(result);
        data["items"]!.AsArray().Should().HaveCount(50);
        data["items_total"]!.GetValue<int>().Should().Be(1000);
        data.AsObject().ContainsKey("chart").Should().BeFalse();
        result.Output.Length.Should().BeLessThan(20_000, "the full thousand rows would be ten times that");
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task ChartsPreview_WithRows_ShowsThatMany(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Post, "/charts/preview-config", """{"chart":{},"items":[{"v":1},{"v":2},{"v":3}],"total":3}""");

        var result = await Run(scope, "charts_preview", new { entity = "orders", type = "column", group_by = "status", rows = 2 }, mock);

        Parsed(result)["items"]!.AsArray().Should().HaveCount(2);
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task Json_KeepsQuotesAngleBracketsAndAccentsAsTyped(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Get, "/charts/12", """{"id":12,"name":"Café <orders> \"live\"","data_source":"entity","chart_type":"stat","entity_name":"orders"}""");

        var result = await Run(scope, "charts_get", new { id = 12 }, mock);

        result.Output.Should().Contain("Café <orders> \\\"live\\\"").And.NotContain("\\u");
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task DashboardsData_WithManyWidgets_StaysSmall(CliToolScope scope)
    {
        var widgets = new JsonArray(Enumerable.Range(1, 10).Select(i => (JsonNode)new JsonObject
        {
            ["id"] = i, ["type"] = "chart", ["title"] = $"Chart {i}", ["config_json"] = $$"""{"chartId":{{i}}}""", ["sort_order"] = i,
        }).ToArray());
        var data = new JsonObject();
        foreach (var i in Enumerable.Range(1, 10))
            data[i.ToString()] = new JsonObject
            {
                ["chart"] = new JsonObject { ["id"] = i, ["name"] = $"Chart {i}", ["chart_type"] = "line", ["entity_name"] = "orders", ["x_field"] = "created_at" },
                ["items"] = new JsonArray(Enumerable.Range(1, 100).Select(r => (JsonNode)new JsonObject { ["period"] = $"2026-01-{r}", ["value"] = r }).ToArray()),
                ["total"] = 100,
            };
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Get, "/dashboards/3", new JsonObject { ["id"] = 3, ["name"] = "Big", ["widgets"] = widgets }.ToJsonString());
        Reply(mock, HttpMethod.Post, "/dashboards/3/data", data.ToJsonString());

        var result = await Run(scope, "dashboards_data", new { id = 3 }, mock);

        var all = Parsed(result).AsArray();
        all.Should().HaveCount(10);
        all.Should().OnlyContain(w => w!["data"]!["items"]!.AsArray().Count == 20 && w["data"]!["items_total"]!.GetValue<int>() == 100);
        result.Output.Should().NotContain("x_field");
    }

    // ── Rule: reads, additions and changes carry the right hints ───────────────

    [Theory]
    [InlineData("charts_list", true, null)]
    [InlineData("charts_get", true, null)]
    [InlineData("charts_preview", true, null)]
    [InlineData("charts_platform_metrics", true, null)]
    [InlineData("dashboards_list", true, null)]
    [InlineData("dashboards_get", true, null)]
    [InlineData("dashboards_mine", true, null)]
    [InlineData("dashboards_data", true, null)]
    [InlineData("dashboards_widget_preview", true, null)]
    [InlineData("charts_create", false, false)]
    [InlineData("dashboards_create", false, false)]
    [InlineData("dashboards_add_chart", false, false)]
    [InlineData("charts_update", false, true)]
    [InlineData("charts_delete", false, true)]
    [InlineData("dashboards_update", false, true)]
    [InlineData("dashboards_delete", false, true)]
    [InlineData("dashboards_remove_widget", false, true)]
    [InlineData("dashboards_set_home", false, true)]
    [InlineData("dashboards_promote_to_default", false, true)]
    public void Annotations_ReadsAreReadOnly_AdditionsAreNot_AndChangesAreDestructive(string name, bool readOnly, bool? destructive)
    {
        foreach (var scope in new[] { CliToolScope.Local, CliToolScope.Internal, CliToolScope.Hosted })
        {
            var annotations = CliCommandTool.All(scope).Single(t => t.ProtocolTool.Name == name).ProtocolTool.Annotations!;

            annotations.ReadOnlyHint.Should().Be(readOnly, $"{name} in {scope}");
            annotations.DestructiveHint.Should().Be(destructive, $"{name} in {scope}");
        }
    }

    [Theory]
    [MemberData(nameof(AllScopes))]
    public void ChartAndDashboardTools_AreAvailableInEveryScope(CliToolScope scope)
    {
        var names = CliCommandTool.All(scope).Select(t => t.ProtocolTool.Name).ToList();

        names.Should().Contain([
            "charts_list", "charts_get", "charts_create", "charts_update", "charts_delete", "charts_preview", "charts_platform_metrics",
            "dashboards_list", "dashboards_get", "dashboards_mine", "dashboards_create", "dashboards_update", "dashboards_delete",
            "dashboards_data", "dashboards_set_home", "dashboards_promote_to_default", "dashboards_widget_preview",
            "dashboards_add_chart", "dashboards_remove_widget"]);
    }

    // ── Rule: remote callers can pass a name with spaces, but not a path trick as an id ──

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task AnIdThatIsAPathTrick_IsRefusedBeforeAnyRequest(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        var any = mock.When("*").Respond("application/json", "{}");

        var result = await Run(scope, "charts_get", new { id = "12/../13" }, mock);

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("'id' can only contain letters, digits");
        NothingWasSent(mock, any);
    }

    // ── Rule: a remote caller sees only the status of an API refusal ───────────

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task AForbiddenDashboard_ShowsOnlyTheStatus_ToARemoteCaller(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Get, "/dashboards/3", "secret detail", status: HttpStatusCode.Forbidden);

        var result = await Run(scope, "dashboards_get", new { id = 3 }, mock);

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("status 403").And.NotContain("secret detail");
    }

    // ── Rule: the table output names what to do next ──────────────────────────

    [Fact]
    public async Task Get_AsATable_ListsEachWidgetWithItsChartAndPosition()
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Get, "/dashboards/3", Dashboard3);

        var result = await RunCli(CliToolScope.Local, mock, "dashboards", "get", "3");

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("Dashboard: Sales").And.Contain("chart 12").And.Contain("x0 y0 6x3").And.Contain("Notes");
    }

    [Fact]
    public async Task Create_AsATable_PointsToPuttingTheChartOnADashboard()
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Post, "/charts", Chart12, status: HttpStatusCode.Created);

        var result = await RunCli(CliToolScope.Local, mock, "charts", "create", "Orders by status", "--entity", "orders", "--type", "pie", "--group-by", "status");

        result.Output.Should().Contain("created (id: 12)").And.Contain("dashboards add-chart <dashboard-id> 12");
    }

    [Fact]
    public async Task Preview_AsATable_ShowsTheValueAndTheRows()
    {
        var mock = new MockHttpMessageHandler();
        Reply(mock, HttpMethod.Post, "/charts/preview-config", """{"chart":{},"items":[{"status":"paid","value":7}],"total":1}""");

        var result = await RunCli(CliToolScope.Local, mock, "charts", "preview", "--entity", "orders", "--type", "pie", "--group-by", "status");

        result.Output.Should().Contain("paid").And.Contain("7").And.NotContain("chart_type");
    }
}
