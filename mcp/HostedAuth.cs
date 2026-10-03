using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace AnythinkMcp;

public sealed class HostedCredentials
{
    public string? OrgId { get; set; }
    public string? InstanceUrl { get; set; }
    public string? Token { get; set; }
}

internal static class HostedAuth
{
    public static Task OnTokenValidated(TokenValidatedContext context)
    {
        var principal = context.Principal;
        var tid = principal?.FindFirstValue("tid");
        var instanceUrl = principal?.FindFirstValue("instance_url");

        if (string.IsNullOrEmpty(tid) || !tid.All(char.IsAsciiDigit) || string.IsNullOrEmpty(instanceUrl))
        {
            context.Fail("Token is missing a valid 'tid' or 'instance_url' claim.");
            return Task.CompletedTask;
        }

        var services = context.HttpContext.RequestServices;
        var options = services.GetRequiredService<HostedMode.Options>();

        if (!IsAllowedInstanceUrl(instanceUrl, options.AllowLoopbackInstance, options.AllowedInstanceHostSuffixes))
            context.Fail("Token 'instance_url' claim is not an allowed project API host.");

        return Task.CompletedTask;
    }

    public static async Task<bool> TryPopulateCredentialsAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true) return true;

        var inbound = await context.GetTokenAsync(JwtBearerDefaults.AuthenticationScheme, "access_token");
        if (string.IsNullOrEmpty(inbound))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return false;
        }

        string exchanged;
        try
        {
            exchanged = await context.RequestServices.GetRequiredService<ITokenExchanger>()
                .ExchangeAsync(inbound, context.RequestAborted);
        }
        catch (TokenExchangeException ex)
        {
            if (ex.Rejected)
                await context.ChallengeAsync();
            else
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
            await context.Response.WriteAsJsonAsync(new { error = "Token exchange failed." });
            return false;
        }

        var creds = context.RequestServices.GetRequiredService<HostedCredentials>();
        creds.OrgId = context.User.FindFirstValue("tid");
        creds.InstanceUrl = context.User.FindFirstValue("instance_url")?.TrimEnd('/');
        creds.Token = exchanged;
        return true;
    }

    public static async Task ValidateHostAndOrigin(
        HttpContext context, RequestDelegate next, IReadOnlyList<string> allowedHosts, IReadOnlyList<string> allowedOrigins)
    {
        if (context.Request.Path == "/health")
        {
            await next(context);
            return;
        }

        var hostAllowed = allowedHosts.Contains(context.Request.Host.Host, StringComparer.OrdinalIgnoreCase);
        var origin = context.Request.Headers.Origin.ToString();
        var originAllowed = origin.Length == 0 || allowedOrigins.Contains(origin.TrimEnd('/'), StringComparer.OrdinalIgnoreCase);

        if (!hostAllowed || !originAllowed)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await next(context);
    }

    internal static bool IsAllowedInstanceUrl(string url, bool allowLoopback, IReadOnlyList<string> allowedHostSuffixes)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.UserInfo.Length > 0 || url.Contains('@')) return false;
        if (allowLoopback && uri.IsLoopback && uri.Scheme is "http" or "https") return true;

        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort) return false;
        if (uri.Query.Length > 0 || uri.Fragment.Length > 0 || url.Contains('?') || url.Contains('#')) return false;
        if (uri.AbsolutePath != "/") return false;

        var host = uri.IdnHost.ToLowerInvariant();
        if (!host.StartsWith("api.", StringComparison.Ordinal)) return false;
        return allowedHostSuffixes.Any(suffix =>
        {
            var d = suffix.TrimStart('.').ToLowerInvariant();
            return host == d || host.EndsWith("." + d);
        });
    }
}
