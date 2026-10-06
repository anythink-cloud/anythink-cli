using System.Text.Json;
using System.Text.Json.Nodes;
using AnythinkCli.Client;
using AnythinkMcp.Cli;
using FluentAssertions;
using RichardSzalay.MockHttp;
using Spectre.Console;

namespace AnythinkMcp.Tests;

public class MachineOutputTests
{
    private const string ApiUrl = "https://api.my.anythink.cloud";

    private static CliCommandTool Tool(string name) =>
        CliCommandTool.All(CliToolScope.Internal).Single(t => t.ProtocolTool.Name == name);

    private static Dictionary<string, JsonElement> Args(object values) =>
        JsonSerializer.SerializeToElement(values).EnumerateObject().ToDictionary(p => p.Name, p => p.Value);

    private static AnythinkClient Client(MockHttpMessageHandler mock) => new("42", ApiUrl, new HttpClient(mock));

    private static JsonObject Entity(string name, int fields) => new()
    {
        ["id"] = 1, ["name"] = name, ["table_name"] = "_" + name,
        ["enable_rls"] = false, ["is_system"] = false, ["is_public"] = false, ["lock_new_records"] = false,
        ["fields"] = new JsonArray(Enumerable.Range(0, fields).Select(i => (JsonNode)new JsonObject
        {
            ["id"] = i, ["name"] = $"field_{i}", ["database_type"] = "varchar", ["display_type"] = "input", ["is_required"] = i % 2 == 0,
        }).ToArray()),
    };

    private static JsonArray JsonArrayIn(string output) =>
        JsonNode.Parse(output[output.IndexOf('[')..(output.LastIndexOf(']') + 1)])!.AsArray();

    // ── Rule: a table comes back as JSON rows, with no rules, boxes or spinner text ──

    [Fact]
    public async Task EntitiesList_IsATitleAndJsonRows_WithNoRulesBoxesOrStatusText()
    {
        var mock = new MockHttpMessageHandler();
        mock.When($"{ApiUrl}/org/42/entities")
            .Respond("application/json", new JsonArray(Entity("blog_posts", 16), Entity("sources", 9)).ToJsonString());

        var result = await Tool("entities_list").RunAsync(Args(new { }), Client(mock));

        result.ExitCode.Should().Be(0);
        result.Output.Should().NotContainAny("─", "│", "╭", "Fetching");
        result.Output.Should().StartWith("Entities (2)");
        var rows = JsonArrayIn(result.Output);
        rows.Should().HaveCount(2);
        rows[0]!["Name"]!.GetValue<string>().Should().Be("blog_posts");
        rows[0]!["Fields"]!.GetValue<string>().Should().Be("16");
    }

    [Fact]
    public void TableCells_WithMarkup_ComeBackAsPlainText()
    {
        var table = new Table().AddColumn("[bold #F97316]Enabled[/]").AddColumn("Name");
        table.AddRow("[green]yes[/]", Markup.Escape("a [b] c"));

        var rows = JsonNode.Parse(AmbientConsole.TableJson(table))!.AsArray();

        rows.Single()!["Enabled"]!.GetValue<string>().Should().Be("yes");
        rows.Single()!["Name"]!.GetValue<string>().Should().Be("a [b] c");
    }

    [Fact]
    public void RepeatedOrEmptyHeaders_GetDistinctKeys_SoNoCellIsLost()
    {
        var table = new Table().AddColumn("A").AddColumn("A").AddColumn("").AddColumn("Column 3");
        table.AddRow("first", "second", "third", "fourth");

        var row = JsonNode.Parse(AmbientConsole.TableJson(table))!.AsArray().Single()!.AsObject();

        row.Select(p => (p.Key, p.Value!.GetValue<string>())).Should().Equal(
            ("A", "first"), ("A 2", "second"), ("Column 3", "third"), ("Column 3 2", "fourth"));
    }

    // ── Rule: people at a terminal still get the drawn table ──────────────────

    [Fact]
    public void ATerminalConsole_StillDrawsTablesWithBoxes()
    {
        var output = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Ansi = AnsiSupport.No, Out = new AnsiConsoleOutput(output) });

        console.Write(AnythinkCli.Output.Renderer.BuildTable("Name").AddRow("blog_posts"));

        output.ToString().Should().Contain("╭").And.Contain("blog_posts");
    }

    // ── Rule: JSON is written whole, never wrapped ────────────────────────────

    [Fact]
    public async Task DataGet_LongValues_AreNotWrapped_AndTheJsonParses()
    {
        var body = new string('x', 6_000);
        var mock = new MockHttpMessageHandler();
        mock.When($"{ApiUrl}/org/42/entities/posts/items/1")
            .Respond("application/json", new JsonObject { ["id"] = 1, ["body"] = body }.ToJsonString());

        var result = await Tool("data_get").RunAsync(Args(new { entity = "posts", id = "1" }), Client(mock));

        result.ExitCode.Should().Be(0);
        var json = result.Output[result.Output.IndexOf('{')..(result.Output.LastIndexOf('}') + 1)];
        JsonNode.Parse(json)!["body"]!.GetValue<string>().Should().Be(body);
    }

    // ── Rule: a get stays small enough to be worth sending to a model ─────────

    [Fact]
    public async Task EntitiesGet_ForASixteenFieldEntity_StaysUnderThreeThousandCharacters()
    {
        var mock = new MockHttpMessageHandler();
        mock.When($"{ApiUrl}/org/42/entities/blog_posts").Respond("application/json", Entity("blog_posts", 16).ToJsonString());

        var result = await Tool("entities_get").RunAsync(Args(new { name = "blog_posts" }), Client(mock));

        result.ExitCode.Should().Be(0);
        result.Output.Length.Should().BeLessThan(3_000);
        result.Output.Should().NotContain("─");
    }
}
