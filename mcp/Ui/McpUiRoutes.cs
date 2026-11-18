using System.Text.Json;
using System.Text.Json.Nodes;

namespace AnythinkMcp.Ui;

public enum UiTarget { Dashboard, Account }

public sealed record UiRoute(UiTarget Target, string Template);

public static class McpUiRoutes
{
    public const string ResourceUri = "ui://anythink/view";

    private static readonly Dictionary<string, UiRoute> Routes = Build(
        (UiTarget.Dashboard, "/entities/{entity}", "data_list data_create"),
        (UiTarget.Dashboard, "/entities/{entity}/{id}", "data_get data_update"),
        (UiTarget.Dashboard, "/settings/data-model", "entities_list entities_create entities_get entities_update fields_add fields_get fields_list fields_update"),
        (UiTarget.Dashboard, "/workflows", "workflows_list workflows_create"),
        (UiTarget.Dashboard, "/workflows/{id}", "workflows_get workflows_jobs workflows_enable workflows_disable workflows_trigger workflows_update workflows_export"),
        (UiTarget.Dashboard, "/workflows/{workflow_id}", "workflows_step_add workflows_step_get workflows_step_link workflows_step_update workflows_file_handler_add workflows_integration_add"),
        (UiTarget.Dashboard, "/dashboard", "dashboards_list dashboards_mine dashboards_create charts_list charts_get charts_create charts_update"),
        (UiTarget.Dashboard, "/dashboard/{id}", "dashboards_get dashboards_data dashboards_update"),
        (UiTarget.Dashboard, "/dashboard/{dashboard_id}", "dashboards_add_chart"),
        (UiTarget.Dashboard, "/settings/users", "users_list users_invite users_me"),
        (UiTarget.Dashboard, "/settings/users/{id}", "users_get"),
        (UiTarget.Dashboard, "/settings/roles-and-permissions", "roles_list roles_create"),
        (UiTarget.Dashboard, "/settings/roles-and-permissions/role/{role_id}", "roles_permissions_add roles_permissions_list"),
        (UiTarget.Dashboard, "/files", "files_list files_get"),
        (UiTarget.Dashboard, "/settings/integrations", "integrations_connect integrations_connections_list integrations_disable integrations_enable integrations_execute integrations_get integrations_list integrations_oauth_callback_url integrations_oauth_configure integrations_oauth_status integrations_test"),
        (UiTarget.Dashboard, "/settings/menu", "menus_add_item menus_create menus_get menus_list menus_reorder menus_reorder_items menus_update menus_update_item"),
        (UiTarget.Dashboard, "/integrations/anythinkpay/payments", "pay_payments"),
        (UiTarget.Dashboard, "/integrations/anythinkpay/subscriptions", "pay_subscriptions_list"),
        (UiTarget.Dashboard, "/integrations/anythinkpay/subscriptions/{id}", "pay_subscriptions_get"),
        (UiTarget.Dashboard, "/integrations/anythinkpay/subscriptions/plans", "pay_plans_list pay_plans_create"),
        (UiTarget.Dashboard, "/integrations/anythinkpay/subscriptions/plans/{id}", "pay_plans_get pay_plans_update"),
        (UiTarget.Account, "/dashboard", "accounts_list accounts_create"),
        (UiTarget.Account, "/accounts/{account_id}", "projects_create"));

    public static bool TryGet(string toolName, out UiRoute route) => Routes.TryGetValue(toolName, out route!);

    public static IEnumerable<string> ToolNames => Routes.Keys;

    public static JsonObject ToolMeta() => new() { ["ui"] = new JsonObject { ["resourceUri"] = ResourceUri } };

    public static string Path(UiRoute route, IReadOnlyDictionary<string, JsonElement> args)
    {
        var template = route.Template;
        foreach (var name in Placeholders(template))
        {
            if (!args.TryGetValue(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined || Text(value).Trim().Length == 0)
                return Fill(template[..template.IndexOf("/{" + name + "}", StringComparison.Ordinal)], args);
        }
        return Fill(template, args);
    }

    public static IEnumerable<string> Placeholders(string template) =>
        template.Split('/').Where(s => s.Length > 2 && s[0] == '{' && s[^1] == '}').Select(s => s[1..^1]);

    private static string Fill(string template, IReadOnlyDictionary<string, JsonElement> args)
    {
        foreach (var name in Placeholders(template))
        {
            var text = Text(args[name]);
            template = template.Replace("{" + name + "}", text is "." or ".." ? text.Replace(".", "%2E") : Uri.EscapeDataString(text));
        }
        return template;
    }

    private static string Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();

    private static Dictionary<string, UiRoute> Build(params (UiTarget Target, string Template, string Tools)[] rows) =>
        rows.SelectMany(row => row.Tools.Split(' ').Select(tool => (tool, route: new UiRoute(row.Target, row.Template))))
            .ToDictionary(x => x.tool, x => x.route);
}
