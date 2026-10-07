using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AnythinkCli.Models;

public static class WorkflowJson
{
    // Keeps scripts readable: quotes and angle brackets aren't turned into \uXXXX escapes.
    public static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions PrettyOmitNull = new(Pretty)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly HashSet<string> WorkflowStorage =
    [
        "tenant_id",
        "created_at",
        "updated_at",
        "editor_state",
        "jobs",
        "last_run_at",
        "last_run_status",
        "options_json",
        "locked",
        "created_by",
        "updated_by",
    ];

    private static readonly HashSet<string> StepStorage =
    [
        "workflow_id",
        "tenant_id",
        "created_at",
        "updated_at",
        "on_success_step",
        "on_failure_step",
        "parameters_json",
        "locked",
        "created_by",
        "updated_by",
    ];

    private static readonly HashSet<string> TriggerStorage = ["tenant_id", "workflow_id", "config_json"];

    // Definition for `workflows export`: no ids at all, links by step key.
    public static string ForExport(string rawWorkflowJson)
    {
        using var doc = JsonDocument.Parse(rawWorkflowJson);
        var root = doc.RootElement;

        var idToKey = new Dictionary<int, string>();
        if (root.TryGetProperty("steps", out var stepList) && stepList.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in stepList.EnumerateArray())
            {
                if (s.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var id)
                    && s.TryGetProperty("key", out var keyEl) && keyEl.GetString() is { } k)
                {
                    idToKey[id] = k;
                }
            }
        }

        var exported = new Dictionary<string, object?> { ["schema_version"] = 1 };
        Copy(root, exported, WorkflowStorage, ["id"], keepIds: false, keepRunState: false);

        var steps = Steps(root, StepStorage, ["id", "on_success_step_id", "on_failure_step_id"], (step, source) =>
        {
            if (LinkedKey(source, "on_success_step_id", idToKey) is { } onSuccess) step["on_success"] = onSuccess;
            if (LinkedKey(source, "on_failure_step_id", idToKey) is { } onFailure) step["on_failure"] = onFailure;
        });
        exported["steps"] = steps;

        return JsonSerializer.Serialize(exported, PrettyOmitNull);
    }

    // Everything `export` keeps, plus the ids to follow up with step and trigger commands and each trigger's last and next run.
    public static string ForGet(string rawWorkflowJson)
    {
        using var doc = JsonDocument.Parse(rawWorkflowJson);
        var root = doc.RootElement;

        var result = new Dictionary<string, object?>();
        Copy(root, result, WorkflowStorage, [], keepIds: true, keepRunState: true);
        result["steps"] = Steps(root, StepStorage, [], (_, _) => { });

        return JsonSerializer.Serialize(result, PrettyOmitNull);
    }

    public static string ForList(IEnumerable<Workflow> workflows)
    {
        var summaries = workflows.OrderBy(w => w.Id).Select(w => new Dictionary<string, object?>
        {
            ["id"] = w.Id,
            ["name"] = w.Name,
            ["description"] = w.Description,
            ["group"] = w.Group,
            ["enabled"] = w.Enabled,
            ["triggers"] = WorkflowTriggers.Effective(w).Select(t => new Dictionary<string, object?>
            {
                ["type"] = t.Type,
                ["enabled"] = t.Enabled,
                ["config"] = WorkflowTriggers.WithoutRunState(t.Config),
            }).ToList(),
            ["step_count"] = w.Steps?.Count ?? 0,
        }).ToList();

        return JsonSerializer.Serialize(summaries, Pretty);
    }

    private static void Copy(
        JsonElement root, Dictionary<string, object?> target, HashSet<string> strip, string[] stripToo, bool keepIds, bool keepRunState)
    {
        foreach (var prop in root.EnumerateObject())
        {
            if (strip.Contains(prop.Name) || stripToo.Contains(prop.Name) || prop.Name == "steps") continue;
            target[prop.Name] = prop.Name == "triggers" && prop.Value.ValueKind == JsonValueKind.Array
                ? Triggers(prop.Value, keepIds, keepRunState)
                : ToObject(prop.Value);
        }
    }

    private static List<Dictionary<string, object?>> Triggers(JsonElement triggers, bool keepIds, bool keepRunState)
    {
        var result = new List<Dictionary<string, object?>>();
        foreach (var trigger in triggers.EnumerateArray())
        {
            if (trigger.ValueKind != JsonValueKind.Object) continue;
            var copy = new Dictionary<string, object?>();
            foreach (var prop in trigger.EnumerateObject())
            {
                if (TriggerStorage.Contains(prop.Name) || (!keepIds && prop.Name == "id")) continue;
                copy[prop.Name] = prop.Name == "config" && prop.Value.ValueKind == JsonValueKind.Object && !keepRunState
                    ? WorkflowTriggers.WithoutRunState(prop.Value)
                    : ToObject(prop.Value);
            }
            result.Add(copy);
        }
        return result;
    }

    private static List<Dictionary<string, object?>> Steps(
        JsonElement root, HashSet<string> strip, string[] stripToo, Action<Dictionary<string, object?>, JsonElement> link)
    {
        var result = new List<Dictionary<string, object?>>();
        if (!root.TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var source in steps.EnumerateArray())
        {
            var step = new Dictionary<string, object?>();
            foreach (var prop in source.EnumerateObject())
            {
                if (strip.Contains(prop.Name) || stripToo.Contains(prop.Name)) continue;
                step[prop.Name] = ToObject(prop.Value);
            }

            if (source.TryGetProperty("parameters_json", out var pj) && pj.ValueKind == JsonValueKind.String
                && !string.IsNullOrEmpty(pj.GetString()))
            {
                using var parsed = JsonDocument.Parse(pj.GetString()!);
                step["parameters"] = ToObject(parsed.RootElement);
            }

            link(step, source);
            result.Add(step);
        }
        return result;
    }

    private static string? LinkedKey(JsonElement step, string property, Dictionary<int, string> idToKey) =>
        step.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.Number
        && el.TryGetInt32(out var id) && idToKey.TryGetValue(id, out var key)
            ? key
            : null;

    // A plain object tree, so the outer serialiser emits proper JSON instead of escaped raw text.
    private static object? ToObject(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => e.EnumerateObject().ToDictionary(p => p.Name, p => ToObject(p.Value)),
        JsonValueKind.Array => e.EnumerateArray().Select(ToObject).ToList(),
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };
}
