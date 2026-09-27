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
}
