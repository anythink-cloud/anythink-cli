using System.Text.Json;
using AnythinkMcp.Cli;

namespace AnythinkMcp;

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
            input_schema = tool.ProtocolTool.InputSchema
        }).ToList();

    public static async Task<string> ExecuteToolAsync(string toolName, JsonElement arguments, IServiceProvider services)
    {
        if (!Tools.Value.TryGetValue(toolName, out var tool))
            throw new ArgumentException($"Unknown tool: {toolName}");

        var client = services.GetRequiredService<McpClientFactory>().GetClient();
        var properties = arguments.ValueKind == JsonValueKind.Object
            ? arguments.EnumerateObject().Select(p => KeyValuePair.Create(p.Name, p.Value))
            : null;

        return (await tool.RunAsync(properties, client)).Output;
    }
}
