using AnythinkMcp.Tools;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.AspNetCore.Authentication;

namespace AnythinkMcp;

/// <summary>Builds the OAuth-protected MCP endpoint used by Claude and other hosted connectors.</summary>
public static class HostedMode
{
    public sealed class Options
    {
        /// <summary>The resource identifier Claude connects to, e.g. https://mcp.anythink.dev/mcp.</summary>
        public required string PublicUrl { get; init; }

        /// <summary>The Anythink OAuth authorisation server's issuer URL.</summary>
        public required string Issuer { get; init; }

        /// <summary>The audience tokens must carry. Defaults to <see cref="PublicUrl"/>.</summary>
        public required string Audience { get; init; }
    }

    public static WebApplication BuildPublicApp(
        WebApplicationBuilder builder,
        Options options,
        McpClientFactory factory,
        Action<JwtBearerOptions>? configureJwtBearer = null)
    {
        var resourcePath = new Uri(options.PublicUrl).AbsolutePath.TrimEnd('/');
        if (resourcePath.Length == 0) resourcePath = "/";

        builder.Logging.AddConsole();
        builder.Services.AddSingleton(factory);
        builder.Services.AddScoped<HostedCredentials>();

        builder.Services
            .AddAuthentication(auth =>
            {
                auth.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
                auth.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(jwt =>
            {
                // Otherwise ASP.NET Core renames "tid" to the tenantid XML claim URI, breaking FindFirstValue("tid").
                jwt.MapInboundClaims = false;
                jwt.Authority = options.Issuer;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromSeconds(60),
                };
                jwt.Events = new JwtBearerEvents { OnTokenValidated = HostedAuth.OnTokenValidated };
                configureJwtBearer?.Invoke(jwt);
            })
            .AddMcp(mcp =>
            {
                mcp.ResourceMetadata = new()
                {
                    Resource = options.PublicUrl,
                    AuthorizationServers = { options.Issuer },
                    ScopesSupported = ["offline_access"],
                    ResourceName = "Anythink",
                    ResourceDocumentation = "https://anythink.cloud",
                };
            });

        builder.Services.AddAuthorization();

        builder.Services
            .AddMcpServer(server => server.ServerInfo = new() { Name = "anythink", Version = "1.0.0" })
            .WithTools<HostedTools>()
            .WithHttpTransport(http => http.SessionMode = HttpServerSessionMode.Stateless);

        var app = builder.Build();

        app.UseAuthentication();
        app.UseAuthorization();

        // Runs after authorization, so claims are already validated — just copies them into HostedCredentials.
        app.Use(async (context, next) =>
        {
            HostedAuth.PopulateCredentials(context);
            await next();
        });

        app.MapGet("/health", () => new { status = "healthy", timestamp = DateTime.UtcNow });
        app.MapMcp(resourcePath).RequireAuthorization();

        return app;
    }
}
