using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace AnythinkCli.Commands;

/// <summary>
/// Lists all REST API endpoints for the current org — both static platform
/// endpoints and the dynamically generated entity data endpoints.
/// </summary>
public class ApiListSettings : CommandSettings
{
    [CommandOption("--json")]
    [Description("Output as JSON")]
    public bool Json { get; set; }

    [CommandOption("--base-url <URL>")]
    [Description("Override base URL for this command")]
    public string? BaseUrl { get; set; }
}

public class ApiListCommand : BaseCommand<ApiListSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ApiListSettings settings)
    {
        try
        {
            var client  = GetClient();
            var baseUrl = settings.BaseUrl ?? client.BaseUrl;
            var orgId   = client.OrgId;
            var base_   = $"{baseUrl.TrimEnd('/')}/org/{orgId}";

            // Fetch entities and workflows in parallel
            var entitiesTask  = client.GetEntitiesAsync();
            var workflowsTask = client.GetWorkflowsAsync();
            await Task.WhenAll(entitiesTask, workflowsTask);
            var entities  = entitiesTask.Result;
            var workflows = workflowsTask.Result;

            if (settings.Json)
            {
                var endpoints = BuildEndpointList(base_, orgId, entities, workflows);
                var json = System.Text.Json.JsonSerializer.Serialize(endpoints, Renderer.PrettyJson);
                Renderer.PrintJson(json);
                return 0;
            }

            Renderer.Header("Platform API Routes");

            // Static platform endpoints
            var staticTable = Renderer.BuildTable("Method", "Path", "Description");
            foreach (var (method, path, desc) in StaticRoutes(base_))
                Renderer.AddRow(staticTable, method, path, desc);
            AnsiConsole.Write(staticTable);

            // Dynamic entity data endpoints
            AnsiConsole.WriteLine();
            Renderer.Header($"Dynamic Entity Routes ({entities.Count} entities)");

            var dynTable = Renderer.BuildTable("Method", "Path", "Description");
            foreach (var e in entities.OrderBy(x => x.Name))
            {
                var ep = $"{base_}/entities/{e.Name}/items";
                var pub = e.IsPublic ? " [public]" : "";
                Renderer.AddRow(dynTable, "GET",    ep,           $"List {e.Name} records{pub}");
                Renderer.AddRow(dynTable, "GET",    ep + "/{id}", $"Get {e.Name} by ID");
                Renderer.AddRow(dynTable, "POST",   ep,           $"Create {e.Name} record");
                Renderer.AddRow(dynTable, "PUT",    ep + "/{id}", $"Update {e.Name} record");
                Renderer.AddRow(dynTable, "DELETE", ep + "/{id}", $"Delete {e.Name} record");

                if (e.IsPublic)
                {
                    var pubEp = $"{base_}/entities/{e.Name}/public/items";
                    Renderer.AddRow(dynTable, "GET", pubEp, $"List {e.Name} (no auth)");
                    Renderer.AddRow(dynTable, "GET", pubEp + "/{id}", $"Get {e.Name} by ID (no auth)");
                }
            }
            AnsiConsole.Write(dynTable);

            // Workflow API routes
            var apiWorkflows = workflows.Where(w => w.Trigger == "Api" && !string.IsNullOrEmpty(w.Trigger)).ToList();
            if (apiWorkflows.Count > 0)
            {
                AnsiConsole.WriteLine();
                Renderer.Header("Workflow API Routes");
                var wfTable = Renderer.BuildTable("Method", "Path", "Workflow");
                foreach (var w in apiWorkflows)
                    Renderer.AddRow(wfTable, "POST", $"{base_}/workflows/api/[route]", w.Name);
                AnsiConsole.Write(wfTable);
            }

            AnsiConsole.WriteLine();
            Renderer.Info($"Auth header: [bold]X-API-Key: <key>[/] or [bold]Authorization: Bearer <token>[/]");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }

    private static IEnumerable<(string Method, string Path, string Desc)> StaticRoutes(string base_)
    {
        var o = base_;
        return [
            ("POST",   $"{o}/auth/v1/token",          "Login — get JWT access token"),
            ("POST",   $"{o}/auth/v1/refresh",         "Refresh access token"),
            ("POST",   $"{o}/auth/v1/logout",          "Invalidate refresh token"),
            ("POST",   $"{o}/auth/v1/register",        "Register new user"),
            ("GET",    $"{o}/api-keys",                "List API keys"),
            ("POST",   $"{o}/api-keys",                "Create API key"),
            ("DELETE", $"{o}/api-keys/{{id}}",         "Revoke API key"),
            ("GET",    $"{o}/entities",                "List all entities"),
            ("GET",    $"{o}/entities/{{name}}",       "Get entity + fields"),
            ("POST",   $"{o}/entities",                "Create entity"),
            ("PUT",    $"{o}/entities/{{name}}",       "Update entity settings"),
            ("DELETE", $"{o}/entities/{{name}}",       "Delete entity"),
            ("GET",    $"{o}/entities/{{e}}/fields",   "List fields"),
            ("POST",   $"{o}/entities/{{e}}/fields",   "Add field"),
            ("PUT",    $"{o}/entities/{{e}}/fields/{{id}}", "Update field"),
            ("DELETE", $"{o}/entities/{{e}}/fields/{{id}}", "Delete field"),
            ("GET",    $"{o}/workflows",               "List workflows"),
            ("GET",    $"{o}/workflows/{{id}}",        "Get workflow + steps"),
            ("POST",   $"{o}/workflows",               "Create workflow"),
            ("PUT",    $"{o}/workflows/{{id}}",        "Update workflow"),
            ("DELETE", $"{o}/workflows/{{id}}",        "Delete workflow"),
            ("POST",   $"{o}/workflows/{{id}}/steps",  "Add workflow step"),
            ("PUT",    $"{o}/workflows/{{id}}/steps/{{sid}}", "Update step"),
            ("DELETE", $"{o}/workflows/{{id}}/steps/{{sid}}", "Delete step"),
            ("POST",   $"{o}/workflows/{{id}}/enable", "Enable workflow"),
            ("POST",   $"{o}/workflows/{{id}}/disable","Disable workflow"),
            ("POST",   $"{o}/workflows/{{id}}/trigger","Trigger workflow manually"),
        ];
    }

    private static object BuildEndpointList(
        string base_, string orgId,
        List<Models.Entity> entities,
        List<Models.Workflow> workflows)
    {
        var staticRoutes = StaticRoutes(base_)
            .Select(r => new { method = r.Method, path = r.Path, description = r.Desc, type = "platform" });

        var dynRoutes = entities.SelectMany(e =>
        {
            var ep = $"{base_}/entities/{e.Name}/items";
            var routes = new List<object>
            {
                new { method = "GET",    path = ep,        description = $"List {e.Name}", entity = e.Name, type = "data" },
                new { method = "GET",    path = ep+"/{id}",description = $"Get {e.Name} by ID", entity = e.Name, type = "data" },
                new { method = "POST",   path = ep,        description = $"Create {e.Name}", entity = e.Name, type = "data" },
                new { method = "PUT",    path = ep+"/{id}",description = $"Update {e.Name}", entity = e.Name, type = "data" },
                new { method = "DELETE", path = ep+"/{id}",description = $"Delete {e.Name}", entity = e.Name, type = "data" },
            };
            if (e.IsPublic)
            {
                var pub = $"{base_}/entities/{e.Name}/public/items";
                routes.Add(new { method = "GET", path = pub, description = $"List {e.Name} (public)", entity = e.Name, type = "public-data" });
            }
            return routes;
        });

        return new { base_url = base_, org_id = orgId, routes = staticRoutes.Concat<object>(dynRoutes) };
    }
}
