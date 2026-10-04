using AnythinkMcp.Cli;
using System.Text.Json;
using AnythinkMcp;

var profile = ResolveFlag(args, "--profile", "-p");
var hostedMode = args.Contains("--hosted");
var httpMode = args.Contains("--http");
var internalMode = args.Contains("--internal");
var port = ResolveIntFlag(args, "--port", "MCP_PORT", 5300);
var internalPort = ResolveIntFlag(args, "--internal-port", "MCP_INTERNAL_PORT", 5301);

if (hostedMode)
    await RunHostedServer(profile, port, internalPort);
else if (httpMode)
    await RunLocalHttpServer(profile, port);
else if (internalMode)
    await RunInternalServer(profile, port);
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
            .WithToolsFromAssembly()
            .WithTools(CliCommandTool.All(CliToolScope.Local));
    });

    await builder.Build().RunAsync();
}

static async Task RunHostedServer(string? profile, int port, int internalPort)
{
    var options = HostedConfig.FromEnvironment(Environment.GetEnvironmentVariable);
    var internalOptions = InternalApi.FromEnvironment(Environment.GetEnvironmentVariable);

    var publicApp = HostedMode.BuildPublicApp(
        WebApplication.CreateBuilder(), options, new McpClientFactory());

    var internalApp = BuildRestApp(profile, ResolveCorsOrigins(), internalOptions);

    publicApp.Logger.LogInformation(
        "Hosted MCP server on port {Port}, resource {Resource}, issuer {Issuer}", port, options.PublicUrl, options.Issuer);
    publicApp.Logger.LogInformation(
        "Internal REST server on {Bind}:{InternalPort}", internalOptions.Bind, internalPort);

    publicApp.Urls.Add($"http://0.0.0.0:{port}");
    internalApp.Urls.Add($"http://{internalOptions.Bind}:{internalPort}");
    await publicApp.StartAsync();
    await internalApp.StartAsync();
    await publicApp.WaitForShutdownAsync();
    await internalApp.StopAsync();
}

static async Task RunLocalHttpServer(string? profile, int port)
{
    var app = LocalHttpMode.BuildApp(WebApplication.CreateBuilder(), new McpClientFactory(profile));
    app.Urls.Add($"http://localhost:{port}");
    app.Logger.LogInformation("Anythink MCP server on http://localhost:{Port}/mcp", port);
    await app.RunAsync();
}

static async Task RunInternalServer(string? profile, int port)
{
    var internalOptions = InternalApi.FromEnvironment(Environment.GetEnvironmentVariable);
    var app = BuildRestApp(profile, ResolveCorsOrigins(), internalOptions);
    await app.RunAsync($"http://{internalOptions.Bind}:{port}");
}

static WebApplication BuildRestApp(string? profile, string[] corsOrigins, InternalApiOptions internalOptions)
{
    var builder = WebApplication.CreateBuilder();
    builder.Logging.AddConsole();

    builder.WebHost.ConfigureKestrel(opts => opts.Limits.MaxRequestBodySize = HostedMode.MaxRequestBodyBytes);

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

    app.Logger.LogInformation("MCP internal REST server starting with CORS origins: {Origins}",
        string.Join(", ", corsOrigins));

    InternalApi.MapEndpoints(app, internalOptions);

    return app;
}

static string[] ResolveCorsOrigins() =>
    Environment.GetEnvironmentVariable("MCP_CORS_ORIGINS")?.Split(',') ?? ["http://localhost:5200"];

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
