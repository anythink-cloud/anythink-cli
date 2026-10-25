using AnythinkCli.Client;
using AnythinkCli.Importers;
using Anythink.ProjectImporters;
using Anythink.ProjectImporters.Engine;
using Anythink.ProjectImporters.Supabase;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace AnythinkCli.Commands;

public class ImportSupabaseSettings : CommandSettings
{
    [CommandOption("--db <CONNECTION_STRING>")]
    [Description("Postgres connection string of the Supabase project (the postgres:// URI from Connect, password URL-encoded). Prefer the SUPABASE_DB_URL environment variable: a flag lands in shell history and the process list. The role must be able to read every table and bypass row-level security (the postgres role, or a role with BYPASSRLS), otherwise the read fails rather than returning filtered rows. Every transaction is opened read-only and only SELECT statements are sent")]
    public string? Db { get; set; }

    [CommandOption("--url <URL>")]
    [Description("Supabase project URL (e.g. https://abcd.supabase.co). Only needed with --include-files")]
    public string? Url { get; set; }

    [CommandOption("--service-key <KEY>")]
    [Description("Supabase service role key, used only to download Storage files with --include-files. Prefer the SUPABASE_SERVICE_KEY environment variable")]
    public string? ServiceKey { get; set; }

    [CommandOption("--schemas <NAMES>")]
    [Description("Comma-separated schemas to import tables from (default public). Supabase's own schemas are never imported; tables outside the first schema are prefixed with their schema name")]
    public string? Schemas { get; set; }

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
    [Description("Also import the rows of each table (tables without a primary key are schema only)")]
    public bool IncludeData { get; set; }

    [CommandOption("--include-files")]
    [Description("Also copy Storage files into Anythink as private files (needs --url and a service key; runs before --include-data)")]
    public bool IncludeFiles { get; set; }
}

[CommandGuide("Copies a Supabase project's tables and columns into the active project as entities and fields; rows and Storage files come across only with include_data and include_files. Only reads Supabase, using the connection string in the SUPABASE_DB_URL environment variable (and SUPABASE_SERVICE_KEY with include_files), which are never passed in a call. Row-level security policies, auth users, views, functions, triggers, extensions, cron jobs and realtime publications are listed in the plan as not brought across. Call it with dry_run first and show the user the plan, because a call without dry_run applies the import straight away.")]
public class ImportSupabaseCommand : BaseCommand<ImportSupabaseSettings>
{
    public const string DbEnvVar = "SUPABASE_DB_URL";
    public const string ServiceKeyEnvVar = "SUPABASE_SERVICE_KEY";

    internal Func<string, string?, string?, SupabaseImportOptions, IPlatformImporter>? ImporterFactory { get; set; }

    public override async Task<int> ExecuteAsync(CommandContext context, ImportSupabaseSettings settings)
    {
        try
        {
            if (settings.Force && settings.Plan is null)
                throw new CliException("--force only applies to [bold #F97316]--plan <file>[/].");
            if (settings.Plan is not null) PlanFile.EnsureWritable(settings.Plan, settings.Force);
            var url = settings.Url is null ? null : ValidateUrl(settings.Url);
            if (settings.IncludeFiles && url is null)
                throw new CliException("--include-files needs [bold #F97316]--url[/] (the project URL, e.g. https://abcd.supabase.co).");

            var planOnly = settings.DryRun || settings.Plan is not null;
            var interactive = ImportCommandSupport.IsInteractive();
            ImportCommandSupport.RequireConfirmationMode(planOnly, settings.Yes, interactive);

            var db = ResolveSecret(settings.Db, Environment.GetEnvironmentVariable(DbEnvVar), interactive, "Postgres connection string", DbEnvVar, "--db");
            var key = settings.IncludeFiles
                ? ResolveSecret(settings.ServiceKey, Environment.GetEnvironmentVariable(ServiceKeyEnvVar), interactive, "Supabase service role key", ServiceKeyEnvVar, "--service-key")
                : null;
            var options = new SupabaseImportOptions(settings.Schemas?.Split(','));

            var importer = ImporterFactory?.Invoke(db, url, key, options) ?? new SupabaseImporter(db, url, key, options);
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

    internal static string ValidateUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new CliException("--url must be an http:// or https:// URL.");
        if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
            throw new CliException("Refusing a plain http:// URL: the service role key would travel unencrypted. Use https://.");
        return url;
    }

    internal static string ResolveSecret(string? option, string? envValue, bool interactive, string what, string envVar, string flag, Func<string>? prompt = null)
    {
        if (!string.IsNullOrWhiteSpace(option)) return option;
        if (!string.IsNullOrWhiteSpace(envValue)) return envValue;
        if (interactive)
        {
            var typed = prompt?.Invoke() ?? AnsiConsole.Prompt(new TextPrompt<string>($"{Markup.Escape(what)}:").Secret());
            if (!string.IsNullOrWhiteSpace(typed)) return typed;
        }
        throw new CliException($"A {Markup.Escape(what)} is required. Set the [bold #F97316]{Markup.Escape(envVar)}[/] environment variable (preferred) or pass {Markup.Escape(flag)}.");
    }
}
