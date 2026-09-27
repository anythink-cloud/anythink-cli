using AnythinkCli.Commands;
using AnythinkCli.Models;
using FluentAssertions;
using System.Text.Json;

namespace AnythinkCli.Tests;

public class MigrateCurrentApiTests
{
    private static Field FieldOf(int? entityId) =>
        new(1, "name", null, "varchar", "input", false, false, false, false, false, false, false, null, false,
            EntityId: entityId);

    private static Entity EntityOf(int? id, params Field[] fields) =>
        new("things", "t_things", false, false, false, false, false, fields.ToList(), id);

    private static Workflow WorkflowOf(string? legacyTrigger, string? triggersJson) =>
        new(1, "wf", null, legacyTrigger, false, [],
            Triggers: triggersJson is null ? null : JsonDocument.Parse(triggersJson).RootElement);

    [Fact]
    public void EntityIdOf_Prefers_Top_Level_Id() =>
        MigrateCommand.EntityIdOf(EntityOf(7, FieldOf(9))).Should().Be(7);

    [Fact]
    public void EntityIdOf_Falls_Back_To_Field_Entity_Id() =>
        MigrateCommand.EntityIdOf(EntityOf(null, FieldOf(null), FieldOf(843))).Should().Be(843);

    [Fact]
    public void EntityIdOf_Is_Null_When_Nothing_Carries_It() =>
        MigrateCommand.EntityIdOf(EntityOf(null, FieldOf(null))).Should().BeNull();

    [Fact]
    public void TriggerRequests_Keeps_Only_Type_Enabled_And_Config()
    {
        var src = JsonDocument.Parse("""
            [{"id": 29, "workflow_id": 40, "tenant_id": 1, "type": "Event", "enabled": false,
              "config_json": "{}", "config": {"event": "EntityUpdated", "event_entity": "resource_check_ins"}}]
            """).RootElement;

        var result = MigrateCommand.TriggerRequests(src)!;

        result.Should().HaveCount(1);
        var trigger = result[0]!.AsObject();
        trigger.Select(p => p.Key).Should().BeEquivalentTo(["type", "enabled", "config"]);
        trigger["type"]!.GetValue<string>().Should().Be("Event");
        trigger["enabled"]!.GetValue<bool>().Should().BeFalse();
        trigger["config"]!["event_entity"]!.GetValue<string>().Should().Be("resource_check_ins");
    }

    [Fact]
    public void TriggerRequests_Is_Null_For_Missing_Or_Empty_List()
    {
        MigrateCommand.TriggerRequests(null).Should().BeNull();
        MigrateCommand.TriggerRequests(JsonDocument.Parse("[]").RootElement).Should().BeNull();
    }

    [Fact]
    public void TriggerTypes_Reads_The_List_Over_The_Legacy_Field() =>
        WorkflowOf(null, """[{"type": "Event"}, {"type": "Api"}]""").TriggerTypes()
            .Should().Equal("Event", "Api");

    [Fact]
    public void TriggerTypes_Falls_Back_To_The_Legacy_Field() =>
        WorkflowOf("Api", null).TriggerTypes().Should().Equal("Api");

    [Fact]
    public void TriggerLabel_Is_A_Dash_When_There_Are_No_Triggers() =>
        WorkflowOf(null, "[]").TriggerLabel().Should().Be("—");

    // ── --data-entities: never copy data for an entity that wasn't named ──

    [Fact]
    public void DataEntities_Absent_Means_Every_Entity() =>
        MigrateCommand.CopiesDataFor("profiles", MigrateCommand.DataEntitiesFilter(null)).Should().BeTrue();

    [Fact]
    public void DataEntities_Copies_Only_Named_Entities()
    {
        var filter = MigrateCommand.DataEntitiesFilter(" resources , Mental_Edge_Questions ");

        MigrateCommand.CopiesDataFor("resources", filter).Should().BeTrue();
        MigrateCommand.CopiesDataFor("mental_edge_questions", filter).Should().BeTrue();
        foreach (var personal in new[] { "profiles", "weekly_report", "ai_chat_sessions", "resource_check_ins" })
            MigrateCommand.CopiesDataFor(personal, filter).Should().BeFalse(personal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" , ")]
    public void DataEntities_Given_But_Empty_Copies_Nothing(string setting) =>
        MigrateCommand.CopiesDataFor("profiles", MigrateCommand.DataEntitiesFilter(setting)).Should().BeFalse();

    // ── Pagination: follow has_next_page, not the requested page size ──

    private static PaginatedResult<int> Page(int count, bool hasNext) =>
        new(Enumerable.Range(0, count).ToList(), null, null, hasNext, 1, 100);

    [Fact]
    public void Short_Page_With_More_To_Come_Keeps_Paging() =>
        MigrateCommand.HasMorePages(Page(25, hasNext: true)).Should().BeTrue();

    [Fact]
    public void Last_Page_Or_Empty_Page_Stops() {
        MigrateCommand.HasMorePages(Page(25, hasNext: false)).Should().BeFalse();
        MigrateCommand.HasMorePages(Page(0, hasNext: true)).Should().BeFalse();
    }

    // ── Search flags ──

    private static Field SearchField(bool searchable, bool publicly) =>
        new(1, "title", null, "varchar", "input", false, false, false, false, searchable, publicly, false, null, false);

    [Fact]
    public void Public_Search_Is_Dropped_When_Field_Is_Not_Searchable() =>
        MigrateCommand.PublicSearchFlag(SearchField(searchable: false, publicly: true)).Should().BeFalse();

    [Fact]
    public void Public_Search_Is_Kept_When_Field_Is_Searchable() =>
        MigrateCommand.PublicSearchFlag(SearchField(searchable: true, publicly: true)).Should().BeTrue();

    // ── Step updates carry the whole step ──

    [Fact]
    public void Step_Update_Carries_Parameters_Enabled_And_Start_Step()
    {
        var parameters = JsonDocument.Parse("""{"entity_name": "weekly_report"}""").RootElement;
        var step = new WorkflowStep(5, "save", "Save", "Saves the report", "CreateData",
            Enabled: true, IsStartStep: true, OnSuccessStepId: null, OnFailureStepId: null, Parameters: parameters);

        var json = JsonSerializer.SerializeToNode(MigrateCommand.StepUpdate(step, 12, null))!.AsObject();

        json["on_success_step_id"]!.GetValue<int>().Should().Be(12);
        json["enabled"]!.GetValue<bool>().Should().BeTrue();
        json["is_start_step"]!.GetValue<bool>().Should().BeTrue();
        json["description"]!.GetValue<string>().Should().Be("Saves the report");
        json["parameters"]!["entity_name"]!.GetValue<string>().Should().Be("weekly_report");
    }

    // ── Entity recovery after an empty error ──

    [Fact]
    public async Task Recovers_An_Entity_That_Exists_Despite_The_Error() =>
        (await MigrateCommand.RecoverCreatedEntity(() => Task.FromResult(EntityOf(3)))).Should().NotBeNull();

    [Fact]
    public async Task Reports_Absent_When_The_Entity_Really_Is_Missing() =>
        (await MigrateCommand.RecoverCreatedEntity(() => throw new InvalidOperationException("404")))
            .Should().BeNull();
}

