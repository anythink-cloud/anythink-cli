using System.Security.Cryptography;
using System.Text;

namespace AnythinkMcp.Ui;

public sealed record McpUiOptions(IReadOnlyDictionary<string, string> Dashboards, string? MyAnythinkUrl, string SandboxDomain)
{
    private const string DashboardsSetting = "MCP_UI_DASHBOARDS";
    private const string MyAnythinkSetting = "MCP_UI_MY_ANYTHINK_URL";

    public IReadOnlyList<string> FrameDomains => Dashboards.Values.Distinct().ToList();

    public static McpUiOptions? FromEnvironment(Func<string, string?> get, string publicUrl)
    {
        if (HostedConfig.Optional(get, DashboardsSetting) is not { } raw) return null;

        var pairs = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(pair => pair.Split('=') is [var instance, var dashboard]
                ? (Instance: Origin(instance, DashboardsSetting), Dashboard: Origin(dashboard, DashboardsSetting))
                : throw new InvalidOperationException($"{DashboardsSetting} entries must be <instance origin>=<dashboard origin>."))
            .ToList();
        if (pairs.Count == 0) throw new InvalidOperationException($"{DashboardsSetting} must hold at least one entry.");
        if (pairs.GroupBy(p => p.Instance).FirstOrDefault(g => g.Count() > 1) is { } repeated)
            throw new InvalidOperationException($"{DashboardsSetting} repeats instance {repeated.Key}.");
        var dashboards = pairs.ToDictionary(p => p.Instance, p => p.Dashboard);

        var myAnythink = HostedConfig.Optional(get, MyAnythinkSetting) is { } url ? Origin(url, MyAnythinkSetting) : null;
        return new McpUiOptions(dashboards, myAnythink, SandboxDomainFor(publicUrl));
    }

    public static string SandboxDomainFor(string publicUrl) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(publicUrl))).ToLowerInvariant()[..32] + ".claudemcpcontent.com";

    private static string Origin(string value, string setting) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.PathAndQuery == "/" && uri.Fragment.Length == 0 && uri.UserInfo.Length == 0 && !uri.Host.Contains('*')
            ? uri.GetLeftPart(UriPartial.Authority)
            : throw new InvalidOperationException($"{setting} must hold https origins with no path, got '{value}'.");
}
