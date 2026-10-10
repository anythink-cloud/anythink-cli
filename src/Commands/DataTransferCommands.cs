using AnythinkCli.DataTransfer;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace AnythinkCli.Commands;

public class DataExportSettings : CommandSettings
{
    [CommandArgument(0, "<ENTITY>")]
    [Description("Entity name")]
    public string Entity { get; set; } = "";

    [CommandArgument(1, "<FILE>")]
    [Description("Output file, or - for stdout")]
    public string File { get; set; } = "";

    [CommandOption("--format <FORMAT>")]
    [Description("csv, json or jsonl (default: from the file extension)")]
    public string? Format { get; set; }

    [CommandOption("--filter <FILTER>")]
    [Description("Same filter syntax as 'data list --filter'")]
    public string? Filter { get; set; }

    [CommandOption("--fields <FIELDS>")]
    [Description("Comma-separated fields to export (default: all)")]
    public string? Fields { get; set; }

    [CommandOption("--page-size <N>")]
    [Description("Records fetched per request (default: 200, max: 1000)")]
    public int PageSize { get; set; } = 200;

    [CommandOption("--force")]
    [Description("Overwrite FILE if it already exists")]
    public bool Force { get; set; }

    [CommandOption("--no-formula-guard")]
    [Description("CSV: don't prefix text starting with = + - @ with a quote (exact values, but unsafe to open in a spreadsheet)")]
    public bool NoFormulaGuard { get; set; }
}

public class DataExportCommand : BaseCommand<DataExportSettings>
{
    internal Func<AnythinkCli.Client.AnythinkClient>? ClientFactory { get; set; }

    public override async Task<int> ExecuteAsync(CommandContext context, DataExportSettings settings)
    {
        try
        {
            var format = RowReader.Parse(settings.Format)
                ?? (settings.File == DataExportRunner.Stdout ? null : RowReader.FromExtension(settings.File))
                ?? throw new Client.CliException("Can't tell the file format. Pass --format csv|json|jsonl.");
            var fields = settings.Fields?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var options = new DataExportOptions(settings.Entity, settings.File, format, settings.Filter, fields,
                settings.PageSize, settings.Force, !settings.NoFormulaGuard);
            using var cts = new CancellationTokenSource();
            ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; cts.Cancel(); };
            Console.CancelKeyPress += onCancel;
            int rows;
            try { rows = await DataExportRunner.RunAsync((ClientFactory ?? GetClient)(), options, ct: cts.Token); }
            finally { Console.CancelKeyPress -= onCancel; }

            var message = $"Exported {rows} record(s) from {Markup.Escape(settings.Entity)}" +
                          (settings.File == DataExportRunner.Stdout ? "." : $" to {Markup.Escape(settings.File)}.");
            if (settings.File == DataExportRunner.Stdout) Console.Error.WriteLine(message); else Renderer.Success(message);
            return 0;
        }
        catch (Exception ex)
        {
            if (settings.File != DataExportRunner.Stdout) HandleError(ex);
            else Console.Error.WriteLine("Error: " + Markup.Remove(ex is OperationCanceledException ? "Cancelled." : ex.Message));
            return 1;
        }
    }
}

public class DataImportSettings : CommandSettings
{
    [CommandArgument(0, "<ENTITY>")]
    [Description("Entity name")]
    public string Entity { get; set; } = "";

    [CommandArgument(1, "<FILE>")]
    [Description("CSV, JSON array or JSONL file")]
    public string File { get; set; } = "";

    [CommandOption("--format <FORMAT>")]
    [Description("csv, json or jsonl (default: from the file extension, else sniffed)")]
    public string? Format { get; set; }

    [CommandOption("--dry-run")]
    [Description("Validate every row and report what would be created; sends nothing")]
    public bool DryRun { get; set; }

    [CommandOption("--concurrency <N>")]
    [Description("Parallel create requests (default: 4, max: 16)")]
    public int Concurrency { get; set; } = 4;

    [CommandOption("--errors <FILE>")]
    [Description("Write rows that were skipped or failed, with an _error column, so they can be fixed and re-imported")]
    public string? Errors { get; set; }

    [CommandOption("--ignore-unknown")]
    [Description("Drop columns that aren't fields on the entity instead of rejecting the row")]
    public bool IgnoreUnknown { get; set; }

    [CommandOption("-y|--yes")]
    [Description("Skip the confirmation prompt (required when not running interactively)")]
    public bool Yes { get; set; }
}

public class DataImportCommand : BaseCommand<DataImportSettings>
{
    internal Func<AnythinkCli.Client.AnythinkClient>? ClientFactory { get; set; }

    public override async Task<int> ExecuteAsync(CommandContext context, DataImportSettings settings)
    {
        try
        {
            var interactive = AnsiConsole.Profile.Capabilities.Interactive && !Console.IsInputRedirected;
            var options = new DataImportOptions(settings.Entity, settings.File, RowReader.Parse(settings.Format), settings.DryRun,
                settings.Concurrency, settings.Errors, settings.Yes, settings.IgnoreUnknown, interactive);
            using var cts = new CancellationTokenSource();
            ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; cts.Cancel(); };
            Console.CancelKeyPress += onCancel;
            DataImportResult result;
            try { result = await new DataImportRunner((ClientFactory ?? GetClient)()).RunAsync(options, cts.Token); }
            finally { Console.CancelKeyPress -= onCancel; }

            if (result.Cancelled) { Renderer.Info("Cancelled."); return 0; }
            foreach (var note in result.Notes) Renderer.Warn(Markup.Escape(note));
            if (result.Interrupted) Renderer.Warn("Interrupted: the rest of the file was not read. Counts below are partial.");

            foreach (var problem in result.Problems) Renderer.Warn(Markup.Escape(problem));
            if (result.Failed + result.Skipped > result.Problems.Count)
                Renderer.Warn($"…and {result.Failed + result.Skipped - result.Problems.Count} more." +
                              (settings.Errors is null ? " Use --errors FILE to capture them all." : ""));

            Renderer.KeyValue("Created", settings.DryRun ? $"{result.WouldCreate} (dry run, nothing sent)" : result.Created.ToString());
            Renderer.KeyValue("Failed", result.Failed.ToString());
            Renderer.KeyValue("Skipped", result.Skipped.ToString());
            if (settings.Errors is not null && result.HasProblems)
                Renderer.Info($"Rows needing attention written to {Markup.Escape(settings.Errors)}.");
            return result.HasProblems ? 1 : 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}
