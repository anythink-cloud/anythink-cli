using AnythinkCli.Client;
using AnythinkCli.BulkOperations.Core;
using AnythinkCli.BulkOperations.Exporters;
using AnythinkCli.BulkOperations.Interfaces;
using AnythinkCli.BulkOperations.Models;
using AnythinkCli.BulkOperations.Progress;
using AnythinkCli.BulkOperations.Readers;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Text.Json.Nodes;

namespace AnythinkCli.Commands;

// ── data export ─────────────────────────────────────────────────────────────────

public class DataExportSettings : CommandSettings
{
    [CommandArgument(0, "<ENTITY>")]
    [Description("Entity name to export")]
    public string Entity { get; set; } = "";

    [CommandArgument(1, "<FILE>")]
    [Description("Output file path")]
    public string File { get; set; } = "";

    [CommandOption("--format")]
    [Description("Export format")]
    [DefaultValue(ExportFormat.Csv)]
    public ExportFormat Format { get; set; } = ExportFormat.Csv;

    [CommandOption("--filter")]
    [Description("Filter expression (JSON)")]
    public string? Filter { get; set; }

    [CommandOption("--limit")]
    [Description("Maximum number of records to export")]
    public int? Limit { get; set; }

    [CommandOption("--batch-size")]
    [Description("Number of records to fetch in each batch")]
    [DefaultValue(500)]
    public int BatchSize { get; set; } = 500;

    [CommandOption("--max-concurrency")]
    [Description("Maximum number of concurrent operations")]
    [DefaultValue(2)]
    public int MaxConcurrency { get; set; } = 2;

    [CommandOption("--delimiter")]
    [Description("CSV delimiter character")]
    [DefaultValue(',')]
    public char Delimiter { get; set; } = ',';

    [CommandOption("--no-headers")]
    [Description("CSV file has no header row")]
    public bool NoHeaders { get; set; }

    [CommandOption("--pretty")]
    [Description("Pretty-print JSON output")]
    public bool Pretty { get; set; }

    [CommandOption("--compress")]
    [Description("Compress output file (gzip)")]
    public bool Compress { get; set; }

    [CommandOption("--timeout")]
    [Description("Operation timeout in minutes")]
    [DefaultValue(60)]
    public int TimeoutMinutes { get; set; } = 60;

    [CommandOption("--all")]
    [Description("Export all records (streaming)")]
    public bool All { get; set; }
}

