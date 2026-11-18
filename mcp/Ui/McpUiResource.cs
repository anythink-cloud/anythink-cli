using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AnythinkMcp.Ui;

public sealed class McpUiResource : McpServerResource
{
    private const string MimeType = "text/html;profile=mcp-app";

    private readonly string _html = Embedded("AnythinkMcp.Ui.view.html").Replace("/*VIEW_JS*/", Embedded("AnythinkMcp.Ui.view.js"));

    public McpUiResource(McpUiOptions options)
    {
        var meta = new JsonObject
        {
            ["ui"] = new JsonObject
            {
                ["csp"] = new JsonObject
                {
                    ["frameDomains"] = new JsonArray(options.FrameDomains.Select(d => (JsonNode)JsonValue.Create(d)!).ToArray()),
                    ["connectDomains"] = new JsonArray(),
                    ["resourceDomains"] = new JsonArray(),
                },
                ["domain"] = options.SandboxDomain,
                ["prefersBorder"] = false,
            },
        };
        ProtocolResource = new Resource { Uri = McpUiRoutes.ResourceUri, Name = "anythink-view", MimeType = MimeType, Meta = meta };
        ProtocolResourceTemplate = new ResourceTemplate { UriTemplate = McpUiRoutes.ResourceUri, Name = "anythink-view", MimeType = MimeType, Meta = meta };
    }

    public override ResourceTemplate ProtocolResourceTemplate { get; }

    public override Resource? ProtocolResource { get; }

    public override IReadOnlyList<object> Metadata => [];

    public override bool IsMatch(string uri) => uri == McpUiRoutes.ResourceUri;

    public override ValueTask<ReadResourceResult> ReadAsync(RequestContext<ReadResourceRequestParams> request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new ReadResourceResult
        {
            Contents = [new TextResourceContents { Uri = McpUiRoutes.ResourceUri, MimeType = MimeType, Text = _html, Meta = ProtocolResource!.Meta }],
        });

    private static string Embedded(string name)
    {
        using var stream = typeof(McpUiResource).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded resource {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
