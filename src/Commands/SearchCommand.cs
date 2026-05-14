using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AnythinkCli.Commands;

// ── search query ─────────────────────────────────────────────────────────────

public class SearchQuerySettings : CommandSettings
{
    [CommandArgument(0, "<TEXT>")]
    [Description("Search text. Use '*' for wildcard / browse all.")]
    public string Text { get; set; } = "";

    [CommandOption("--entities <LIST>")]
    [Description("Comma-separated entity names to search. Default: all indexed entities.")]
    public string? Entities { get; set; }

    [CommandOption("--filter <EXPR>")]
    [Description("Filter expression, e.g. \"status=published AND category=news\". Supports _geoRadius and _geoBoundingBox.")]
    public string? Filter { get; set; }

    [CommandOption("--sort <LIST>")]
    [Description("Comma-separated sort fields, e.g. \"created_at:desc,id:asc\".")]
    public string? Sort { get; set; }

    [CommandOption("--facet <FIELDS>")]
    [Description("Comma-separated fields to compute facet aggregations on.")]
    public string? Facet { get; set; }

    [CommandOption("--highlight")]
    [Description("Enable result highlighting on matched terms.")]
    public bool Highlight { get; set; }

    [CommandOption("--page <N>")]
    [Description("Page number (default: 1)")]
    [DefaultValue(1)]
    public int Page { get; set; } = 1;

    [CommandOption("--limit <N>")]
    [Description("Results per page (1-100, default: 20)")]
    [DefaultValue(20)]
    public int Limit { get; set; } = 20;

    [CommandOption("--public")]
    [Description("Use the unauthenticated /search/public endpoint (returns only public-marked fields)")]
    public bool Public { get; set; }

    [CommandOption("--json")]
    [Description("Print the raw response JSON instead of the formatted summary")]
    public bool Json { get; set; }
}

