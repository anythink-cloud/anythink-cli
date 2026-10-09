using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AnythinkCli.DataTransfer;

internal sealed class ErrorSink(string path, DataFormat format, IReadOnlyList<string>? header) : IDisposable
{
    private readonly object _gate = new();
    private StreamWriter? _text;
    private Utf8JsonWriter? _json;
    private FileStream? _file;

    public bool Wrote { get; private set; }

    public void Write(RawRow row, string error)
    {
        lock (_gate)
        {
            Wrote = true;
            var values = row.Values ?? new JsonObject();
            if (format == DataFormat.Csv)
            {
                _text ??= OpenCsv();
                // Guard is off: this file is meant to be fixed and re-imported, so cells must stay verbatim.
                var extras = values["_extra"] is JsonArray a ? a.Select(x => x?.GetValue<string>()) : [];
                Csv.WriteRow(_text, (header ?? []).Select(h => Csv.ToCell(values[h], false)).Append(error).Concat(extras));
                return;
            }

            var copy = (JsonObject)values.DeepClone();
            copy["_error"] = error;
            if (format == DataFormat.Jsonl)
            {
                _text ??= new StreamWriter(path, false, new UTF8Encoding(false));
                _text.WriteLine(copy.ToJsonString());
                return;
            }
            if (_json is null)
            {
                _file = File.Create(path);
                _json = new Utf8JsonWriter(_file, new JsonWriterOptions { Indented = true });
                _json.WriteStartArray();
            }
            copy.WriteTo(_json);
        }
    }

    private StreamWriter OpenCsv()
    {
        var w = new StreamWriter(path, false, new UTF8Encoding(false));
        Csv.WriteRow(w, (header ?? []).Append("_error"));
        return w;
    }

    public void Dispose()
    {
        _text?.Dispose();
        if (_json is null) return;
        _json.WriteEndArray();
        _json.Dispose();
        _file?.Dispose();
    }
}
