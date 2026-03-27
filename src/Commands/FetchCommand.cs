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

            Renderer.Info($"[bold]{settings.Method}[/] {url}");

            var result = await client.FetchRawAsync(url, settings.Method, settings.Body);

            // Try to pretty-print as JSON
            try
            {
                var parsed = JsonSerializer.Deserialize<JsonElement>(result);
                var pretty = JsonSerializer.Serialize(parsed, Renderer.PrettyJson);
                Renderer.PrintJson(pretty);
            }
            catch
            {
                AnsiConsole.WriteLine(result);
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
