using System.Text.Json;
using AnythinkMcp;

// ── Anythink MCP Server ──────────────────────────────────────────────────────
//
// Supports three transports:
//
//   stdio (default):  Claude Code launches it and talks over stdin/stdout.
//     anythink-mcp
//     anythink-mcp --profile my-project
//
//   http:  Runs the internal REST API used by the AI sidebar. Callers pass
//     Authorization, X-Org-Id and X-Instance-Url headers on every request.
//     anythink-mcp --http
//     anythink-mcp --http --port 5300
//     Set MCP_CORS_ORIGINS env var to configure allowed origins (comma-separated).
//
//   hosted:  Speaks MCP over Streamable HTTP, OAuth-protected, for Claude and
//     other remote connectors. The internal REST API keeps running alongside it
//     on its own port, for the AI sidebar.
//     anythink-mcp --hosted
//     Configuration is via environment variables — see mcp/README.md.

var profile = ResolveFlag(args, "--profile", "-p");
var hostedMode = args.Contains("--hosted");
var httpMode = args.Contains("--http");
var port = ResolveIntFlag(args, "--port", "MCP_PORT", 5300);
var internalPort = ResolveIntFlag(args, "--internal-port", "MCP_INTERNAL_PORT", 5301);

if (hostedMode)
    await RunHostedServer(profile, port, internalPort);
else if (httpMode)
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

// ── Hosted mode (Claude and other remote MCP connectors) ────────────────────

static async Task RunHostedServer(string? profile, int port, int internalPort)
{
    var publicUrl = Environment.GetEnvironmentVariable("MCP_PUBLIC_URL")
        ?? throw new InvalidOperationException(
            "MCP_PUBLIC_URL must be set in --hosted mode, e.g. https://mcp.anythink.dev/mcp");
    var issuer = Environment.GetEnvironmentVariable("MCP_AUTH_ISSUER")
        ?? throw new InvalidOperationException("MCP_AUTH_ISSUER must be set in --hosted mode.");
    var audience = Environment.GetEnvironmentVariable("MCP_AUTH_AUDIENCE") ?? publicUrl;

    var options = new HostedMode.Options { PublicUrl = publicUrl, Issuer = issuer, Audience = audience };
    var publicApp = HostedMode.BuildPublicApp(
        WebApplication.CreateBuilder(), options, new McpClientFactory(profile));

    var internalApp = BuildRestApp(profile, ResolveCorsOrigins());

    var logger = publicApp.Services.GetRequiredService<ILogger<Program>>();
    logger.LogInformation(
        "Hosted MCP server on port {Port}, resource {Resource}, issuer {Issuer}", port, publicUrl, issuer);
    logger.LogInformation(
        "Internal REST server (AI sidebar) on port {InternalPort}", internalPort);

    await Task.WhenAll(
        publicApp.RunAsync($"http://0.0.0.0:{port}"),
        internalApp.RunAsync($"http://0.0.0.0:{internalPort}"));
}

// ── HTTP mode (internal REST API for the AI sidebar) ─────────────────────────

static async Task RunHttpServer(string? profile, int port)
{
    var app = BuildRestApp(profile, ResolveCorsOrigins());
    await app.RunAsync($"http://0.0.0.0:{port}");
}

static WebApplication BuildRestApp(string? profile, string[] corsOrigins)
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

    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
            policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod());
    });

    var app = builder.Build();
    app.UseCors();

    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("MCP internal REST server starting with CORS origins: {Origins}",
        string.Join(", ", corsOrigins));

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

    return app;
}

static string[] ResolveCorsOrigins() =>
    Environment.GetEnvironmentVariable("MCP_CORS_ORIGINS")?.Split(',') ?? ["http://localhost:5200"];

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

static int ResolveIntFlag(string[] args, string flag, string envVar, int defaultValue)
{
    var raw = ResolveFlag(args, flag) ?? Environment.GetEnvironmentVariable(envVar);
    return int.TryParse(raw, out var value) ? value : defaultValue;
}
