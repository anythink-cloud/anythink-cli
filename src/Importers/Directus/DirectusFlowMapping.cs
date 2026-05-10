using System.Text.Json;

namespace AnythinkCli.Importers.Directus;

// Maps Directus flow triggers and operation types onto Anythink workflow
// trigger strings and WorkflowAction values.
//
// Anythink workflow actions: ReadData, CreateData, UpdateData, DeleteData,
// CallAnApi, RunScript, SendACommand, Condition, SendAnEmail, Integration

public static class DirectusFlowMapping
{
    /// <summary>
    /// Maps a Directus flow trigger to an Anythink (trigger, options) pair.
    /// </summary>
    public static (string Trigger, object Options) MapTrigger(DirectusFlow flow)
    {
        var opts = flow.Options;

        switch (flow.Trigger.ToLowerInvariant())
        {
            case "schedule":
            {
                var cron = "0 9 * * *";
                if (opts.HasValue &&
                    opts.Value.TryGetProperty("cron", out var cronEl) &&
                    cronEl.ValueKind == JsonValueKind.String)
                    cron = cronEl.GetString() ?? cron;
                return ("Timed", new { cron_expression = cron, event_entity = "" });
            }

            case "event":
            {
                // Directus event options: { scope: ["items.create"], collections: ["articles"] }
                var entity    = "";
                var eventName = "EntityCreated";

                if (opts.HasValue)
                {
                    if (opts.Value.TryGetProperty("collections", out var colsEl) &&
                        colsEl.ValueKind == JsonValueKind.Array &&
                        colsEl.GetArrayLength() > 0)
                        entity = colsEl[0].GetString() ?? "";

                    if (opts.Value.TryGetProperty("scope", out var scopeEl) &&
                        scopeEl.ValueKind == JsonValueKind.Array &&
                        scopeEl.GetArrayLength() > 0)
                    {
                        var scope = scopeEl[0].GetString() ?? "";
                        eventName = scope switch
                        {
                            "items.create" => "EntityCreated",
                            "items.update" => "EntityUpdated",
                            "items.delete" => "EntityDeleted",
                            _              => "EntityCreated"
                        };
                    }
                }

                return ("Event", new { @event = eventName, event_entity = entity });
            }

            case "webhook":
                return ("Api", new { api_route = "" });

            default: // manual, operation, or unknown
                return ("Manual", new { });
        }
    }

    /// <summary>
    /// Maps a Directus operation type to a valid Anythink WorkflowAction value.
    /// Falls back to <c>RunScript</c> for any operation we can't represent —
    /// the user will need to fix it up manually after import.
    /// </summary>
    public static string MapAction(string directusType) =>
        directusType.ToLowerInvariant() switch
        {
            "exec-script"  => "RunScript",
            "log"          => "RunScript",     // closest fit — log via custom script
            "mail"         => "SendAnEmail",
            "notification" => "Integration",
            "request"      => "CallAnApi",
            "item-create"  => "CreateData",
            "item-read"    => "ReadData",
            "item-update"  => "UpdateData",
            "item-delete"  => "DeleteData",
            "condition"    => "Condition",
            "transform"    => "RunScript",
            "trigger"      => "SendACommand",
            "sleep"        => "RunScript",     // Anythink has no Delay action
            _              => "RunScript"
        };
}
