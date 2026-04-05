using AnythinkCli.BulkOperations.Interfaces;
using AnythinkCli.BulkOperations.Models;
using AnythinkCli.Output;
using Spectre.Console;

namespace AnythinkCli.BulkOperations.Progress;

/// <summary>
/// Rich console progress reporter with live updates
/// </summary>
public class ConsoleProgressReporter : IBulkProgressReporter
{
    private readonly ProgressTask _progressTask;
    private readonly ProgressContext _progressContext;
    private readonly string _operationName;
    private readonly long _estimatedTotal;
    private readonly DateTime _startTime;
    private readonly List<BulkError> _errors = new();
    private readonly List<BulkWarning> _warnings = new();
    private readonly object _lock = new();

    public ConsoleProgressReporter(ProgressContext context, string operationName, long estimatedTotal)
    {
        _progressContext = context;
        _operationName = operationName;
        _estimatedTotal = estimatedTotal;
        _startTime = DateTime.UtcNow;
        
        _progressTask = context.AddTask($"[bold]{operationName}[/]", new ProgressTaskSettings
        {
            MaxValue = estimatedTotal > 0 ? estimatedTotal : 100,
            AutoStart = true
        });
    }

    public void Report(BulkProgress progress)
    {
        lock (_lock)
        {
            UpdateProgress(progress);
        }
    }

    public void StartOperation(string operationName, long estimatedTotal)
    {
        // Already initialized in constructor
    }

    public void CompleteOperation(bool success)
    {
        lock (_lock)
        {
            _progressTask.StopTask();
            
            if (success)
            {
                _progressTask.Value = _progressTask.MaxValue;
                AnsiConsole.MarkupLine($"[green]✓[/] {_operationName} completed successfully");
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]✗[/] {_operationName} failed");
            }

            ShowSummary();
        }
    }

    public void ReportError(BulkError error)
    {
        lock (_lock)
        {
            _errors.Add(error);
            if (_errors.Count <= 5) // Show first 5 errors inline
            {
                AnsiConsole.MarkupLine($"[red]Error line {error.LineNumber}:[/] {Markup.Escape(error.Message)}");
            }
        }
    }

    public void ReportWarning(BulkWarning warning)
    {
        lock (_lock)
        {
            _warnings.Add(warning);
            if (_warnings.Count <= 3) // Show first 3 warnings inline
            {
                AnsiConsole.MarkupLine($"[yellow]Warning line {warning.LineNumber}:[/] {Markup.Escape(warning.Message)}");
            }
        }
    }

    private void UpdateProgress(BulkProgress progress)
    {
        var description = BuildDescription(progress);
        _progressTask.Description = description;
        _progressTask.Value = progress.Processed;

        if (_estimatedTotal > 0)
        {
            var percent = (double)progress.Processed / _estimatedTotal * 100;
            _progressTask.MaxValue = _estimatedTotal;
        }
    }

    private string BuildDescription(BulkProgress progress)
    {
        var parts = new List<string> { $"[bold]{_operationName}[/]" };

        if (progress.Processed > 0)
        {
            parts.Add($"[green]{progress.Processed:N0}[/] processed");
        }

        if (progress.Errors > 0)
        {
            parts.Add($"[red]{progress.Errors:N0}[/] errors");
        }

        if (progress.Warnings > 0)
        {
            parts.Add($"[yellow]{progress.Warnings:N0}[/] warnings");
        }

        if (progress.EstimatedTimeRemaining != TimeSpan.Zero)
        {
            var eta = FormatTimeSpan(progress.EstimatedTimeRemaining);
            parts.Add($"ETA: {eta}");
        }

        var rate = progress.ProcessedPerSecond;
        if (rate > 0)
        {
            parts.Add($"[dim]{rate:N0}/sec[/]");
        }

        return string.Join(" • ", parts);
    }

    private void ShowSummary()
    {
        var duration = DateTime.UtcNow - _startTime;
        
        AnsiConsole.WriteLine();
        Renderer.Header("Operation Summary");
        
        var table = new Table();
        table.AddColumn("Metric");
        table.AddColumn("Value");

        table.AddRow("Duration", FormatTimeSpan(duration));
        table.AddRow("Processed", $"{_progressTask.Value:N0}");
        
        if (_errors.Count > 0)
        {
            table.AddRow("Errors", $"[red]{_errors.Count:N0}[/]");
        }
        
        if (_warnings.Count > 0)
        {
            table.AddRow("Warnings", $"[yellow]{_warnings.Count:N0}[/]");
        }

        if (_progressTask.Value > 0 && duration.TotalSeconds > 0)
        {
            var rate = _progressTask.Value / duration.TotalSeconds;
            table.AddRow("Average Rate", $"{rate:N0}/sec");
        }

        AnsiConsole.Write(table);
    }

    private static string FormatTimeSpan(TimeSpan span)
    {
        if (span.TotalHours >= 1)
            return $"{span.Hours}h {span.Minutes}m {span.Seconds}s";
        if (span.TotalMinutes >= 1)
            return $"{span.Minutes}m {span.Seconds}s";
        return $"{span.Seconds}s";
    }
}
