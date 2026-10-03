namespace AnythinkMcp;

internal static class HostedConfig
{
    public static HostedMode.Options FromEnvironment(Func<string, string?> get)
    {
        string Required(string name) => get(name) is { Length: > 0 } v
            ? v
            : throw new InvalidOperationException($"{name} must be set in --hosted mode.");

        var publicUrl = Required("MCP_PUBLIC_URL").TrimEnd('/');
        string? Optional(string name) => HostedConfig.Optional(get, name);

        return new HostedMode.Options
        {
            PublicUrl = publicUrl,
            Issuer = Required("MCP_AUTH_ISSUER"),
            Audience = Optional("MCP_AUTH_AUDIENCE") ?? publicUrl,
            Exchange = new TokenExchangeOptions
            {
                ClientId = Required("MCP_EXCHANGE_CLIENT_ID"),
                ClientSecret = Required("MCP_EXCHANGE_CLIENT_SECRET"),
                Audience = Required("MCP_UPSTREAM_AUDIENCE"),
                TokenEndpoint = Optional("MCP_TOKEN_ENDPOINT"),
            },
            AllowedInstanceHostSuffixes = InstanceHostSuffixes(get),
            AllowedOrigins = List(get("MCP_ALLOWED_ORIGINS")),
            AllowedHosts = List(get("MCP_ALLOWED_HOSTS")),
            AllowLoopbackInstance = LoopbackOptIn(get),
        };
    }

    public static bool LoopbackOptIn(Func<string, string?> get)
    {
        if (!string.Equals(get("MCP_ALLOW_LOOPBACK_INSTANCE"), "true", StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(get("ASPNETCORE_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("MCP_ALLOW_LOOPBACK_INSTANCE is only allowed when ASPNETCORE_ENVIRONMENT is Development.");
        return true;
    }

    public static string? Optional(Func<string, string?> get, string name) =>
        get(name) is { Length: > 0 } value ? value : null;

    public static IReadOnlyList<string> InstanceHostSuffixes(Func<string, string?> get) =>
        List(get("MCP_INSTANCE_HOST_SUFFIXES")) is { Count: > 0 } suffixes ? suffixes : HostedMode.DefaultInstanceHostSuffixes;

    private static List<string> List(string? raw) =>
        raw?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList() ?? [];
}
