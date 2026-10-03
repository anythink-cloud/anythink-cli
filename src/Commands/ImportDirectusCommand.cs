using AnythinkCli.Client;
using AnythinkCli.Importers;
using AnythinkCli.Importers.Directus;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace AnythinkCli.Commands;

// ── anythink import directus ──────────────────────────────────────────────────
//
//  Imports collections + fields from a Directus instance into an Anythink
//  project. Optionally also imports flows as Anythink workflows. Schema only —
//  data migration is a separate step.
//
//  Usage:
//    anythink import directus --url https://cms.example.com --token <token>
//    anythink import directus --url https://cms.example.com --token <token> --dry-run
//    anythink import directus --url https://cms.example.com --token <token> --include-flows
// ─────────────────────────────────────────────────────────────────────────────

public class ImportDirectusSettings : CommandSettings
{
    [CommandOption("--url <URL>")]
    [Description("Directus instance base URL (e.g. https://cms.example.com)")]
    public string? Url { get; set; }

    [CommandOption("--token <TOKEN>")]
    [Description("Directus static token or admin token")]
    public string? Token { get; set; }

    [CommandOption("--to <PROFILE>")]
    [Description("Target Anythink project profile (defaults to the active profile)")]
    public string? To { get; set; }

    [CommandOption("--dry-run")]
    [Description("Preview what would be imported without making any changes")]
    public bool DryRun { get; set; }

    [CommandOption("--include-flows")]
    [Description("Also import Directus flows as Anythink workflows")]
    public bool IncludeFlows { get; set; }

    [CommandOption("--include-data")]
    [Description("Also import records from each collection (schema must already match)")]
    public bool IncludeData { get; set; }

    [CommandOption("--include-files")]
    [Description("Also download files from Directus and re-upload to Anythink (runs before --include-data)")]
    public bool IncludeFiles { get; set; }

    [CommandOption("--include-roles")]
    [Description("Also import Directus roles + permissions, and promote collections to is_public when the Public policy can read them")]
    public bool IncludeRoles { get; set; }
}

public class ImportDirectusCommand : BaseCommand<ImportDirectusSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ImportDirectusSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Url))
            throw new CliException("--url is required. Example: [bold #F97316]--url https://cms.example.com[/]");
        if (!Uri.TryCreate(settings.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new CliException("--url must be an http:// or https:// URL.");
        if (string.IsNullOrWhiteSpace(settings.Token))
            throw new CliException("--token is required. Provide a Directus static or admin token.");

        try
        {
            var importer = new DirectusImporter(settings.Url, settings.Token);
            var target   = settings.To is not null ? GetClientForProfile(settings.To) : GetClient();

            var runner = new ImportRunner(importer, target,
                new ImportOptions(
                    DryRun:       settings.DryRun,
                    IncludeFlows: settings.IncludeFlows,
                    IncludeData:  settings.IncludeData,
                    IncludeFiles: settings.IncludeFiles,
                    IncludeRoles: settings.IncludeRoles));

            var result = await runner.RunAsync();
            return result.Errors.Count > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}
