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
    public void GetToolDefinitions_ShouldIncludeCliTool()
    {
        var tools = McpToolRegistry.GetToolDefinitions();
        var json = System.Text.Json.JsonSerializer.Serialize(tools);

        json.Should().Contain("\"cli\"", "the catch-all CLI tool should be registered");
    }

    [Fact]
    public void GetToolDefinitions_ShouldIncludeCoreTools()
    {
        var tools = McpToolRegistry.GetToolDefinitions();
        var json = System.Text.Json.JsonSerializer.Serialize(tools);

        json.Should().Contain("\"cli\"", "catch-all CLI tool should be registered");
        json.Should().Contain("\"projects_list\"", "project management tools should be registered");
        json.Should().Contain("\"login\"", "auth tools should be registered");
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
