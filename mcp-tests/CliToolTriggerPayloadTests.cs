using System.Text.Json;
using AnythinkMcp.Tools;
using FluentAssertions;

namespace AnythinkMcp.Tests;

public class CliToolTriggerPayloadTests
{
    private static List<string> Args(params string[] args) => ["trigger", "154", .. args];

    [Fact]
    public void Trigger_Payload_SentAsStringifiedData()
    {
        var body = CliTool.BuildTriggerPayload(Args("--payload", """{"title":"Hello","n":1}"""));

        body!["data"]!.GetValue<string>().Should().Be("""{"title":"Hello","n":1}""");
    }

    [Fact]
    public void Trigger_EntityFlags_SentAsSnakeCaseKeys()
    {
        var body = CliTool.BuildTriggerPayload(Args("--entity", "blog_posts", "--entity-id", "42"));

        body!["entity_name"]!.GetValue<string>().Should().Be("blog_posts");
        body["entity_id"]!.GetValue<int>().Should().Be(42);
        body.ContainsKey("data").Should().BeFalse();
    }

    [Fact]
    public void Trigger_NoFlags_SendsNoPayload()
    {
        CliTool.BuildTriggerPayload(Args()).Should().BeNull();
    }

    [Fact]
    public void Trigger_InvalidPayloadJson_Throws()
    {
        var act = () => CliTool.BuildTriggerPayload(Args("--payload", "{not json"));

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Trigger_NonIntegerEntityId_Throws()
    {
        var act = () => CliTool.BuildTriggerPayload(Args("--entity-id", "abc"));

        act.Should().Throw<ArgumentException>();
    }
}
