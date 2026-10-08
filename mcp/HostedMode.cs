using AnythinkMcp.Cli;
using System.Security.Claims;
using System.Threading.RateLimiting;
using AnythinkMcp.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.AspNetCore.Authentication;

namespace AnythinkMcp;

public static class HostedMode
{
    public const int MaxRequestBodyBytes = 1_048_576;
    public const int DefaultMaxConcurrentRequestsPerProject = 8;
    private const string ProjectConcurrencyPolicy = "per-project";

    public static readonly string[] DefaultInstanceHostSuffixes = [".anythink.cloud", ".anythink.dev", ".anythink.uk"];

    public sealed class Options
    {
        public required string PublicUrl { get; init; }
        public required string Issuer { get; init; }
        public string? BillingUrl { get; init; }
        public string BillingBaseUrl => BillingUrl ?? Issuer;
        public required string Audience { get; init; }
        public required TokenExchangeOptions Exchange { get; init; }
        public IReadOnlyList<string> AllowedInstanceHostSuffixes { get; init; } = DefaultInstanceHostSuffixes;
        public IReadOnlyList<string> AllowedOrigins { get; init; } = [];
        public IReadOnlyList<string> AllowedHosts { get; init; } = [];
        public bool AllowLoopbackInstance { get; init; }
        public int MaxConcurrentRequestsPerProject { get; init; } = DefaultMaxConcurrentRequestsPerProject;
    }

    public static WebApplication BuildPublicApp(
        WebApplicationBuilder builder,
        Options options,
        McpClientFactory factory,
        Action<JwtBearerOptions>? configureJwtBearer = null,
        HttpMessageHandler? exchangeHandler = null)
    {
        var resourcePath = new Uri(options.PublicUrl).AbsolutePath.TrimEnd('/');
        if (resourcePath.Length == 0) resourcePath = "/";

        builder.Logging.AddConsole();
        builder.Services.AddSingleton(factory);
        builder.Services.AddSingleton(options);
        builder.Services.AddScoped<HostedCredentials>();
        builder.Services.AddScoped<HostedProjects>();
        builder.Services.AddScoped<HostedAccounts>();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = MaxRequestBodyBytes);
        builder.Services.AddSingleton<ITokenExchanger>(sp => new HostedTokenExchanger(
            exchangeHandler is null
                ? new HttpClient(HostedTokenExchanger.CreateHandler()) { Timeout = TimeSpan.FromSeconds(10) }
                : new HttpClient(exchangeHandler),
            options.Issuer, options.Exchange, logger: sp.GetRequiredService<ILoggerFactory>().CreateLogger<HostedTokenExchanger>()));

        builder.Services
            .AddAuthentication(auth =>
            {
                auth.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
                auth.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(jwt =>
            {
                jwt.MapInboundClaims = false;
                jwt.SaveToken = true;
                jwt.Authority = options.Issuer;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidAlgorithms = ["RS256"],
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
                    ScopesSupported = ["offline_access", "account"],
                    ResourceName = "Anythink",
                    ResourceDocumentation = "https://anythink.cloud",
                };
            });

        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(ProjectConcurrencyPolicy, context => RateLimitPartition.GetConcurrencyLimiter(
                HostedAuth.RateLimitPartition(context.User),
                _ => new ConcurrencyLimiterOptions
                {
                    PermitLimit = options.MaxConcurrentRequestsPerProject,
                    QueueLimit = options.MaxConcurrentRequestsPerProject,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                }));
        });

        var hostedTools = CliCommandTool.All(CliToolScope.Hosted);
        var accountTools = hostedTools.Where(tool => tool.NeedsAccountAccess).Select(tool => tool.ProtocolTool.Name).ToHashSet();

        builder.Services
            .AddMcpServer(server => server.ServerInfo = new() { Name = "anythink", Version = "1.0.0" })
            .WithTools<HostedTools>()
            .WithTools(hostedTools)
            .WithRequestFilters(filters => filters.AddListToolsFilter(next => async (request, cancellationToken) =>
            {
                var result = await next(request, cancellationToken);
                if (request.Services?.GetService<HostedAccounts>()?.HasAccess != true)
                    result.Tools = result.Tools.Where(tool => !accountTools.Contains(tool.Name)).ToList();
                return result;
            }))
            .WithHttpTransport(http => http.SessionMode = HttpServerSessionMode.Stateless);

        var app = builder.Build();

        var publicUri = new Uri(options.PublicUrl);
        var allowedHosts = options.AllowedHosts.Append(publicUri.Host).ToArray();
        app.Use((context, next) => HostedAuth.ValidateHostAndOrigin(context, next, allowedHosts, options.AllowedOrigins));
        app.Use((context, next) =>
        {
            context.Request.Scheme = publicUri.Scheme;
            return next(context);
        });

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

        app.Use(async (context, next) =>
        {
            if (context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>() is not null
                && !await HostedAuth.TryPopulateCredentialsAsync(context))
                return;
            await next();
        });

        app.MapGet("/health", () => new { status = "healthy", timestamp = DateTime.UtcNow });
        app.MapMcp(resourcePath).RequireAuthorization().RequireRateLimiting(ProjectConcurrencyPolicy);

        return app;
    }
}
