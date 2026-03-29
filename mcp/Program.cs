using System.Text.Json;
using AnythinkMcp;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

// ── Anythink MCP Server ──────────────────────────────────────────────────────
//
// A thin MCP wrapper around the Anythink CLI client library.
// Supports two transport modes:
//
//   stdio (default):  Claude Code launches it and talks over stdin/stdout.
//     anythink-mcp
//     anythink-mcp --profile my-project
//
//   http:  Runs as an HTTP server for the AI sidebar and multi-tenant services.
//     anythink-mcp --http
//     anythink-mcp --http --port 5300
//     Requires Authorization, X-Org-Id, and X-Instance-Url headers on every request.
//     Set MCP_CORS_ORIGINS env var to configure allowed origins (comma-separated).

var profile = ResolveFlag(args, "--profile", "-p");
var httpMode = args.Contains("--http");
var port = int.TryParse(ResolveFlag(args, "--port"), out var p) ? p : 5300;

if (httpMode)
    await RunHttpServer(profile, port);
else
    await RunStdioServer(profile);

// ── Stdio mode (for Claude Code) ─────────────────────────────────────────────

static async Task RunStdioServer(string? profile)
{
    var builder = new HostBuilder();
    builder.ConfigureLogging(logging => logging.ClearProviders());
    builder.ConfigureServices(services =>
    {
        services.AddSingleton(new McpClientFactory(profile));
        services
            .AddMcpServer(server =>
            {
                server.ServerInfo = new() { Name = "anythink", Version = "1.0.0" };
            })
            .WithStdioServerTransport()
            .WithToolsFromAssembly();
    });

    await builder.Build().RunAsync();
}

// ── HTTP mode (for AI sidebar / multi-tenant) ────────────────────────────────

static async Task RunHttpServer(string? profile, int port)
{
    var builder = WebApplication.CreateBuilder();
    builder.Logging.AddConsole();

    // Limit request body size to prevent DoS
    builder.WebHost.ConfigureKestrel(opts => opts.Limits.MaxRequestBodySize = 1_048_576); // 1 MB

    builder.Services.AddSingleton(new McpClientFactory(profile));
    builder.Services
        .AddMcpServer(server =>
        {
            server.ServerInfo = new() { Name = "anythink", Version = "1.0.0" };
        })
        .WithToolsFromAssembly();

    var corsOrigins = Environment.GetEnvironmentVariable("MCP_CORS_ORIGINS")?.Split(',')
        ?? ["http://localhost:5200"];
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
            policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod());
    });

    var app = builder.Build();
    app.UseCors();

    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("MCP HTTP server starting on port {Port} with CORS origins: {Origins}",
        port, string.Join(", ", corsOrigins));

    // Health check (unauthenticated — standard for K8s probes)
    app.MapGet("/health", () => new { status = "healthy", timestamp = DateTime.UtcNow });

    // List available tools — no auth required (tool definitions are not sensitive,
    // and the sidebar needs to cache them without tenant context)
    app.MapGet("/tools", () =>
    {
        var tools = McpToolRegistry.GetToolDefinitions();
        return Results.Json(tools);
    });

    // Execute a tool (requires auth + tenant context)
    app.MapPost("/tools/call", async (HttpContext context) =>
    {
        if (!ExtractAuth(context, out var token, out var orgId, out var instanceUrl, out var error))
            return error!;

        // Parse request body
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

        var arguments = body.TryGetProperty("arguments", out var args)
            ? args
            : JsonSerializer.Deserialize<JsonElement>("{}");

        // Block config-mutating tools in HTTP mode
        if (toolName is "login" or "login_direct" or "signup" or "logout"
            or "config_use" or "config_remove" or "config_show"
            or "accounts_use")
        {
            return Results.Json(new
            {
                error = new { message = $"Tool '{toolName}' is not available in HTTP mode." }
            }, statusCode: 403);
        }

        // Set per-request credentials and execute
        McpClientFactory.SetRequestCredentials(orgId!, instanceUrl!, token!);
        try
        {
            logger.LogInformation("Tool call: {ToolName} args={Args}", toolName, arguments.ToString());
            var result = await McpToolRegistry.ExecuteToolAsync(toolName, arguments,
                context.RequestServices);
            logger.LogInformation("Tool result: {ToolName} => {Result}", toolName, result.Length > 500 ? result[..500] + "..." : result);

            return Results.Json(new
            {
                result = new { content = new[] { new { type = "text", text = result } } }
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tool '{ToolName}' execution failed for org {OrgId}", toolName, orgId);
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

    app.Run($"http://0.0.0.0:{port}");
}

// ── Auth extraction helper ───────────────────────────────────────────────────

static bool ExtractAuth(HttpContext context, out string? token, out string? orgId,
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

    // Proper Bearer token extraction
    token = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        ? authHeader[7..]
        : authHeader;

    if (string.IsNullOrEmpty(orgId))
    {
        error = Results.Json(new { error = "X-Org-Id header required" }, statusCode: 400);
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

// ── Helpers ──────────────────────────────────────────────────────────────────

static string? ResolveFlag(string[] args, string flag, string? shortFlag = null)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == flag || (shortFlag != null && args[i] == shortFlag))
            return args[i + 1];
    }
    return null;
}
