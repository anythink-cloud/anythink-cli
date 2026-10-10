using AnythinkCli.Client;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Text.Json;

namespace AnythinkCli.Commands;

public class FetchSettings : CommandSettings
{
    [CommandArgument(0, "<path>")]
    [Description("API path (e.g. /integrations/definitions/slack)")]
    public string Path { get; set; } = null!;

    [CommandOption("--method <METHOD>")]
    [Description("HTTP method (default: GET)")]
    [DefaultValue("GET")]
    public string Method { get; set; } = "GET";

    [CommandOption("--body <JSON>")]
    [Description("Request body (JSON string)")]
    public string? Body { get; set; }

    [CommandOption("--all")]
    [Description("Follow every page of a paged GET response, printing each page as it arrives")]
    public bool All { get; set; }
}

public class FetchCommand : BaseCommand<FetchSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, FetchSettings settings)
    {
        try
        {
            var client = GetClient();
            var path = settings.Path.StartsWith("/") ? settings.Path : "/" + settings.Path;
            var url = $"{client.BaseUrl}/org/{client.OrgId}{path}";
            var get = settings.Method.Equals("GET", StringComparison.OrdinalIgnoreCase);

            if (settings.All && !get)
            {
                Renderer.Error("--all only works with GET.");
                return 1;
            }

            if (get && FetchPaging.IsOversized(url))
            {
                url = FetchPaging.CapPageSize(url);
                Renderer.Warn($"pageSize capped at {FetchPaging.MaxPageSize}. Use --all to fetch every page.");
            }

            Renderer.Info($"[bold]{settings.Method}[/] {url}");

            if (settings.All)
                await foreach (var page in client.FetchPagesAsync(url))
                    Print(page);
            else
                Print(await client.FetchRawAsync(url, settings.Method, settings.Body));

            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }

    private static void Print(string result)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<JsonElement>(result);
            Renderer.PrintJson(JsonSerializer.Serialize(parsed, Renderer.PrettyJson));
        }
        catch
        {
            AnsiConsole.WriteLine(result);
        }
    }
}
