using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AnythinkCli.Client;
using AnythinkCli.Models;
using ModelContextProtocol.Server;

namespace AnythinkMcp.Tools;

/// <summary>
/// Generic MCP tool that covers every CLI command.
///
/// In stdio mode: shells out to the anythink CLI binary (as before).
/// In HTTP mode: routes commands through AnythinkClient in-process,
/// using per-request credentials from McpClientFactory.
/// </summary>
[McpServerToolType]
public class CliTool
{
    private readonly McpClientFactory _factory;
    public CliTool(McpClientFactory factory) => _factory = factory;

    private static readonly Regex SafeArgs = new(
        @"^[\w\s\-\./:=,@""'\{\}\[\]]+$", RegexOptions.Compiled);

    [McpServerTool(Name = "cli"),
     Description(
        "Run any Anythink CLI command and return its output. " +
        "Use this for commands not covered by dedicated tools (entities, fields, data, workflows, " +
        "roles, menus, secrets, users, files, pay, oauth, migrate, fetch, api, docs, etc.). " +
        "Pass the command exactly as you would after 'anythink', e.g. 'entities list' or 'data list posts'. " +
        "Menu commands: 'menus list' shows dashboard menus with tree structure; " +
        "'menus add-item <menu_id> <entity> --icon <Icon> --parent <parent_id>' adds an entity to a dashboard menu. " +
        "For destructive commands add '--yes' to skip confirmation prompts. " +
        "Add '--json' where supported for machine-readable output.")]
    public async Task<string> RunCli(
        [Description(
            "CLI arguments after 'anythink', e.g. 'entities list', 'users me', " +
            "'data list blog_posts --json', 'migrate --from a --to b --dry-run', 'fetch /some/path'. " +
            "Do NOT include 'anythink' itself or '--profile' (profile is injected automatically).")]
        string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return "Error: command must not be empty.";

        if (!SafeArgs.IsMatch(command))
            return "Error: command contains disallowed characters.";

        // HTTP mode: execute in-process via AnythinkClient
        if (McpClientFactory.IsHttpMode)
            return await ExecuteInProcess(command);

        // Stdio mode: shell out to the CLI binary
        return await ExecuteViaProcess(command);
    }

    // ── HTTP mode: in-process execution ──────────────────────────────────

