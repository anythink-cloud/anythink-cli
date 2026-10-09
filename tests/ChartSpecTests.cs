using System.Text.Json.Nodes;
using AnythinkCli.Models;
using FluentAssertions;

namespace AnythinkCli.Tests;

public class ChartSpecTests
{
    private static ChartBuild Create(ChartInput input) => ChartSpec.Build(input, existing: null, create: true);

    private static ChartInput Valid(ChartInput? with = null)
    {
        var input = with ?? new ChartInput();
        return input with
        {
            Name = input.Name ?? "Orders by status",
            Entity = input.Entity ?? "orders",
            Type = input.Type ?? "pie",
            GroupBy = input.GroupBy ?? "status",
        };
    }

    private static JsonObject Existing(string json) => JsonNode.Parse(json)!.AsObject();

    private static void Matches(JsonNode? actual, string expected) =>
        JsonNode.DeepEquals(actual, JsonNode.Parse(expected)).Should().BeTrue($"expected {expected} but was {actual?.ToJsonString()}");

    // ── Rule: the typed options are sent under the names the API accepts ──────

    [Fact]
    public void Create_SendsTheCamelCaseNamesTheApiAccepts_AndDefaultsTheSourceToEntity()
    {
        var build = Create(new ChartInput(
            Name: "Revenue per month", Entity: "orders", Type: "column", GroupBy: "created_at", Interval: "month",
            Measure: "total", Agg: "sum", StackBy: "region", Limit: 12, Sort: "desc", Cumulative: true, Target: 500m, TargetMode: "goal"));

        build.Ok.Should().BeTrue(string.Join("; ", build.Problems));
        Matches(build.Body,
            """
            {"dataSource":"entity","name":"Revenue per month","chartType":"column","entityName":"orders","xField":"created_at",
             "xInterval":"month","yField":"total","yAgg":"sum","stackBy":"region","limit":12,"sortDirection":"desc",
             "cumulative":true,"target":500,"targetMode":"goal"}
            """);
    }

