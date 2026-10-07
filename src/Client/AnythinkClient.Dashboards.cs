using System.Text.Json.Nodes;

namespace AnythinkCli.Client;

public partial class AnythinkClient
{
    // ── Charts ────────────────────────────────────────────────────────────────

    public async Task<JsonArray> GetChartsAsync(string? entityName = null)
        => await GetAsync<JsonArray>(_org + "/charts" + (string.IsNullOrEmpty(entityName) ? "" : "?entityName=" + Uri.EscapeDataString(entityName))) ?? [];

    public Task<JsonObject> GetChartAsync(int id)
        => GetOrNotFoundAsync<JsonObject>(_org + $"/charts/{id}", $"Chart {id} not found.");

    public Task<JsonObject> CreateChartAsync(JsonObject body)
        => PostAsync<JsonObject>(_org + "/charts", body);

    public async Task<JsonObject> UpdateChartAsync(int id, JsonObject body)
        => await PutAsync<JsonObject>(_org + $"/charts/{id}", body) ?? throw new AnythinkException($"Chart {id} not found.", 404);

    public Task DeleteChartAsync(int id)
        => DeleteAsync(_org + $"/charts/{id}");

    public async Task<JsonNode?> PreviewChartAsync(JsonObject body)
        => await PostOptionalAsync<JsonNode>(_org + "/charts/preview-config", body);

    public async Task<JsonArray> GetPlatformMetricsAsync()
        => await GetAsync<JsonArray>(_org + "/charts/platform-metrics") ?? [];

    // ── Dashboards ────────────────────────────────────────────────────────────

    public async Task<JsonArray> GetDashboardsAsync()
        => await GetAsync<JsonArray>(_org + "/dashboards") ?? [];

    public Task<JsonObject> GetMyDashboardAsync()
        => GetOrNotFoundAsync<JsonObject>(_org + "/dashboards/me", "No dashboard found.");

    public Task<JsonObject> GetDashboardAsync(int id)
        => GetOrNotFoundAsync<JsonObject>(_org + $"/dashboards/{id}", $"Dashboard {id} not found.");

    public Task<JsonObject> CreateDashboardAsync(JsonObject body)
        => PostAsync<JsonObject>(_org + "/dashboards", body);

    public async Task<JsonObject> UpdateDashboardAsync(int id, JsonObject body)
        => await PutAsync<JsonObject>(_org + $"/dashboards/{id}", body) ?? throw new AnythinkException($"Dashboard {id} not found.", 404);

    public Task DeleteDashboardAsync(int id)
        => DeleteAsync(_org + $"/dashboards/{id}");

    public Task<JsonObject> GetDashboardDataAsync(int id, IEnumerable<int> widgetIds)
        => PostAsync<JsonObject>(_org + $"/dashboards/{id}/data", new JsonObject { ["widget_ids"] = new JsonArray(widgetIds.Select(w => (JsonNode)w).ToArray()) });

    public async Task<JsonNode?> PreviewWidgetAsync(string type, string? configJson)
        => await PostOptionalAsync<JsonNode>(_org + "/dashboards/widgets/preview", new JsonObject { ["type"] = type, ["config_json"] = configJson });

    public Task<JsonObject> SetHomeDashboardAsync(int id)
        => PostAsync<JsonObject>(_org + $"/dashboards/{id}/set-home");

    public Task<JsonObject> PromoteDashboardToDefaultAsync(int id)
        => PostAsync<JsonObject>(_org + $"/dashboards/{id}/promote-to-default");

    private async Task<T> GetOrNotFoundAsync<T>(string url, string notFound) where T : class
    {
        try
        {
            return await GetAsync<T>(url) ?? throw new AnythinkException(notFound, 404);
        }
        catch (AnythinkException ex) when (ex.StatusCode == 404 && string.IsNullOrWhiteSpace(ex.Message))
        {
            throw new AnythinkException(notFound, 404);
        }
    }
}
