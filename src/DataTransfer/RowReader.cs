using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AnythinkCli.Client;

namespace AnythinkCli.DataTransfer;

internal enum DataFormat { Csv, Json, Jsonl }

internal sealed record RawRow(int Number, JsonObject? Values, string? Error);

internal sealed class RowReader(Stream stream, DataFormat format)
{
    public IReadOnlyList<string>? Header { get; private set; }

    public static DataFormat? Parse(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        "csv" => DataFormat.Csv,
        "json" => DataFormat.Json,
        "jsonl" or "ndjson" => DataFormat.Jsonl,
        var other => throw new CliException($"Unknown format '{Spectre.Console.Markup.Escape(other)}'. Use csv, json or jsonl.")
    };

    public static DataFormat? FromExtension(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".csv" => DataFormat.Csv,
        ".json" => DataFormat.Json,
        ".jsonl" or ".ndjson" => DataFormat.Jsonl,
        _ => null
    };

    public static DataFormat Detect(DataFormat? flag, string path, Stream seekable)
    {
        if ((flag ?? FromExtension(path)) is { } known) return known;

        int b;
        do { b = seekable.ReadByte(); } while (b is ' ' or '\t' or '\r' or '\n' or 0xEF or 0xBB or 0xBF);
        seekable.Position = 0;
        return b switch
        {
            '[' => DataFormat.Json,
            '{' => DataFormat.Jsonl,
            _ => throw new CliException("Can't tell the file format. Pass --format csv|json|jsonl.")
        };
    }

    public async IAsyncEnumerable<RawRow> ReadAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        if (format == DataFormat.Json)
        {
            await foreach (var row in ReadJsonArrayAsync(ct)) yield return row;
        }
        else if (format == DataFormat.Jsonl)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, true, 64 * 1024, leaveOpen: true);
            var line = 0;
            while (await reader.ReadLineAsync(ct) is { } text)
            {
                line++;
                if (text.AsSpan().Trim().IsEmpty) continue;
                yield return ParseObject(line, text);
            }
        }
        else
        {
            foreach (var row in ReadCsv()) yield return row;
        }
    }

    private IEnumerable<RawRow> ReadCsv()
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 64 * 1024, leaveOpen: true);
        using var records = Csv.ReadRecords(reader).GetEnumerator();
        var number = 0;
        while (true)
        {
            List<string>? record = null;
            string? failure = null;
            try { if (records.MoveNext()) record = records.Current; }
            catch (FormatException ex) { failure = ex.Message; }
            if (failure is not null) { yield return new RawRow(number + 1, null, failure); yield break; }
            if (record is null) yield break;
            number++;

            if (Header is null)
            {
                Header = record.Select(h => h.Trim()).ToList();
                if (Header.Any(h => h.Length == 0) || Header.Distinct(StringComparer.Ordinal).Count() != Header.Count)
                    throw new CliException("The CSV header has an empty or duplicated column name.");
                continue;
            }
            if (record.Count == 1 && record[0].Length == 0) continue;

            var values = new JsonObject();
            for (var i = 0; i < Math.Min(record.Count, Header.Count); i++) values[Header[i]] = record[i];
            if (record.Count > Header.Count) values["_extra"] = new JsonArray(record.Skip(Header.Count).Select(c => (JsonNode?)c).ToArray());
            yield return record.Count == Header.Count
                ? new RawRow(number, values, null)
                : new RawRow(number, values, $"expected {Header.Count} cells, found {record.Count}");
        }
    }

    private async IAsyncEnumerable<RawRow> ReadJsonArrayAsync([EnumeratorCancellation] CancellationToken ct)
    {
        await using var items = JsonSerializer.DeserializeAsyncEnumerable<JsonNode?>(stream, cancellationToken: ct).GetAsyncEnumerator(ct);
        var number = 0;
        while (true)
        {
            JsonNode? node = null;
            string? failure = null;
            var more = false;
            try { more = await items.MoveNextAsync(); if (more) node = items.Current; }
            catch (JsonException ex) { failure = $"invalid JSON: {ex.Message}"; }
            if (failure is not null) { yield return new RawRow(number + 1, null, failure); yield break; }
            if (!more) yield break;
            number++;
            yield return node is JsonObject obj
                ? new RawRow(number, obj, null)
                : new RawRow(number, null, "array element is not an object");
        }
    }

    private static RawRow ParseObject(int number, string line)
    {
        try
        {
            return JsonNode.Parse(line) is JsonObject obj
                ? new RawRow(number, obj, null)
                : new RawRow(number, new JsonObject { ["_raw"] = line }, "line is not a JSON object");
        }
        catch (JsonException ex)
        {
            return new RawRow(number, new JsonObject { ["_raw"] = line }, $"invalid JSON: {ex.Message}");
        }
    }
}
