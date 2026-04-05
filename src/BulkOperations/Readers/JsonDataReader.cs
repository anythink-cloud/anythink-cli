using AnythinkCli.BulkOperations.Interfaces;
using AnythinkCli.BulkOperations.Models;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AnythinkCli.BulkOperations.Readers;

/// <summary>
/// Streaming JSON reader supporting both array and line-delimited formats
/// </summary>
public class JsonDataReader : IDataReader
{
    private readonly Stream _stream;
    private readonly JsonReaderOptions _options;
    private bool _disposed;

    public long? EstimatedTotalRecords { get; private set; }
    public string Source { get; }

    public JsonDataReader(Stream stream, JsonReaderOptions? options = null)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _options = options ?? new JsonReaderOptions();
        Source = _options.Source ?? "stream";

        EstimateTotalRecords();
    }

    public async IAsyncEnumerable<DataRecord> ReadRecordsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_options.Format == JsonFormat.LineDelimited)
        {
            await foreach (var record in ReadLineDelimitedAsync(cancellationToken))
            {
                yield return record;
            }
        }
        else
        {
            await foreach (var record in ReadArrayFormatAsync(cancellationToken))
            {
                yield return record;
            }
        }
    }

    public Dictionary<string, object> GetMetadata()
    {
        return new Dictionary<string, object>
        {
            ["format"] = _options.Format.ToString().ToLowerInvariant(),
            ["estimated_total"] = EstimatedTotalRecords ?? 0,
            ["encoding"] = "utf-8",
            ["array_mode"] = _options.Format == JsonFormat.Array
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            await _stream.DisposeAsync();
            _disposed = true;
        }
    }

    private async IAsyncEnumerable<DataRecord> ReadLineDelimitedAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(_stream);
        var lineNumber = 1;

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line)) continue;

            DataRecord record;
            try
            {
                var jsonObject = JsonNode.Parse(line) as JsonObject;
                if (jsonObject != null)
                {
                    record = new DataRecord
                    {
                        LineNumber = lineNumber,
                        Data = jsonObject,
                        Source = Source,
                        Metadata = new Dictionary<string, object>
                        {
                            ["raw_line"] = line
                        }
                    };
                }
                else
                {
                    continue;
                }
            }
            catch (JsonException ex)
            {
                // Return record with parse error for validation
                record = new DataRecord
                {
                    LineNumber = lineNumber,
                    Data = new JsonObject(),
                    Source = Source,
                    Metadata = new Dictionary<string, object>
                    {
                        ["parse_error"] = ex.Message,
                        ["raw_line"] = line
                    }
                };
            }

            yield return record;
            lineNumber++;
        }
    }

    private async IAsyncEnumerable<DataRecord> ReadArrayFormatAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var lineNumber = 1;

        await foreach (var record in ProcessJsonArrayAsync(_stream, cancellationToken))
        {
            lineNumber++;
            yield return record;
        }
    }

    private async IAsyncEnumerable<DataRecord> ProcessJsonArrayAsync(Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Expected JSON array at root level");
        }

        var lineNumber = 1;
        foreach (var element in root.EnumerateArray())
        {
            DataRecord record;
            try
            {
                var jsonNode = JsonNode.Parse(element.GetRawText()) as JsonObject;
                if (jsonNode != null)
                {
                    record = new DataRecord
                    {
                        LineNumber = lineNumber,
                        Data = jsonNode,
                        Source = Source,
                        Metadata = new Dictionary<string, object>
                        {
                            ["array_index"] = lineNumber - 1
                        }
                    };
                }
                else
                {
                    continue;
                }
            }
            catch (JsonException ex)
            {
                record = new DataRecord
                {
                    LineNumber = lineNumber,
                    Data = new JsonObject(),
                    Source = Source,
                    Metadata = new Dictionary<string, object>
                    {
                        ["parse_error"] = ex.Message,
                        ["array_index"] = lineNumber - 1,
                        ["raw_element"] = element.GetRawText()
                    }
                };
            }

            yield return record;
            lineNumber++;
        }
    }

    private void EstimateTotalRecords()
    {
        try
        {
            if (_options.Format == JsonFormat.LineDelimited)
            {
                EstimateLineDelimitedCount();
            }
            else
            {
                EstimateArrayCount();
            }
        }
        catch
        {
            // If we can't estimate, don't fail
            EstimatedTotalRecords = null;
        }
    }

    private void EstimateLineDelimitedCount()
    {
        var originalPosition = _stream.Position;
        var lineCount = 0;

        using var reader = new StreamReader(_stream);
        while (reader.ReadLine() != null)
        {
            lineCount++;
        }

        EstimatedTotalRecords = lineCount;

        // Reset stream position
        _stream.Position = 0;
    }

    private void EstimateArrayCount()
    {
        var originalPosition = _stream.Position;
        
        using var document = JsonDocument.Parse(_stream);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            EstimatedTotalRecords = root.GetArrayLength();
        }

        // Reset stream position
        _stream.Position = 0;
    }
}

/// <summary>
/// JSON format options
/// </summary>
public enum JsonFormat
{
    Array,
    LineDelimited
}

/// <summary>
/// Configuration options for JSON reading
/// </summary>
public class JsonReaderOptions
{
    public JsonFormat Format { get; set; } = JsonFormat.LineDelimited;
    public string? Source { get; set; }
    public JsonDocumentOptions DocumentOptions { get; set; } = new JsonDocumentOptions
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };
}
