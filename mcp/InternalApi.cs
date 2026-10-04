using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AnythinkMcp;

internal sealed class InternalApiOptions
{
    public const string TokenHeader = "X-Internal-Token";

    public required string Bind { get; init; }
    public string? Token { get; init; }
    public IReadOnlyList<string> InstanceHostSuffixes { get; init; } = HostedMode.DefaultInstanceHostSuffixes;
    public bool AllowLoopbackInstance { get; init; }
}

internal static class InternalApi
{
    public static InternalApiOptions FromEnvironment(Func<string, string?> get)
    {
        var bind = HostedConfig.Optional(get, "MCP_INTERNAL_BIND") ?? "127.0.0.1";
        var options = Build(get, bind);

        if (!IsLoopbackBind(bind) && options.Token is null)
            throw new InvalidOperationException(
                "MCP_INTERNAL_TOKEN must be set when MCP_INTERNAL_BIND is not a loopback address.");

        return options;
    }

    public static bool IsLoopbackBind(string bind) =>
        bind.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(bind, out var ip) && IPAddress.IsLoopback(ip));

    public static IResult? CheckToken(HttpContext context, InternalApiOptions options)
    {
        if (options.Token is null) return null;

        var supplied = context.Request.Headers[InternalApiOptions.TokenHeader].ToString();
        var matches = CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied)),
            SHA256.HashData(Encoding.UTF8.GetBytes(options.Token)));

        return matches ? null : Results.Json(new { error = "Invalid or missing internal token" }, statusCode: 401);
    }

    public static IResult? CheckInstanceUrl(string instanceUrl, InternalApiOptions options) =>
        HostedAuth.IsAllowedInstanceUrl(instanceUrl, options.AllowLoopbackInstance, options.InstanceHostSuffixes)
            ? null
            : Results.Json(new { error = "X-Instance-Url is not an allowed project API host" }, statusCode: 400);

    private static InternalApiOptions Build(Func<string, string?> get, string bind) => new()
    {
        Bind = bind,
        Token = HostedConfig.Optional(get, "MCP_INTERNAL_TOKEN"),
        InstanceHostSuffixes = HostedConfig.InstanceHostSuffixes(get),
        AllowLoopbackInstance = HostedConfig.LoopbackOptIn(get),
    };

    public static void MapEndpoints(WebApplication app, InternalApiOptions options)
    {
        var logger = app.Logger;

        app.MapGet("/health", () => new { status = "healthy", timestamp = DateTime.UtcNow });

        app.MapGet("/tools", (HttpContext context) =>
        {
            if (CheckToken(context, options) is { } denied) return denied;
            var tools = McpToolRegistry.GetToolDefinitions();
            return Results.Json(tools);
        });

        app.MapPost("/tools/call", async (HttpContext context) =>
        {
            if (CheckToken(context, options) is { } denied) return denied;
            if (!ExtractAuth(context, out var token, out var orgId, out var instanceUrl, out var error))
                return error!;
            if (CheckInstanceUrl(instanceUrl!, options) is { } badInstance) return badInstance;

            JsonElement body;
            try
            {
                body = await JsonSerializer.DeserializeAsync<JsonElement>(context.Request.Body);
            }
            catch (JsonException)
            {
                return Results.Json(new { error = new { message = "Invalid JSON" } }, statusCode: 400);
            }

            if (!body.TryGetProperty("name", out var nameEl) || nameEl.GetString() is not { } toolName)
                return Results.Json(new { error = new { message = "'name' field required" } }, statusCode: 400);

            if (!McpToolRegistry.Contains(toolName))
                return Results.Json(new { error = new { message = $"Unknown tool: {toolName}" } }, statusCode: 404);

            var arguments = body.TryGetProperty("arguments", out var args)
                ? args
                : JsonSerializer.Deserialize<JsonElement>("{}");

            McpClientFactory.SetRequestCredentials(orgId!, instanceUrl!, token!);
            try
            {
                logger.LogInformation("Tool call: {ToolName} args={ArgNames}", toolName,
                    arguments.ValueKind == JsonValueKind.Object ? string.Join(",", arguments.EnumerateObject().Select(a => a.Name)) : "");
                var result = await McpToolRegistry.ExecuteToolAsync(toolName, arguments,
                    context.RequestServices);
                logger.LogInformation("Tool result: {ToolName} ({Length} chars)", toolName, result.Length);

                return Results.Json(new
                {
                    result = new { content = new[] { new { type = "text", text = result } } }
                });
            }
            catch (Exception ex)
            {
                logger.LogError("Tool '{ToolName}' execution failed for org {OrgId}: {ErrorType}", toolName, orgId, ex.GetType().Name);
                return Results.Json(new
                {
                    error = new { message = "Tool execution failed. Check server logs for details." }
                }, statusCode: 500);
            }
            finally
            {
                McpClientFactory.ClearRequestCredentials();
            }
        });
    }

    private static bool ExtractAuth(HttpContext context, out string? token, out string? orgId,
        out string? instanceUrl, out IResult? error)
    {
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        orgId = context.Request.Headers["X-Org-Id"].FirstOrDefault();
        instanceUrl = context.Request.Headers["X-Instance-Url"].FirstOrDefault();

        if (string.IsNullOrEmpty(authHeader))
        {
            token = null;
            error = Results.Json(new { error = "Authorization header required" }, statusCode: 401);
            return false;
        }

        token = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authHeader[7..]
            : authHeader;

        if (string.IsNullOrEmpty(orgId) || orgId.Length > 12 || !orgId.All(char.IsAsciiDigit))
        {
            error = Results.Json(new { error = "X-Org-Id header must be a numeric org id" }, statusCode: 400);
            return false;
        }
        if (string.IsNullOrEmpty(instanceUrl))
        {
            error = Results.Json(new { error = "X-Instance-Url header required" }, statusCode: 400);
            return false;
        }

        error = null;
        return true;
    }
}
