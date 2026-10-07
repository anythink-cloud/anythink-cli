using System.Net;
using System.Text.Json.Nodes;
using AnythinkCli.Commands;
using FluentAssertions;

namespace AnythinkCli.Tests;

[Collection("ConsoleOutput")]
public class WorkflowsUpdateTests
{
    private const string Existing = """
        {"id":31,"tenant_id":1,"name":"nightly","description":"old text","group":"ops","enabled":true,
         "editor_state":"{\"nodePositions\":{\"a\":1}}",
         "triggers":[
           {"id":8,"type":"Timed","enabled":true,"config":{"cron_expression":"0 2 * * *","next_run_at":"2026-10-08T02:00:00Z"}},
           {"id":9,"type":"Manual","enabled":false,"config":{"manual_entities":["sources"]}}],
         "steps":[{"id":1,"key":"a","name":"A","action":"RunScript","enabled":true,"is_start_step":true}]}
        """;

    private static RecordingHandler Api() => new((request, _) =>
        (HttpStatusCode.OK, request.Method == HttpMethod.Put ? Existing.Replace("old text", "new text") : Existing));

    private static async Task<(RecordingHandler Handler, int Code, JsonObject Put)> Update(WorkflowUpdateSettings settings)
    {
        var handler = Api();
        var (code, _) = await CommandRunner.RunAsync(handler, () => new WorkflowsUpdateCommand().ExecuteAsync(null!, settings));
        var put = handler.Requests.SingleOrDefault(r => r.Method == HttpMethod.Put).Body;
        return (handler, code, put is null ? new JsonObject() : JsonNode.Parse(put)!.AsObject());
    }

    [Fact]
    public async Task Update_OnlyTheDescription_KeepsTheTriggersEnabledGroupAndEditorState()
    {
        var (_, code, put) = await Update(new WorkflowUpdateSettings { Id = 31, Description = "new text" });

        code.Should().Be(0);
        put["name"]!.GetValue<string>().Should().Be("nightly");
        put["description"]!.GetValue<string>().Should().Be("new text");
        put["group"]!.GetValue<string>().Should().Be("ops");
        put["enabled"]!.GetValue<bool>().Should().BeTrue();
        put["editor_state"]!.GetValue<string>().Should().Be("""{"nodePositions":{"a":1}}""");

        var triggers = put["triggers"]!.AsArray();
        triggers.Select(t => (t!["type"]!.GetValue<string>(), t["enabled"]!.GetValue<bool>()))
            .Should().Equal(("Timed", true), ("Manual", false));
        triggers[0]!["config"]!["cron_expression"]!.GetValue<string>().Should().Be("0 2 * * *");
        triggers[0]!["config"]!["next_run_at"]!.GetValue<string>().Should().Be("2026-10-08T02:00:00Z");
        triggers[1]!["config"]!["manual_entities"]!.AsArray().Select(e => e!.GetValue<string>()).Should().Equal("sources");
    }

    [Fact]
    public async Task Update_OnlyTheName_KeepsTheDescription()
    {
        var (_, _, put) = await Update(new WorkflowUpdateSettings { Id = 31, Name = "renamed" });

        put["name"]!.GetValue<string>().Should().Be("renamed");
        put["description"]!.GetValue<string>().Should().Be("old text");
    }

    [Fact]
    public async Task Update_WithNothingToChange_SendsNothing()
    {
        var (handler, code, _) = await Update(new WorkflowUpdateSettings { Id = 31 });

        code.Should().Be(1);
        handler.Requests.Should().BeEmpty();
    }
}
