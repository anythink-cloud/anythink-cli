using System.Net;
using AnythinkCli.Client;
using AnythinkCli.Commands;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkCli.Tests;

public class WorkflowsTriggerTests
{
    private const string OrgPath = "https://api.example.com/org/99999";

    // ── Wire shape: snake_case keys, data as a JSON string ───────────────────

    [Fact]
    public void BuildPayload_UsesSnakeCaseKeys_AndStringifiesData()
    {
        var body = WorkflowsTriggerCommand.BuildPayload("""{"title":"Hi"}""", "blog_posts", 42)!;

        body["entity_name"]!.GetValue<string>().Should().Be("blog_posts");
        body["entity_id"]!.GetValue<int>().Should().Be(42);
        body["data"]!.GetValue<string>().Should().Be("""{"title":"Hi"}""");
        body.ContainsKey("entityName").Should().BeFalse();
    }

    [Fact]
    public void BuildPayload_NoOptions_ReturnsNull()
        => WorkflowsTriggerCommand.BuildPayload(null, null, null).Should().BeNull();

    [Fact]
    public void BuildPayload_EntityOnly_OmitsData()
    {
        var body = WorkflowsTriggerCommand.BuildPayload(null, "blog_posts", null)!;
        body.ContainsKey("data").Should().BeFalse();
        body.ContainsKey("entity_id").Should().BeFalse();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    public void BuildPayload_InvalidJson_Throws(string json)
    {
        var act = () => WorkflowsTriggerCommand.BuildPayload(json, null, null);
        act.Should().Throw<System.Text.Json.JsonException>();
    }

    // ── The endpoint answers 204 No Content ──────────────────────────────────

    [Fact]
    public async Task TriggerWorkflowAsync_NoContent_SendsSnakeCaseBody()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Post, $"{OrgPath}/workflows/76/trigger")
               .WithPartialContent("\"entity_name\":\"blog_posts\"")
               .Respond(HttpStatusCode.NoContent);

        var client = new AnythinkClient("99999", "https://api.example.com", new HttpClient(handler));
        await client.TriggerWorkflowAsync(76, WorkflowsTriggerCommand.BuildPayload("""{"a":1}""", "blog_posts", null));

        handler.VerifyNoOutstandingExpectation();
    }
}
