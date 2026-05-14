using AnythinkCli.Importers.Directus;
using FluentAssertions;
using System.Text.Json;

namespace AnythinkCli.Tests;

// ── DirectusFlowMapping ───────────────────────────────────────────────────────
// Covers trigger mapping (Directus event/schedule/webhook → Anythink
// Event/Timed/Api triggers with proper config) and operation translation
// (Directus op type + options → valid Anythink WorkflowAction + parameters
// in the snake_case shape AnyAPI's action parameter records expect).

public class DirectusFlowMappingTests
{
    private static DirectusFlow Flow(string trigger, object? options = null) =>
        new(Id: "f1", Name: "test", Status: "active", Trigger: trigger,
            Options: options is null ? null : JsonSerializer.SerializeToElement(options),
            FirstOperation: null);

    private static DirectusOperation Op(string type, object? options = null, string id = "op1", string key = "step1") =>
        new(Id: id, Name: "Step", Key: key, Type: type, Flow: "f1",
            Resolve: null, Reject: null,
            Options: options is null ? null : JsonSerializer.SerializeToElement(options));

    // ── Trigger mapping ───────────────────────────────────────────────────────

    [Fact]
    public void Schedule_Trigger_Maps_To_Timed_With_Cron()
    {
        var t = DirectusFlowMapping.MapTrigger(Flow("schedule", new { cron = "*/5 * * * *" }));

        t.Type.Should().Be("Timed");
        t.Config.CronExpression.Should().Be("*/5 * * * *");
        t.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Schedule_Without_Cron_Falls_Back_To_Default()
    {
        var t = DirectusFlowMapping.MapTrigger(Flow("schedule"));

        t.Type.Should().Be("Timed");
        t.Config.CronExpression.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("items.create", "EntityCreated")]
    [InlineData("items.update", "EntityUpdated")]
    [InlineData("items.delete", "EntityDeleted")]
    [InlineData("items.unknown", "EntityCreated")] // safe fallback
    public void Event_Trigger_Maps_Item_Scope_To_Anythink_Event(string scope, string expectedEvent)
    {
        var t = DirectusFlowMapping.MapTrigger(Flow("event", new
        {
            scope = new[] { scope },
            collections = new[] { "articles" }
        }));

        t.Type.Should().Be("Event");
        t.Config.Event.Should().Be(expectedEvent);
        t.Config.EventEntity.Should().Be("articles");
    }

    [Fact]
    public void Webhook_Trigger_Produces_Api_With_Sanitised_Route()
    {
        // AnyAPI requires a non-empty api_route on Api triggers, and
        // Directus webhooks don't carry one — derive from the flow name.
        var t = DirectusFlowMapping.MapTrigger(new DirectusFlow(
            Id: "f1", Name: "Sync External Articles!", Status: "active",
            Trigger: "webhook", Options: null, FirstOperation: null));

        t.Type.Should().Be("Api");
        t.Config.ApiRoute.Should().Be("sync-external-articles");
    }

    [Fact]
    public void Unknown_Trigger_Falls_Back_To_Manual()
    {
        var t = DirectusFlowMapping.MapTrigger(Flow("something-weird"));
        t.Type.Should().Be("Manual");
    }

    // ── Operation translation ────────────────────────────────────────────────

    [Fact]
    public void Log_Op_Becomes_RunScript_With_Console_Log()
    {
        var r = DirectusFlowMapping.Translate(Op("log", new { message = "hello world" }));

        r.Action.Should().Be("RunScript");
        var script = r.Parameters.GetProperty("script").GetString()!;
        // Must be valid JS that emits the original message — JSON-encoded for safety.
        script.Should().Contain("console.log(");
        script.Should().Contain("hello world");
        r.NeedsManualReview.Should().BeFalse();
    }

    [Fact]
    public void Log_With_Mustache_Is_Flagged_For_Review()
    {
        var r = DirectusFlowMapping.Translate(Op("log", new { message = "id={{ $trigger.id }}" }));

        r.NeedsManualReview.Should().BeTrue();
        r.ReviewNote.Should().Contain("templating");
    }

    [Fact]
    public void Mail_Op_Becomes_SendAnEmail_With_Snake_Case_Params()
    {
        var r = DirectusFlowMapping.Translate(Op("mail", new
        {
            to      = new[] { "a@example.com", "b@example.com" },
            subject = "Hi",
            body    = "<p>x</p>",
        }));

        r.Action.Should().Be("SendAnEmail");
        r.Parameters.GetProperty("to").GetString().Should().Be("a@example.com,b@example.com");
        r.Parameters.GetProperty("template_type").GetString().Should().Be("Custom");
        // Subject + body get stringified inside payload so the email worker
        // can pull them out as part of the Custom template render.
        r.Parameters.GetProperty("payload").GetString().Should().Contain("Hi");
    }

    [Fact]
    public void Request_Op_Becomes_CallAnApi_With_Headers_Dict()
    {
        var r = DirectusFlowMapping.Translate(Op("request", new
        {
            url     = "https://api.example.com/x",
            method  = "post",
            headers = new { Accept = "application/json" }
        }));

        r.Action.Should().Be("CallAnApi");
        r.Parameters.GetProperty("url").GetString().Should().Be("https://api.example.com/x");
        r.Parameters.GetProperty("method").GetString().Should().Be("POST");  // upper-cased
        r.Parameters.GetProperty("headers").GetProperty("Accept").GetString()
            .Should().Be("application/json");
    }

    [Fact]
    public void Request_Headers_From_Array_Form_Become_Dict()
    {
        var r = DirectusFlowMapping.Translate(Op("request", new
        {
            url     = "https://x.com",
            method  = "GET",
            headers = new[] { new { header = "X-Custom", value = "1" } }
        }));

        r.Parameters.GetProperty("headers").GetProperty("X-Custom").GetString().Should().Be("1");
    }

    [Theory]
    [InlineData("item-create", "CreateData")]
    [InlineData("item-update", "UpdateData")]
    [InlineData("item-delete", "DeleteData")]
    [InlineData("item-read",   "ReadData")]
    public void Item_CRUD_Ops_Map_To_Anythink_Data_Actions(string opType, string expectedAction)
    {
        var r = DirectusFlowMapping.Translate(Op(opType,
            new { collection = "articles", key = "5",
                  payload    = new { title = "x" } }));

        r.Action.Should().Be(expectedAction);
    }

    [Fact]
    public void Item_Create_Stringifies_Payload()
    {
        var r = DirectusFlowMapping.Translate(Op("item-create", new
        {
            collection = "articles",
            payload    = new { title = "Hello", views = 3 }
        }));

        r.Parameters.GetProperty("entity_name").GetString().Should().Be("articles");
        // CreateData.Payload is a string (the action validator reparses it)
        var payloadStr = r.Parameters.GetProperty("payload").GetString()!;
        payloadStr.Should().Contain("\"title\":\"Hello\"");
        payloadStr.Should().Contain("\"views\":3");
    }

    [Fact]
    public void Item_Delete_Builds_Filter_For_The_Key()
    {
        var r = DirectusFlowMapping.Translate(Op("item-delete", new { collection = "articles", key = "42" }));

        r.Action.Should().Be("DeleteData");
        r.Parameters.GetProperty("entity_name").GetString().Should().Be("articles");
        var filters = r.Parameters.GetProperty("filter_conditions");
        filters.GetArrayLength().Should().Be(1);
        filters[0].GetProperty("field").GetString().Should().Be("id");
        filters[0].GetProperty("value").GetString().Should().Be("42");
    }

    [Theory]
    [InlineData("notification")]
    [InlineData("sleep")]
    [InlineData("transform")]
    [InlineData("exec-script")]
    [InlineData("trigger")]
    [InlineData("totally-unknown-op-type")]
    public void Ops_Without_Direct_Equivalent_Flag_Manual_Review(string opType)
    {
        var r = DirectusFlowMapping.Translate(Op(opType));
        r.NeedsManualReview.Should().BeTrue();
        r.ReviewNote.Should().NotBeNullOrEmpty();
    }
}
