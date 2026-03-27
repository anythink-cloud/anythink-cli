using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AnythinkCli.Commands;

// ── data list ─────────────────────────────────────────────────────────────────

public class DataListSettings : CommandSettings
{
    [CommandArgument(0, "<ENTITY>")]
    [Description("Entity name")]
    public string Entity { get; set; } = "";

    [CommandOption("--page <N>")]
    [Description("Page number (default: 1)")]
    public int Page { get; set; } = 1;

    [CommandOption("--limit <N>")]
    [Description("Records per page (default: 20)")]
    public int Limit { get; set; } = 20;

    [CommandOption("--filter <JSON>")]
    [Description("Filter expression (JSON)")]
    public string? Filter { get; set; }

    [CommandOption("--json")]
    [Description("Output raw JSON instead of table")]
    public bool Json { get; set; }

    [CommandOption("--all")]
    [Description("Stream all pages to stdout as sequential JSON objects (requires --json). Each object contains one page of results.")]
    public bool All { get; set; }
}

public class DataListCommand : BaseCommand<DataListSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DataListSettings settings)
    {
        try
        {
            var client = GetClient();

            // --all streams pages as newline-delimited JSON — constant memory.
            if (settings.All)
            {
                if (!settings.Json)
                {
                    Renderer.Error("--all requires --json (tables can't stream). Use --page/--limit for table output.");
                    return 1;
                }

                var page = 1;
                var total = 0;
                while (true)
                {
                    var r = await client.ListItemsAsync(settings.Entity, page, settings.Limit, settings.Filter);
                    Console.WriteLine(JsonSerializer.Serialize(new
                    {
                        page,
                        limit = settings.Limit,
                        total_count = r.TotalCount,
                        has_next_page = r.HasNextPage,
                        items = r.Items
                    }, Renderer.PrettyJson));
                    total += r.Items.Count;
                    if (!r.HasNextPage || r.Items.Count == 0) break;
                    page++;
                }
                return 0;
            }

            if (settings.Json)
            {
                var r = await client.ListItemsAsync(settings.Entity, settings.Page, settings.Limit, settings.Filter);
                Console.WriteLine(JsonSerializer.Serialize(r.Items, Renderer.PrettyJson));
                return 0;
            }

            // Table output — single page only.
            var result = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Fetching {settings.Entity} items...", async _ =>
                    await client.ListItemsAsync(settings.Entity, settings.Page, settings.Limit, settings.Filter));

            var items = result.Items;
            var totalCount = result.TotalCount ?? items.Count;
            var totalPages = result.TotalPages ?? (int)Math.Ceiling((double)totalCount / settings.Limit);

            Renderer.Header($"{settings.Entity} — page {settings.Page}/{totalPages}, {items.Count}/{totalCount} records");

            if (items.Count == 0)
            {
                Renderer.Info("No records found.");
                return 0;
            }

            // Discover columns from first item
            var columns = items[0].Select(kv => kv.Key).ToList();

            var table = Renderer.BuildTable(columns.ToArray());
            foreach (var item in items)
            {
                var cells = columns.Select(c =>
                {
                    var val = item[c];
                    if (val == null) return "—";
                    var str = val.ToString();
                    return str.Length > 60 ? str[..57] + "..." : str;
                }).ToArray();
                Renderer.AddRow(table, cells);
            }

            AnsiConsole.Write(table);

            if (result.HasNextPage)
                Renderer.Info($"Showing page {settings.Page} of {totalPages}. Use --page N or --all --json to fetch more.");

            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── data get ──────────────────────────────────────────────────────────────────

public class DataGetSettings : CommandSettings
{
    [CommandArgument(0, "<ENTITY>")]
    [Description("Entity name")]
    public string Entity { get; set; } = "";

    [CommandArgument(1, "<ID>")]
    [Description("Record ID")]
    public int Id { get; set; }
}

public class DataGetCommand : BaseCommand<DataGetSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DataGetSettings settings)
    {
        try
        {
            var client = GetClient();
            JsonObject? item = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Fetching {settings.Entity}/{settings.Id}...", async _ =>
                {
                    item = await client.GetItemAsync(settings.Entity, settings.Id);
                });

            Renderer.Header($"{settings.Entity} / {settings.Id}");
            Renderer.PrintJsonObject(item);
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── data create ───────────────────────────────────────────────────────────────

public class DataCreateSettings : CommandSettings
{
    [CommandArgument(0, "<ENTITY>")]
    [Description("Entity name")]
    public string Entity { get; set; } = "";

    [CommandOption("--data <JSON>")]
    [Description("JSON object with field values, e.g. '{\"name\":\"Alice\",\"age\":30}'")]
    public string? Data { get; set; }
}

public class DataCreateCommand : BaseCommand<DataCreateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DataCreateSettings settings)
    {
        var rawData = settings.Data;
        if (string.IsNullOrEmpty(rawData))
            rawData = AnsiConsole.Ask<string>("[#F97316]JSON data:[/]");

        JsonObject data;
        try { data = JsonSerializer.Deserialize<JsonObject>(rawData)!; }
        catch { Renderer.Error("Invalid JSON."); return 1; }

        try
        {
            var client = GetClient();
            JsonObject? created = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Creating {settings.Entity} record...", async _ =>
                {
                    created = await client.CreateItemAsync(settings.Entity, data);
                });

            var id = created?["id"]?.ToString() ?? "?";
            Renderer.Success($"Record created in [#F97316]{Markup.Escape(settings.Entity)}[/] (id: {Markup.Escape($"{id}")}).");
            Renderer.PrintJsonObject(created);
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── data update ───────────────────────────────────────────────────────────────

public class DataUpdateSettings : CommandSettings
{
    [CommandArgument(0, "<ENTITY>")]
    [Description("Entity name")]
    public string Entity { get; set; } = "";

    [CommandArgument(1, "<ID>")]
    [Description("Record ID")]
    public int Id { get; set; }

    [CommandOption("--data <JSON>")]
    [Description("JSON object with fields to update")]
    public string? Data { get; set; }
}

public class DataUpdateCommand : BaseCommand<DataUpdateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DataUpdateSettings settings)
    {
        var rawData = settings.Data;
        if (string.IsNullOrEmpty(rawData))
            rawData = AnsiConsole.Ask<string>("[#F97316]JSON data:[/]");

        JsonObject data;
        try { data = JsonSerializer.Deserialize<JsonObject>(rawData)!; }
        catch { Renderer.Error("Invalid JSON."); return 1; }

        try
        {
            var client = GetClient();
            JsonObject? updated = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Updating {settings.Entity}/{settings.Id}...", async _ =>
                {
                    updated = await client.UpdateItemAsync(settings.Entity, settings.Id, data);
                });

            Renderer.Success($"Record [#F97316]{Markup.Escape(settings.Entity)}/{Markup.Escape(settings.Id.ToString())}[/] updated.");
            if (updated is not null) Renderer.PrintJsonObject(updated);
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── data delete ───────────────────────────────────────────────────────────────

public class DataDeleteSettings : CommandSettings
{
    [CommandArgument(0, "<ENTITY>")]
    [Description("Entity name")]
    public string Entity { get; set; } = "";

    [CommandArgument(1, "<ID>")]
    [Description("Record ID")]
    public int Id { get; set; }

    [CommandOption("-y|--yes")]
    [Description("Skip confirmation")]
    public bool Yes { get; set; }
}

public class DataDeleteCommand : BaseCommand<DataDeleteSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DataDeleteSettings settings)
    {
        if (!settings.Yes)
        {
            var confirm = AnsiConsole.Confirm(
                $"[yellow]Delete[/] [bold red]{settings.Entity}/{settings.Id}[/][yellow]?[/]",
                defaultValue: false);
            if (!confirm) { Renderer.Info("Cancelled."); return 0; }
        }

        try
        {
            var client = GetClient();

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Deleting record...", async _ =>
                {
                    await client.DeleteItemAsync(settings.Entity, settings.Id);
                });

            Renderer.Success($"Record [#F97316]{Markup.Escape(settings.Entity)}/{Markup.Escape(settings.Id.ToString())}[/] deleted.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── data rls ─────────────────────────────────────────────────────────────────

public class DataRlsSettings : CommandSettings
{
    [CommandArgument(0, "<ENTITY>")]
    [Description("Entity name")]
    public string Entity { get; set; } = "";

    [CommandArgument(1, "<ID>")]
    [Description("Record ID")]
    public int Id { get; set; }

    [CommandOption("--user <USER_ID>")]
    [Description("User ID to grant access to")]
    public int? UserId { get; set; }

    [CommandOption("--readonly")]
    [Description("Grant read-only access (default: full access)")]
    public bool ReadOnly { get; set; }
}

public class DataRlsCommand : BaseCommand<DataRlsSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DataRlsSettings settings)
    {
        try
        {
            var client = GetClient();

            if (settings.UserId == null)
            {
                // List current RLS users
                string? raw = null;
                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync("Fetching RLS users...", async _ =>
                    {
                        raw = await client.FetchRawAsync(
                            $"{client.BaseUrl}/org/{client.OrgId}/entities/{settings.Entity}/items/{settings.Id}/rls-users");
                    });

                var users = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(raw!);
                if (users.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var table = Renderer.BuildTable("User ID", "Name", "Read Only");
                    foreach (var u in users.EnumerateArray())
                    {
                        var uid = u.TryGetProperty("user_id", out var uidProp) ? uidProp.ToString() : "?";
                        var name = u.TryGetProperty("user_name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                        var ro = u.TryGetProperty("readonly", out var roProp) ? (roProp.GetBoolean() ? "yes" : "no") : "?";
                        Renderer.AddRow(table, uid, name, ro);
                    }
                    Renderer.Header($"RLS Users for {settings.Entity}/{settings.Id}");
                    AnsiConsole.Write(table);
                }
                else
                {
                    AnsiConsole.WriteLine(raw!);
                }
            }
            else
            {
                // Set RLS user
                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync($"Setting RLS access for user {settings.UserId}...", async _ =>
                    {
                        var body = $"{{\"user_id\":{settings.UserId},\"readonly\":{settings.ReadOnly.ToString().ToLower()}}}";
                        await client.FetchRawAsync(
                            $"{client.BaseUrl}/org/{client.OrgId}/entities/{settings.Entity}/items/{settings.Id}/rls-users",
                            "PUT", body);
                    });

                Renderer.Success($"RLS access set for user {settings.UserId} on {settings.Entity}/{settings.Id} (readonly: {settings.ReadOnly}).");
            }

            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}
