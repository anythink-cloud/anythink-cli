using AnythinkCli.BulkOperations.Interfaces;
using AnythinkCli.BulkOperations.Models;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AnythinkCli.BulkOperations.Exporters;

/// <summary>
/// Streaming CSV exporter with configurable formatting
/// </summary>
public class CsvDataExporter : IDataExporter
{
    private readonly Stream _stream;
    private readonly StreamWriter _writer;
    private readonly CsvExportOptions _options;
    private readonly string[] _headers;
    private bool _headersWritten;
    private bool _disposed;

    public string Source { get; }

    public CsvDataExporter(Stream stream, string[] headers, CsvExportOptions? options = null)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _writer = new StreamWriter(stream, Encoding.UTF8);
        _options = options ?? new CsvExportOptions();
        _headers = headers ?? throw new ArgumentNullException(nameof(headers));
        Source = _options.Source ?? "stream";
        _headersWritten = false;
    }

    public async Task ExportAsync(IAsyncEnumerable<DataRecord> records, CancellationToken cancellationToken = default)
    {
        await foreach (var record in records.WithCancellation(cancellationToken))
        {
            await ExportRecordAsync(record, cancellationToken);
        }
        
        await FlushAsync(cancellationToken);
    }

    public string GetFormat() => "csv";

    public Dictionary<string, object> GetMetadata()
    {
        return new Dictionary<string, object>
        {
            ["format"] = "csv",
            ["headers"] = _headers,
            ["encoding"] = "utf-8",
            ["delimiter"] = _options.Delimiter,
            ["include_headers"] = _options.IncludeHeaders,
            ["quote_all"] = _options.QuoteAll
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            await FlushAsync();
            await _writer.DisposeAsync();
            await _stream.DisposeAsync();
            _disposed = true;
        }
    }

    private async Task ExportRecordAsync(DataRecord record, CancellationToken cancellationToken = default)
    {
        // Write headers if this is the first record and headers are enabled
        if (!_headersWritten && _options.IncludeHeaders)
        {
            await WriteHeadersAsync(cancellationToken);
            _headersWritten = true;
        }

        var values = new List<string>();
        foreach (var header in _headers)
        {
            var value = GetFieldValue(record.Data, header);
            values.Add(FormatCsvValue(value));
        }

        var line = string.Join(_options.Delimiter, values);
        await _writer.WriteLineAsync(line);
    }

    private async Task WriteHeadersAsync(CancellationToken cancellationToken = default)
    {
        var headerLine = string.Join(_options.Delimiter, _headers.Select(FormatCsvValue));
        await _writer.WriteLineAsync(headerLine);
    }

    private string FormatCsvValue(object? value)
    {
        var stringValue = value?.ToString() ?? "";

        // Quote if necessary
        if (_options.QuoteAll || NeedsQuoting(stringValue))
        {
            return $"\"{stringValue.Replace("\"", "\"\"")}\"";
        }

        return stringValue;
    }

    private static bool NeedsQuoting(string value)
    {
        return value.Contains(',') || 
               value.Contains('"') || 
               value.Contains('\n') || 
               value.Contains('\r') ||
               value.StartsWith(' ') ||
               value.EndsWith(' ');
    }

    private static object? GetFieldValue(JsonObject data, string fieldName)
    {
        return data.ContainsKey(fieldName) ? data[fieldName]?.AsValue() : null;
    }

    private async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await _writer.FlushAsync(cancellationToken);
    }
}

/// <summary>
/// Configuration options for CSV export
/// </summary>
public class CsvExportOptions
{
    public char Delimiter { get; set; } = ',';
    public bool IncludeHeaders { get; set; } = true;
    public bool QuoteAll { get; set; } = false;
    public string? Source { get; set; }
    public Encoding Encoding { get; set; } = Encoding.UTF8;
}
