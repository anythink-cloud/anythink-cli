using AnythinkCli.BulkOperations.Interfaces;
using AnythinkCli.BulkOperations.Models;
using AnythinkCli.BulkOperations.Readers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AnythinkCli.BulkOperations.Exporters;

/// <summary>
/// Streaming JSON exporter supporting both array and line-delimited formats
/// </summary>
public class JsonDataExporter : IDataExporter
{
    private readonly Stream _stream;
    private readonly StreamWriter _writer;
    private readonly JsonExportOptions _options;
    private bool _firstRecord;
    private bool _disposed;

    public string Source { get; }

    public JsonDataExporter(Stream stream, JsonExportOptions? options = null)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _writer = new StreamWriter(stream, Encoding.UTF8);
        _options = options ?? new JsonExportOptions();
        Source = _options.Source ?? "stream";
        _firstRecord = true;

        // Initialize output format
        if (_options.Format == JsonFormat.Array)
        {
            _writer.Write("[");
        }
    }

    public async Task ExportAsync(IAsyncEnumerable<DataRecord> records, CancellationToken cancellationToken = default)
    {
        await foreach (var record in records.WithCancellation(cancellationToken))
        {
            await ExportRecordAsync(record, cancellationToken);
        }
        
        await FinalizeOutputAsync(cancellationToken);
    }

    public string GetFormat() => _options.Format.ToString().ToLowerInvariant();

    public Dictionary<string, object> GetMetadata()
    {
        return new Dictionary<string, object>
        {
            ["format"] = _options.Format.ToString().ToLowerInvariant(),
            ["encoding"] = "utf-8",
            ["indent"] = _options.Indent,
            ["array_mode"] = _options.Format == JsonFormat.Array,
            ["pretty_print"] = _options.PrettyPrint
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            await FinalizeOutputAsync();
            await _writer.DisposeAsync();
            await _stream.DisposeAsync();
            _disposed = true;
        }
    }

    private async Task ExportRecordAsync(DataRecord record, CancellationToken cancellationToken = default)
    {
        var jsonOptions = _options.PrettyPrint 
            ? new JsonSerializerOptions { WriteIndented = true }
            : new JsonSerializerOptions { WriteIndented = false };

        var jsonString = record.Data.ToJsonString(jsonOptions);

        if (_options.Format == JsonFormat.LineDelimited)
        {
            await _writer.WriteLineAsync(jsonString);
        }
        else // Array format
        {
            if (!_firstRecord)
            {
                await _writer.WriteAsync(",");
            }
            
            await _writer.WriteAsync(jsonString);
            _firstRecord = false;
        }
    }

    private async Task FinalizeOutputAsync(CancellationToken cancellationToken = default)
    {
        if (_options.Format == JsonFormat.Array && !_disposed)
        {
            await _writer.WriteAsync("]");
        }
        
        await _writer.FlushAsync(cancellationToken);
    }
}

/// <summary>
/// Configuration options for JSON export
/// </summary>
public class JsonExportOptions
{
    public JsonFormat Format { get; set; } = JsonFormat.LineDelimited;
    public bool PrettyPrint { get; set; } = false;
    public int Indent { get; set; } = 2;
    public string? Source { get; set; }
    public Encoding Encoding { get; set; } = Encoding.UTF8;
}
