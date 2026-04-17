using AnythinkCli.Client;
using AnythinkCli.BulkOperations.Core;
using AnythinkCli.BulkOperations.Interfaces;
using AnythinkCli.BulkOperations.Models;
using AnythinkCli.BulkOperations.Progress;
using AnythinkCli.BulkOperations.Readers;
using AnythinkCli.BulkOperations.Validators;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace AnythinkCli.Commands;

// ── data import ─────────────────────────────────────────────────────────────────

public class DataImportSettings : CommandSettings
{
    [CommandArgument(0, "<FILE>")]
    [Description("Path to the data file (CSV or JSON)")]
    public string File { get; set; } = "";

    [CommandArgument(1, "<ENTITY>")]
    [Description("Target entity name")]
    public string Entity { get; set; } = "";

    [CommandOption("--format")]
    [Description("File format (auto-detected if not specified)")]
    [DefaultValue(AutoFormat.Auto)]
    public FileFormat Format { get; set; } = AutoFormat.Auto;

    [CommandOption("--batch-size")]
    [Description("Number of records to process in each batch")]
    [DefaultValue(100)]
    public int BatchSize { get; set; } = 100;

    [CommandOption("--max-concurrency")]
    [Description("Maximum number of concurrent operations")]
    [DefaultValue(5)]
    public int MaxConcurrency { get; set; } = 5;

    [CommandOption("--validate-only")]
    [Description("Validate data without importing")]
    public bool ValidateOnly { get; set; }

    [CommandOption("--continue-on-error")]
    [Description("Continue processing even if some records fail")]
    [DefaultValue(true)]
    public bool ContinueOnError { get; set; } = true;

    [CommandOption("--max-retries")]
    [Description("Maximum number of retry attempts for failed records")]
    [DefaultValue(3)]
    public int MaxRetries { get; set; } = 3;

    [CommandOption("--dry-run")]
    [Description("Show what would be imported without actually importing")]
    public bool DryRun { get; set; }

    [CommandOption("--delimiter")]
    [Description("CSV delimiter character")]
    [DefaultValue(',')]
    public char Delimiter { get; set; } = ',';

    [CommandOption("--no-headers")]
    [Description("CSV file has no header row")]
    public bool NoHeaders { get; set; }

    [CommandOption("--skip-validation")]
    [Description("Skip data validation")]
    public bool SkipValidation { get; set; }

    [CommandOption("--timeout")]
    [Description("Operation timeout in minutes")]
    [DefaultValue(60)]
    public int TimeoutMinutes { get; set; } = 60;
}

