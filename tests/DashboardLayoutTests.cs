using System.Text.Json.Nodes;
using AnythinkCli.Models;
using FluentAssertions;

namespace AnythinkCli.Tests;

public class DashboardLayoutTests
{
    private static JsonObject Widget(int id, string type = "chart") => new() { ["id"] = id, ["type"] = type };

    private static void Matches(JsonNode? actual, string expected) =>
        JsonNode.DeepEquals(actual, JsonNode.Parse(expected)).Should().BeTrue($"expected {expected} but was {actual?.ToJsonString()}");

    // ── Rule: placing a chart keeps every existing position exactly as it was ──

    [Fact]
    public void Place_AppendsTheNewWidget_AndLeavesEveryExistingEntryUntouched()
    {
        var layout = DashboardLayout.Parse("""[{"widgetId":1,"x":0,"y":0,"w":6,"h":3,"minH":2},{"widgetId":2,"x":6,"y":0,"w":6,"h":3}]""");

        DashboardLayout.Place(layout, [Widget(1), Widget(2)], newWidgetId: 3, new WidgetPlacement(Width: 12));

        Matches(layout,
            """
            [{"widgetId":1,"x":0,"y":0,"w":6,"h":3,"minH":2},{"widgetId":2,"x":6,"y":0,"w":6,"h":3},
             {"widgetId":3,"x":0,"y":3,"w":12,"h":3}]
            """);
    }

    [Fact]
    public void Place_WithEveryPositionGiven_UsesThem()
    {
        var layout = DashboardLayout.Parse(null);

        DashboardLayout.Place(layout, [], 9, new WidgetPlacement(X: 4, Y: 7, Width: 8, Height: 2));

        Matches(layout, """[{"widgetId":9,"x":4,"y":7,"w":8,"h":2}]""");
    }

    [Fact]
    public void Place_WithNoSavedLayout_GoesBelowWhereTheDashboardFlowsItsWidgets()
    {
        var widgets = new[] { Widget(1), Widget(2), Widget(3), Widget(4) };
        var layout = DashboardLayout.Parse(null);

        DashboardLayout.Place(layout, widgets, 5, new WidgetPlacement(Width: 4));

        // Four 4x3 widgets flow three to a row, so the second row ends at y=6.
        layout[0]!["y"]!.GetValue<int>().Should().Be(6);
    }

    [Fact]
    public void Place_CountsAWidgetWithoutASavedPosition_AlongsideThoseWithOne()
    {
        var layout = DashboardLayout.Parse("""[{"widgetId":1,"x":0,"y":0,"w":12,"h":5}]""");

        DashboardLayout.Place(layout, [Widget(1), Widget(2, "quick_action")], 3, new WidgetPlacement(Width: 4));

        layout.OfType<JsonObject>().Last()["y"]!.GetValue<int>().Should().Be(5);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"a\":1}")]
    [InlineData("")]
    public void Parse_OfALayoutThatIsntAList_IsEmpty(string text)
        => DashboardLayout.Parse(text).Should().BeEmpty();

    // ── Rule: removing a widget drops only its own position ───────────────────

    [Fact]
    public void Remove_DropsOnlyThatWidgetsEntry()
    {
        var layout = DashboardLayout.Parse("""[{"widgetId":1,"x":0,"y":0,"w":6,"h":3},{"widgetId":2,"x":6,"y":0,"w":6,"h":3,"minH":1}]""");

        DashboardLayout.Remove(layout, 1).Should().BeTrue();

        Matches(layout, """[{"widgetId":2,"x":6,"y":0,"w":6,"h":3,"minH":1}]""");
    }

    [Fact]
    public void Remove_OfAWidgetWithNoEntry_ChangesNothing()
    {
        var layout = DashboardLayout.Parse("""[{"widgetId":2,"x":0,"y":0,"w":6,"h":3}]""");

        DashboardLayout.Remove(layout, 1).Should().BeFalse();
        layout.Should().HaveCount(1);
    }

    // ── Rule: a position that can't fit the 12-column grid is refused ──────────

    [Theory]
    [InlineData(-1, null, null, null, "--column can't be negative")]
    [InlineData(null, -1, null, null, "--row can't be negative")]
    [InlineData(null, null, 0, null, "--width must be at least 1")]
    [InlineData(null, null, null, 0, "--height must be at least 1")]
    [InlineData(null, null, 13, null, "--width can't be more than 12")]
    [InlineData(9, null, 4, null, "run past the right edge")]
    [InlineData(10, null, null, null, "run past the right edge")]
    public void Check_RefusesAPositionOutsideTheGrid(int? x, int? y, int? w, int? h, string message)
        => DashboardLayout.Check(new WidgetPlacement(x, y, w, h)).Should().ContainSingle().Which.Should().Contain(message);

    [Fact]
    public void Check_AcceptsAFullWidthWidget()
        => DashboardLayout.Check(new WidgetPlacement(0, 0, 12, 4)).Should().BeEmpty();
}
