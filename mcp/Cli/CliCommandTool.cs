using System.Text.Json;
using System.Text.Json.Nodes;
using AnythinkCli.Client;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AnythinkMcp.Cli;

public sealed class CliCommandTool : McpServerTool
{
    private readonly CliCommand _command;
    private readonly CliToolScope _scope;
    private readonly IReadOnlyList<CliParameter> _parameters;

    public CliCommandTool(CliCommand command, CliToolScope scope)
    {
        _command = command;
        _scope = scope;
        _parameters = CliToolPolicy.Parameters(command, scope);

        var title = Title(command);
        var readOnly = CliToolPolicy.IsReadOnly(command, scope);
        ProtocolTool = new Tool
        {
            Name = command.ToolName,
            Title = title,
            Description = command.Description,
            InputSchema = Schema(_parameters),
            Annotations = new ToolAnnotations
            {
                Title = title,
                ReadOnlyHint = readOnly,
                DestructiveHint = readOnly ? null : CliToolPolicy.IsDestructive(command, scope),
                OpenWorldHint = CliToolPolicy.IsOpenWorld(command)
            }
        };
    }

    public static IReadOnlyList<CliCommandTool> All(CliToolScope scope) =>
        CliCommandModel.All
            .Where(command => CliToolPolicy.Includes(command, scope))
            .Select(command => new CliCommandTool(command, scope))
            .ToList();

    public override Tool ProtocolTool { get; }

    public override IReadOnlyList<object> Metadata => [];

    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        CliRunResult result;
        try
        {
            result = await RunAsync(request.Params?.Arguments, ResolveClient(request.Services!), cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            result = new CliRunResult(1, $"Error: {ex.Message}");
        }

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = result.Output.Length > 0 ? result.Output : "(no output)" }],
            IsError = result.ExitCode != 0
        };
    }

    public async Task<CliRunResult> RunAsync(
        IEnumerable<KeyValuePair<string, JsonElement>>? arguments, AnythinkClient? client, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> args;
        try
        {
            args = BuildArgs(arguments);
        }
        catch (ArgumentException ex)
        {
            return new CliRunResult(1, $"Error: {ex.Message}");
        }

        return await CliRunner.RunAsync(args, client, _scope, cancellationToken);
    }

    internal IReadOnlyList<string> BuildArgs(IEnumerable<KeyValuePair<string, JsonElement>>? arguments)
    {
        var values = (arguments ?? [])
            .Where(argument => argument.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            .ToDictionary(argument => argument.Key, argument => argument.Value);

        var unknown = values.Keys.Except(_parameters.Select(p => p.Name)).ToList();
        if (unknown.Count > 0)
            throw new ArgumentException($"Unknown parameter: {string.Join(", ", unknown)}.");

        var args = new List<string>(_command.Path);
        var skipped = false;
        foreach (var parameter in _parameters.Where(p => p.Kind == CliParameterKind.Argument).OrderBy(p => p.Position))
        {
            if (!values.TryGetValue(parameter.Name, out var value))
            {
                if (parameter.Required)
                    throw new ArgumentException($"'{parameter.Name}' is required.");
                skipped = true;
                continue;
            }

            if (skipped)
                throw new ArgumentException($"'{parameter.Name}' needs the arguments before it.");

            var text = Scalar(value);
            if (text.StartsWith('-'))
                throw new ArgumentException($"'{parameter.Name}' can't start with '-'.");
            if (CliToolPolicy.IsRemote(_scope) && !CliToolPolicy.IsFreeText(_command, parameter) && !CliToolPolicy.IsPlainPathValue(text))
                throw new ArgumentException($"'{parameter.Name}' can only contain letters, digits, '_', '-' and '.'.");
            args.Add(text);
        }

        foreach (var parameter in _parameters.Where(p => p.Kind != CliParameterKind.Argument))
        {
            if (!values.TryGetValue(parameter.Name, out var value))
                continue;

            switch (parameter.Kind)
            {
                case CliParameterKind.Flag:
                    if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        throw new ArgumentException($"'{parameter.Name}' must be true or false.");
                    if (parameter.ExplicitBoolean)
                        args.Add($"{parameter.Token}={Scalar(value)}");
                    else if (value.ValueKind == JsonValueKind.True)
                        args.Add(parameter.Token);
                    break;
                case CliParameterKind.Vector:
                    var items = value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToList() : [value];
                    args.AddRange(items.Select(item => $"{parameter.Token}={Scalar(item)}"));
                    break;
                default:
                    args.Add($"{parameter.Token}={Scalar(value)}");
                    break;
            }
        }

        args.AddRange(CliToolPolicy.AutomaticArgs(_command));
        return args;
    }

    private AnythinkClient? ResolveClient(IServiceProvider services)
    {
        var factory = services.GetRequiredService<McpClientFactory>();
        if (services.GetService<HostedCredentials>() is { } credentials)
            return factory.GetClient(credentials);

        return CliToolPolicy.IsRemote(_scope)
            ? throw new InvalidOperationException("The request carries no credentials.")
            : factory.GetClientOrNull();
    }

    private static string Scalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()!,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => value.GetRawText()
    };

    private static string Title(CliCommand command)
    {
        var words = command.Path.Select(segment => segment.Replace('-', ' ')).ToList();
        var subject = char.ToUpperInvariant(words[0][0]) + words[0][1..];
        return words.Count == 1 ? subject : $"{subject}: {string.Join(' ', words.Skip(1))}";
    }

    private static JsonElement Schema(IReadOnlyList<CliParameter> parameters)
    {
        var properties = new JsonObject();
        foreach (var parameter in parameters)
        {
            var property = parameter.Kind == CliParameterKind.Vector
                ? new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = JsonType(parameter.ClrType) } }
                : new JsonObject { ["type"] = parameter.Kind == CliParameterKind.Flag ? "boolean" : JsonType(parameter.ClrType) };
            if (parameter.Description.Length > 0)
                property["description"] = parameter.Description;
            properties[parameter.Name] = property;
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["additionalProperties"] = false
        };

        var required = parameters.Where(p => p.Required).Select(p => (JsonNode)p.Name).ToArray();
        if (required.Length > 0)
            schema["required"] = new JsonArray(required);

        return JsonSerializer.SerializeToElement(schema);
    }

    private static string JsonType(string clrType) => clrType switch
    {
        _ when clrType.Contains("Int32") || clrType.Contains("Int64") => "integer",
        _ when clrType.Contains("Decimal") || clrType.Contains("Double") || clrType.Contains("Single") => "number",
        _ when clrType.Contains("Boolean") => "boolean",
        _ => "string"
    };
}
