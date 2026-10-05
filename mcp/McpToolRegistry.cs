using System.Text.Json;
using AnythinkMcp.Cli;

namespace AnythinkMcp;

public sealed record ToolCallResult(string Output, bool IsError);

public static class McpToolRegistry
{
    private static readonly Lazy<Dictionary<string, CliCommandTool>> Tools =
        new(() => CliCommandTool.All(CliToolScope.Remote).ToDictionary(tool => tool.ProtocolTool.Name));

    public static bool Contains(string toolName) => Tools.Value.ContainsKey(toolName);

    public static List<object> GetToolDefinitions() =>
        Tools.Value.Values.Select(tool => (object)new
        {
            name = tool.ProtocolTool.Name,
            description = tool.ProtocolTool.Description,
            input_schema = tool.ProtocolTool.InputSchema,
            annotations = new
            {
                title = tool.ProtocolTool.Annotations?.Title,
                read_only_hint = tool.ProtocolTool.Annotations?.ReadOnlyHint ?? false,
                destructive_hint = tool.ProtocolTool.Annotations?.DestructiveHint ?? false,
                open_world_hint = tool.ProtocolTool.Annotations?.OpenWorldHint ?? false
            }
        }).ToList();

    public static async Task<ToolCallResult> ExecuteToolAsync(
        string toolName, JsonElement arguments, IServiceProvider services, CancellationToken cancellationToken = default)
    {
        if (!Tools.Value.TryGetValue(toolName, out var tool))
            throw new ArgumentException($"Unknown tool: {toolName}");

        var client = services.GetRequiredService<McpClientFactory>().GetRequestClient();
        var properties = arguments.ValueKind == JsonValueKind.Object
            ? arguments.EnumerateObject().Select(p => KeyValuePair.Create(p.Name, p.Value))
            : null;

        var result = await tool.RunAsync(properties, client, cancellationToken);
        return new ToolCallResult(result.Output, result.ExitCode != 0);
    }
}
