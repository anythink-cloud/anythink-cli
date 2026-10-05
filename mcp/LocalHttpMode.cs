using AnythinkMcp.Cli;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;

namespace AnythinkMcp;

public static class LocalHttpMode
{
    public static readonly string[] LoopbackHosts = ["localhost", "127.0.0.1", "[::1]"];

    public static WebApplication BuildApp(WebApplicationBuilder builder, McpClientFactory factory)
    {
        builder.Logging.AddConsole();
        builder.Services.AddSingleton(factory);
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = HostedMode.MaxRequestBodyBytes);
        builder.Services
            .AddMcpServer(server => server.ServerInfo = new() { Name = "anythink", Version = "1.0.0" })
            .WithToolsFromAssembly()
            .WithTools(CliCommandTool.All(CliToolScope.Local))
            .WithHttpTransport(http => http.SessionMode = HttpServerSessionMode.Stateless);

        var app = builder.Build();
        app.Use((context, next) => HostedAuth.ValidateHostAndOrigin(context, next, LoopbackHosts, []));
        app.MapGet("/health", () => new { status = "healthy", timestamp = DateTime.UtcNow });
        app.MapMcp("/mcp");
        return app;
    }
}
