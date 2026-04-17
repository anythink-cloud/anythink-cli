using AnythinkCli.BulkOperations.Interfaces;
using AnythinkCli.BulkOperations.Models;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AnythinkCli.BulkOperations.Readers;

/// <summary>
/// Streaming CSV reader with robust parsing and error handling
/// </summary>
public class CsvDataReader : IDataReader
{
    private readonly Stream _stream;
    private readonly StreamReader _reader;
    private readonly string[] _headers;
    private readonly CsvReaderOptions _options;
    private bool _disposed;

    public long? EstimatedTotalRecords { get; private set; }
    public string Source { get; }

    public CsvDataReader(Stream stream, CsvReaderOptions? options = null)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _reader = new StreamReader(stream, Encoding.UTF8);
        _options = options ?? new CsvReaderOptions();
        Source = _options.Source ?? "stream";

        _headers = ReadHeaders();
        EstimateFromFileSize();
    }

    public async IAsyncEnumerable<DataRecord> ReadRecordsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var lineNumber = 2; // Start after header row
        await foreach (var line in ReadLinesAsync(cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var record = ParseLine(line, lineNumber);
            if (record != null)
            {
                yield return record;
            }
            lineNumber++;
        }
    }

    public Dictionary<string, object> GetMetadata()
    {
        return new Dictionary<string, object>
        {
            ["format"] = "csv",
            ["headers"] = _headers,
            ["estimated_total"] = EstimatedTotalRecords ?? 0,
            ["encoding"] = "utf-8",
            ["delimiter"] = _options.Delimiter,
            ["has_headers"] = _options.HasHeaders
        };
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _reader.Dispose();
            _stream.Dispose();
            _disposed = true;
        }
        return ValueTask.CompletedTask;
    }

    private string[] ReadHeaders()
    {
        if (_options.HasHeaders)
        {
            var firstLine = _reader.ReadLine();
            if (firstLine == null)
                throw new InvalidOperationException("CSV file is empty");

            return ParseCsvLine(firstLine);
        }

        // Generate default headers if no header row
        var sampleLine = _reader.ReadLine();
        if (sampleLine == null)
            throw new InvalidOperationException("CSV file is empty");

        var columnCount = ParseCsvLine(sampleLine).Length;
        var headers = new string[columnCount];
        for (int i = 0; i < columnCount; i++)
        {
            headers[i] = $"column_{i + 1}";
        }

        // Reset stream position to beginning
        _stream.Position = 0;
        _reader.DiscardBufferedData();
        return headers;
    }

    private void EstimateFromFileSize()
    {
        try
        {
            if (!_stream.CanSeek) return;
            var headerBytes = _stream.Position;
            if (headerBytes <= 0) return;
            var remainingBytes = _stream.Length - headerBytes;
            // Rough estimate: remaining bytes / header line length gives approximate row count
            EstimatedTotalRecords = Math.Max(1, remainingBytes / headerBytes);
        }
        catch
        {
            EstimatedTotalRecords = null;
        }
    }

    private async IAsyncEnumerable<string> ReadLinesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!_reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await _reader.ReadLineAsync();
            if (line != null)
            {
                yield return line;
            }
        }
    }

    private DataRecord? ParseLine(string line, int lineNumber)
    {
        try
        {
            var values = ParseCsvLine(line);
            var jsonObject = new JsonObject();

            for (int i = 0; i < Math.Min(values.Length, _headers.Length); i++)
            {
                var header = _headers[i];
                var value = values[i];

                // Try to parse as JSON first, then as primitive types
                if (string.IsNullOrWhiteSpace(value))
                {
                    jsonObject[header] = null;
                }
                else if (value.StartsWith("{") || value.StartsWith("["))
                {
                    try
                    {
                        var jsonValue = JsonNode.Parse(value);
                        jsonObject[header] = jsonValue;
                    }
                    catch
                    {
                        jsonObject[header] = value;
                    }
                }
                else if (bool.TryParse(value, out var boolValue))
                {
                    jsonObject[header] = boolValue;
                }
                else if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
                {
                    jsonObject[header] = longValue;
                }
                else if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
                {
                    jsonObject[header] = doubleValue;
                }
                else
                {
                    jsonObject[header] = value.Trim('"');
                }
            }

            return new DataRecord
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
        catch (Exception ex)
        {
            // Return null for invalid lines - let the validator handle this
            return new DataRecord
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
    }

    private string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var i = 0;

        while (i < line.Length)
        {
            var c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    // Escaped quote
                    current.Append('"');
                    i += 2;
                }
                else
                {
                    // Toggle quote state
                    inQuotes = !inQuotes;
                    i++;
                }
            }
            else if (c == _options.Delimiter && !inQuotes)
            {
                // Field separator
                result.Add(current.ToString());
                current.Clear();
                i++;
            }
            else
            {
                current.Append(c);
                i++;
            }
        }

        // Add the last field
        result.Add(current.ToString());

        return result.ToArray();
    }
}

/// <summary>
/// Configuration options for CSV reading
/// </summary>
public class CsvReaderOptions
{
    public char Delimiter { get; set; } = ',';
    public bool HasHeaders { get; set; } = true;
    public bool TrimFields { get; set; } = true;
    public string? Source { get; set; }
    public CultureInfo Culture { get; set; } = CultureInfo.InvariantCulture;
    public bool SkipEmptyLines { get; set; } = true;
}
