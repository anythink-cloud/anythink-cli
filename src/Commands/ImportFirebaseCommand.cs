using AnythinkCli.Client;
using AnythinkCli.Importers;
using Anythink.ProjectImporters;
using Anythink.ProjectImporters.Engine;
using Anythink.ProjectImporters.Firebase;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace AnythinkCli.Commands;

public class ImportFirebaseSettings : CommandSettings
{
    [CommandOption("--key <FILE>")]
    [Description("Path to a Firebase service account key (JSON), defaulting to GOOGLE_APPLICATION_CREDENTIALS. Create one with only the Cloud Datastore Viewer, Storage Object Viewer and Firebase Authentication Viewer roles. The key is only read, never printed or saved")]
    public string? Key { get; set; }

    [CommandOption("--bucket <NAME>")]
    [Description("Cloud Storage bucket to copy files from (defaults to the project's default bucket)")]
    public string? Bucket { get; set; }

    [CommandOption("--sample-size <COUNT>")]
    [Description("Documents sampled per collection to work out its fields (default 100). Fields only seen outside the sample are kept in a firebase_extra json field")]
    public int SampleSize { get; set; } = DefaultSampleSize;

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

    [CommandOption("--include-data")]
    [Description("Also import documents from each top-level collection")]
    public bool IncludeData { get; set; }

    [CommandOption("--include-files")]
    [Description("Also copy files from Cloud Storage into Anythink (runs before --include-data)")]
    public bool IncludeFiles { get; set; }

    internal const int DefaultSampleSize = 100;
}

[CommandGuide("Copies a Firebase project's top-level Firestore collections into the active project as entities and fields, working the fields out from a sample of documents; documents and Cloud Storage files come across only with include_data and include_files. Authentication users, subcollections, Security Rules and Stripe extension data are listed in the plan as not brought across. Call it with dry_run first and show the user the plan, because a call without dry_run applies the import straight away.")]
public class ImportFirebaseCommand : BaseCommand<ImportFirebaseSettings>
{
    public const string KeyEnvVar = "GOOGLE_APPLICATION_CREDENTIALS";

    internal Func<string, FirebaseImportOptions, IPlatformImporter>? ImporterFactory { get; set; }

    public override async Task<int> ExecuteAsync(CommandContext context, ImportFirebaseSettings settings)
    {
        try
        {
            if (settings.SampleSize < 1)
                throw new CliException("--sample-size must be at least 1.");
            if (settings.Force && settings.Plan is null)
                throw new CliException("--force only applies to [bold #F97316]--plan <file>[/].");
            if (settings.Plan is not null) PlanFile.EnsureWritable(settings.Plan, settings.Force);

            var planOnly = settings.DryRun || settings.Plan is not null;
            ImportCommandSupport.RequireConfirmationMode(planOnly, settings.Yes, ImportCommandSupport.IsInteractive());

            var keyJson = ReadKey(settings.Key, Environment.GetEnvironmentVariable(KeyEnvVar));
            var options = new FirebaseImportOptions(settings.SampleSize, settings.Bucket);
            var importer = ImporterFactory?.Invoke(keyJson, options) ?? new FirebaseImporter(FirebaseServiceAccountKey.Parse(keyJson), options);
            var target = settings.To is not null ? GetClientForProfile(settings.To) : GetClient();

            var runner = new ImportRunner(importer, target,
                new ImportOptions(
                    DryRun: planOnly,
                    IncludeFlows: false,
                    IncludeData: settings.IncludeData,
                    IncludeFiles: settings.IncludeFiles,
                    RequireConfirmation: !planOnly && !settings.Yes,
                    PlanPath: settings.Plan,
                    ForcePlanFile: settings.Force,
                    PlanAsJson: settings.Json && planOnly),
                new SpectreImportReporter());

            return ImportCommandSupport.ExitCodeFor(await runner.RunAsync());
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }

    internal static string ReadKey(string? option, string? envValue)
    {
        var path = !string.IsNullOrWhiteSpace(option) ? option : envValue;
        if (string.IsNullOrWhiteSpace(path))
            throw new CliException($"A service account key file is required. Pass [bold #F97316]--key <file.json>[/] or set [bold #F97316]{KeyEnvVar}[/].");
        try { return File.ReadAllText(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CliException($"Couldn't read the key file at {Spectre.Console.Markup.Escape(path)}.");
        }
    }
}
