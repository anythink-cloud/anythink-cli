using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AnythinkMcp.Ui;

public sealed class UiOpenTool(McpUiOptions options) : McpServerTool
{
    private const int MaxPathLength = 512;
    private const int MaxDecodeRounds = 5;

    public override Tool ProtocolTool { get; } = new()
    {
        Name = "ui_open",
        Title = "Open view",
        Description = "Used by the Anythink view to get the page for the tool that just ran. Not for direct use.",
        InputSchema = JsonSerializer.SerializeToElement(new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["tool"] = new JsonObject { ["type"] = "string" },
                ["arguments"] = new JsonObject { ["type"] = "object" },
                ["path"] = new JsonObject { ["type"] = "string" },
            },
            ["required"] = new JsonArray("tool"),
            ["additionalProperties"] = false,
        }),
        Annotations = new ToolAnnotations { Title = "Open view", ReadOnlyHint = true },
        Meta = new JsonObject { ["ui"] = new JsonObject { ["visibility"] = new JsonArray("app") } },
    };

    public override IReadOnlyList<object> Metadata => [];

    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        var input = request.Params?.Arguments;
        var tool = input is not null && input.TryGetValue("tool", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        var path = input is not null && input.TryGetValue("path", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        var args = input is not null && input.TryGetValue("arguments", out var a) && a.ValueKind == JsonValueKind.Object
            ? a.EnumerateObject().ToDictionary(x => x.Name, x => x.Value)
            : [];

        UiRoute? route = null;
        if (path is null && (tool is null || !McpUiRoutes.TryGet(tool, out route)))
            return Fail("There's no view for this tool.");
        if (path is not null && !SafePath(path))
            return Fail("That path isn't a valid dashboard path.");

        try
        {
            return route?.Target == UiTarget.Account
                ? AccountLink(route, args)
                : await DashboardLinkAsync(request.Services!, args, route, path, cancellationToken);
        }
        catch (HostedProjectException ex)
        {
            return Fail(ex.Message);
        }
    }

    private CallToolResult AccountLink(UiRoute route, Dictionary<string, JsonElement> args) =>
        options.MyAnythinkUrl is null
            ? Fail("Account pages aren't available here yet.")
            : Link(options.MyAnythinkUrl + McpUiRoutes.Path(route, args), options.MyAnythinkUrl, "account", false);

    private async Task<CallToolResult> DashboardLinkAsync(
        IServiceProvider services, Dictionary<string, JsonElement> args, UiRoute? route, string? path, CancellationToken cancellationToken)
    {
        var project = args.TryGetValue(HostedProjects.ParameterName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        var resolved = await services.GetRequiredService<HostedProjects>().ResolveAsync(project, cancellationToken);
        if (!options.Dashboards.TryGetValue(new Uri(resolved.InstanceUrl).GetLeftPart(UriPartial.Authority), out var origin))
            return Fail("The dashboard can't be shown here for this project's region yet.");

        var orgPrefix = "/org/" + resolved.OrgId;
        if (path is not null && !path.StartsWith(orgPrefix + "/", StringComparison.Ordinal))
            return Fail("That page isn't available.");
        var page = path ?? orgPrefix + McpUiRoutes.Path(route!, args);

        var accounts = services.GetRequiredService<HostedAccounts>();
        if (accounts.HasAccess && await TransferTokenAsync(services, accounts, resolved.ProjectId, cancellationToken) is { } token)
            return Link(
                $"{origin}{orgPrefix}/auth/transfer?transfer_token={Uri.EscapeDataString(token)}&target_org_id={resolved.OrgId}&redirect_to={Uri.EscapeDataString(page)}&embed=true",
                origin, "dashboard", true);
        return Link(origin + page, origin, "dashboard", false);
    }

    private static async Task<string?> TransferTokenAsync(
        IServiceProvider services, HostedAccounts accounts, Guid projectId, CancellationToken cancellationToken)
    {
        try
        {
            using var projects = JsonDocument.Parse(await services.GetRequiredService<HostedProjects>().ListAsync(cancellationToken));
            var account = projects.RootElement.EnumerateArray()
                .Where(p => IdOf(p, "project_id") == projectId)
                .Select(p => IdOf(p, "account_id"))
                .FirstOrDefault(id => id != Guid.Empty);
            return account == Guid.Empty ? null : (await accounts.Client().GetTransferTokenAsync(account, projectId)).TransferToken;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static Guid IdOf(JsonElement entry, string name) =>
        entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty(name, out var id) && id.ValueKind == JsonValueKind.String
        && Guid.TryParse(id.GetString(), out var parsed) ? parsed : Guid.Empty;

    private static bool SafePath(string path)
    {
        for (var round = 0; round < MaxDecodeRounds; round++)
        {
            if (!SafeForm(path)) return false;
            var decoded = Uri.UnescapeDataString(path);
            if (decoded == path) return true;
            path = decoded;
        }
        return false;
    }

    private static bool SafeForm(string path) =>
        path.Length <= MaxPathLength && path.StartsWith('/') && !path.Contains("//")
        && !path.Contains("..") && !path.Any(c => c is ':' or '\\' || char.IsControl(c));

    private static CallToolResult Link(string url, string origin, string target, bool signedIn) => new()
    {
        Content = [new TextContentBlock { Text = JsonSerializer.Serialize(new { url, origin, target, signedIn }) }],
    };

    private static CallToolResult Fail(string message) => new()
    {
        Content = [new TextContentBlock { Text = message }],
        IsError = true,
    };
}
