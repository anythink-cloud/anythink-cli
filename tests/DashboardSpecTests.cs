using System.Text.Json.Nodes;
using AnythinkCli.Models;
using FluentAssertions;

namespace AnythinkCli.Tests;

public class DashboardSpecTests
{
    private static DashboardBuild Create(DashboardInput input, params ChartWidgetRef[] charts) => DashboardSpec.Build(input, charts, create: true);

    private static DashboardBuild Update(DashboardInput input) => DashboardSpec.Build(input, [], create: false);

    private static void Matches(JsonNode? actual, string expected) =>
        JsonNode.DeepEquals(actual, JsonNode.Parse(expected)).Should().BeTrue($"expected {expected} but was {actual?.ToJsonString()}");

    // ── Rule: a dashboard request is written in snake_case, as the API reads it ──

    [Fact]
    public void Create_SendsTheNameAndFlagsInSnakeCase()
    {
        var build = Create(new DashboardInput(Name: "Sales", Description: "Weekly numbers", Shared: true, AllowSharedEdit: true, Home: true));

        build.Ok.Should().BeTrue(string.Join("; ", build.Problems));
        Matches(build.Body, """{"name":"Sales","description":"Weekly numbers","is_shared":true,"allow_shared_edit":true,"is_home":true}""");
    }

    [Fact]
    public void Create_WithCharts_AddsOneChartWidgetEach_TitledAfterTheChart_InOrder()
    {
        var build = Create(new DashboardInput(Name: "Sales"), new ChartWidgetRef(12, "Orders by status"), new ChartWidgetRef(14, "Revenue"));

        Matches(build.Body!["widgets"],
            """
            [{"type":"chart","title":"Orders by status","config_json":"{\"chartId\":12}","sort_order":0},
             {"type":"chart","title":"Revenue","config_json":"{\"chartId\":14}","sort_order":1}]
            """);
    }

    [Fact]
    public void Create_PutsChartWidgetsAfterTheWidgetsInConfig()
    {
        var build = Create(new DashboardInput(Name: "Sales", Config: """{"widgets":[{"type":"markdown","title":"Notes","config":{"body":"Hi"}}]}"""),
            new ChartWidgetRef(12, "Orders"));

        var widgets = build.Body!["widgets"]!.AsArray();
        widgets.Select(w => w!["title"]!.GetValue<string>()).Should().Equal("Notes", "Orders");
        widgets.Select(w => w!["sort_order"]!.GetValue<int>()).Should().Equal(0, 1);
    }

    [Fact]
    public void Config_WidgetSettingsGivenAsAnObject_AreSentAsTheJsonTextTheApiExpects()
    {
        var build = Create(new DashboardInput(Name: "D", Config: """{"widgets":[{"type":"list","title":"Latest","config":{"entityName":"orders","limit":5}}]}"""));

        build.Body!["widgets"]![0]!["config_json"]!.GetValue<string>().Should().Be("""{"entityName":"orders","limit":5}""");
    }

    [Fact]
    public void Config_ReadsAChartWidgetsChartId_AsTheChartSetting()
    {
        var build = Create(new DashboardInput(Name: "D", Config: """{"widgets":[{"id":41,"type":"chart","title":"Orders","chart_id":12,"sort_order":3}]}"""));

        build.Ok.Should().BeTrue(string.Join("; ", build.Problems));
        Matches(build.Body!["widgets"], """[{"id":41,"type":"chart","title":"Orders","config_json":"{\"chartId\":12}","sort_order":3}]""");
    }

    [Fact]
    public void Config_LayoutGivenAsPositions_IsStoredInTheFormTheDashboardReads()
    {
        var build = Update(new DashboardInput(Config: """{"layout":[{"widget_id":41,"x":0,"y":0,"w":6,"h":3}]}"""));

        build.Ok.Should().BeTrue(string.Join("; ", build.Problems));
        build.Body!["layout_json"]!.GetValue<string>().Should().Be("""[{"widgetId":41,"x":0,"y":0,"w":6,"h":3}]""");
    }

    // ── Rule: a missing or wrong field is refused locally, naming it ──────────

    [Fact]
    public void Create_WithoutAName_IsRefused()
    {
        var build = Create(new DashboardInput(Name: " "));

        build.Ok.Should().BeFalse();
        build.Problems.Should().ContainSingle().Which.Should().Contain("needs a name");
    }

    [Theory]
    [InlineData("""{"widgets":[{"title":"No type"}]}""", "Widget 1 needs a type")]
    [InlineData("""{"widgets":[{"type":"markdown"}]}""", "Widget 1 needs a title")]
    [InlineData("""{"widgets":[{"type":"pie","title":"x"}]}""", "Widget 1 has type 'pie'")]
    [InlineData("""{"widgets":[{"type":"chart","title":"x"}]}""", "Widget 1 is a chart, so it needs a chart_id")]
    [InlineData("""{"widgets":[{"type":"markdown","title":"x","colour":"red"}]}""", "Widget 1 has settings a widget doesn't have: colour")]
    [InlineData("""{"widgets":"none"}""", "widgets must be a list")]
    [InlineData("""{"layout":[{"x":1}]}""", "Each layout position needs a widget_id")]
    [InlineData("""{"owner_user_id":5,"colour":"red"}""", "colour")]
    [InlineData("not json", "--config isn't valid JSON")]
    public void Create_WithAWrongConfig_NamesWhatIsWrong(string config, string message)
    {
        var build = Create(new DashboardInput(Name: "D", Config: config));

        build.Problems.Should().Contain(p => p.Contains(message));
        build.Body.Should().BeNull();
    }

    [Fact]
    public void Update_WithNothingToChange_IsRefused()
    {
        var build = Update(new DashboardInput());

        build.Problems.Should().ContainSingle().Which.Should().Contain("Nothing to update");
    }

    [Fact]
    public void Update_SendsOnlyWhatWasPassed_SoNothingElseChanges()
    {
        var build = Update(new DashboardInput(Name: "Sales overview"));

        Matches(build.Body, """{"name":"Sales overview"}""");
    }

    [Fact]
    public void Update_DoesNotTakeSettingsOnlyCreateHas()
    {
        var build = Update(new DashboardInput(Config: """{"is_home":true}"""));

        build.Problems.Should().ContainSingle().Which.Should().Contain("is_home").And.Contain("Allowed");
    }

    [Fact]
    public void Create_OfTheDefaultDashboardThatIsAlsoShared_IsRefused()
    {
        var build = Create(new DashboardInput(Name: "D", Shared: true, Default: true));

        build.Problems.Should().ContainSingle().Which.Should().Contain("already visible to everyone");
    }
}
