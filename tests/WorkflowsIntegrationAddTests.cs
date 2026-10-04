using AnythinkCli.Commands;
using FluentAssertions;

namespace AnythinkCli.Tests;

public class WorkflowsIntegrationAddTests
{
    private static WorkflowIntegrationAddSettings Settings(Action<WorkflowIntegrationAddSettings>? configure = null)
    {
        var s = new WorkflowIntegrationAddSettings { WorkflowId = 1, Key = "ai", Provider = "claude", Operation = "generate-text" };
        configure?.Invoke(s);
        return s;
    }

    // ── Wire shape: snake_case keys the server binds ─────────────────────────

    [Fact]
    public void BuildParameters_EntityFieldSource_UsesCredentialFieldPathKey()
    {
        var p = WorkflowsIntegrationAddCommand.BuildParameters(Settings(s =>
        {
            s.CredentialSource = "entity_field";
            s.CredentialField = "{{ $anythink.trigger.data.api_key }}";
        }));

        p.GetProperty("credential_field_path").GetString().Should().Be("{{ $anythink.trigger.data.api_key }}");
        p.TryGetProperty("credential_field", out _).Should().BeFalse();
    }

    [Fact]
    public void BuildParameters_ConnectionSource_UsesConnectionIdKey()
    {
        var p = WorkflowsIntegrationAddCommand.BuildParameters(Settings(s =>
        {
            s.CredentialSource = "connection";
            s.ConnectionId = "12";
        }));

        p.GetProperty("credential_source").GetString().Should().Be("connection");
        p.GetProperty("connection_id").GetString().Should().Be("12");
    }

    [Fact]
    public void BuildParameters_InputsMerge_JsonThenFlagsThenModel()
    {
        var p = WorkflowsIntegrationAddCommand.BuildParameters(Settings(s =>
        {
            s.InputsJson = """{"prompt":"from-json","temperature":0.2}""";
            s.Inputs = ["prompt=from-flag=with-equals"];
            s.Model = "m1";
        }));

        var inputs = p.GetProperty("inputs");
        inputs.GetProperty("prompt").GetString().Should().Be("from-flag=with-equals");
        inputs.GetProperty("temperature").GetDouble().Should().Be(0.2);
        inputs.GetProperty("model").GetString().Should().Be("m1");
    }

    // ── Validation mirrors the server's credential-source rules ──────────────

    [Theory]
    [InlineData("connection")]
    [InlineData("entity_field")]
    public void BuildParameters_SourceWithoutItsRequiredValue_Throws(string source)
    {
        var act = () => WorkflowsIntegrationAddCommand.BuildParameters(Settings(s => s.CredentialSource = source));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BuildParameters_UnknownSource_Throws()
    {
        var act = () => WorkflowsIntegrationAddCommand.BuildParameters(Settings(s => s.CredentialSource = "tenant"));
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("[1,2]")]
    [InlineData("not json")]
    public void BuildParameters_InputsNotAJsonObject_Throws(string json)
    {
        var act = () => WorkflowsIntegrationAddCommand.BuildParameters(Settings(s => s.InputsJson = json));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BuildParameters_InputWithoutKey_Throws()
    {
        var act = () => WorkflowsIntegrationAddCommand.BuildParameters(Settings(s => s.Inputs = ["=value"]));
        act.Should().Throw<ArgumentException>();
    }
}