    private async Task<string> ExecuteInProcess(string command)
    {
        var args = SplitArgs(command);
        if (args.Count == 0) return "Error: empty command.";

        var subcommand = args[0].ToLowerInvariant();
        var subArgs = args.Skip(1).ToList();
        var jsonMode = subArgs.Remove("--json");

        try
        {
            var client = _factory.GetClient();

            return subcommand switch
            {
                "entities" => await HandleEntities(client, subArgs, jsonMode),
                "fields" => await HandleFields(client, subArgs, jsonMode),
                "data" => await HandleData(client, subArgs, jsonMode),
                "workflows" => await HandleWorkflows(client, subArgs, jsonMode),
                "users" => await HandleUsers(client, subArgs, jsonMode),
                "roles" => await HandleRoles(client, subArgs, jsonMode),
                "secrets" => await HandleSecrets(client, subArgs, jsonMode),
                "files" => await HandleFiles(client, subArgs, jsonMode),
                "fetch" => await HandleFetch(client, subArgs),
                "docs" => await HandleDocs(client, jsonMode),
                _ => $"Command '{subcommand}' is not supported in HTTP mode."
            };
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private static async Task<string> HandleEntities(AnythinkClient client, List<string> args, bool json)
    {
        if (args.Count == 0 || args[0] == "list")
        {
            var entities = await client.GetEntitiesAsync();
            if (json) return Serialize(entities);
            return FormatTable("Entities", entities.Select(e => new
            {
                e.Name, e.TableName, Fields = e.Fields?.Count ?? 0,
                Public = e.IsPublic ? "yes" : "no",
                RLS = e.EnableRls ? "yes" : "no"
            }));
        }
        if (args[0] == "get" && args.Count > 1)
            return Serialize(await client.GetEntityAsync(args[1]));
        if (args[0] == "create" && args.Count > 1)
            return Serialize(await client.CreateEntityAsync(new CreateEntityRequest(args[1])));
        if (args[0] == "delete" && args.Count > 1)
        {
            await client.DeleteEntityAsync(args[1]);
            return $"Entity '{args[1]}' deleted.";
        }
        return "Usage: entities [list|get|create|delete] <name>";
    }

    private static async Task<string> HandleFields(AnythinkClient client, List<string> args, bool json)
    {
        if (args.Count < 2) return "Usage: fields [list|add|delete] <entity> [field] [--type type]";

        var action = args[0];
        var entity = args[1];

        if (action == "list")
        {
            var fields = await client.GetFieldsAsync(entity);
            if (json) return Serialize(fields);
            return FormatTable($"Fields on {entity}", fields.Select(f => new
            {
                f.Name, f.DatabaseType, f.DisplayType, Required = f.IsRequired ? "yes" : "no",
                Unique = f.IsUnique ? "yes" : "no"
            }));
        }
        if ((action == "add" || action == "create") && args.Count > 2)
        {
            var fieldName = args[2];
            var dbType = GetFlag(args, "--type") ?? "varchar";
            var displayType = GetFlag(args, "--display") ?? dbType switch
            {
                "varchar" => "input",
                "text" => "textarea",
                "integer" or "bigint" => "input",
                "decimal" => "input",
                "boolean" => "checkbox",
                "timestamp" => "timestamp",
                "json" or "jsonb" => "json",
                _ => "input"
            };
            var required = args.Contains("--required");
            return Serialize(await client.AddFieldAsync(entity,
                new CreateFieldRequest(fieldName, dbType, displayType, IsRequired: required)));
        }
        if (action == "delete" && args.Count > 2)
        {
            var fields = await client.GetFieldsAsync(entity);
            var field = fields.FirstOrDefault(f =>
                string.Equals(f.Name, args[2], StringComparison.OrdinalIgnoreCase));
            if (field == null) return $"Field '{args[2]}' not found on '{entity}'.";
            await client.DeleteFieldAsync(entity, field.Id);
            return $"Field '{args[2]}' deleted from '{entity}'.";
        }
        return "Usage: fields [list|add|delete] <entity> [field] [--type type]";
    }

    private static async Task<string> HandleData(AnythinkClient client, List<string> args, bool json)
    {
        if (args.Count < 2) return "Usage: data [list|get|create|update|delete] <entity> [id]";

        var action = args[0];
        var entity = args[1];

        if (action == "list")
        {
            var pageSize = int.TryParse(GetFlag(args, "--limit"), out var l) ? l : 25;
            var result = await client.ListItemsAsync(entity, pageSize: pageSize);
            return json ? Serialize(result) : Serialize(result.Items);
        }
        if (action == "get" && args.Count > 2 && int.TryParse(args[2], out var getId))
            return Serialize(await client.GetItemAsync(entity, getId));
        if (action == "create" || action == "insert")
        {
            var dataJson = GetFlag(args, "--data") ?? GetFlag(args, "-d");
            if (dataJson == null) return "Usage: data create <entity> --data '{...}'";
            var obj = JsonNode.Parse(dataJson)?.AsObject() ?? new JsonObject();
            return Serialize(await client.CreateItemAsync(entity, obj));
        }
        if (action == "update" && args.Count > 2 && int.TryParse(args[2], out var updateId))
        {
            var dataJson = GetFlag(args, "--data") ?? GetFlag(args, "-d");
            if (dataJson == null) return "Usage: data update <entity> <id> --data '{...}'";
            var obj = JsonNode.Parse(dataJson)?.AsObject() ?? new JsonObject();
            return Serialize(await client.UpdateItemAsync(entity, updateId, obj));
        }
        if (action == "delete" && args.Count > 2 && int.TryParse(args[2], out var deleteId))
        {
            await client.DeleteItemAsync(entity, deleteId);
            return $"Record {deleteId} deleted from '{entity}'.";
        }
        return "Usage: data [list|get|create|update|delete] <entity> [id]";
    }

    private static async Task<int?> ResolveWorkflowId(AnythinkClient client, string idOrName)
    {
        if (int.TryParse(idOrName, out var id)) return id;
        var all = await client.GetWorkflowsAsync();
        return all.FirstOrDefault(w => string.Equals(w.Name, idOrName, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    private static async Task<string> HandleWorkflows(AnythinkClient client, List<string> args, bool json)
    {
        if (args.Count == 0 || args[0] == "list")
        {
            var workflows = await client.GetWorkflowsAsync();
            if (json) return Serialize(workflows);
            return FormatTable("Workflows", workflows.Select(w => new
            {
                w.Id, w.Name, w.Description, w.Enabled, w.Trigger
            }));
        }
        if (args[0] == "get" && args.Count > 1)
        {
            var id = await ResolveWorkflowId(client, args[1]);
            if (id == null) return $"Workflow '{args[1]}' not found.";
            return Serialize(await client.GetWorkflowAsync(id.Value));
        }
        if (args[0] == "create" && args.Count > 1)
        {
            var name = args[1];
            // Normalize trigger casing — API expects PascalCase
            var rawTrigger = GetFlag(args, "--trigger") ?? "Manual";
            var trigger = rawTrigger.ToLowerInvariant() switch
            {
                "manual" => "Manual",
                "timed" => "Timed",
                "event" => "Event",
                "api" => "Api",
                _ => rawTrigger
            };
            var description = GetFlag(args, "--description");
            var cron = GetFlag(args, "--cron");
            var eventType = GetFlag(args, "--event");
            var eventEntity = GetFlag(args, "--entity") ?? GetFlag(args, "--event-entity");
            var apiRoute = GetFlag(args, "--api-route");
            var enabled = args.Contains("--enabled");

            object options = trigger switch
            {
                "Timed" => new { cron_expression = cron ?? "0 9 * * *", event_entity = eventEntity ?? "" },
                "Event" => (object)new EventWorkflowOptions(eventType ?? "EntityCreated", eventEntity ?? ""),
                "Api" => new { api_route = apiRoute ?? "", event_entity = eventEntity ?? "" },
                _ => new { event_entity = eventEntity ?? "", manual_entities = eventEntity != null ? new[] { eventEntity } : Array.Empty<string>() }
            };

            var wf = await client.CreateWorkflowAsync(new CreateWorkflowRequest(
                name, description, trigger, enabled, options, trigger == "Api" ? apiRoute : null));
            return Serialize(wf);
        }
        if (args[0] == "update" && args.Count > 1 && (await ResolveWorkflowId(client, args[1])) is { } updateId)
        {
            var name = GetFlag(args, "--name");
            var description = GetFlag(args, "--description");
            var wf = await client.UpdateWorkflowAsync(updateId, new UpdateWorkflowRequest(name, description));
            return Serialize(wf);
        }
        if (args[0] == "delete" && args.Count > 1 && (await ResolveWorkflowId(client, args[1])) is { } deleteId)
        {
            await client.DeleteWorkflowAsync(deleteId);
            return $"Workflow {deleteId} deleted.";
        }
        if (args[0] == "trigger" && args.Count > 1 && (await ResolveWorkflowId(client, args[1])) is { } triggerId)
        {
            var payloadStr = GetFlag(args, "--payload");
            object? payload = null;
            if (payloadStr != null)
                payload = new { data = JsonNode.Parse(payloadStr) };
            await client.TriggerWorkflowAsync(triggerId, payload);
            return $"Workflow {triggerId} triggered.";
        }
        if (args[0] == "enable" && args.Count > 1 && (await ResolveWorkflowId(client, args[1])) is { } enableId)
        {
            await client.EnableWorkflowAsync(enableId);
            return $"Workflow {enableId} enabled.";
        }
        if (args[0] == "disable" && args.Count > 1 && (await ResolveWorkflowId(client, args[1])) is { } disableId)
        {
            await client.DisableWorkflowAsync(disableId);
            return $"Workflow {disableId} disabled.";
        }
        if (args[0] == "jobs" && args.Count > 1 && (await ResolveWorkflowId(client, args[1])) is { } jobsWfId)
        {
            var result = await client.GetWorkflowJobsAsync(jobsWfId);
            return Serialize(result);
        }
        if (args[0] == "step-add" && args.Count > 2 && int.TryParse(args[1], out var stepAddWfId))
        {
            var key = args[2];
            var stepName = GetFlag(args, "--name") ?? key;
            var action = GetFlag(args, "--action") ?? "RunScript";
            var paramsJson = GetFlag(args, "--params");
            var isStart = args.Contains("--start");
            var stepEnabled = !args.Contains("--disabled");

            JsonElement? parameters = null;
            if (paramsJson != null)
                parameters = JsonSerializer.Deserialize<JsonElement>(paramsJson);

            var step = await client.AddWorkflowStepAsync(stepAddWfId,
                new CreateWorkflowStepRequest(key, stepName, action, stepEnabled, isStart, null, parameters));
            return Serialize(step);
        }
        if (args[0] == "step-get" && args.Count > 2 && int.TryParse(args[1], out var stepGetWfId) && int.TryParse(args[2], out var stepGetId))
        {
            var wf = await client.GetWorkflowAsync(stepGetWfId);
            var step = wf.Steps?.FirstOrDefault(s => s.Id == stepGetId);
            if (step == null) return $"Step {stepGetId} not found in workflow {stepGetWfId}.";
            return Serialize(step);
        }
        if (args[0] == "step-update" && args.Count > 2 && int.TryParse(args[1], out var stepUpdWfId) && int.TryParse(args[2], out var stepUpdId))
        {
            // Fetch current step to preserve values
            var wf = await client.GetWorkflowAsync(stepUpdWfId);
            var step = wf.Steps?.FirstOrDefault(s => s.Id == stepUpdId);
            if (step == null) return $"Step {stepUpdId} not found in workflow {stepUpdWfId}.";

            var body = new Dictionary<string, object?>
            {
                ["name"] = GetFlag(args, "--name") ?? step.Name,
                ["action"] = GetFlag(args, "--action") ?? step.Action,
                ["enabled"] = args.Contains("--disabled") ? false : step.Enabled,
                ["is_start_step"] = args.Contains("--start") || step.IsStartStep,
                ["on_success_step_id"] = step.OnSuccessStepId,
                ["on_failure_step_id"] = step.OnFailureStepId,
            };

            var paramsJson = GetFlag(args, "--params");
            if (paramsJson != null)
                body["parameters"] = JsonSerializer.Deserialize<JsonElement>(paramsJson);
            else if (step.Parameters.HasValue)
                body["parameters"] = step.Parameters.Value;

            var updated = await client.UpdateWorkflowStepFullAsync(stepUpdWfId, stepUpdId, body);
            return Serialize(updated);
        }
        if (args[0] == "step-link" && args.Count > 2 && int.TryParse(args[1], out var linkWfId) && int.TryParse(args[2], out var linkStepId))
        {
            var wf = await client.GetWorkflowAsync(linkWfId);
            var step = wf.Steps?.FirstOrDefault(s => s.Id == linkStepId);
            if (step == null) return $"Step {linkStepId} not found in workflow {linkWfId}.";

            var onSuccess = GetFlag(args, "--on-success");
            var onFailure = GetFlag(args, "--on-failure");

            var body = new Dictionary<string, object?>
            {
                ["name"] = step.Name,
                ["action"] = step.Action,
                ["enabled"] = step.Enabled,
                ["is_start_step"] = step.IsStartStep,
                ["on_success_step_id"] = onSuccess != null && int.TryParse(onSuccess, out var sId) ? sId : step.OnSuccessStepId,
                ["on_failure_step_id"] = onFailure != null && int.TryParse(onFailure, out var fId) ? fId : step.OnFailureStepId,
            };
            if (step.Parameters.HasValue) body["parameters"] = step.Parameters.Value;

            var updated = await client.UpdateWorkflowStepFullAsync(linkWfId, linkStepId, body);
            return Serialize(updated);
        }
        return "Usage: workflows [list|get|create|update|delete|trigger|enable|disable|jobs|step-add|step-get|step-update|step-link] [id] [options]";
    }

    private static async Task<string> HandleUsers(AnythinkClient client, List<string> args, bool json)
    {
        if (args.Count == 0 || args[0] == "list")
            return Serialize(await client.GetUsersAsync());
        if (args[0] == "me")
            return Serialize(await client.GetMeAsync());
        if (args[0] == "get" && args.Count > 1 && int.TryParse(args[1], out var userId))
            return Serialize(await client.GetUserAsync(userId));
        return "Usage: users [list|me|get] [id]";
    }

    private static async Task<string> HandleRoles(AnythinkClient client, List<string> args, bool json)
    {
        if (args.Count == 0 || args[0] == "list")
            return Serialize(await client.GetRolesAsync());
        if (args[0] == "get" && args.Count > 1 && int.TryParse(args[1], out var roleId))
            return Serialize(await client.GetRoleAsync(roleId));
        return "Usage: roles [list|get] [id]";
    }

    private static async Task<string> HandleSecrets(AnythinkClient client, List<string> args, bool json)
    {
        if (args.Count == 0 || args[0] == "list")
            return Serialize(await client.GetSecretsAsync());
        if (args[0] == "create" && args.Count > 1)
        {
            var key = args[1];
            var value = GetFlag(args, "--value") ?? "";
            return Serialize(await client.CreateSecretAsync(new CreateSecretRequest(key, value)));
        }
        if (args[0] == "delete" && args.Count > 1)
        {
            await client.DeleteSecretAsync(args[1]);
            return $"Secret '{args[1]}' deleted.";
        }
        return "Usage: secrets [list|create|delete] [key] [--value val]";
    }

    private static async Task<string> HandleFiles(AnythinkClient client, List<string> args, bool json)
    {
        if (args.Count == 0 || args[0] == "list")
            return Serialize(await client.GetFilesAsync());
        if (args[0] == "get" && args.Count > 1 && int.TryParse(args[1], out var fileId))
            return Serialize(await client.GetFileAsync(fileId));
        if (args[0] == "delete" && args.Count > 1 && int.TryParse(args[1], out var delId))
        {
            await client.DeleteFileAsync(delId);
            return $"File {delId} deleted.";
        }
        return "Usage: files [list|get|delete] [id]";
    }

    private static async Task<string> HandleFetch(AnythinkClient client, List<string> args)
    {
        if (args.Count == 0) return "Usage: fetch [METHOD] <path> [--data '{...}'] or fetch <path> [--method METHOD] [--data '{...}']";

        // Support both: `fetch POST /path -d '{}'` and `fetch /path --method POST --data '{}'`
        var method = "GET";
        var path = args[0];
        if (args.Count > 1 && args[0] is "GET" or "POST" or "PUT" or "PATCH" or "DELETE")
        {
            method = args[0];
            path = args[1];
            args = args.Skip(2).ToList();
        }
        else
        {
            args = args.Skip(1).ToList();
            method = GetFlag(args, "--method") ?? "GET";
        }
        var body = GetFlag(args, "--data") ?? GetFlag(args, "-d");
        // Ensure path is a full URL — prepend the tenant base URL if it's a relative path
        if (path.StartsWith('/'))
            path = $"{client.BaseUrl}/org/{client.OrgId}{path}";
        return await client.FetchRawAsync(path, method, body);
    }

    private static async Task<string> HandleDocs(AnythinkClient client, bool json)
    {
        return await client.FetchRawAsync("/docs", "GET");
    }

    // ── Stdio mode: subprocess execution (unchanged) ─────────────────────

    private async Task<string> ExecuteViaProcess(string command)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "anythink",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            Environment = { ["NO_COLOR"] = "1", ["TERM"] = "dumb" }
        };

        var profile = _factory.ProfileName;
        if (!string.IsNullOrEmpty(profile))
        {
            psi.ArgumentList.Add("--profile");
            psi.ArgumentList.Add(profile);
        }

        foreach (var arg in SplitArgs(command))
            psi.ArgumentList.Add(arg);

        try
        {
            using var process = Process.Start(psi)!;
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var msg = !string.IsNullOrWhiteSpace(stderr) ? stderr.Trim() : stdout.Trim();
                return $"CLI exited with code {process.ExitCode}: {msg}";
            }

            return string.IsNullOrWhiteSpace(stdout) ? "(no output)" : stdout.Trim();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return "Error: 'anythink' CLI not found on PATH. Install it with: dotnet tool install -g anythink-cli";
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string Serialize(object? obj) =>
        JsonSerializer.Serialize(obj, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = true
        });

    private static string FormatTable(string title, IEnumerable<object> rows)
    {
        var items = rows.ToList();
        if (items.Count == 0) return $"{title}: (none)";
        return $"{title} ({items.Count}):\n{Serialize(items)}";
    }

    private static string? GetFlag(List<string> args, string flag)
    {
        var idx = args.IndexOf(flag);
        if (idx >= 0 && idx < args.Count - 1)
        {
            var value = args[idx + 1];
            args.RemoveAt(idx + 1);
            args.RemoveAt(idx);
            return value;
        }
        return null;
    }

    internal static List<string> SplitArgs(string input)
    {
        var args = new List<string>();
        var current = "";
        var inSingle = false;
        var inDouble = false;

        foreach (var c in input)
        {
            if (c == '\'' && !inDouble) { inSingle = !inSingle; continue; }
            if (c == '"' && !inSingle) { inDouble = !inDouble; continue; }
            if (c == ' ' && !inSingle && !inDouble)
            {
                if (current.Length > 0) { args.Add(current); current = ""; }
                continue;
            }
            current += c;
        }
        if (current.Length > 0) args.Add(current);

        return args;
    }
}
