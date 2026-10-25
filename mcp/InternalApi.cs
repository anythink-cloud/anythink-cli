using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

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
    public static InternalApiOptions ForHosted(Func<string, string?> get)
    {
        var bind = get("MCP_INTERNAL_BIND") is { Length: > 0 } b ? b : "127.0.0.1";
        var options = Build(get, bind);

        if (!IsLoopbackBind(bind) && options.Token is null)
            throw new InvalidOperationException(
                "MCP_INTERNAL_TOKEN must be set when MCP_INTERNAL_BIND is not a loopback address.");

        return options;
    }

    public static InternalApiOptions ForHttp(Func<string, string?> get) => Build(get, "0.0.0.0");

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
        Token = get("MCP_INTERNAL_TOKEN") is { Length: > 0 } t ? t : null,
        InstanceHostSuffixes = get("MCP_INSTANCE_HOST_SUFFIXES")
            ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is { Length: > 0 } s
            ? s
            : HostedMode.DefaultInstanceHostSuffixes,
        AllowLoopbackInstance = HostedConfig.LoopbackOptIn(get),
    };
}