    [Fact]
    public void Create_SendsNoSnakeCaseNames()
    {
        var build = Create(Valid(new ChartInput(Compare: true, CompareDays: 7, CompareOverlay: false)));

        build.Body!.Select(p => p.Key).Should().OnlyContain(key => !key.Contains('_'));
        build.Body!["compareEnabled"]!.GetValue<bool>().Should().BeTrue();
        build.Body!["comparePreviousDays"]!.GetValue<int>().Should().Be(7);
        build.Body!["compareShowOverlay"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public void Create_ForAPlatformChart_NeedsNoEntity()
    {
        var build = Create(new ChartInput(Name: "Runs", Type: "line", Source: "platform", PlatformMetric: "workflow_runs", PlatformPeriod: "week"));

        build.Ok.Should().BeTrue(string.Join("; ", build.Problems));
        build.Body!["platformMetric"]!.GetValue<string>().Should().Be("workflow_runs");
        build.Body!["platformPeriod"]!.GetValue<string>().Should().Be("week");
    }

    // ── Rule: a missing required field is refused, naming it and how to fix it ──

    [Theory]
    [InlineData("name", "A chart needs a name")]
    [InlineData("type", "A chart needs --type")]
    [InlineData("entity", "An entity chart needs --entity")]
    [InlineData("group-by", "needs --group-by")]
    public void Create_WithoutARequiredField_IsRefusedNamingIt(string missing, string message)
    {
        var input = Valid();
        input = missing switch
        {
            "name" => input with { Name = "  " },
            "type" => input with { Type = null },
            "entity" => input with { Entity = null },
            _ => input with { GroupBy = null },
        };

        var build = Create(input);

        build.Ok.Should().BeFalse();
        build.Body.Should().BeNull();
        build.Problems.Should().Contain(p => p.Contains(message));
    }

    [Fact]
    public void Create_ReportsEveryMissingFieldAtOnce()
    {
        var build = Create(new ChartInput());

        build.Problems.Should().Contain(p => p.Contains("needs a name"))
            .And.Contain(p => p.Contains("--type"))
            .And.Contain(p => p.Contains("--entity"));
    }

    [Theory]
    [InlineData("sum")]
    [InlineData("avg")]
    [InlineData("min")]
    [InlineData("max")]
    public void Create_WithANumericAggregationAndNoMeasure_IsRefused(string agg)
    {
        var build = Create(Valid(new ChartInput(Agg: agg)));

        build.Problems.Should().ContainSingle().Which.Should().Contain($"--agg {agg} needs --measure");
    }

    [Fact]
    public void Create_WithCountAndNoMeasure_IsAccepted()
        => Create(Valid(new ChartInput(Agg: "count"))).Ok.Should().BeTrue();

    [Theory]
    [InlineData("stat")]
    [InlineData("gauge")]
    public void Create_OfAStatOrGauge_NeedsNoGroupBy(string type)
        => Create(new ChartInput(Name: "Orders", Entity: "orders", Type: type)).Ok.Should().BeTrue();

    [Fact]
    public void Create_OfAFunnelWithoutStages_IsRefusedWithAnExample()
    {
        var build = Create(new ChartInput(Name: "Funnel", Entity: "users", Type: "funnel"));

        build.Problems.Should().ContainSingle().Which.Should().Contain("needs stages").And.Contain("funnelStages");
    }

    [Fact]
    public void Create_OfAFunnel_TakesItsStagesFromConfig_AndChecksEachHasALabel()
    {
        var good = Create(new ChartInput(Name: "Funnel", Entity: "users", Type: "funnel",
            Config: """{"funnelStages":[{"label":"Signed up"},{"label":"Paid","filters":[{"field":"plan","op":"ne","value":"free"}]}]}"""));
        var bad = Create(new ChartInput(Name: "Funnel", Entity: "users", Type: "funnel",
            Config: """{"funnelStages":[{"label":"Signed up"},{"filters":[]}]}"""));

        good.Ok.Should().BeTrue(string.Join("; ", good.Problems));
        good.Body!["funnelStages"]!.AsArray().Should().HaveCount(2);
        bad.Problems.Should().ContainSingle().Which.Should().Be("Funnel stage 2 needs a label.");
    }

    [Fact]
    public void Create_OfACohortFunnelWithoutACohortField_IsRefused()
    {
        var build = Create(new ChartInput(Name: "Funnel", Entity: "users", Type: "funnel",
            Config: """{"funnelStages":[{"label":"A"}],"funnelMode":"cohort"}"""));

        build.Problems.Should().ContainSingle().Which.Should().Contain("cohortField");
    }

    [Fact]
    public void Create_OfAPlatformChartWithoutAMetric_PointsToTheMetricList()
    {
        var build = Create(new ChartInput(Name: "Runs", Type: "line", Source: "platform"));

        build.Problems.Should().ContainSingle().Which.Should().Contain("--platform-metric").And.Contain("charts platform-metrics");
    }

    [Fact]
    public void Create_OfAQuotaChartWithoutAMetric_ListsTheMetrics()
    {
        var build = Create(new ChartInput(Name: "Storage", Type: "gauge", Source: "quota"));

        build.Problems.Should().ContainSingle().Which.Should().Contain("--quota-metric").And.Contain("storage");
    }

    // ── Rule: a value the dashboard wouldn't offer is refused with the ones it would ──

    [Theory]
    [InlineData("type", "scatter", "chart type")]
    [InlineData("source", "warehouse", "data source")]
    [InlineData("agg", "median", "aggregation")]
    [InlineData("interval", "fortnight", "interval")]
    [InlineData("sort", "up", "sort direction")]
    [InlineData("timeframe", "fortnight", "timeframe")]
    [InlineData("target-mode", "aim", "target mode")]
    public void Create_WithAnUnknownChoice_NamesTheChoiceAndListsTheValidOnes(string option, string value, string label)
    {
        var build = Create(Valid(option switch
        {
            "type" => new ChartInput(Type: value),
            "source" => new ChartInput(Source: value),
            "agg" => new ChartInput(Agg: value, Measure: "total"),
            "interval" => new ChartInput(Interval: value),
            "sort" => new ChartInput(Sort: value),
            "timeframe" => new ChartInput(Timeframe: value),
            _ => new ChartInput(TargetMode: value),
        }));

        build.Problems.Should().Contain(p => p.Contains($"'{value}' isn't a valid {label}") && p.Contains("Use one of:"));
    }

    [Fact]
    public void Create_AcceptsAChoiceInAnyCase_AndCallsABarChartColumn()
    {
        var upper = Create(Valid(new ChartInput(Type: "PIE", Agg: "Count")));
        var bar = Create(Valid(new ChartInput(Type: "bar")));

        upper.Body!["chartType"]!.GetValue<string>().Should().Be("pie");
        upper.Body!["yAgg"]!.GetValue<string>().Should().Be("count");
        bar.Body!["chartType"]!.GetValue<string>().Should().Be("column");
    }

    // ── Rule: filters are written as field:operator:value and sent as the API reads them ──

    [Fact]
    public void Filter_ReadsFieldOperatorAndValue_KeepingColonsInTheValue()
    {
        var filter = ChartSpec.ParseFilter("created_at:gte:2026-01-01T09:30:00Z", out var error);

        error.Should().BeNull();
        Matches(filter, """{"field":"created_at","op":"gte","value":"2026-01-01T09:30:00Z"}""");
    }

    [Fact]
    public void Filter_WithoutAValue_IsFineForExistsAndNotExists()
    {
        var build = Create(Valid(new ChartInput(Filters: ["deleted_at:not_exists", "email:exists"])));

        build.Ok.Should().BeTrue(string.Join("; ", build.Problems));
        Matches(build.Body!["filters"], """[{"field":"deleted_at","op":"not_exists"},{"field":"email","op":"exists"}]""");
    }

    [Theory]
    [InlineData("status")]
    [InlineData("status:")]
    [InlineData(":eq:x")]
    public void Filter_WithoutAFieldAndOperator_IsRefusedWithTheExpectedForm(string text)
    {
        var build = Create(Valid(new ChartInput(Filters: [text])));

        build.Problems.Should().ContainSingle().Which.Should().Contain("field:operator:value");
    }

    [Fact]
    public void Filter_WithAnUnknownOperator_ListsTheOperators()
    {
        var build = Create(Valid(new ChartInput(Filters: ["status:like:x"])));

        build.Problems.Should().ContainSingle().Which.Should().Contain("'like'").And.Contain("not_exists");
    }

    [Fact]
    public void Filter_ThatNeedsAValue_IsRefusedWithoutOne()
    {
        var build = Create(Valid(new ChartInput(Filters: ["status:eq"])));

        build.Problems.Should().ContainSingle().Which.Should().Contain("needs a value");
    }

    [Fact]
    public void Filters_OnTheSameFieldTwice_AreRefused_BecauseTheChartKeepsOnePerField()
    {
        var build = Create(Valid(new ChartInput(Filters: ["total:gte:10", "total:lte:100"])));

        build.Problems.Should().ContainSingle().Which.Should().Contain("two filters on total");
    }

    [Fact]
    public void Filters_InConfig_HaveTheirNumbersAndBooleansSentAsText()
    {
        var build = Create(Valid(new ChartInput(Config: """{"filters":[{"field":"total","op":"GT","value":10},{"field":"paid","op":"eq","value":true}]}""")));

        build.Ok.Should().BeTrue(string.Join("; ", build.Problems));
        Matches(build.Body!["filters"], """[{"field":"total","op":"gt","value":"10"},{"field":"paid","op":"eq","value":"true"}]""");
    }

    // ── Rule: --config fills in what the options don't cover, and the options win ──

    [Fact]
    public void Config_SetsWhatTheOptionsDont_AndAnOptionOverridesIt()
    {
        var build = Create(Valid(new ChartInput(Limit: 5, Config: """{"limit":99,"timeframeOffsetDays":7,"compareEnabled":true}""")));

        build.Ok.Should().BeTrue(string.Join("; ", build.Problems));
        build.Body!["limit"]!.GetValue<int>().Should().Be(5);
        build.Body!["timeframeOffsetDays"]!.GetValue<int>().Should().Be(7);
        build.Body!["compareEnabled"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void Config_AcceptsSnakeCaseNames_SoAnAnswerFromChartsGetCanBePassedBack()
    {
        var build = Create(Valid(new ChartInput(Config: """{"x_interval":"week","funnel_style":"sankey","timeframe_offset_days":3}""")));

        build.Ok.Should().BeTrue(string.Join("; ", build.Problems));
        build.Body!["xInterval"]!.GetValue<string>().Should().Be("week");
        build.Body!["funnelStyle"]!.GetValue<string>().Should().Be("sankey");
        build.Body!["timeframeOffsetDays"]!.GetValue<int>().Should().Be(3);
    }

    [Fact]
    public void Config_WithFunnelStagesInSnakeCase_IsSentWithTheNamesTheApiReads()
    {
        var build = Create(new ChartInput(Name: "F", Entity: "u", Type: "funnel",
            Config: """{"funnel_stages":[{"label":"A","entity_name":"signups","within_days":7,"source_filter":{"some_key":"x"}}]}"""));

        build.Ok.Should().BeTrue(string.Join("; ", build.Problems));
        Matches(build.Body!["funnelStages"], """[{"label":"A","entityName":"signups","withinDays":7,"sourceFilter":{"some_key":"x"}}]""");
    }

    [Fact]
    public void Config_WithASettingNoChartHas_IsRefusedNamingIt()
    {
        var build = Create(Valid(new ChartInput(Config: """{"colour":"red","limt":5}""")));

        build.Problems.Should().ContainSingle().Which.Should().Contain("colour, limt").And.Contain("Known settings");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("null")]
    public void Config_ThatIsntAJsonObject_IsRefused(string config)
    {
        var build = Create(Valid(new ChartInput(Config: config)));

        build.Problems.Should().ContainSingle().Which.Should().Contain("--config");
    }

    [Fact]
    public void Config_WithTheWrongKindOfValue_NamesTheSetting()
    {
        var build = Create(Valid(new ChartInput(Config: """{"limit":"ten","cumulative":"yes"}""")));

        build.Problems.Should().Contain("limit must be a whole number.").And.Contain("cumulative must be true or false.");
    }

    // ── Rule: a custom date range is written the way the dashboard writes it ──

    [Fact]
    public void FromAndTo_MakeACustomRange_WithTheWholeLastDayIncluded()
    {
        var build = Create(Valid(new ChartInput(From: "2026-01-01", To: "2026-03-31")));

        build.Ok.Should().BeTrue(string.Join("; ", build.Problems));
        build.Body!["timeframePreset"]!.GetValue<string>().Should().Be("custom");
        build.Body!["timeframeFrom"]!.GetValue<string>().Should().Be("2026-01-01T00:00:00Z");
        build.Body!["timeframeTo"]!.GetValue<string>().Should().Be("2026-03-31T23:59:59Z");
    }

    [Fact]
    public void FromAndTo_WithAPreset_AreRefused_BecauseThePresetWouldWin()
    {
        var build = Create(Valid(new ChartInput(Timeframe: "30d", From: "2026-01-01")));

        build.Problems.Should().ContainSingle().Which.Should().Contain("can't go with --timeframe 30d");
    }

    [Fact]
    public void ACustomTimeframeWithoutDates_IsRefused()
    {
        var build = Create(Valid(new ChartInput(Timeframe: "custom")));

        build.Problems.Should().ContainSingle().Which.Should().Contain("--timeframe custom needs --from");
    }

    [Fact]
    public void APresetClearsAnyEarlierCustomDates()
    {
        var existing = Existing("""{"id":1,"name":"C","data_source":"entity","chart_type":"stat","entity_name":"orders","timeframe_preset":"custom","timeframe_from":"2026-01-01T00:00:00Z","timeframe_to":"2026-02-01T00:00:00Z"}""");

        var build = ChartSpec.Build(new ChartInput(Timeframe: "7d"), existing, create: false);

        build.Ok.Should().BeTrue(string.Join("; ", build.Problems));
        build.Body!["timeframePreset"]!.GetValue<string>().Should().Be("7d");
        build.Body!["timeframeFrom"].Should().BeNull();
        build.Body!["timeframeTo"].Should().BeNull();
    }

    [Theory]
    [InlineData("yesterday")]
    [InlineData("2026-13-45")]
    public void ADateThatIsntADate_IsRefusedNamingTheOption(string from)
    {
        var build = Create(Valid(new ChartInput(From: from)));

        build.Problems.Should().ContainSingle().Which.Should().StartWith($"--from '{from}' isn't a date");
    }

    [Fact]
    public void AnEndBeforeTheStart_IsRefused()
    {
        var build = Create(Valid(new ChartInput(From: "2026-03-01", To: "2026-01-01")));

        build.Problems.Should().ContainSingle().Which.Should().Contain("is before --from");
    }

    // ── Rule: an update changes only what was passed and keeps every other setting ──

    private const string StoredChart = """
        {"id":12,"name":"Orders by status","data_source":"entity","chart_type":"column","entity_name":"orders","x_field":"status",
         "y_field":"total","y_agg":"sum","x_interval":null,"timeframe_preset":"30d","limit":10,"sort_direction":"desc","cumulative":true,
         "target":500,"filters":"[{\"field\":\"region\",\"op\":\"eq\",\"value\":\"uk\"}]","funnel_stages":null,"config":null,
         "tenant_id":42,"created_at":"2026-01-01T00:00:00Z","created_by":7,"updated_at":"2026-01-02T00:00:00Z","updated_by":7,"locked":false}
        """;

    [Fact]
    public void Update_KeepsEverySettingThatWasntPassed()
    {
        var build = ChartSpec.Build(new ChartInput(Name: "Orders by state"), Existing(StoredChart), create: false);

        build.Ok.Should().BeTrue(string.Join("; ", build.Problems));
        Matches(build.Body,
            """
            {"name":"Orders by state","dataSource":"entity","chartType":"column","entityName":"orders","xField":"status",
             "yField":"total","yAgg":"sum","timeframePreset":"30d","limit":10,"sortDirection":"desc","cumulative":true,
             "target":500,"filters":[{"field":"region","op":"eq","value":"uk"}]}
            """);
    }

    [Fact]
    public void Update_DoesNotSendWhatTheServerManages()
    {
        var build = ChartSpec.Build(new ChartInput(Limit: 20), Existing(StoredChart), create: false);

        build.Body!.Select(p => p.Key).Should().NotContain(["id", "tenantId", "tenant_id", "createdAt", "createdBy", "updatedAt", "updatedBy", "locked"]);
    }

    [Fact]
    public void Update_ChangesOnlyThePassedSetting()
    {
        var build = ChartSpec.Build(new ChartInput(Limit: 20, Filters: ["region:eq:eu"]), Existing(StoredChart), create: false);

        build.Body!["limit"]!.GetValue<int>().Should().Be(20);
        Matches(build.Body!["filters"], """[{"field":"region","op":"eq","value":"eu"}]""");
        build.Body!["yAgg"]!.GetValue<string>().Should().Be("sum");
        build.Body!["target"]!.GetValue<decimal>().Should().Be(500m);
    }

    [Fact]
    public void Update_ThatLeavesTheChartIncomplete_IsRefused()
    {
        var build = ChartSpec.Build(new ChartInput(Type: "line", Config: """{"x_field":null}"""), Existing(StoredChart), create: false);

        build.Problems.Should().ContainSingle().Which.Should().Contain("needs --group-by");
    }

    [Fact]
    public void Update_CanClearASetting_WithANullInConfig()
    {
        var build = ChartSpec.Build(new ChartInput(Config: """{"filters":null,"limit":null}"""), Existing(StoredChart), create: false);

        build.Ok.Should().BeTrue(string.Join("; ", build.Problems));
        build.Body!["filters"].Should().BeNull();
        build.Body!["limit"].Should().BeNull();
    }

    [Fact]
    public void Preview_NeedsNoName()
    {
        var build = ChartSpec.Build(new ChartInput(Entity: "orders", Type: "stat"), existing: null, create: false);

        build.Ok.Should().BeTrue(string.Join("; ", build.Problems));
        build.Body!.ContainsKey("name").Should().BeFalse();
    }
}
