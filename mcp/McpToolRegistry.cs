using System.Reflection;
using System.Text.Json;
using AnythinkMcp.Tools;
using ModelContextProtocol.Server;

namespace AnythinkMcp;

/// <summary>
/// Registry for MCP tools — discovers tools from the assembly and provides
/// execution by name for the HTTP transport. In stdio mode, the MCP SDK
/// handles this automatically; in HTTP mode we need to invoke tools manually.
/// </summary>
public static class McpToolRegistry
{
    private static readonly Dictionary<string, ToolInfo> Tools = DiscoverTools();

    /// <summary>Returns tool definitions in Claude API tool_use format.</summary>
    public static List<object> GetToolDefinitions()
    {
        return Tools.Values.Select(t => new
        {
            name = t.Name,
            description = t.Description,
            input_schema = t.InputSchema
        }).Cast<object>().ToList();
    }

    /// <summary>Executes a tool by name, returning the text result.</summary>
    public static async Task<string> ExecuteToolAsync(string toolName, JsonElement arguments,
        IServiceProvider services)
    {
        if (!Tools.TryGetValue(toolName, out var tool))
            throw new ArgumentException($"Unknown tool: {toolName}");

        var factory = services.GetRequiredService<McpClientFactory>();

        // Create an instance of the tool class (all tool classes take McpClientFactory in constructor)
        var instance = Activator.CreateInstance(tool.DeclaringType, factory)!;

        // Build method arguments from the JSON
        var methodParams = tool.Method.GetParameters();
        var invokeArgs = new object?[methodParams.Length];

        for (var i = 0; i < methodParams.Length; i++)
        {
            var param = methodParams[i];
            if (arguments.TryGetProperty(ToCamelCase(param.Name!), out var value) ||
                arguments.TryGetProperty(param.Name!, out value))
            {
                invokeArgs[i] = ConvertJsonElement(value, param.ParameterType);
            }
            else if (param.HasDefaultValue)
            {
                invokeArgs[i] = param.DefaultValue;
            }
            else
            {
                invokeArgs[i] = param.ParameterType.IsValueType
                    ? Activator.CreateInstance(param.ParameterType)
                    : null;
            }
        }

        // Invoke and await
        var result = tool.Method.Invoke(instance, invokeArgs);
        if (result is Task<string> taskString)
            return await taskString;
        if (result is Task task)
        {
            await task;
            return "OK";
        }
        return result?.ToString() ?? "";
    }

    private static Dictionary<string, ToolInfo> DiscoverTools()
    {
        var tools = new Dictionary<string, ToolInfo>();

        // Find all types with [McpServerToolType] attribute
        var toolTypes = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() != null);

        foreach (var type in toolTypes)
        {
            // Find methods with [McpServerTool] attribute
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                var toolAttr = method.GetCustomAttribute<McpServerToolAttribute>();
                if (toolAttr == null) continue;

                var descAttr = method.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>();

                var name = toolAttr.Name ?? method.Name;
                var description = descAttr?.Description ?? "";

                // Build input schema from method parameters
                var properties = new Dictionary<string, object>();
                var required = new List<string>();

                foreach (var param in method.GetParameters())
                {
                    var paramDesc = param.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>();
                    var paramName = ToCamelCase(param.Name!);

                    properties[paramName] = new
                    {
                        type = GetJsonType(param.ParameterType),
                        description = paramDesc?.Description ?? param.Name
                    };

                    if (!param.HasDefaultValue && !IsNullable(param.ParameterType))
                        required.Add(paramName);
                }

                tools[name] = new ToolInfo
                {
                    Name = name,
                    Description = description,
                    DeclaringType = type,
                    Method = method,
                    InputSchema = new
                    {
                        type = "object",
                        properties,
                        required = required.ToArray()
                    }
                };
            }
        }

        return tools;
    }

    private static string GetJsonType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(string)) return "string";
        if (type == typeof(int) || type == typeof(long) || type == typeof(double) || type == typeof(float)) return "number";
        if (type == typeof(bool)) return "boolean";
        return "string";
    }

    private static bool IsNullable(Type type) =>
        !type.IsValueType || Nullable.GetUnderlyingType(type) != null;

    private static string ToCamelCase(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];

    private static object? ConvertJsonElement(JsonElement element, Type targetType)
    {
        targetType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (element.ValueKind == JsonValueKind.Null) return null;
        if (targetType == typeof(string)) return element.GetString();
        if (targetType == typeof(int)) return element.GetInt32();
        if (targetType == typeof(long)) return element.GetInt64();
        if (targetType == typeof(bool)) return element.GetBoolean();
        if (targetType == typeof(double)) return element.GetDouble();
        if (targetType == typeof(Guid)) return Guid.Parse(element.GetString()!);

        return element.GetString();
    }
}

internal class ToolInfo
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public Type DeclaringType { get; init; } = null!;
    public MethodInfo Method { get; init; } = null!;
    public object InputSchema { get; init; } = null!;
}