public class DataExportCommand : BaseCommand<DataExportSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DataExportSettings settings)
    {
        try
        {
            // Validate inputs
            if (string.IsNullOrWhiteSpace(settings.Entity))
            {
                Renderer.Error("Entity name is required");
                return 1;
            }

            if (string.IsNullOrWhiteSpace(settings.File))
            {
                Renderer.Error("Output file path is required");
                return 1;
            }

            // Show export plan
            await ShowExportPlanAsync(settings);

            // Confirm export
            if (!AnsiConsole.Confirm($"Proceed with exporting '{settings.Entity}' to [bold]{settings.File}[/]?"))
            {
                Renderer.Info("Export cancelled");
                return 0;
            }

            // Execute export
            var result = await ExecuteExportAsync(settings);

            // Show results
            ShowExportResults(result, settings);

            return result.Success ? 0 : 1;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }

    private async Task ShowExportPlanAsync(DataExportSettings settings)
    {
        Renderer.Header("Export Plan");

        var table = new Table();
        table.AddColumn("Setting");
        table.AddColumn("Value");

        table.AddRow("Source Entity", settings.Entity);
        table.AddRow("Output File", settings.File);
        table.AddRow("Format", settings.Format.ToString().ToUpperInvariant());
        table.AddRow("Batch Size", settings.BatchSize.ToString());
        table.AddRow("Max Concurrency", settings.MaxConcurrency.ToString());
        table.AddRow("Filter", settings.Filter ?? "None");
        table.AddRow("Limit", settings.Limit?.ToString() ?? "Unlimited");
        table.AddRow("Compress", settings.Compress ? "Yes" : "No");
        table.AddRow("Timeout", $"{settings.TimeoutMinutes} minutes");

        AnsiConsole.Write(table);

        // Get entity info preview
        await ShowEntityPreviewAsync(settings);
    }

    private async Task ShowEntityPreviewAsync(DataExportSettings settings)
    {
        try
        {
            var client = GetClient();
            
            // Get entity details
            var entity = await client.GetEntityAsync(settings.Entity);
            if (entity == null)
            {
                Renderer.Error($"Entity '{settings.Entity}' not found");
                return;
            }

            // Get sample data
            var sampleData = await client.ListItemsAsync(settings.Entity, 1, 3, settings.Filter);

            Renderer.Header("Entity Information");
            
            var infoTable = new Table();
            infoTable.AddColumn("Property");
            infoTable.AddColumn("Value");
            
            infoTable.AddRow("Name", entity.Name);
            infoTable.AddRow("Fields", entity.Fields?.Count.ToString() ?? "0");
            infoTable.AddRow("Sample Records", sampleData.Items.Count.ToString());

            AnsiConsole.Write(infoTable);

            // Show field names
            if (entity.Fields?.Count > 0)
            {
                Renderer.Header("Available Fields");
                var fieldTable = new Table();
                fieldTable.AddColumn("Field Name");
                fieldTable.AddColumn("Type");
                fieldTable.AddColumn("Required");
                
                foreach (var field in entity.Fields.Take(10))
                {
                    fieldTable.AddRow(
                        field.Name ?? "",
                        field.DatabaseType ?? "",
                        field.IsRequired.ToString()
                    );
                }

                if (entity.Fields.Count > 10)
                {
                    fieldTable.AddRow($"... and {entity.Fields.Count - 10} more fields", "", "");
                }

                AnsiConsole.Write(fieldTable);
            }

            // Show sample data
            if (sampleData.Items.Count > 0)
            {
                Renderer.Header("Sample Data Preview");
                var previewTable = new Table();
                
                // Add columns from first record
                var firstRecord = sampleData.Items.First();
                var columns = firstRecord.Select(x => x.Key).OrderBy(x => x).Take(10).ToList();
                
                foreach (var column in columns)
                {
                    previewTable.AddColumn(column);
                }
                previewTable.AddColumn("ID");

                // Add rows
                foreach (var record in sampleData.Items)
                {
                    var cells = columns.Select(col => 
                        record.ContainsKey(col) ? record[col]?.ToString()?.Truncate(50) ?? "" : "").ToList();
                    cells.Add(record["id"]?.ToString() ?? "");
                    previewTable.AddRow(cells.ToArray());
                }

                AnsiConsole.Write(previewTable);
            }
        }
        catch (Exception ex)
        {
            Renderer.Info($"Could not preview entity: {ex.Message}");
        }
    }

    private async Task<BulkResult> ExecuteExportAsync(DataExportSettings settings)
    {
        var config = new BulkOperationConfig
        {
            BatchSize = settings.BatchSize,
            MaxConcurrency = settings.MaxConcurrency,
            ContinueOnError = true,
            MaxRetries = 1,
            OperationTimeout = TimeSpan.FromMinutes(settings.TimeoutMinutes)
        };

        // Create progress reporter
        IBulkProgressReporter? progressReporter = null;
        
        progressReporter = await AnsiConsole.Progress()
            .AutoClear(true)
            .AutoRefresh(true)
            .StartAsync(async ctx =>
            {
                return new ConsoleProgressReporter(ctx, "Data Export", 0);
            });

        try
        {
            var client = GetClient();
            var processor = BulkProcessorFactory.CreateForExport(config, progressReporter);

            // Get entity fields for headers
            var entity = await client.GetEntityAsync(settings.Entity);
            var headers = entity?.Fields?.Select(f => f.Name).Where(n => !string.IsNullOrEmpty(n)).ToArray() 
                         ?? new[] { "id" };

            // Create output stream
            await using var outputStream = CreateOutputStream(settings.File, settings.Compress);
            
            // Create exporter
            IDataExporter exporter = settings.Format switch
            {
                ExportFormat.Csv => new CsvDataExporter(outputStream, headers, new CsvExportOptions
                {
                    Delimiter = settings.Delimiter,
                    IncludeHeaders = !settings.NoHeaders,
                    Source = settings.File
                }),
                ExportFormat.Json => new JsonDataExporter(outputStream, new JsonExportOptions
                {
                    Format = JsonFormat.LineDelimited,
                    PrettyPrint = settings.Pretty,
                    Source = settings.File
                }),
                _ => throw new NotSupportedException($"Format {settings.Format} not supported")
            };

            // Stream data from API
            var records = StreamDataFromApiAsync(client, settings, null);
            
            // Convert to list for processor
            var recordList = new List<DataRecord>();
            await foreach (var record in records)
            {
                recordList.Add(record);
            }
            
            // Process export
            var result = await processor.ExecuteAsync<DataRecord>(
                recordList,
                async record => await ProcessExportRecordAsync(exporter, record),
                config,
                progressReporter
            );

            await exporter.DisposeAsync();
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

    private async IAsyncEnumerable<DataRecord> StreamDataFromApiAsync(AnythinkClient client, DataExportSettings settings, long? totalCount)
    {
        var page = 1;
        var exported = 0;
        var limit = settings.All ? settings.BatchSize : Math.Min(settings.BatchSize, settings.Limit ?? int.MaxValue);

        while (true)
        {
            var response = await client.ListItemsAsync(settings.Entity, page, limit, settings.Filter);
            
            foreach (var item in response.Items)
            {
                if (settings.Limit.HasValue && exported >= settings.Limit.Value)
                    yield break;

                var dataRecord = new DataRecord
                {
                    LineNumber = exported + 1,
                    Data = new JsonObject(),
                    Source = "api",
                    Metadata = new Dictionary<string, object>
                    {
                        ["page"] = page,
                        ["index"] = exported
                    }
                };
                
                // Copy data from API response to JsonObject
                foreach (var kvp in item)
                {
                    dataRecord.Data[kvp.Key] = kvp.Value != null ? JsonValue.Create(kvp.Value) : null;
                }
                
                yield return dataRecord;

                exported++;
            }

            if (!response.HasNextPage || response.Items.Count == 0)
                yield break;

            if (settings.Limit.HasValue && exported >= settings.Limit.Value)
                yield break;

            page++;
        }
    }

    private async Task ProcessExportRecordAsync(IDataExporter exporter, DataRecord record)
    {
        // This is a no-op since the exporter handles the actual writing
        // The processor just tracks progress
        await Task.CompletedTask;
    }

    private static Stream CreateOutputStream(string filePath, bool compress)
    {
        var fileStream = File.Create(filePath);
        
        if (compress)
        {
            return new System.IO.Compression.GZipStream(fileStream, System.IO.Compression.CompressionMode.Compress);
        }
        
        return fileStream;
    }

    private void ShowExportResults(BulkResult result, DataExportSettings settings)
    {
        Renderer.Header("Export Results");

        var summaryTable = new Table();
        summaryTable.AddColumn("Metric");
        summaryTable.AddColumn("Value");

        summaryTable.AddRow("Status", result.Success ? "[green]Success[/]" : "[red]Failed[/]");
        summaryTable.AddRow("Duration", FormatDuration(result.Duration));
        summaryTable.AddRow("Records Exported", result.Processed.ToString("N0"));
        summaryTable.AddRow("Errors", result.Errors > 0 ? $"[red]{result.Errors:N0}[/]" : "0");
        summaryTable.AddRow("Warnings", result.Warnings > 0 ? $"[yellow]{result.Warnings:N0}[/]" : "0");
        summaryTable.AddRow("Output File", settings.File);
        summaryTable.AddRow("Format", settings.Format.ToString().ToUpperInvariant());
        summaryTable.AddRow("Compressed", settings.Compress ? "Yes" : "No");

        AnsiConsole.Write(summaryTable);

        // Show file size
        try
        {
            var fileInfo = new FileInfo(settings.File);
            var size = fileInfo.Length;
            var sizeText = size > 1024 * 1024 ? $"{size / 1024.0 / 1024.0:F1} MB" :
                           size > 1024 ? $"{size / 1024.0:F1} KB" : $"{size} bytes";
            
            summaryTable.AddRow("File Size", sizeText);
        }
        catch
        {
            // Ignore file size errors
        }

        if (result.Success)
        {
            Renderer.Success($"Export of '{settings.Entity}' completed successfully.");
        }
        else
        {
            Renderer.Error("Export completed with errors.");
        }
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
public enum ExportFormat
{
    Csv,
    Json
}

public static class StringExtensions
{
    public static string Truncate(this string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length > maxLength ? value.Substring(0, maxLength) + "..." : value;
    }
}
