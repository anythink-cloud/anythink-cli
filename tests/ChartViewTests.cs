using System.Text.Json.Nodes;
using AnythinkCli.Models;
using FluentAssertions;

namespace AnythinkCli.Tests;

public class ChartViewTests
{
    private static JsonObject Obj(string json) => JsonNode.Parse(json)!.AsObject();

    // ── Rule: a chart read back is small: no server bookkeeping, filters as a list ──

    [Fact]
    public void Detail_DropsBookkeepingAndNulls_AndReadsFiltersAsAList()
    {
        var chart = Obj("""
            {"id":12,"name":"Orders","data_source":"entity","chart_type":"pie","entity_name":"orders","x_field":"status","y_field":null,
             "filters":"[{\"field\":\"region\",\"op\":\"eq\",\"value\":\"uk\"}]","funnel_stages":null,"config":"",
             "tenant_id":42,"created_by":7,"updated_by":7,"locked":false,"created_at":"2026-01-01T00:00:00Z"}
            """);

        var detail = ChartView.Detail(chart);

        detail.Select(p => p.Key).Should().Equal("id", "name", "chart_type", "data_source", "entity_name", "x_field", "filters", "created_at");
        detail["filters"].Should().BeOfType<JsonArray>().Which[0]!["field"]!.GetValue<string>().Should().Be("region");
    }

    [Fact]
    public void Summary_KeepsJustWhatIdentifiesTheChart()
    {
        var chart = Obj("""{"id":12,"name":"Orders","data_source":"entity","chart_type":"pie","entity_name":"orders","x_field":"status","limit":5,"filters":"[]","tenant_id":42}""");

        ChartView.Summary(chart).Select(p => p.Key).Should().Equal("id", "name", "chart_type", "data_source", "entity_name", "x_field");
    }

    // ── Rule: the data a chart draws is shown without the chart's own settings echoed back ──

    [Fact]
    public void Payload_DropsTheEchoedChart()
    {
        var payload = Obj("""{"chart":{"id":0,"name":"Preview","chart_type":"stat"},"value":42,"unit":"records","target":null}""");

        var compact = ChartView.Payload(payload, 50)!.AsObject();

        compact.ContainsKey("chart").Should().BeFalse();
        compact["value"]!.GetValue<int>().Should().Be(42);
    }

    [Fact]
    public void Payload_DropsTheEchoedChart_InsideAComparison()
    {
        var payload = Obj("""{"primary":{"chart":{"id":0},"value":42},"compare":{"items":[]},"delta_pct":5.5}""");

        var compact = ChartView.Payload(payload, 50)!.AsObject();

        compact["primary"]!.AsObject().ContainsKey("chart").Should().BeFalse();
        compact["delta_pct"]!.GetValue<double>().Should().Be(5.5);
    }

    [Fact]
    public void Payload_CapsRows_AndSaysHowManyThereWere()
    {
        var rows = string.Join(",", Enumerable.Range(1, 200).Select(i => $$"""{"status":"s{{i}}","value":{{i}}}"""));
        var payload = Obj($$"""{"chart":{},"items":[{{rows}}],"total":200}""");

        var compact = ChartView.Payload(payload, 25)!.AsObject();

        compact["items"]!.AsArray().Should().HaveCount(25);
        compact["items_total"]!.GetValue<int>().Should().Be(200);
        compact["total"]!.GetValue<int>().Should().Be(200);
    }

    [Fact]
    public void Payload_WithFewerRowsThanTheCap_AddsNoTotal()
    {
        var compact = ChartView.Payload(Obj("""{"items":[{"v":1}],"total":1}"""), 25)!.AsObject();

        compact.ContainsKey("items_total").Should().BeFalse();
    }

    [Fact]
    public void PayloadError_FindsAnErrorTheApiReturnedWithAnOkStatus()
    {
        ChartView.PayloadError(Obj("""{"chart":{},"error":"Unknown metric: nope"}""")).Should().Be("Unknown metric: nope");
        ChartView.PayloadError(Obj("""{"primary":{"error":"No stages configured"}}""")).Should().Be("No stages configured");
        ChartView.PayloadError(Obj("""{"value":1}""")).Should().BeNull();
    }

    // ── Rule: a dashboard read back carries each widget's position on the widget, not in a second list ──

    private const string StoredDashboard = """
        {"id":3,"name":"Sales","description":"","is_default":false,"is_home":true,"is_shared":false,"allow_shared_edit":false,
         "owner_user_id":7,"tenant_id":42,"locked":false,"created_by":7,
         "layout_json":"[{\"widgetId\":41,\"x\":0,\"y\":0,\"w\":6,\"h\":3},{\"widgetId\":999,\"x\":6,\"y\":0,\"w\":6,\"h\":3}]",
         "widgets":[
           {"id":42,"type":"markdown","title":"Notes","config_json":"{\"body\":\"Hi\"}","sort_order":1,"tenant_id":42,"dashboard_id":3},
           {"id":41,"type":"chart","title":"Orders","config_json":"{\"chartId\":12}","sort_order":0,"tenant_id":42,"dashboard_id":3}]}
        """;

    [Fact]
    public void DashboardDetail_PutsEachWidgetsPositionOnTheWidget_InOrder_WithoutALayoutList()
    {
        var detail = DashboardView.Detail(Obj(StoredDashboard));

        detail.ContainsKey("layout_json").Should().BeFalse();
        detail.ContainsKey("layout").Should().BeFalse();
        detail.ContainsKey("tenant_id").Should().BeFalse();
        var widgets = detail["widgets"]!.AsArray();
        widgets.Select(w => w!["id"]!.GetValue<int>()).Should().Equal(41, 42);
        widgets[0]!["chart_id"]!.GetValue<int>().Should().Be(12);
        widgets[0]!["position"]!["w"]!.GetValue<int>().Should().Be(6);
        widgets[1]!["config"]!["body"]!.GetValue<string>().Should().Be("Hi");
        widgets[1]!.AsObject().ContainsKey("position").Should().BeFalse();
    }

    [Fact]
    public void DashboardSummary_HasNoWidgets_AndNoBlankDescription()
    {
        var summary = DashboardView.Summary(Obj(StoredDashboard));

        summary.Select(p => p.Key).Should().Equal("id", "name", "is_default", "is_home", "is_shared", "owner_user_id");
    }
}
