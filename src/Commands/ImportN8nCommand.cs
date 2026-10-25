using AnythinkCli.Client;
using AnythinkCli.Importers;
using Anythink.ProjectImporters;
using Anythink.ProjectImporters.Engine;
using Anythink.ProjectImporters.N8n;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace AnythinkCli.Commands;

public class ImportN8nSettings : CommandSettings
{
    [CommandOption("--url <URL>")]
    [Description("n8n instance base URL (e.g. https://n8n.example.com)")]
    public string? Url { get; set; }

    [CommandOption("--token <TOKEN>")]
    [Description("n8n API key. Prefer the N8N_API_KEY environment variable: a flag lands in shell history and the process list")]
    public string? Token { get; set; }

    [CommandOption("--allow-insecure")]
    [Description("Allow a plain http:// n8n URL (localhost is always allowed)")]
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
}

[CommandGuide("Copies an n8n instance's workflows into the active project as disabled workflows with disabled triggers; nodes with no Anythink equivalent become disabled placeholder steps that stop the run, and credentials are never imported (the plan lists the connections to set up). Call it with dry_run first and show the user the plan, because a call without dry_run applies the import straight away.")]
public class ImportN8nCommand : BaseCommand<ImportN8nSettings>
{
    public const string TokenEnvVar = "N8N_API_KEY";

    internal Func<string, string, IPlatformImporter>? ImporterFactory { get; set; }

    public override async Task<int> ExecuteAsync(CommandContext context, ImportN8nSettings settings)
    {
        try
        {
            var url = ImportDirectusCommand.ValidateUrl(settings.Url, settings.AllowInsecure);
            if (settings.Force && settings.Plan is null)
                throw new CliException("--force only applies to [bold #F97316]--plan <file>[/].");
            if (settings.Plan is not null) PlanFile.EnsureWritable(settings.Plan, settings.Force);

            var planOnly = settings.DryRun || settings.Plan is not null;
            var interactive = ImportDirectusCommand.IsInteractive();
            ImportDirectusCommand.RequireConfirmationMode(planOnly, settings.Yes, interactive);
            var token = ImportDirectusCommand.ResolveToken(settings.Token, Environment.GetEnvironmentVariable(TokenEnvVar), interactive,
                () => AnsiConsole.Prompt(new TextPrompt<string>("n8n API key:").Secret()), "n8n", TokenEnvVar);

            var importer = ImporterFactory?.Invoke(url, token) ?? new N8nImporter(url, token);
            var target = settings.To is not null ? GetClientForProfile(settings.To) : GetClient();

            var runner = new ImportRunner(importer, target,
                new ImportOptions(
                    DryRun: planOnly,
                    IncludeFlows: true,
                    RequireConfirmation: !planOnly && !settings.Yes,
                    PlanPath: settings.Plan,
                    ForcePlanFile: settings.Force,
                    PlanAsJson: settings.Json && planOnly),
                new SpectreImportReporter());

            return ImportDirectusCommand.ExitCodeFor(await runner.RunAsync());
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}