public class DataImportCommand : BaseCommand<DataImportSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DataImportSettings settings)
    {
        try
        {
            // Validate inputs
            if (!File.Exists(settings.File))
            {
                Renderer.Error($"File not found: {settings.File}");
                return 1;
            }

            if (string.IsNullOrWhiteSpace(settings.Entity))
            {
                Renderer.Error("Entity name is required");
                return 1;
            }

            // Detect format if auto
            var format = settings.Format;
            if (format == AutoFormat.Auto)
            {
                format = DetectFileFormat(settings.File);
            }

            // Show import plan
            await ShowImportPlanAsync(settings, format);

            // Confirm unless dry-run or validate-only
            if (!settings.DryRun && !settings.ValidateOnly)
            {
                if (!AnsiConsole.Confirm($"Proceed with importing to [bold]{settings.Entity}[/]?"))
                {
                    Renderer.Info("Import cancelled");
                    return 0;
                }
            }

            // Execute import
            var result = await ExecuteImportAsync(settings, format);

            // Show results
            await ShowImportResultsAsync(result, settings);

            return result.Success ? 0 : 1;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }

    private async Task ShowImportPlanAsync(DataImportSettings settings, FileFormat format)
    {
        Renderer.Header("Import Plan");

        var table = new Table();
        table.AddColumn("Setting");
        table.AddColumn("Value");

        table.AddRow("Source File", settings.File);
        table.AddRow("Target Entity", settings.Entity);
        table.AddRow("Format", format.ToString().ToUpperInvariant());
        table.AddRow("Batch Size", settings.BatchSize.ToString());
        table.AddRow("Max Concurrency", settings.MaxConcurrency.ToString());
        table.AddRow("Validate Only", settings.ValidateOnly ? "Yes" : "No");
        table.AddRow("Dry Run", settings.DryRun ? "Yes" : "No");
        table.AddRow("Continue on Error", settings.ContinueOnError ? "Yes" : "No");
        table.AddRow("Max Retries", settings.MaxRetries.ToString());
        table.AddRow("Timeout", $"{settings.TimeoutMinutes} minutes");

        AnsiConsole.Write(table);

        // Show file preview
        await ShowFilePreviewAsync(settings, format);
    }

    private async Task ShowFilePreviewAsync(DataImportSettings settings, FileFormat format)
    {
        try
        {
            await using var stream = File.OpenRead(settings.File);
            IDataReader reader = format switch
            {
                FileFormat.Csv => new CsvDataReader(stream, new CsvReaderOptions
                {
                    Delimiter = settings.Delimiter,
                    HasHeaders = !settings.NoHeaders,
                    Source = settings.File
                }),
                FileFormat.Json => new JsonDataReader(stream, new JsonReaderOptions
                {
                    Source = settings.File
                }),
                _ => throw new NotSupportedException($"Format {format} not supported")
            };

            var metadata = reader.GetMetadata();
            Renderer.Header("File Information");

            var infoTable = new Table();
            infoTable.AddColumn("Property");
            infoTable.AddColumn("Value");

            foreach (var kvp in metadata)
            {
                infoTable.AddRow(kvp.Key, kvp.Value?.ToString() ?? "");
            }

            AnsiConsole.Write(infoTable);

            // Show preview of first few records
            var previewRecords = new List<DataRecord>();
            await foreach (var record in reader.ReadRecordsAsync())
            {
                previewRecords.Add(record);
                if (previewRecords.Count >= 3) break;
            }

            if (previewRecords.Count > 0)
            {
                Renderer.Header("Data Preview");
                var previewTable = new Table();
                
                // Add columns from first record
                var firstRecord = previewRecords.First();
                foreach (var property in firstRecord.Data.OrderBy(x => x.Key))
                {
                    previewTable.AddColumn(property.Key);
                }
                previewTable.AddColumn("Line");

                // Add rows
                foreach (var record in previewRecords)
                {
                    var cells = record.Data.OrderBy(x => x.Key).Select(x => 
                        x.Value?.ToString() ?? "").ToList();
                    cells.Add(record.LineNumber.ToString());
                    previewTable.AddRow(cells.ToArray());
                }

                AnsiConsole.Write(previewTable);
            }

            await reader.DisposeAsync();
        }
        catch (Exception ex)
        {
            Renderer.Info($"Could not preview file: {ex.Message}");
        }
    }

    private async Task<BulkResult> ExecuteImportAsync(DataImportSettings settings, FileFormat format)
    {
        var config = new BulkOperationConfig
        {
            BatchSize = settings.BatchSize,
            MaxConcurrency = settings.MaxConcurrency,
            ContinueOnError = settings.ContinueOnError,
            ValidateOnly = settings.ValidateOnly || settings.DryRun,
            MaxRetries = settings.MaxRetries,
            OperationTimeout = TimeSpan.FromMinutes(settings.TimeoutMinutes)
        };

        // Create progress reporter
        IBulkProgressReporter? progressReporter = null;
        
        if (!settings.ValidateOnly && !settings.DryRun)
        {
            progressReporter = await AnsiConsole.Progress()
                .AutoClear(true)
                .AutoRefresh(true)
                .StartAsync(ctx =>
                {
                    return Task.FromResult<IBulkProgressReporter>(new ConsoleProgressReporter(ctx, "Data Import", 0));
                });
        }
        else
        {
            progressReporter = new MemoryProgressTracker();
        }

        try
        {
            await using var stream = File.OpenRead(settings.File);
            IDataReader reader = format switch
            {
                FileFormat.Csv => new CsvDataReader(stream, new CsvReaderOptions
                {
                    Delimiter = settings.Delimiter,
                    HasHeaders = !settings.NoHeaders,
                    Source = settings.File
                }),
                FileFormat.Json => new JsonDataReader(stream, new JsonReaderOptions
                {
                    Source = settings.File
                }),
                _ => throw new NotSupportedException($"Format {format} not supported")
            };

            var client = GetClient();
            var processor = BulkProcessorFactory.CreateForImport(config, progressReporter);

            // Process records
            var records = new List<DataRecord>();
            await foreach (var record in reader.ReadRecordsAsync())
            {
                records.Add(record);
            }
            var result = await processor.ExecuteAsync<DataRecord>(
                records,
                async record => await ProcessRecordAsync(client, settings.Entity, record, settings),
                config,
                progressReporter
            );

            await reader.DisposeAsync();
            return result;
        }
        finally
        {
            if (progressReporter is IDisposable disposableReporter)
            {
                disposableReporter.Dispose();
            }
        }
    }

    private async Task ProcessRecordAsync(AnythinkClient client, string entity, DataRecord record, DataImportSettings settings)
    {
        try
        {
            if (settings.ValidateOnly || settings.DryRun)
            {
                // Simulate processing time for validation
                await Task.Delay(1);
                return;
            }

            await client.CreateItemAsync(entity, record.Data);
        }
        catch (Exception ex)
        {
            // Let the processor handle the error
            throw new InvalidOperationException($"Failed to import record from line {record.LineNumber}: {ex.Message}", ex);
        }
    }

    private Task ShowImportResultsAsync(BulkResult result, DataImportSettings settings)
    {
        Renderer.Header("Import Results");

        var summaryTable = new Table();
        summaryTable.AddColumn("Metric");
        summaryTable.AddColumn("Value");

        summaryTable.AddRow("Status", result.Success ? "[green]Success[/]" : "[red]Failed[/]");
        summaryTable.AddRow("Duration", FormatDuration(result.Duration));
        summaryTable.AddRow("Total Records", result.Total.ToString("N0"));
        summaryTable.AddRow("Processed", result.Processed.ToString("N0"));
        summaryTable.AddRow("Errors", result.Errors > 0 ? $"[red]{result.Errors:N0}[/]" : "0");
        summaryTable.AddRow("Warnings", result.Warnings > 0 ? $"[yellow]{result.Warnings:N0}[/]" : "0");
        summaryTable.AddRow("Success Rate", $"{result.SuccessRate:F1}%");

        AnsiConsole.Write(summaryTable);

        // Show errors if any
        if (result.ErrorDetails.Count > 0)
        {
            Renderer.Header("Errors");
            var errorTable = new Table();
            errorTable.AddColumn("Line");
            errorTable.AddColumn("Error");
            errorTable.AddColumn("Details");

            foreach (var error in result.ErrorDetails.Take(10))
            {
                errorTable.AddRow(
                    error.LineNumber.ToString(),
                    Markup.Escape(error.Message),
                    Markup.Escape(error.ErrorCode ?? "")
                );
            }

            if (result.ErrorDetails.Count > 10)
            {
                errorTable.AddRow("", $"[dim]... and {result.ErrorDetails.Count - 10} more errors[/]", "");
            }

            AnsiConsole.Write(errorTable);
        }

        // Show operation type
        if (settings.ValidateOnly)
        {
            Renderer.Success("Validation completed. No data was imported.");
        }
        else if (settings.DryRun)
        {
            Renderer.Success("Dry run completed. No data was imported.");
        }
        else
        {
            Renderer.Success($"Import to '{settings.Entity}' completed successfully.");
        }

        return Task.CompletedTask;
    }

    private static FileFormat DetectFileFormat(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".csv" => FileFormat.Csv,
            ".json" => FileFormat.Json,
            ".jsonl" => FileFormat.Json,
            ".ndjson" => FileFormat.Json,
            _ => throw new NotSupportedException($"Cannot auto-detect format for file: {filePath}")
        };
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return $"{duration.Hours}h {duration.Minutes}m {duration.Seconds}s";
        if (duration.TotalMinutes >= 1)
            return $"{duration.Minutes}m {duration.Seconds}s";
        return $"{duration.Seconds}.{duration.Milliseconds / 100:F0}s";
    }
}

// Supporting types
public enum FileFormat
{
    Auto,
    Csv,
    Json
}

public static class AutoFormat
{
    public const FileFormat Auto = FileFormat.Auto;
}
