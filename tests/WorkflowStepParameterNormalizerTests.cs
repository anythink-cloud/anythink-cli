using System.Text.Json;
using AnythinkCli.Commands;
using FluentAssertions;

namespace AnythinkCli.Tests;

public class WorkflowStepParameterNormalizerTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void ReadData_KeepsEntity_SoSeededReadStepsStillRun()
    {
        var result = WorkflowStepParameterNormalizer.Normalize("ReadData", Parse("""{"entity":"albums","limit":1}"""));

        result.GetProperty("entity").GetString().Should().Be("albums");
        result.TryGetProperty("entity_name", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("CreateData")]
    [InlineData("UpdateData")]
    [InlineData("UpsertData")]
    public void WriteActions_RenameEntityToEntityName(string action)
    {
        var result = WorkflowStepParameterNormalizer.Normalize(action, Parse("""{"entity":"albums"}"""));

        result.GetProperty("entity_name").GetString().Should().Be("albums");
        result.TryGetProperty("entity", out _).Should().BeFalse();
    }

    [Fact]
    public void UpdateData_DataBecomesPayloadString()
    {
        var result = WorkflowStepParameterNormalizer.Normalize("UpdateData", Parse("""{"entity_name":"albums","data":{"name":"x"}}"""));

        result.GetProperty("payload").GetString().Should().Be("""{"name":"x"}""");
    }

    [Fact]
    public void ParametersWithBothKeys_AreLeftAlone()
    {
        var input = Parse("""{"entity":"albums","entity_name":"albums"}""");

        WorkflowStepParameterNormalizer.Normalize("ReadData", input).GetRawText().Should().Be(input.GetRawText());
    }
}
