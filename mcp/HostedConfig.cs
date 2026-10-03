namespace AnythinkMcp;

internal static class HostedConfig
{
    public static HostedMode.Options FromEnvironment(Func<string, string?> get)
    {
        string Required(string name) => get(name) is { Length: > 0 } v
            ? v
            : throw new InvalidOperationException($"{name} must be set in --hosted mode.");

        var publicUrl = Required("MCP_PUBLIC_URL").TrimEnd('/');

        return new HostedMode.Options
        {
            PublicUrl = publicUrl,
            Issuer = Required("MCP_AUTH_ISSUER"),
            Audience = get("MCP_AUTH_AUDIENCE") is { Length: > 0 } a ? a : publicUrl,
            Exchange = new TokenExchangeOptions
            {
                ClientId = Required("MCP_EXCHANGE_CLIENT_ID"),
                ClientSecret = Required("MCP_EXCHANGE_CLIENT_SECRET"),
                Audience = Required("MCP_UPSTREAM_AUDIENCE"),
                TokenEndpoint = get("MCP_TOKEN_ENDPOINT") is { Length: > 0 } te ? te : null,
            },
            AllowedInstanceHostSuffixes = List(get("MCP_INSTANCE_HOST_SUFFIXES")) is { Count: > 0 } s
                ? s
                : HostedMode.DefaultInstanceHostSuffixes,
            AllowedOrigins = List(get("MCP_ALLOWED_ORIGINS")),
            AllowedHosts = List(get("MCP_ALLOWED_HOSTS")),
            AllowLoopbackInstance = LoopbackOptIn(get),
        };
    }

    public static bool LoopbackOptIn(Func<string, string?> get) =>
        string.Equals(get("MCP_ALLOW_LOOPBACK_INSTANCE"), "true", StringComparison.OrdinalIgnoreCase);

    private static List<string> List(string? raw) =>
        raw?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList() ?? [];
}
