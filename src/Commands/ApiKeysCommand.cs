using AnythinkCli.Config;
using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace AnythinkCli.Commands;

// ── api-keys list ────────────────────────────────────────────────────────────

public class ApiKeysListCommand : BaseCommand<EmptySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EmptySettings settings)
    {
        try
        {
            var client = GetClient();
            List<ApiKeyResponse> keys = [];

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching API keys...", async _ =>
                {
                    keys = await client.GetApiKeysAsync();
                });

            if (keys.Count == 0)
            {
                Renderer.Info("No API keys found.");
                return 0;
            }

            Renderer.Header($"API Keys ({keys.Count})");

            var table = Renderer.BuildTable("ID", "Name", "Permissions", "Expires", "Status");
            foreach (var k in keys.OrderBy(x => x.Id))
            {
                var status = k.Revoked
                    ? "revoked"
                    : k.ExpiresAt < DateTime.UtcNow ? "expired" : "active";
                Renderer.AddRow(table,
                    k.Id.ToString(),
                    Markup.Escape(k.Name),
                    k.Permissions.Count.ToString(),
                    k.ExpiresAt.ToString("yyyy-MM-dd"),
                    status
                );
            }
            AnsiConsole.Write(table);
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── api-keys create ──────────────────────────────────────────────────────────

public class ApiKeyCreateSettings : CommandSettings
{
    [CommandArgument(0, "<NAME>")]
    [Description("Name for the API key (e.g. \"github-actions\", \"backup-script\")")]
    public string Name { get; set; } = "";

    [CommandOption("--permissions <LIST>")]
    [Description("Comma-separated permission names, e.g. \"data:read,data:create\"")]
    public string? Permissions { get; set; }

    [CommandOption("--expires-in <DAYS>")]
    [Description("Days until the key expires (default: 90, max: 365 unless --no-expiry-cap)")]
    [DefaultValue(90)]
    public int ExpiresInDays { get; set; } = 90;

    [CommandOption("--no-expiry-cap")]
    [Description("Allow --expires-in greater than 365 days (use sparingly)")]
    public bool NoExpiryCap { get; set; }

    [CommandOption("--save-as <PROFILE>")]
    [Description("Save the new key directly into a CLI profile instead of printing it")]
    public string? SaveAs { get; set; }

    [CommandOption("--json")]
    [Description("Print the response as JSON to stdout (logs the key — handle carefully)")]
    public bool Json { get; set; }

    [CommandOption("-y|--yes")]
    [Description("Skip the confirmation prompt")]
    public bool Yes { get; set; }
}

public class ApiKeysCreateCommand : BaseCommand<ApiKeyCreateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ApiKeyCreateSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Permissions))
        {
            Renderer.Error("--permissions is required. Use 'anythink fetch /permissions' to list available permission names.");
            return 1;
        }

        if (settings.ExpiresInDays < 1)
        {
            Renderer.Error("--expires-in must be at least 1 day.");
            return 1;
        }
        if (settings.ExpiresInDays > 365 && !settings.NoExpiryCap)
        {
            Renderer.Error("--expires-in is capped at 365 days. Use --no-expiry-cap to override.");
            return 1;
        }

        try
        {
            var client = GetClient();

            var requested = settings.Permissions
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            List<Permission> available = [];
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Resolving permissions...", async _ =>
                {
                    available = await client.GetPermissionsAsync();
                });

            var byName = available
                .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var resolved = new List<Permission>();
            var missing = new List<string>();
            foreach (var name in requested)
            {
                if (byName.TryGetValue(name, out var p)) resolved.Add(p);
                else missing.Add(name);
            }

            if (missing.Count > 0)
            {
                Renderer.Error($"Unknown permission(s): {string.Join(", ", missing)}");
                Renderer.Info("Run 'anythink fetch /permissions' to see what's available for your user.");
                return 1;
            }

            if (!settings.Yes)
            {
                Renderer.Header($"Create API key: {Markup.Escape(settings.Name)}");
                var summary = Renderer.BuildTable("Property", "Value");
                Renderer.AddRow(summary, "Expires in", $"{settings.ExpiresInDays} days");
                Renderer.AddRow(summary, "Permissions", string.Join(", ", resolved.Select(p => p.Name)));
                if (!string.IsNullOrEmpty(settings.SaveAs))
                    Renderer.AddRow(summary, "Save as profile", settings.SaveAs);
                AnsiConsole.Write(summary);

                if (!AnsiConsole.Confirm("Proceed?"))
                {
                    Renderer.Info("Cancelled.");
                    return 0;
                }
            }

            ApiKeyResponse? created = null;
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Creating API key '{settings.Name}'...", async _ =>
                {
                    created = await client.CreateApiKeyAsync(new CreateApiKeyRequest(
                        settings.Name,
                        settings.ExpiresInDays,
                        resolved.Select(p => p.Id).ToList()
                    ));
                });

            if (created is null)
            {
                Renderer.Error("Failed to create API key (no response).");
                return 1;
            }

            // Server silently drops permissions the user lacks. Detect and warn.
            var grantedIds = created.Permissions.Select(p => p.Id).ToHashSet();
            var dropped = resolved.Where(p => !grantedIds.Contains(p.Id)).Select(p => p.Name).ToList();
            if (dropped.Count > 0)
            {
                Renderer.Error($"Server dropped {dropped.Count} permission(s) the current user does not hold: {string.Join(", ", dropped)}");
                Renderer.Info("The key was created but won't have those permissions. Consider revoking it and asking an admin for the role you need.");
            }

            // Save to profile if requested — never echo the key.
            if (!string.IsNullOrEmpty(settings.SaveAs))
            {
                var active = ConfigService.GetActiveProfile()
                             ?? throw new Exception("No active profile to clone instance URL from.");

                ConfigService.SaveProfile(settings.SaveAs, new Config.Profile
                {
                    OrgId = active.OrgId,
                    InstanceApiUrl = active.InstanceApiUrl,
                    ApiKey = created.Key,
                    Alias = settings.SaveAs,
                });

                Renderer.Success($"Created API key '{Markup.Escape(created.Name)}' (id: {created.Id}) and saved as profile '{Markup.Escape(settings.SaveAs)}'.");
                Renderer.Info("Use it with: anythink --profile " + settings.SaveAs + " <command>");
                return 0;
            }

            if (settings.Json)
            {
                // Stdout for piping; the key is in this output.
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(created, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                Console.Error.WriteLine();
                Console.Error.WriteLine("Warning: API key is in the JSON above. Pipe to a secret store, do not commit or log.");
                return 0;
            }

            // Default: success summary on stdout, raw key on stderr so it's easy to capture
            // with `2> key.txt` while the success message stays in `> log.txt`.
            Renderer.Success($"Created API key '{Markup.Escape(created.Name)}' (id: {created.Id}) — expires {created.ExpiresAt:yyyy-MM-dd}.");
            AnsiConsole.MarkupLine("[bold yellow]Save this key now — it will not be shown again.[/]");
            Console.Error.WriteLine();
            Console.Error.WriteLine(created.Key);
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── api-keys revoke ──────────────────────────────────────────────────────────

public class ApiKeyRevokeSettings : CommandSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("API key ID to revoke")]
    public int Id { get; set; }

    [CommandOption("-y|--yes")]
    [Description("Skip the confirmation prompt")]
    public bool Yes { get; set; }
}

