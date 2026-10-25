using System.Text.Json.Nodes;
using AnythinkCli.Commands;
using AnythinkCli.Output;
using FluentAssertions;
using RichardSzalay.MockHttp;
using Spectre.Console;

namespace AnythinkCli.Tests;

[Collection("ConsoleOutput")]
public class JsonOutputTests
{
    private const string Items = CommandRunner.Org + "/entities/articles/items";

    // Longer than any console line, so Spectre's JSON view would have split it.
    private static readonly string LongTitle = string.Join(" ", Enumerable.Repeat("a sentence long enough to wrap", 20));

    private static string Article(int id = 7) => new JsonObject { ["id"] = id, ["title"] = LongTitle, ["status"] = "draft" }.ToJsonString();

    private static MockHttpMessageHandler Api()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, CommandRunner.Org + "/entities/articles/fields").Respond("application/json", "[]");
        mock.When(HttpMethod.Get, Items + "/7").Respond("application/json", Article());
        mock.When(HttpMethod.Post, Items).Respond("application/json", Article());
        mock.When(HttpMethod.Put, Items + "/7").Respond("application/json", Article());
        mock.When(HttpMethod.Delete, Items + "/7").Respond(System.Net.HttpStatusCode.NoContent);
        mock.When(HttpMethod.Get, Items).Respond("application/json",
            $$"""{"items":[{{Article()}}],"total_items":1,"total_pages":1,"has_next_page":false}""");
        mock.When(HttpMethod.Get, CommandRunner.Org).Respond("application/json",
            new JsonObject { ["id"] = 99999, ["name"] = "Demo", ["description"] = LongTitle }.ToJsonString());
        return mock;
    }

    public static TheoryData<string> JsonCommands => new()
    {
        "data list", "data get", "data create", "data update", "data delete", "settings get",
    };

    public static TheoryData<string> RecordCommands => new()
    {
        "data list", "data get", "data create", "data update", "settings get",
    };

    private static Task<int> Run(string command) => command switch
    {
        "data list" => new DataListCommand().ExecuteAsync(null!, new DataListSettings { Entity = "articles", Json = true }),
        "data get" => new DataGetCommand().ExecuteAsync(null!, new DataGetSettings { Entity = "articles", Id = 7, Json = true }),
        "data create" => new DataCreateCommand().ExecuteAsync(null!,
            new DataCreateSettings { Entity = "articles", Data = """{"title":"x"}""", Json = true }),
        "data update" => new DataUpdateCommand().ExecuteAsync(null!,
            new DataUpdateSettings { Entity = "articles", Id = 7, Data = """{"status":"draft"}""", Json = true }),
        "data delete" => new DataDeleteCommand().ExecuteAsync(null!,
            new DataDeleteSettings { Entity = "articles", Id = 7, Yes = true, Json = true }),
        "settings get" => new SettingsGetCommand().ExecuteAsync(null!, new SettingsGetSettings { Json = true }),
        _ => throw new ArgumentOutOfRangeException(nameof(command)),
    };

    // ── Rule: with --json, stdout is one JSON document and nothing else ──

    [Theory]
    [MemberData(nameof(JsonCommands))]
    public async Task JsonOutput_StdoutIsOnlyTheJsonDocument(string command)
    {
        var (code, output) = await CommandRunner.RunAsync(Api(), () => Run(command));

        code.Should().Be(0);
        output.Should().NotContainAny("✓", "Creating", "Fetching", "Updating", "Deleting", "──");
        var parse = () => JsonNode.Parse(output);
        parse.Should().NotThrow();
    }

    [Fact]
    public async Task JsonOutput_DataCreate_PrintsTheCreatedRecord()
    {
        var (_, output) = await CommandRunner.RunAsync(Api(), () => Run("data create"));

        JsonNode.Parse(output)!["id"]!.GetValue<int>().Should().Be(7);
    }

    [Fact]
    public void JsonOutput_WithJsonFlag_StatusAndErrorsGoToStderr()
    {
        var (stdout, stderr) = (new StringWriter(), new StringWriter());
        var (originalOut, originalError, originalConsole) = (Console.Out, Console.Error, AnsiConsole.Console);
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            JsonOutput.SendConsoleToStderrFor(["data", "create", "articles", "--json"]);
            Renderer.Success("Record created.");
            Renderer.Error("Something failed.");
            JsonOutput.Write(new JsonObject { ["id"] = 7 });
        }
        finally
        {
            AnsiConsole.Console = originalConsole;
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        JsonNode.Parse(stdout.ToString())!["id"]!.GetValue<int>().Should().Be(7);
        stderr.ToString().Should().Contain("Record created.").And.Contain("Something failed.");
    }

    [Fact]
    public void JsonOutput_WithoutJsonFlag_ConsoleIsLeftAlone()
    {
        var original = AnsiConsole.Console;

        JsonOutput.SendConsoleToStderrFor(["data", "create", "articles", "--data", "{\"note\":\"--json\"}"]);

        AnsiConsole.Console.Should().BeSameAs(original);
    }

    // ── Rule: long string values are never wrapped onto a new line inside the JSON ──

    [Theory]
    [MemberData(nameof(RecordCommands))]
    public async Task JsonOutput_LongStrings_AreNotWrapped(string command)
    {
        var (_, output) = await CommandRunner.RunAsync(Api(), () => Run(command), width: 80);

        var json = JsonNode.Parse(output)!;
        var record = json is JsonArray list ? list[0]! : json;
        (record["title"] ?? record["description"])!.GetValue<string>().Should().Be(LongTitle);
    }
}
