using System.Text.Json.Nodes;

namespace AnythinkCli.Client;

public static class ChartsApi
{
    private static string Org(AnythinkClient client) => $"{client.BaseUrl}/org/{client.OrgId}";

    private static async Task<JsonNode?> SendAsync(AnythinkClient client, string method, string path, JsonNode? body = null, string? notFound = null)
    {
        string raw;
        try
        {
            raw = await client.FetchRawAsync(Org(client) + path, method, body?.ToJsonString());
        }
        catch (AnythinkException ex) when (notFound is not null && ex.StatusCode == 404 && string.IsNullOrWhiteSpace(ex.Message))
        {
            throw new AnythinkException(notFound, 404);
        }

        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            return JsonNode.Parse(raw);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new AnythinkException($"Parse error: {ex.Message}", 200);
        }
    }

    private static async Task<JsonObject> ObjectAsync(AnythinkClient client, string method, string path, JsonNode? body = null, string? notFound = null) =>
        await SendAsync(client, method, path, body, notFound) as JsonObject ?? throw new AnythinkException(notFound ?? "Empty response.", 404);

    // ── Charts ────────────────────────────────────────────────────────────────

    public static async Task<JsonArray> GetChartsAsync(this AnythinkClient client, string? entityName = null) =>
        await SendAsync(client, "GET", "/charts" + (string.IsNullOrEmpty(entityName) ? "" : "?entityName=" + Uri.EscapeDataString(entityName))) as JsonArray ?? [];

    public static Task<JsonObject> GetChartAsync(this AnythinkClient client, int id) =>
        ObjectAsync(client, "GET", $"/charts/{id}", notFound: $"Chart {id} not found.");

    public static Task<JsonObject> CreateChartAsync(this AnythinkClient client, JsonObject body) =>
        ObjectAsync(client, "POST", "/charts", body);

    public static Task<JsonObject> UpdateChartAsync(this AnythinkClient client, int id, JsonObject body) =>
        ObjectAsync(client, "PUT", $"/charts/{id}", body, $"Chart {id} not found.");

    public static Task DeleteChartAsync(this AnythinkClient client, int id) =>
        SendAsync(client, "DELETE", $"/charts/{id}");

    public static Task<JsonNode?> PreviewChartAsync(this AnythinkClient client, JsonObject body) =>
        SendAsync(client, "POST", "/charts/preview-config", body);

    public static async Task<JsonArray> GetPlatformMetricsAsync(this AnythinkClient client) =>
        await SendAsync(client, "GET", "/charts/platform-metrics") as JsonArray ?? [];

    // ── Dashboards ────────────────────────────────────────────────────────────

    public static async Task<JsonArray> GetDashboardsAsync(this AnythinkClient client) =>
        await SendAsync(client, "GET", "/dashboards") as JsonArray ?? [];

    public static Task<JsonObject> GetMyDashboardAsync(this AnythinkClient client) =>
        ObjectAsync(client, "GET", "/dashboards/me", notFound: "No dashboard found.");

    public static Task<JsonObject> GetDashboardAsync(this AnythinkClient client, int id) =>
        ObjectAsync(client, "GET", $"/dashboards/{id}", notFound: $"Dashboard {id} not found.");

    public static Task<JsonObject> CreateDashboardAsync(this AnythinkClient client, JsonObject body) =>
        ObjectAsync(client, "POST", "/dashboards", body);

    public static Task<JsonObject> UpdateDashboardAsync(this AnythinkClient client, int id, JsonObject body) =>
        ObjectAsync(client, "PUT", $"/dashboards/{id}", body, $"Dashboard {id} not found.");

    public static Task DeleteDashboardAsync(this AnythinkClient client, int id) =>
        SendAsync(client, "DELETE", $"/dashboards/{id}");

    public static Task<JsonObject> GetDashboardDataAsync(this AnythinkClient client, int id, IEnumerable<int> widgetIds) =>
        ObjectAsync(client, "POST", $"/dashboards/{id}/data", new JsonObject { ["widget_ids"] = new JsonArray(widgetIds.Select(w => (JsonNode)w).ToArray()) });

    public static Task<JsonNode?> PreviewWidgetAsync(this AnythinkClient client, string type, string? configJson) =>
        SendAsync(client, "POST", "/dashboards/widgets/preview", new JsonObject { ["type"] = type, ["config_json"] = configJson });

    public static Task<JsonObject> SetHomeDashboardAsync(this AnythinkClient client, int id) =>
        ObjectAsync(client, "POST", $"/dashboards/{id}/set-home", new JsonObject());

    public static Task<JsonObject> PromoteDashboardToDefaultAsync(this AnythinkClient client, int id) =>
        ObjectAsync(client, "POST", $"/dashboards/{id}/promote-to-default", new JsonObject());
}
