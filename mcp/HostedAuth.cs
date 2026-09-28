using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Security.Claims;

namespace AnythinkMcp;

/// <summary>Per-request Anythink credentials from the token's own claims; scoped rather than AsyncLocal because the MCP SDK resolves each tool call's services from HttpContext.RequestServices, not the ambient ExecutionContext.</summary>
public sealed class HostedCredentials
{
    public string? OrgId { get; set; }
    public string? InstanceUrl { get; set; }
    public string? Token { get; set; }
}

/// <summary>Validates a bearer token's Anythink-specific claims once JwtBearer has checked its signature, issuer, audience and lifetime.</summary>
internal static class HostedAuth
{
    public static Task OnTokenValidated(TokenValidatedContext context)
    {
        var principal = context.Principal;
        var tid = principal?.FindFirstValue("tid");
        var instanceUrl = principal?.FindFirstValue("instance_url");

        if (string.IsNullOrEmpty(tid) || string.IsNullOrEmpty(instanceUrl))
        {
            context.Fail("Token is missing the required 'tid' or 'instance_url' claim.");
            return Task.CompletedTask;
        }

        var isDevelopment = context.HttpContext.RequestServices
            .GetRequiredService<IHostEnvironment>().IsDevelopment();

        if (!IsAllowedInstanceUrl(instanceUrl, isDevelopment))
        {
            context.Fail("Token 'instance_url' claim must be HTTPS.");
            return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }

    /// <summary>Reads the claims OnTokenValidated already approved into this request's scoped HostedCredentials.</summary>
    public static void PopulateCredentials(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true) return;

        var creds = context.RequestServices.GetRequiredService<HostedCredentials>();
        creds.OrgId = context.User.FindFirstValue("tid");
        creds.InstanceUrl = context.User.FindFirstValue("instance_url")?.TrimEnd('/');

        var authHeader = context.Request.Headers.Authorization.ToString();
        creds.Token = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authHeader["Bearer ".Length..]
            : authHeader;
    }

    /// <summary>https always allowed; http only for localhost, and only outside Production.</summary>
    internal static bool IsAllowedInstanceUrl(string url, bool isDevelopment)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme == Uri.UriSchemeHttps) return true;
        return isDevelopment && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
    }
}
