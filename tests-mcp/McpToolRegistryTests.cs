using FluentAssertions;

namespace AnythinkMcp.Tests;

/// <summary>
/// Tests for tool discovery and definition generation.
/// </summary>
public class McpToolRegistryTests
{
    [Fact]
    public void GetToolDefinitions_ShouldReturnNonEmptyList()
    {
        var tools = McpToolRegistry.GetToolDefinitions();

        tools.Should().NotBeEmpty("MCP server should expose at least one tool");
    }

    [Fact]
    public void GetToolDefinitions_ShouldExposeCliCommandsAsTools()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(McpToolRegistry.GetToolDefinitions());

        json.Should().Contain("\"entities_list\"").And.Contain("\"data_list\"").And.Contain("\"workflows_create\"");
    }

    [Fact]
    public void GetToolDefinitions_ShouldLeaveOutLocalOnlyTools()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(McpToolRegistry.GetToolDefinitions());

        json.Should().NotContain("\"cli\"").And.NotContain("\"login\"").And.NotContain("\"projects_list\"")
            .And.NotContain("\"files_upload\"");
    }

    [Fact]
    public void GetToolDefinitions_ShouldHaveInputSchemas()
    {
        var tools = McpToolRegistry.GetToolDefinitions();
        var json = System.Text.Json.JsonSerializer.Serialize(tools);

        json.Should().Contain("input_schema", "every tool should have an input schema");
        json.Should().Contain("\"type\":\"object\"", "schemas should be object type");
    }

    [Fact]
    public void GetToolDefinitions_ShouldHaveDescriptions()
    {
        var tools = McpToolRegistry.GetToolDefinitions();
        var json = System.Text.Json.JsonSerializer.Serialize(tools);

        json.Should().Contain("\"description\"", "every tool should have a description");
    }
}
