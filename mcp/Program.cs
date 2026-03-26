using AnythinkMcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

// ── Anythink MCP Server ──────────────────────────────────────────────────────
//
// A thin MCP wrapper around the Anythink CLI client library.
// Runs as a local stdio process — Claude launches it and talks over stdin/stdout.
//
// Uses the same profiles, auth, and AnythinkClient as the CLI:
//   anythink-mcp                       → uses the active profile
//   anythink-mcp --profile my-project  → uses a named profile
//
// Tools are organised by domain (entities, fields, data, workflows, roles,
// secrets) and delegate to AnythinkClient methods — no business logic here.

var profile = ResolveProfile(args);

var builder = new HostBuilder();

builder.ConfigureLogging(logging => logging.ClearProviders());

builder.ConfigureServices(services =>
{
    services.AddSingleton(new McpClientFactory(profile));

    services
        .AddMcpServer(server =>
        {
            server.ServerInfo = new()
            {
                Name = "anythink",
                Version = "1.0.0"
            };
        })
        .WithStdioServerTransport()
        .WithToolsFromAssembly();
});

await builder.Build().RunAsync();

// ── Helpers ──────────────────────────────────────────────────────────────────

static string? ResolveProfile(string[] args)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i] is "--profile" or "-p")
            return args[i + 1];
    }
    return null;
}