public class SearchQueryCommand : BaseCommand<SearchQuerySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, SearchQuerySettings settings)
    {
        try
        {
            var client = GetClient();
            var qs = SearchQueryStringBuilder.Build(settings);

            SearchResult? result = null;
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Searching{(settings.Public ? " (public)" : "")}...", async _ =>
                {
                    result = await client.SearchAsync(qs, settings.Public);
                });

            if (settings.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(result,
                    new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }

            SearchResultRenderer.Render(result!, settings.Public, settings.Page);
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── search similar ───────────────────────────────────────────────────────────

public class SearchSimilarSettings : CommandSettings
{
    [CommandArgument(0, "<ENTITY>")]
    [Description("Entity name")]
    public string Entity { get; set; } = "";

    [CommandArgument(1, "<ID>")]
    [Description("Source document ID — find documents similar to this one")]
    public int Id { get; set; }

    [CommandOption("--limit <N>")]
    [Description("Maximum results (default: 10)")]
    [DefaultValue(10)]
    public int Limit { get; set; } = 10;

    [CommandOption("--public")]
    [Description("Use the public endpoint (only public-marked fields returned)")]
    public bool Public { get; set; }

    [CommandOption("--json")]
    [Description("Print raw JSON")]
    public bool Json { get; set; }
}

public class SearchSimilarCommand : BaseCommand<SearchSimilarSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, SearchSimilarSettings settings)
    {
        try
        {
            var client = GetClient();
            List<JsonObject> items = [];

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Finding documents similar to {settings.Entity}/{settings.Id}...", async _ =>
                {
                    items = await client.SearchSimilarAsync(settings.Entity, settings.Id, settings.Limit, settings.Public);
                });

            if (settings.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(items,
                    new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }

            Renderer.Header($"Similar to {settings.Entity}/{settings.Id} ({items.Count} results)");
            if (items.Count == 0)
            {
                Renderer.Info("No similar documents found.");
                return 0;
            }
            foreach (var item in items)
                SearchResultRenderer.RenderItem(item);
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── search rehydrate ─────────────────────────────────────────────────────────

public class SearchEntityOptionalSettings : CommandSettings
{
    [CommandArgument(0, "[ENTITY]")]
    [Description("Entity name. Omit to operate on the entire index.")]
    public string? Entity { get; set; }

    [CommandOption("-y|--yes")]
    [Description("Skip confirmation")]
    public bool Yes { get; set; }
}

public class SearchRehydrateCommand : BaseCommand<SearchEntityOptionalSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, SearchEntityOptionalSettings settings)
    {
        try
        {
            var scope = string.IsNullOrEmpty(settings.Entity) ? "entire index" : $"entity '{settings.Entity}'";

            if (!settings.Yes)
            {
                AnsiConsole.MarkupLine($"This will rehydrate the {scope} — re-pushing all matching records into the search engine.");
                AnsiConsole.MarkupLine("[dim]Existing entries are replaced; no data loss, but may take time on large entities.[/]");
                if (!AnsiConsole.Confirm("Proceed?", defaultValue: false))
                {
                    Renderer.Info("Cancelled.");
                    return 0;
                }
            }

            var client = GetClient();
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Triggering rehydrate for {scope}...", async _ =>
                {
                    await client.RehydrateSearchIndexAsync(settings.Entity);
                });

            Renderer.Success($"Rehydrate triggered for {scope}.");
            Renderer.Info("Indexing runs in the background. Watch progress in the dashboard or via 'search query' once complete.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── search purge ─────────────────────────────────────────────────────────────

public class SearchPurgeCommand : BaseCommand<SearchEntityOptionalSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, SearchEntityOptionalSettings settings)
    {
        try
        {
            var scope = string.IsNullOrEmpty(settings.Entity) ? "the entire index" : $"the index for '{settings.Entity}'";

            if (!settings.Yes)
            {
                AnsiConsole.MarkupLine($"[red]This will PURGE {scope}.[/] All search entries are deleted; you'll need to rehydrate to restore.");
                if (!AnsiConsole.Confirm("Proceed?", defaultValue: false))
                {
                    Renderer.Info("Cancelled.");
                    return 0;
                }
            }

            var client = GetClient();
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Purging {scope}...", async _ =>
                {
                    await client.PurgeSearchIndexAsync(settings.Entity);
                });

            Renderer.Success($"Purged {scope}.");
            Renderer.Info("Run 'search rehydrate' (with the same scope) to repopulate when you're ready.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── search audit ─────────────────────────────────────────────────────────────

public class SearchAuditSettings : CommandSettings
{
    [CommandArgument(0, "<ENTITY>")]
    [Description("Entity to audit")]
    public string Entity { get; set; } = "";

    [CommandOption("--query <TEXT>")]
    [Description("Search query to use for the audit (default: '*' — wildcard across all records)")]
    public string Query { get; set; } = "*";

    [CommandOption("--sample <N>")]
    [Description("How many records to inspect (default: 5)")]
    [DefaultValue(5)]
    public int Sample { get; set; } = 5;

    [CommandOption("--json")]
    [Description("Print structured audit result as JSON")]
    public bool Json { get; set; }
}

public class SearchAuditCommand : BaseCommand<SearchAuditSettings>
{
    // Fields the platform always injects into search results — not entity-defined,
    // so excluded from the "leak" check.
    private static readonly HashSet<string> InjectedFields = new(StringComparer.Ordinal)
    {
        "__entity", "__url", "id", "_geoPoint",
    };

    public override async Task<int> ExecuteAsync(CommandContext context, SearchAuditSettings settings)
    {
        try
        {
            var client = GetClient();
            Entity? entity = null;
            List<Field> fields = [];
            SearchResult? publicResult = null;
            SearchResult? authResult = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Auditing public search for '{settings.Entity}'...", async _ =>
                {
                    entity = await client.GetEntityAsync(settings.Entity);
                    fields = await client.GetFieldsAsync(settings.Entity);

                    var qs = $"q={Uri.EscapeDataString(settings.Query)}&e={Uri.EscapeDataString(settings.Entity)}&pageSize={settings.Sample}";
                    publicResult = await client.SearchAsync(qs, isPublic: true);
                    authResult = await client.SearchAsync(qs, isPublic: false);
                });

            // What's *configured* as searchable for end users.
            var entityIsPublic = entity!.IsPublic;
            var configuredPublicFields = fields
                .Where(f => f.PubliclySearchable)
                .Select(f => f.Name)
                .ToHashSet(StringComparer.Ordinal);

            // What public search *actually* returned.
            var actualPublicFields = (publicResult!.Items ?? [])
                .SelectMany(item => item.Select(kv => kv.Key))
                .Where(k => !InjectedFields.Contains(k))
                .ToHashSet(StringComparer.Ordinal);

            // What authenticated search returned (gives us the upper-bound field set).
            var actualAuthFields = (authResult!.Items ?? [])
                .SelectMany(item => item.Select(kv => kv.Key))
                .Where(k => !InjectedFields.Contains(k))
                .ToHashSet(StringComparer.Ordinal);

            // Leak: any field that appears in public results but isn't on the allowlist.
            var leakedFields = actualPublicFields
                .Where(f => !configuredPublicFields.Contains(f))
                .OrderBy(f => f)
                .ToList();

            // Configured-but-missing: fields marked publicly_searchable but not actually
            // present in results. Could be benign (records don't have that field set)
            // but worth showing.
            var configuredButAbsent = configuredPublicFields
                .Where(f => !actualPublicFields.Contains(f))
                .OrderBy(f => f)
                .ToList();

            if (settings.Json)
            {
                var report = new
                {
                    entity = settings.Entity,
                    entity_is_public = entityIsPublic,
                    public_search_returned_count = publicResult.Items?.Count ?? 0,
                    auth_search_returned_count = authResult.Items?.Count ?? 0,
                    configured_public_fields = configuredPublicFields.OrderBy(x => x).ToList(),
                    actual_public_fields = actualPublicFields.OrderBy(x => x).ToList(),
                    actual_auth_fields = actualAuthFields.OrderBy(x => x).ToList(),
                    leaked_fields = leakedFields,
                    configured_but_absent = configuredButAbsent,
                };
                Console.WriteLine(JsonSerializer.Serialize(report,
                    new JsonSerializerOptions { WriteIndented = true }));
                return leakedFields.Count > 0 ? 1 : 0;
            }

            Renderer.Header($"Public search audit: {settings.Entity}");

            var summary = Renderer.BuildTable("Property", "Value");
            Renderer.AddRow(summary, "Entity is_public", entityIsPublic ? "yes" : "no — public search will return nothing");
            Renderer.AddRow(summary, "Sample size", settings.Sample.ToString());
            Renderer.AddRow(summary, "Public results returned", (publicResult.Items?.Count ?? 0).ToString());
            Renderer.AddRow(summary, "Auth results returned", (authResult.Items?.Count ?? 0).ToString());
            Renderer.AddRow(summary, "Fields marked publicly_searchable", configuredPublicFields.Count > 0 ? string.Join(", ", configuredPublicFields.OrderBy(x => x)) : "(none)");
            AnsiConsole.Write(summary);

            if (!entityIsPublic)
            {
                Renderer.Info("This entity is not marked is_public — public search returns no records for it. No further audit possible.");
                return 0;
            }

            AnsiConsole.WriteLine();

            if (leakedFields.Count > 0)
            {
                AnsiConsole.MarkupLine($"[red]✗ LEAK DETECTED — {leakedFields.Count} field(s) returned in public search are not marked publicly_searchable:[/]");
                foreach (var f in leakedFields)
                    AnsiConsole.MarkupLine($"  [red]•[/] [bold]{Markup.Escape(f)}[/]");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("Fix by either:");
                AnsiConsole.MarkupLine("  [dim]• Marking these fields as publicly_searchable=true if they should be public[/]");
                AnsiConsole.MarkupLine("  [dim]• Or fixing the index/serialization so they aren't included[/]");
                return 1;
            }

            AnsiConsole.MarkupLine("[green]✓ No leaks: all fields returned by public search are configured as publicly_searchable.[/]");

            if (configuredButAbsent.Count > 0)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[yellow]Note:[/] these fields are marked publicly_searchable but didn't appear in the sample (records may not have values for them):");
                foreach (var f in configuredButAbsent)
                    AnsiConsole.MarkupLine($"  [yellow]•[/] {Markup.Escape(f)}");
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

// ── helpers ──────────────────────────────────────────────────────────────────

internal static class SearchQueryStringBuilder
{
    public static string Build(SearchQuerySettings s)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(s.Text))     parts.Add($"q={Uri.EscapeDataString(s.Text)}");
        if (!string.IsNullOrEmpty(s.Entities)) parts.Add($"e={Uri.EscapeDataString(s.Entities)}");
        if (!string.IsNullOrEmpty(s.Filter))   parts.Add($"fl={Uri.EscapeDataString(s.Filter)}");
        if (!string.IsNullOrEmpty(s.Sort))     parts.Add($"s={Uri.EscapeDataString(s.Sort)}");
        if (!string.IsNullOrEmpty(s.Facet))    parts.Add($"f={Uri.EscapeDataString(s.Facet)}");
        if (s.Highlight) parts.Add("hl=true");
        if (s.Page > 1)   parts.Add($"page={s.Page}");
        if (s.Limit != 20) parts.Add($"pageSize={s.Limit}");
        return string.Join("&", parts);
    }
}

internal static class SearchResultRenderer
{
    public static void Render(SearchResult result, bool isPublic, int currentPage)
    {
        var label = isPublic ? "Public search results" : "Search results";
        var time = result.RetrievalTime.HasValue ? $" in {result.RetrievalTime}ms" : "";
        Renderer.Header($"{label} ({result.TotalItems} matches{time})");

        if ((result.Items?.Count ?? 0) == 0)
        {
            Renderer.Info("No matches.");
            return;
        }

        foreach (var item in result.Items!)
            RenderItem(item);

        if (result.TotalPages > 1)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[dim]Page {currentPage}/{result.TotalPages} ({result.PageSize} per page). Use --page {currentPage + 1} for next.[/]");
        }

        if (result.FacetDistribution.HasValue && result.FacetDistribution.Value.ValueKind == JsonValueKind.Object)
        {
            AnsiConsole.WriteLine();
            Renderer.Header("Facets");
            foreach (var facet in result.FacetDistribution.Value.EnumerateObject())
            {
                var counts = new List<string>();
                if (facet.Value.ValueKind == JsonValueKind.Object)
                    foreach (var bucket in facet.Value.EnumerateObject())
                        counts.Add($"{bucket.Name} ({bucket.Value})");
                AnsiConsole.MarkupLine($"  [bold]{Markup.Escape(facet.Name)}:[/] {Markup.Escape(string.Join(", ", counts))}");
            }
        }
    }

    public static void RenderItem(JsonObject item)
    {
        var entity = item["__entity"]?.ToString() ?? "?";
        var id = item["id"]?.ToString() ?? "?";
        var label = PickLabel(item);

        AnsiConsole.MarkupLine($"[bold]{Markup.Escape(entity)}/{Markup.Escape(id)}[/] — {Markup.Escape(label)}");

        var meta = new List<string>();
        if (item["created_at"] is JsonNode created)
            meta.Add($"created {created}");
        if (item["__url"] is JsonNode url)
            meta.Add(url.ToString());
        if (meta.Count > 0)
            AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(string.Join(" · ", meta))}[/]");
        AnsiConsole.WriteLine();
    }

    // The platform injects __name as the resolved name-field value for each record.
    // Fall back to common label fields if it's missing.
    private static string PickLabel(JsonObject item)
    {
        if (item["__name"] is JsonNode injected && !string.IsNullOrEmpty(injected.ToString()))
            return injected.ToString();
        foreach (var k in new[] { "name", "title", "display_name", "headline", "subject", "label", "email" })
        {
            if (item[k] is JsonNode n && !string.IsNullOrEmpty(n.ToString()))
                return n.ToString();
        }
        return "(no name field)";
    }
}