public class ApiKeysRevokeCommand : BaseCommand<ApiKeyRevokeSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ApiKeyRevokeSettings settings)
    {
        try
        {
            var client = GetClient();

            if (!settings.Yes)
            {
                List<ApiKeyResponse> keys = [];
                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync("Fetching key details...", async _ => keys = await client.GetApiKeysAsync());

                var key = keys.FirstOrDefault(k => k.Id == settings.Id);
                if (key is null)
                {
                    Renderer.Error($"API key {settings.Id} not found.");
                    return 1;
                }
                if (key.Revoked)
                {
                    Renderer.Info($"API key {settings.Id} ('{key.Name}') is already revoked.");
                    return 0;
                }

                Renderer.Header($"Revoke API key {settings.Id}");
                var summary = Renderer.BuildTable("Property", "Value");
                Renderer.AddRow(summary, "Name", Markup.Escape(key.Name));
                Renderer.AddRow(summary, "Permissions", key.Permissions.Count.ToString());
                Renderer.AddRow(summary, "Expires", key.ExpiresAt.ToString("yyyy-MM-dd"));
                AnsiConsole.Write(summary);

                if (!AnsiConsole.Confirm("[red]Revoke this key?[/] This is irreversible."))
                {
                    Renderer.Info("Cancelled.");
                    return 0;
                }
            }

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Revoking API key {settings.Id}...", async _ =>
                {
                    await client.RevokeApiKeyAsync(settings.Id);
                });

            Renderer.Success($"API key {settings.Id} revoked.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}
