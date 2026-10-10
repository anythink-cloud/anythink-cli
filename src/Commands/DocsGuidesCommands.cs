using AnythinkCli.Client;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace AnythinkCli.Commands;

public class DocsSearchSettings : CommandSettings
{
    [CommandOption("--json")]
    [Description("Output as machine-readable JSON")]
    public bool Json { get; set; }

    [CommandArgument(0, "<QUERY>")]
    [Description("What to look for in the guides, e.g. \"row-level security\"")]
    public string Query { get; set; } = "";

    [CommandOption("--limit <N>")]
    [Description("Most guides to return (1-50, default: 10)")]
    [DefaultValue(10)]
    public int Limit { get; set; } = 10;

    public override ValidationResult Validate() =>
        string.IsNullOrWhiteSpace(Query) ? ValidationResult.Error("Give something to search for.")
        : Limit is < 1 or > 50 ? ValidationResult.Error("--limit must be between 1 and 50.")
        : ValidationResult.Success();
}

public class DocsReadSettings : CommandSettings
{
    [CommandOption("--json")]
    [Description("Output as machine-readable JSON")]
    public bool Json { get; set; }

    [CommandArgument(0, "<SLUG_OR_URL>")]
    [Description("The guide to read: a path from 'docs search' (e.g. /guides/workflows/how-workflows-work), its last part, or a full docs.anythink.cloud URL")]
    public string SlugOrUrl { get; set; } = "";
}

public class DocsSearchCommand : BaseCommand<DocsSearchSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DocsSearchSettings settings)
    {
        try
        {
            var result = await DocsSiteClient.Current.SearchAsync(settings.Query.Trim(), settings.Limit, ClientContext.Cancellation);

            if (settings.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(result, DocsJson.Options));
                return 0;
            }

            if (result.Results.Count == 0)
            {
                Renderer.Info($"No guides match '{Markup.Escape(settings.Query)}'.");
                return 0;
            }

            // Plain writes, as the console would otherwise wrap URLs mid-way when output is piped
            foreach (var hit in result.Results)
            {
                Console.WriteLine(hit.Title);
                Console.WriteLine($"  {hit.Url}");
                if (hit.Snippet.Length > 0)
                    Console.WriteLine($"  {hit.Snippet}");
                Console.WriteLine();
            }
            Console.WriteLine("Read one with: anythink docs read <path or URL>");
            return 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Renderer.Error($"Couldn't search the docs: {ex.Message}");
            return 1;
        }
    }
}

public class DocsReadCommand : BaseCommand<DocsReadSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DocsReadSettings settings)
    {
        try
        {
            var page = await DocsSiteClient.Current.ReadAsync(settings.SlugOrUrl, ClientContext.Cancellation);

            if (settings.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(page, DocsJson.Options));
                return 0;
            }

            Console.WriteLine(page.Markdown);
            Console.WriteLine();
            Console.WriteLine($"Source: {page.Url}");
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or DocsPageNotFoundException)
        {
            Renderer.Error(ex.Message);
            return 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Renderer.Error($"Couldn't read the docs: {ex.Message}");
            return 1;
        }
    }
}

internal static class DocsJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
