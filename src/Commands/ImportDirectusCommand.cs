using AnythinkCli.Client;
using AnythinkCli.Importers;
using AnythinkCli.Importers.Directus;
using AnythinkCli.Importers.Engine;
using Spectre.Console.Cli;
using Spectre.Console;
using System.ComponentModel;

namespace AnythinkCli.Commands;

public class ImportDirectusSettings : CommandSettings
{
    [CommandOption("--url <URL>")]
    [Description("Directus instance base URL (e.g. https://cms.example.com)")]
    public string? Url { get; set; }

    [CommandOption("--token <TOKEN>")]
    [Description("Directus static or admin token. Prefer the DIRECTUS_TOKEN environment variable: a flag lands in shell history and the process list")]
    public string? Token { get; set; }

    [CommandOption("--allow-insecure")]
    [Description("Allow a plain http:// Directus URL (localhost is always allowed)")]
    public bool AllowInsecure { get; set; }

    [CommandOption("--yes")]
    [Description("Apply without the confirmation prompt (required when not running interactively)")]
    public bool Yes { get; set; }

    [CommandOption("--to <PROFILE>")]
    [Description("Target Anythink project profile (defaults to the active profile)")]
    public string? To { get; set; }

    [CommandOption("--dry-run")]
    [Description("Preview what would be imported without making any changes")]
    public bool DryRun { get; set; }

    [CommandOption("--plan <FILE>")]
    [Description("Write the import plan to a file and stop. Makes no changes to the project")]
    public string? Plan { get; set; }

    [CommandOption("--force")]
    [Description("Overwrite the --plan file if it already exists")]
    public bool Force { get; set; }

    [CommandOption("--json")]
    [Description("Print the plan as JSON instead of text (with --dry-run or --plan)")]
    public bool Json { get; set; }

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
    public const string TokenEnvVar = "DIRECTUS_TOKEN";

    internal Func<string, string, IPlatformImporter>? ImporterFactory { get; set; }

    public override async Task<int> ExecuteAsync(CommandContext context, ImportDirectusSettings settings)
    {
        try
        {
            var url = Preflight(settings);
            var planOnly = settings.DryRun || settings.Plan is not null;
            var interactive = IsInteractive();
            RequireConfirmationMode(planOnly, settings.Yes, interactive);
            var token = ResolveToken(settings.Token, Environment.GetEnvironmentVariable(TokenEnvVar), interactive,
                () => AnsiConsole.Prompt(new TextPrompt<string>("Directus token:").Secret()));

            var importer = ImporterFactory?.Invoke(url, token) ?? new DirectusImporter(url, token);
            var target = settings.To is not null ? GetClientForProfile(settings.To) : GetClient();

            var runner = new ImportRunner(importer, target,
                new ImportOptions(
                    DryRun: planOnly,
                    IncludeFlows: settings.IncludeFlows,
                    IncludeData: settings.IncludeData,
                    IncludeFiles: settings.IncludeFiles,
                    IncludeRoles: settings.IncludeRoles,
                    RequireConfirmation: !planOnly && !settings.Yes,
                    PlanPath: settings.Plan,
                    ForcePlanFile: settings.Force,
                    PlanAsJson: settings.Json && planOnly));

            return ExitCodeFor(await runner.RunAsync());
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }

    internal static string Preflight(ImportDirectusSettings settings)
    {
        var url = ValidateUrl(settings.Url, settings.AllowInsecure);
        if (settings.Force && settings.Plan is null)
            throw new CliException("--force only applies to [bold #F97316]--plan <file>[/].");
        if (settings.Plan is not null) PlanFile.EnsureWritable(settings.Plan, settings.Force);
        return url;
    }

    internal static int ExitCodeFor(ImportResult result) => result.Errors.Count > 0 ? 1 : 0;

    internal static bool IsInteractive() => !Console.IsInputRedirected && AnsiConsole.Profile.Capabilities.Interactive;

    internal static string ValidateUrl(string? url, bool allowInsecure)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new CliException("--url is required. Example: [bold #F97316]--url https://cms.example.com[/]");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new CliException("--url must be an http:// or https:// URL.");
        if (uri.Scheme == Uri.UriSchemeHttp && !allowInsecure && !uri.IsLoopback)
            throw new CliException("Refusing a plain http:// URL — the token would travel unencrypted. Use https:// or pass [bold #F97316]--allow-insecure[/].");
        return url;
    }

    internal static string ResolveToken(string? option, string? envValue, bool interactive, Func<string> prompt)
    {
        if (!string.IsNullOrWhiteSpace(option)) return option;
        if (!string.IsNullOrWhiteSpace(envValue)) return envValue;
        if (interactive)
        {
            var typed = prompt();
            if (!string.IsNullOrWhiteSpace(typed)) return typed;
        }
        throw new CliException($"A Directus token is required. Set the [bold #F97316]{TokenEnvVar}[/] environment variable (preferred) or pass --token.");
    }

    internal static void RequireConfirmationMode(bool dryRun, bool yes, bool interactive)
    {
        if (!dryRun && !yes && !interactive)
            throw new CliException("Refusing to modify the project without confirmation while not running interactively. Re-run with [bold #F97316]--yes[/] to apply, or [bold #F97316]--dry-run[/] to preview.");
    }
}
